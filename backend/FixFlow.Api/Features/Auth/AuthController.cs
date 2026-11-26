using FixFlow.Api.Infrastructure.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FixFlow.Api.Features.Auth;

/// <summary>
/// Sign-in, refresh, sign-out, and "who am I" (ADR 0005). HTTP concerns only — the refresh cookie, the Origin
/// check, claims, and ProblemDetails. The rules live in <see cref="IAuthService"/>.
/// The access token goes in the response body (the SPA keeps it in memory);
/// the refresh token goes only in an HttpOnly cookie that JavaScript can't read.
/// </summary>
[Route("api/auth")]
[EnableRateLimiting(RateLimitPolicy)]
public class AuthController(IAuthService auth, IConfiguration configuration) : ApiControllerBase
{
    public const string RefreshCookie = "fixflow_refresh";
    public const string RateLimitPolicy = "auth";
    private const string CookiePath = "/api/auth";

    [HttpPost("sign-in")]
    [AllowAnonymous]
    public async Task<IActionResult> SignIn(SignInRequest request, CancellationToken cancellationToken) =>
        SessionOrProblem(await auth.SignInAsync(request, cancellationToken));

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        if (!IsAllowedOrigin()) return ErrorResult(new ServiceError(StatusCodes.Status403Forbidden, "auth.forbidden"));

        var result = await auth.RefreshAsync(Request.Cookies[RefreshCookie], cancellationToken);
        if (!result.IsSuccess) DeleteRefreshCookie(); // a refused refresh always ends the browser's session
        return SessionOrProblem(result);
    }

    [HttpPost("sign-out")]
    [AllowAnonymous]
    public async Task<IActionResult> SignOut(CancellationToken cancellationToken)
    {
        if (!IsAllowedOrigin()) return ErrorResult(new ServiceError(StatusCodes.Status403Forbidden, "auth.forbidden"));

        await auth.SignOutAsync(Request.Cookies[RefreshCookie], cancellationToken);
        DeleteRefreshCookie();
        return NoContent();
    }

    /// <summary>Requires a valid access token (the fallback policy applies; no [AllowAnonymous]).</summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var me = Guid.TryParse(User.FindFirst(FixFlowClaims.UserId)?.Value, out var userId)
            ? await auth.GetMeAsync(userId, cancellationToken)
            : null;
        return me is null ? ErrorResult(new ServiceError(StatusCodes.Status401Unauthorized, "auth.unauthenticated")) : Ok(me);
    }

    /// <summary>
    /// Here, not under /api/profile, because the refresh cookie is only sent to /api/auth: it tells the service which
    /// session to keep signed in. Rate-limited like sign-in, since it checks a password.
    /// </summary>
    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst(FixFlowClaims.UserId)?.Value, out var userId))
            return ErrorResult(new ServiceError(StatusCodes.Status401Unauthorized, "auth.unauthenticated"));
        var result = await auth.ChangePasswordAsync(userId, request, Request.Cookies[RefreshCookie], cancellationToken);
        return result.IsSuccess ? NoContent() : ErrorResult(result.Error!);
    }

    private IActionResult SessionOrProblem(Result<AuthSession> result)
    {
        if (result.Value is not { } session) return ErrorResult(result.Error!);

        Response.Cookies.Append(RefreshCookie, session.RefreshToken, CookieOptions(session.RefreshTokenExpiresAt));
        return Ok(new SessionResponse(session.AccessToken, session.AccessTokenExpiresAt, session.User));
    }

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

    private void DeleteRefreshCookie() => Response.Cookies.Delete(RefreshCookie, CookieOptions(expires: null));

    /// <summary>Second CSRF layer behind SameSite: a browser request from an origin we don't serve is refused.</summary>
    private bool IsAllowedOrigin()
    {
        var origin = Request.Headers.Origin.ToString();
        if (origin == "") return true; // non-browser clients send none
        var allowed = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        return allowed.Contains(origin, StringComparer.OrdinalIgnoreCase)
            || origin.Equals($"{Request.Scheme}://{Request.Host}", StringComparison.OrdinalIgnoreCase);
    }
}
