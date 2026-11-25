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
[ApiController]
[Route("api/auth")]
[EnableRateLimiting(RateLimitPolicy)]
public class AuthController(IAuthService auth, IConfiguration configuration) : ControllerBase
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
        if (!IsAllowedOrigin()) return CodedProblem(new AuthError(StatusCodes.Status403Forbidden, "auth.forbidden"));

        var result = await auth.RefreshAsync(Request.Cookies[RefreshCookie], cancellationToken);
        if (result.Error is not null) DeleteRefreshCookie(); // a refused refresh always ends the browser's session
        return SessionOrProblem(result);
    }

    [HttpPost("sign-out")]
    [AllowAnonymous]
    public async Task<IActionResult> SignOut(CancellationToken cancellationToken)
    {
        if (!IsAllowedOrigin()) return CodedProblem(new AuthError(StatusCodes.Status403Forbidden, "auth.forbidden"));

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
        return me is null ? CodedProblem(new AuthError(StatusCodes.Status401Unauthorized, "auth.unauthenticated")) : Ok(me);
    }

    private IActionResult SessionOrProblem(AuthResult result)
    {
        if (result.Session is not { } session) return CodedProblem(result.Error!);

        Response.Cookies.Append(RefreshCookie, session.RefreshToken, CookieOptions(session.RefreshTokenExpiresAt));
        return Ok(new SessionResponse(session.AccessToken, session.AccessTokenExpiresAt, session.User));
    }

    /// <summary>RFC 9457 ProblemDetails with the stable `code` (and any extra fields such as lockoutEnd).</summary>
    private ObjectResult CodedProblem(AuthError error)
    {
        // The factory already applies the global CustomizeProblemDetails (traceId and a default code by status);
        // the indexer then replaces that default with the specific code. (Problem(extensions:) would Add a duplicate key.)
        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, statusCode: error.Status);
        problem.Extensions["code"] = error.Code;
        foreach (var (key, value) in error.Extensions ?? new Dictionary<string, object?>())
            problem.Extensions[key] = value;
        return new ObjectResult(problem) { StatusCode = error.Status, ContentTypes = { "application/problem+json" } };
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
