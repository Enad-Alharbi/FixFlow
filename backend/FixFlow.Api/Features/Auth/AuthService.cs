using FixFlow.Api.Features.Platform;
using FixFlow.Api.Features.Tenants;
using FixFlow.Api.Features.Users;
using FixFlow.Api.Infrastructure.Data;
using FixFlow.Api.Infrastructure.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FixFlow.Api.Features.Auth;

public class AuthService(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    FixFlowDbContext db,
    TokenService tokens,
    TimeProvider time,
    IPlatformAudit audit,
    ILogger<AuthService> logger) : IAuthService
{
    private static string? _dummyHash;

    public async Task<Result<AuthSession>> SignInAsync(SignInRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrEmpty(request.Password))
            return new ServiceError(StatusCodes.Status400BadRequest, "validation.failed");

        // Usernames can't contain "@" (UserRules), so "@" means the login is an email.
        var login = request.Login.Trim();
        var user = login.Contains('@') ? await users.FindByEmailAsync(login) : await users.FindByNameAsync(login);

        if (user is null || user.IsDeleted)
        {
            // Do the same hashing work as a real check, so response time doesn't reveal whether the account exists.
            DummyPasswordCheck(request.Password);
            return new ServiceError(StatusCodes.Status401Unauthorized, "auth.invalid_credentials");
        }

        // Counts failures and locks after the configured number; while locked it fails even with the right password.
        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            // shortcut: diagnostic log only; the audit log entry for lockouts comes with audit logs (Phase 12).
            logger.LogWarning("Sign-in refused: account {UserId} is locked out", user.Id);
            return new ServiceError(StatusCodes.Status401Unauthorized, "auth.account_locked",
                Extensions: new Dictionary<string, object?> { ["lockoutEnd"] = await users.GetLockoutEndDateAsync(user) });
        }
        if (!result.Succeeded)
            return new ServiceError(StatusCodes.Status401Unauthorized, "auth.invalid_credentials");

        // Status is checked only after the password, so these messages never confirm an account to a guesser.
        var tenant = user.TenantId is { } tenantId
            ? await db.Tenants.SingleAsync(t => t.Id == tenantId, cancellationToken)
            : null;
        if (BlockReason(user, tenant) is { } blocked)
            return new ServiceError(StatusCodes.Status403Forbidden, blocked);

        user.LastSignInAt = time.GetUtcNow();
        var refresh = tokens.AddRefreshToken(user.Id, familyId: Guid.NewGuid());
        await db.SaveChangesAsync(cancellationToken); // saves the refresh token and LastSignInAt together

        return NewSession(user, tenant, refresh);
    }

    public async Task<Result<AuthSession>> RefreshAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
            return SessionExpired();

        var hash = TokenService.Hash(refreshToken);
        var token = await db.RefreshTokens.Include(t => t.User).ThenInclude(u => u.Tenant)
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        var now = time.GetUtcNow();
        if (token is null || token.ExpiresAt <= now)
            return SessionExpired();

        if (token.RevokedAt is not null)
        {
            // A rotated token came back: someone kept a copy. End every session from that sign-in.
            logger.LogWarning("Refresh token reuse detected for user {UserId}; session family revoked", token.UserId);
            await RevokeFamilyAsync(token.FamilyId, now, cancellationToken);
            return SessionExpired();
        }

        if (BlockReason(token.User, token.User.Tenant) is { } blocked)
        {
            await RevokeFamilyAsync(token.FamilyId, now, cancellationToken);
            return token.User.IsDeleted ? SessionExpired() : new ServiceError(StatusCodes.Status403Forbidden, blocked);
        }

        // Rotation: the presented token is spent; a new one from the same family replaces it.
        // shortcut: two simultaneous refreshes with the same token can both rotate; the SPA serializes refreshes
        // across tabs (Web Locks, Phase 3e). Add an optimistic-concurrency check if another client type appears.
        token.RevokedAt = now;
        var refresh = tokens.AddRefreshToken(token.UserId, token.FamilyId);
        await db.SaveChangesAsync(cancellationToken);

        return NewSession(token.User, token.User.Tenant, refresh);
    }

    public async Task SignOutAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(refreshToken)) return;
        var hash = TokenService.Hash(refreshToken);
        var familyId = await db.RefreshTokens.Where(t => t.TokenHash == hash)
            .Select(t => (Guid?)t.FamilyId).SingleOrDefaultAsync(cancellationToken);
        if (familyId is { } id) await RevokeFamilyAsync(id, time.GetUtcNow(), cancellationToken);
    }

    public async Task<MeResponse?> GetMeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.Include(u => u.Tenant).AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return user is null ? null : ToMe(user, user.Tenant);
    }

    public async Task<Result<bool>> ChangePasswordAsync(
        Guid userId, ChangePasswordRequest request, string? currentRefreshToken, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return SessionExpired();

        // Form checks first: a typo in the new password must not cost the user a lockout attempt.
        var errors = new FieldErrors();
        if (string.IsNullOrEmpty(request.CurrentPassword))
            errors.Add("currentPassword", "user.current_password_incorrect");
        if (string.IsNullOrEmpty(request.NewPassword))
            errors.Add("newPassword", "user.password_too_weak");
        else if (request.NewPassword == request.CurrentPassword)
            errors.Add("newPassword", "user.password_same");
        if (request.ConfirmPassword != request.NewPassword)
            errors.Add("confirmPassword", "user.password_mismatch");
        if (errors.Any) return errors.ToError();

        // The same check as sign-in, so a wrong current password counts toward the lockout (CLAUDE.md §7.4).
        // D14: a lock blocks new sign-ins only; this session's access token stays valid.
        var check = await signIn.CheckPasswordSignInAsync(user, request.CurrentPassword!, lockoutOnFailure: true);
        if (check.IsLockedOut)
            return new ServiceError(StatusCodes.Status409Conflict, "auth.account_locked",
                Extensions: new Dictionary<string, object?> { ["lockoutEnd"] = await users.GetLockoutEndDateAsync(user) });
        if (!check.Succeeded)
            return new FieldErrors().Add("currentPassword", "user.current_password_incorrect").ToError();

        // Added first so ChangePasswordAsync's own SaveChanges stores the audit entry with the new hash.
        var entry = audit.Record("user.password_changed", "User", user.Id, user.TenantId);
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword!, request.NewPassword!);
        if (!result.Succeeded)
        {
            db.Entry(entry).State = EntityState.Detached;
            var rules = new FieldErrors();
            foreach (var error in result.Errors) rules.Add("newPassword", error.Code);
            return rules.ToError();
        }

        // Sign out every other session; this browser's session (the refresh cookie's family) stays signed in.
        var hash = currentRefreshToken is null ? null : TokenService.Hash(currentRefreshToken);
        var current = hash is null ? null : await db.RefreshTokens.Where(t => t.TokenHash == hash && t.UserId == user.Id)
            .Select(t => (Guid?)t.FamilyId).SingleOrDefaultAsync(cancellationToken);
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null && t.FamilyId != current)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, time.GetUtcNow()), cancellationToken);
        return true;
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

    private AuthSession NewSession(ApplicationUser user, Tenant? tenant, (string Raw, DateTimeOffset ExpiresAt) refresh)
    {
        var access = tokens.CreateAccessToken(user);
        return new AuthSession(access.Token, access.ExpiresAt, refresh.Raw, refresh.ExpiresAt, ToMe(user, tenant));
    }

    private static MeResponse ToMe(ApplicationUser user, Tenant? tenant) => new(
        user.Id, user.UserName!, user.Email!, user.DisplayName, user.PhoneNumber, user.PreferredLanguage, user.Role,
        tenant is null ? null : new TenantSummary(tenant.Id, tenant.Name, tenant.Code, tenant.DefaultLanguage, tenant.TimeZone));

    private Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);

    private static ServiceError SessionExpired() => new(StatusCodes.Status401Unauthorized, "auth.session_expired");

    private void DummyPasswordCheck(string password)
    {
        var dummy = new ApplicationUser { DisplayName = "dummy" };
        _dummyHash ??= users.PasswordHasher.HashPassword(dummy, Guid.NewGuid().ToString());
        users.PasswordHasher.VerifyHashedPassword(dummy, _dummyHash, password);
    }
}
