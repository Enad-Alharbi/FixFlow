using FixFlow.Api.Features.Users;

namespace FixFlow.Api.Features.Auth;

// ---- API contracts (JSON in and out) ----

public record SignInRequest(string? Login, string? Password);
public record TenantSummary(Guid Id, string Name, string Code, string DefaultLanguage, string TimeZone);
public record MeResponse(Guid Id, string UserName, string Email, string DisplayName, UserRole Role, TenantSummary? Tenant);
/// <summary>Response body of sign-in and refresh. The refresh token is NOT here: it travels only in the HttpOnly cookie.</summary>
public record SessionResponse(string AccessToken, DateTimeOffset ExpiresAt, MeResponse User);

// ---- Service results (between AuthService and AuthController) ----

/// <summary>A new session: the access token for the body, the refresh token for the cookie.</summary>
public record AuthSession(
    string AccessToken, DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken, DateTimeOffset RefreshTokenExpiresAt,
    MeResponse User);

/// <summary>A refused auth operation: a stable error code, the HTTP status to answer with, and optional extra fields.</summary>
public record AuthError(int Status, string Code, IReadOnlyDictionary<string, object?>? Extensions = null);

/// <summary>Either a session or an error — expected failures (wrong password, locked) are results, not exceptions.</summary>
public record AuthResult(AuthSession? Session, AuthError? Error)
{
    public static AuthResult Ok(AuthSession session) => new(session, null);
    public static AuthResult Fail(int status, string code, IReadOnlyDictionary<string, object?>? extensions = null) =>
        new(null, new AuthError(status, code, extensions));
}
