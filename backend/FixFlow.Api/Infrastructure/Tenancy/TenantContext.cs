namespace FixFlow.Api.Infrastructure.Tenancy;

/// <summary>Marker for every tenant-owned entity. It gets the "Tenant" query filter and write stamping automatically.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

/// <summary>Who the current tenant is, for one request (or one unit of background work).</summary>
public interface ITenantContext
{
    /// <summary>The current tenant, or null (Platform Admin, anonymous, or nothing set yet).</summary>
    Guid? CurrentTenantId { get; }

    /// <summary>The current tenant. Throws when there is none: tenant-scoped work fails closed, never "all rows".</summary>
    Guid TenantId { get; }

    /// <summary>Sets the tenant for work outside an HTTP request (seeding, background jobs, tests).</summary>
    void Use(Guid tenantId);
}

public class TenantIsolationException(string message) : InvalidOperationException(message);

/// <summary>
/// Scoped. In an HTTP request the tenant comes ONLY from the authenticated token's tenant_id claim —
/// never from a header, query string, body, or route value (CLAUDE.md §9.2).
/// </summary>
public sealed class TenantContext(IHttpContextAccessor http) : ITenantContext
{
    public const string ClaimType = "tenant_id";

    private Guid? _explicit;

    public Guid? CurrentTenantId => _explicit ?? FromToken();

    public Guid TenantId => CurrentTenantId
        ?? throw new TenantIsolationException("Tenant-scoped data was accessed without a current tenant.");

    public void Use(Guid tenantId)
    {
        // One scope = one tenant. Switching mid-scope would mix tenants in the same DbContext.
        if (CurrentTenantId is { } current && current != tenantId)
            throw new TenantIsolationException("The tenant of this scope is already set to a different tenant.");
        _explicit = tenantId;
    }

    private Guid? FromToken()
    {
        var user = http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return null;
        return Guid.TryParse(user.FindFirst(ClaimType)?.Value, out var id) ? id : null;
    }
}
