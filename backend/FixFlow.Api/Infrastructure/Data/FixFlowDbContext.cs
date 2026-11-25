using System.Reflection;
using FixFlow.Api.Features.Auth;
using FixFlow.Api.Features.Tenants;
using FixFlow.Api.Features.Users;
using FixFlow.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FixFlow.Api.Infrastructure.Data;

// IdentityUserContext (not IdentityDbContext): users only, no AspNetRoles tables, because a user has one Role column.
public class FixFlowDbContext : IdentityUserContext<ApplicationUser, Guid>
{
    // Case- and accent-insensitive with Arabic-aware sorting. It does NOT normalize Arabic letters or tashkeel. See ADR 0003.
    public const string DatabaseCollation = "Arabic_100_CI_AI";

    /// <summary>Name of the query filter on every ITenantOwned entity. Bypass only in platform services: IgnoreQueryFilters([TenantFilter]).</summary>
    public const string TenantFilter = "Tenant";

    private readonly ITenantContext _tenant;

    public FixFlowDbContext(DbContextOptions<FixFlowDbContext> options, ITenantContext tenant) : this((DbContextOptions)options, tenant) { }

    // For derived contexts (the test project adds a test-only tenant-owned entity).
    protected FixFlowDbContext(DbContextOptions options, ITenantContext tenant) : base(options) => _tenant = tenant;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // EF reads this per query (as a SQL parameter), so one cached model serves every tenant.
    // Throws when there is no tenant, so a filtered query can never silently return all tenants' rows.
    private Guid CurrentTenantId => _tenant.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // Identity's tables and indexes
        modelBuilder.UseCollation(DatabaseCollation);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FixFlowDbContext).Assembly);

        // Central, not per entity: a new ITenantOwned entity is filtered without anyone remembering to add it.
        var applyTenantFilter = typeof(FixFlowDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => t.BaseType is null && typeof(ITenantOwned).IsAssignableFrom(t.ClrType)))
            applyTenantFilter.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
    }

    private void ApplyTenantFilter<T>(ModelBuilder modelBuilder) where T : class, ITenantOwned
    {
        modelBuilder.Entity<T>().HasQueryFilter(TenantFilter, e => e.TenantId == CurrentTenantId);
        // A real foreign key (EF also indexes it). Restrict: tenant deletion is its own deliberate operation (Phase 12c).
        modelBuilder.Entity<T>().HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
