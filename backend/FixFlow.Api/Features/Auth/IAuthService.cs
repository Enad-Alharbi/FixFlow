using FixFlow.Api.Infrastructure.Http;

namespace FixFlow.Api.Features.Auth;

/// <summary>Sign-in and session rules (CLAUDE.md §7.4, ADR 0005). Knows nothing about cookies or HTTP requests.</summary>
public interface IAuthService
{
    /// <summary>Checks the password (counting failures toward lockout) and account/tenant status, then opens a session.</summary>
    Task<Result<AuthSession>> SignInAsync(SignInRequest request, CancellationToken cancellationToken);

    /// <summary>Spends a refresh token and issues a new one (rotation). Reuse of a spent token revokes the whole session.</summary>
    Task<Result<AuthSession>> RefreshAsync(string? refreshToken, CancellationToken cancellationToken);

    /// <summary>Revokes the session the refresh token belongs to. Unknown or missing tokens are ignored.</summary>
    Task SignOutAsync(string? refreshToken, CancellationToken cancellationToken);

    /// <summary>
    /// Changes the caller's own password: the current one must match (failures count toward lockout), and every
    /// session except the one this refresh token belongs to is signed out.
    /// </summary>
    Task<Result<bool>> ChangePasswordAsync(
        Guid userId, ChangePasswordRequest request, string? currentRefreshToken, CancellationToken cancellationToken);

    /// <summary>The user and tenant behind an access token, or null if the user no longer exists.</summary>
    Task<MeResponse?> GetMeAsync(Guid userId, CancellationToken cancellationToken);
}
