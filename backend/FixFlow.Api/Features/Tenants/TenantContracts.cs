using FixFlow.Api.Features.Users;

namespace FixFlow.Api.Features.Tenants;

/// <summary>?search=acme&amp;status=Active&amp;status=Suspended&amp;sort=-createdAt&amp;page=1&amp;pageSize=20</summary>
public record TenantQuery(string? Search, TenantStatus[]? Status, string? Sort, int Page = 1, int PageSize = 20);

public record TenantListItem(
    Guid Id, string Name, string Code, TenantStatus Status, string DefaultLanguage, string TimeZone,
    int UserCount, DateTimeOffset CreatedAt);

public record TenantDetails(
    Guid Id, string Name, string Code, TenantStatus Status, string DefaultLanguage, string TimeZone,
    string? ContactName, string? ContactEmail, string? ContactPhone,
    int UserCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    string RowVersion); // base64 rowversion: send it back when editing; a stale value gets 409 concurrency.conflict

/// <summary>A new tenant and its first Supervisor, created together or not at all.</summary>
public record CreateTenantRequest(
    string? Name, string? Code, string? DefaultLanguage, string? TimeZone,
    string? ContactName, string? ContactEmail, string? ContactPhone,
    CreateUserRequest? FirstSupervisor);

/// <summary>Editable settings. The code is fixed at creation (Phase 4 decision 3).</summary>
public record UpdateTenantRequest(
    string? Name, string? DefaultLanguage, string? TimeZone,
    string? ContactName, string? ContactEmail, string? ContactPhone,
    string? RowVersion);

/// <summary>Unarchive returns the tenant to Active or Suspended, as the admin chooses.</summary>
public record UnarchiveTenantRequest(TenantStatus? Status);
