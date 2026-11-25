using FixFlow.Api.Features.Tenants;
using FixFlow.Api.Features.Users;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FixFlow.Api.Infrastructure.Data;

// IdentityUserContext (not IdentityDbContext): users only, no AspNetRoles tables, because a user has one Role column.
public class FixFlowDbContext(DbContextOptions<FixFlowDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    // Case- and accent-insensitive with Arabic-aware sorting. It does NOT normalize Arabic letters or tashkeel. See ADR 0003.
    public const string DatabaseCollation = "Arabic_100_CI_AI";

    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // Identity's tables and indexes
        modelBuilder.UseCollation(DatabaseCollation);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FixFlowDbContext).Assembly);
    }
}
