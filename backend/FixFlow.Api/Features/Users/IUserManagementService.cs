using FixFlow.Api.Infrastructure.Http;

namespace FixFlow.Api.Features.Users;

/// <summary>
/// Managing the users of ONE tenant. The tenant is always a parameter: the Platform Admin passes it from the route
/// (Phase 4), a Supervisor from their token (Phase 5). Every query adds the tenant condition itself, because
/// users are not under the global tenant filter (ADR 0002).
/// </summary>
public interface IUserManagementService
{
    /// <summary>Creates a tenant user with any tenant role. Rule violations come back as per-field error codes.</summary>
    Task<Result<UserDetails>> CreateAsync(Guid tenantId, CreateUserRequest request, CancellationToken cancellationToken);
}
