using FixFlow.Api.Infrastructure.Http;

namespace FixFlow.Api.Features.Users;

/// <summary>
/// Managing the users of ONE tenant. The tenant is always a parameter: the Platform Admin passes it from the route
/// (Phase 4), a Supervisor from their token (Phase 5). Every query adds the tenant condition itself, because
/// users are not under the global tenant filter (ADR 0002). A user of another tenant is simply "not found" (404).
/// </summary>
public interface IUserManagementService
{
    /// <summary>Tenant users only (never the Platform Admin). A null tenant searches every tenant (Control Panel); an unknown tenant is 404.</summary>
    Task<Result<PagedResult<UserListItem>>> ListAsync(Guid? tenantId, UserQuery query, CancellationToken cancellationToken);

    Task<Result<UserDetails>> GetAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Creates a tenant user with any tenant role. Rule violations come back as per-field error codes.</summary>
    Task<Result<UserDetails>> CreateAsync(Guid tenantId, CreateUserRequest request, CancellationToken cancellationToken);

    Task<Result<UserDetails>> UpdateAsync(Guid tenantId, Guid userId, UpdateUserRequest request, CancellationToken cancellationToken);

    /// <summary>Refused with 409 user.last_supervisor when it would leave the tenant without an active Supervisor (D18).</summary>
    Task<Result<UserDetails>> ChangeRoleAsync(Guid tenantId, Guid userId, UserRole? role, CancellationToken cancellationToken);

    /// <summary>Deactivate (false) or reactivate (true). Deactivating ends the user's sessions.</summary>
    Task<Result<UserDetails>> SetActiveAsync(Guid tenantId, Guid userId, bool active, CancellationToken cancellationToken);

    /// <summary>Sets a new password without the old one (forgotten password) and ends the user's sessions.</summary>
    Task<Result<UserDetails>> SetPasswordAsync(Guid tenantId, Guid userId, string? password, CancellationToken cancellationToken);

    /// <summary>Clears a lockout immediately and resets the failed-attempt counter.</summary>
    Task<Result<UserDetails>> UnlockAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Soft delete: hidden from tenant lists, cannot sign in, email/username stay reserved.</summary>
    Task<Result<UserDetails>> SoftDeleteAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Platform Admin only: brings a soft-deleted user back as an active user.</summary>
    Task<Result<UserDetails>> RestoreAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
}
