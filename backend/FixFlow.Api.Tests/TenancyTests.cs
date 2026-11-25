using System.Security.Claims;
using FixFlow.Api.Features.Tenants;
using FixFlow.Api.Infrastructure.Data;
using FixFlow.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FixFlow.Api.Tests;

/// <summary>A tenant-owned entity that exists only in tests: no real one exists until Categories (Phase 6).</summary>
public class Probe : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
}

/// <summary>The production FixFlowDbContext (same filters, same interceptor) plus the Probe entity.</summary>
public class ProbeDbContext(DbContextOptions<ProbeDbContext> options, ITenantContext tenant) : FixFlowDbContext(options, tenant)
{
    public DbSet<Probe> Probes => Set<Probe>();
}

/// <summary>
/// The tenancy enforcement layers (CLAUDE.md §9.3): fail closed, query filter, write stamping, explicit bypass.
/// Runs against real SQL Server in a separate database, created from the model (EnsureCreated) because the
/// Probe table is not part of the app's migrations.
/// </summary>
[Collection("Api")]
public class TenancyTests(ApiFactory factory) : IAsyncLifetime
{
    private Guid _tenantA;
    private Guid _tenantB;

    public async Task InitializeAsync()
    {
        await using var db = NewContext(out _);
        await db.Database.EnsureCreatedAsync();

        // Two fresh tenants per test, each with one row.
        _tenantA = await NewTenantAsync(db);
        _tenantB = await NewTenantAsync(db);
        foreach (var tenantId in new[] { _tenantA, _tenantB })
        {
            await using var scoped = NewContext(out var tenant);
            tenant.Use(tenantId);
            scoped.Probes.Add(new Probe { Name = $"probe of {tenantId}" });
            await scoped.SaveChangesAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Query_without_a_tenant_fails_closed()
    {
        await using var db = NewContext(out _);

        await Assert.ThrowsAsync<TenantIsolationException>(() => db.Probes.ToListAsync());
    }

    [Fact]
    public async Task Each_tenant_sees_only_its_own_rows()
    {
        await using var db = NewContext(out var tenant);
        tenant.Use(_tenantA);

        var rows = await db.Probes.ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.Equal(_tenantA, row.TenantId));
    }

    [Fact]
    public async Task Another_tenants_row_resolves_to_not_found()
    {
        Guid foreignId;
        await using (var asB = NewContext(out var tenantB))
        {
            tenantB.Use(_tenantB);
            foreignId = (await asB.Probes.FirstAsync()).Id;
        }

        await using var asA = NewContext(out var tenantA);
        tenantA.Use(_tenantA);

        Assert.Null(await asA.Probes.FirstOrDefaultAsync(p => p.Id == foreignId));
    }

    [Fact]
    public async Task New_rows_are_stamped_with_the_current_tenant()
    {
        await using var db = NewContext(out var tenant);
        tenant.Use(_tenantA);
        var probe = new Probe { Name = "new" };

        db.Probes.Add(probe);
        await db.SaveChangesAsync();

        Assert.Equal(_tenantA, probe.TenantId);
    }

    [Fact]
    public async Task Adding_a_row_for_another_tenant_throws()
    {
        await using var db = NewContext(out var tenant);
        tenant.Use(_tenantA);

        db.Probes.Add(new Probe { Name = "smuggled", TenantId = _tenantB });

        await Assert.ThrowsAsync<TenantIsolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Saving_without_a_tenant_throws()
    {
        await using var db = NewContext(out _);

        db.Probes.Add(new Probe { Name = "orphan" });

        await Assert.ThrowsAsync<TenantIsolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Moving_a_row_to_another_tenant_throws()
    {
        await using var db = NewContext(out var tenant);
        tenant.Use(_tenantA);
        var probe = await db.Probes.FirstAsync();

        probe.TenantId = _tenantB;

        await Assert.ThrowsAsync<TenantIsolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Changing_a_row_of_another_tenant_loaded_through_the_bypass_throws()
    {
        await using var db = NewContext(out var tenant);
        tenant.Use(_tenantA);
        var foreign = await db.Probes.IgnoreQueryFilters([FixFlowDbContext.TenantFilter])
            .FirstAsync(p => p.TenantId == _tenantB);

        foreign.Name = "tampered";

        await Assert.ThrowsAsync<TenantIsolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Explicit_bypass_reads_across_tenants_without_a_tenant()
    {
        await using var db = NewContext(out _);

        var tenantIds = await db.Probes.IgnoreQueryFilters([FixFlowDbContext.TenantFilter])
            .Where(p => p.TenantId == _tenantA || p.TenantId == _tenantB)
            .Select(p => p.TenantId).Distinct().ToListAsync();

        Assert.Equal(2, tenantIds.Count);
    }

    [Fact]
    public void Tenant_comes_from_the_token_claim()
    {
        var tenantId = Guid.NewGuid();
        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(TenantContext.ClaimType, tenantId.ToString())], "test")),
            },
        };

        Assert.Equal(tenantId, new TenantContext(http).TenantId);
    }

    [Fact]
    public void Unauthenticated_request_has_no_tenant_even_with_a_claim()
    {
        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                // No authentication type = not authenticated.
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(TenantContext.ClaimType, Guid.NewGuid().ToString())])),
            },
        };

        Assert.Null(new TenantContext(http).CurrentTenantId);
    }

    [Fact]
    public void A_scope_cannot_switch_to_another_tenant()
    {
        var tenant = new TenantContext(new HttpContextAccessor());
        tenant.Use(_tenantA);

        Assert.Throws<TenantIsolationException>(() => tenant.Use(_tenantB));
    }

    private ProbeDbContext NewContext(out TenantContext tenant)
    {
        tenant = new TenantContext(new HttpContextAccessor());
        var connectionString = new SqlConnectionStringBuilder(factory.ConnectionString) { InitialCatalog = "FixFlow_TenancyTests" }.ConnectionString;
        var options = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseSqlServer(connectionString)
            .AddInterceptors(new TenantStampingInterceptor(tenant))
            .Options;
        return new ProbeDbContext(options, tenant);
    }

    private static async Task<Guid> NewTenantAsync(FixFlowDbContext db)
    {
        var code = "t" + Guid.NewGuid().ToString("N")[..12];
        var row = new Tenant { Name = code, Code = code, DefaultLanguage = "en", TimeZone = "Asia/Riyadh" };
        db.Tenants.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }
}
