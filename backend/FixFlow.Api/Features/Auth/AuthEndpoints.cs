using FixFlow.Api.Features.Tenants;
using FixFlow.Api.Features.Users;
using FixFlow.Api.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FixFlow.Api.Features.Auth;

public record SignInRequest(string? Login, string? Password);
public record TenantSummary(Guid Id, string Name, string Code, string DefaultLanguage, string TimeZone);
public record MeResponse(Guid Id, string UserName, string Email, string DisplayName, UserRole Role, TenantSummary? Tenant);
public record SessionResponse(string AccessToken, DateTimeOffset ExpiresAt, MeResponse User);

/// <summary>
/// Sign-in, refresh, sign-out, and "who am I". Token design: ADR 0005.
/// The access token goes in the response body (the SPA keeps it in memory);
/// the refresh token goes only in an HttpOnly cookie that JavaScript can't read.
/// </summary>
public static class AuthEndpoints
{
    public const string RefreshCookie = "fixflow_refresh";
    public const string RateLimitPolicy = "auth";
    private const string CookiePath = "/api/auth";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup(CookiePath).RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/sign-in", SignIn).AllowAnonymous();
        auth.MapPost("/refresh", Refresh).AllowAnonymous();
        auth.MapPost("/sign-out", SignOut).AllowAnonymous();
        auth.MapGet("/me", Me); // authenticated by the fallback policy
    }

    private static async Task<IResult> SignIn(
        SignInRequest body, HttpContext http, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
        FixFlowDbContext db, TokenService tokens, TimeProvider time, ILoggerFactory logs)
    {
        if (string.IsNullOrWhiteSpace(body.Login) || string.IsNullOrEmpty(body.Password))
            return Problem(StatusCodes.Status400BadRequest, "validation.failed");

        // Usernames can't contain "@" (UserRules), so "@" means the login is an email.
        var login = body.Login.Trim();
        var user = login.Contains('@') ? await users.FindByEmailAsync(login) : await users.FindByNameAsync(login);

        if (user is null || user.IsDeleted)
        {
            // Do the same hashing work as a real check, so response time doesn't reveal whether the account exists.
            DummyPasswordCheck(users, body.Password);
            return Problem(StatusCodes.Status401Unauthorized, "auth.invalid_credentials");
        }

        // Counts failures and locks after the configured number; while locked it fails even with the right password.
        var result = await signIn.CheckPasswordSignInAsync(user, body.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            // shortcut: diagnostic log only; the audit log entry for lockouts comes with audit logs (Phase 12).
            logs.CreateLogger("FixFlow.Auth").LogWarning("Sign-in refused: account {UserId} is locked out", user.Id);
            return Problem(StatusCodes.Status401Unauthorized, "auth.account_locked",
                new() { ["lockoutEnd"] = await users.GetLockoutEndDateAsync(user) });
        }
        if (!result.Succeeded)
            return Problem(StatusCodes.Status401Unauthorized, "auth.invalid_credentials");

        // Status is checked only after the password, so these messages never confirm an account to a guesser.
        var tenant = user.TenantId is { } tenantId ? await db.Tenants.SingleAsync(t => t.Id == tenantId) : null;
        if (BlockReason(user, tenant) is { } blocked)
            return Problem(StatusCodes.Status403Forbidden, blocked);

        user.LastSignInAt = time.GetUtcNow();
        var refresh = tokens.AddRefreshToken(user.Id, familyId: Guid.NewGuid());
        await db.SaveChangesAsync(); // saves the refresh token and LastSignInAt together

        return Session(http, tokens, user, tenant, refresh);
    }

    private static async Task<IResult> Refresh(HttpContext http, FixFlowDbContext db, TokenService tokens, TimeProvider time, ILoggerFactory logs)
    {
        if (!IsAllowedOrigin(http)) return Problem(StatusCodes.Status403Forbidden, "auth.forbidden");
        if (!http.Request.Cookies.TryGetValue(RefreshCookie, out var raw) || string.IsNullOrEmpty(raw))
            return SessionExpired(http);

        var hash = TokenService.Hash(raw);
        var token = await db.RefreshTokens.Include(t => t.User).ThenInclude(u => u.Tenant)
            .SingleOrDefaultAsync(t => t.TokenHash == hash);
        var now = time.GetUtcNow();
        if (token is null || token.ExpiresAt <= now)
            return SessionExpired(http);

        if (token.RevokedAt is not null)
        {
            // A rotated token came back: someone kept a copy. End every session from that sign-in.
            logs.CreateLogger("FixFlow.Auth").LogWarning("Refresh token reuse detected for user {UserId}; session family revoked", token.UserId);
            await RevokeFamilyAsync(db, token.FamilyId, now);
            return SessionExpired(http);
        }

        if (BlockReason(token.User, token.User.Tenant) is { } blocked)
        {
            await RevokeFamilyAsync(db, token.FamilyId, now);
            if (token.User.IsDeleted) return SessionExpired(http);
            http.Response.Cookies.Delete(RefreshCookie, CookieOptions(expires: null));
            return Problem(StatusCodes.Status403Forbidden, blocked);
        }

        // Rotation: the presented token is spent; a new one from the same family replaces it.
        // shortcut: two simultaneous refreshes with the same token can both rotate; the SPA serializes refreshes
        // across tabs (Web Locks, Phase 3e). Add an optimistic-concurrency check if another client type appears.
        token.RevokedAt = now;
        var refresh = tokens.AddRefreshToken(token.UserId, token.FamilyId);
        await db.SaveChangesAsync();

        return Session(http, tokens, token.User, token.User.Tenant, refresh);
    }

    private static async Task<IResult> SignOut(HttpContext http, FixFlowDbContext db, TimeProvider time)
    {
        if (!IsAllowedOrigin(http)) return Problem(StatusCodes.Status403Forbidden, "auth.forbidden");
        if (http.Request.Cookies.TryGetValue(RefreshCookie, out var raw) && !string.IsNullOrEmpty(raw))
        {
            var hash = TokenService.Hash(raw);
            var familyId = await db.RefreshTokens.Where(t => t.TokenHash == hash).Select(t => (Guid?)t.FamilyId).SingleOrDefaultAsync();
            if (familyId is { } id) await RevokeFamilyAsync(db, id, time.GetUtcNow());
        }
        http.Response.Cookies.Delete(RefreshCookie, CookieOptions(expires: null));
        return Results.NoContent();
    }

    private static async Task<IResult> Me(HttpContext http, FixFlowDbContext db)
    {
        if (!Guid.TryParse(http.User.FindFirst(FixFlowClaims.UserId)?.Value, out var userId))
            return Problem(StatusCodes.Status401Unauthorized, "auth.unauthenticated");
        var user = await db.Users.Include(u => u.Tenant).AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId);
        return user is null
            ? Problem(StatusCodes.Status401Unauthorized, "auth.unauthenticated")
            : Results.Ok(ToMe(user, user.Tenant));
    }

    /// <summary>Why this account may not have a session right now, or null if it may.</summary>
    private static string? BlockReason(ApplicationUser user, Tenant? tenant) => (user, tenant) switch
    {
        ({ IsDeleted: true }, _) => "auth.session_expired",
        ({ IsActive: false }, _) => "auth.account_disabled",
        (_, { Status: TenantStatus.Suspended }) => "tenant.suspended",
        (_, { Status: TenantStatus.Archived }) => "tenant.archived",
        _ => null,
    };

    private static IResult Session(HttpContext http, TokenService tokens, ApplicationUser user, Tenant? tenant, (string Raw, DateTimeOffset ExpiresAt) refresh)
    {
        http.Response.Cookies.Append(RefreshCookie, refresh.Raw, CookieOptions(refresh.ExpiresAt));
        var access = tokens.CreateAccessToken(user);
        return Results.Ok(new SessionResponse(access.Token, access.ExpiresAt, ToMe(user, tenant)));
    }

    private static MeResponse ToMe(ApplicationUser user, Tenant? tenant) => new(
        user.Id, user.UserName!, user.Email!, user.DisplayName, user.Role,
        tenant is null ? null : new TenantSummary(tenant.Id, tenant.Name, tenant.Code, tenant.DefaultLanguage, tenant.TimeZone));

    // HttpOnly: invisible to JavaScript (XSS can't steal it). Secure: HTTPS only (browsers also allow http://localhost).
    // SameSite=Strict: never sent on requests started by another site (CSRF). Path: only sent to /api/auth/*.
    private static CookieOptions CookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = CookiePath,
        Expires = expires,
        IsEssential = true,
    };

    private static async Task RevokeFamilyAsync(FixFlowDbContext db, Guid familyId, DateTimeOffset now) =>
        await db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));

    private static IResult SessionExpired(HttpContext http)
    {
        http.Response.Cookies.Delete(RefreshCookie, CookieOptions(expires: null));
        return Problem(StatusCodes.Status401Unauthorized, "auth.session_expired");
    }

    /// <summary>Second CSRF layer behind SameSite: a browser request from an origin we don't serve is refused.</summary>
    private static bool IsAllowedOrigin(HttpContext http)
    {
        var origin = http.Request.Headers.Origin.ToString();
        if (origin == "") return true; // non-browser clients and same-origin GETs send none
        var allowed = http.RequestServices.GetRequiredService<IConfiguration>().GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        return allowed.Contains(origin, StringComparer.OrdinalIgnoreCase)
            || origin.Equals($"{http.Request.Scheme}://{http.Request.Host}", StringComparison.OrdinalIgnoreCase);
    }

    private static string? _dummyHash;

    private static void DummyPasswordCheck(UserManager<ApplicationUser> users, string password)
    {
        var dummy = new ApplicationUser { DisplayName = "dummy" };
        _dummyHash ??= users.PasswordHasher.HashPassword(dummy, Guid.NewGuid().ToString());
        users.PasswordHasher.VerifyHashedPassword(dummy, _dummyHash, password);
    }

    private static IResult Problem(int status, string code, Dictionary<string, object?>? extra = null)
    {
        var extensions = new Dictionary<string, object?>(extra ?? []) { ["code"] = code };
        return Results.Problem(statusCode: status, extensions: extensions);
    }
}
