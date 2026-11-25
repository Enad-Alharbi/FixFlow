using FixFlow.Api.Infrastructure.Http;

namespace FixFlow.Api.Features.Tenants;

/// <summary>Platform-plane tenant management (CLAUDE.md §9.4, §10.1). Every change writes a platform audit entry.</summary>
public interface ITenantService
{
    Task<PagedResult<TenantListItem>> ListAsync(TenantQuery query, CancellationToken cancellationToken);
    Task<Result<TenantDetails>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Creates the tenant and its first Supervisor in one transaction: if either is rejected, neither exists.</summary>
    Task<Result<TenantDetails>> CreateAsync(CreateTenantRequest request, CancellationToken cancellationToken);

    Task<Result<TenantDetails>> UpdateAsync(Guid id, UpdateTenantRequest request, CancellationToken cancellationToken);

    /// <summary>suspend, reactivate, archive, unarchive — allowed only from the statuses in the transition table.</summary>
    Task<Result<TenantDetails>> ChangeStatusAsync(Guid id, TenantStatusAction action, TenantStatus? unarchiveTo, CancellationToken cancellationToken);
}

public enum TenantStatusAction { Suspend, Reactivate, Archive, Unarchive }
