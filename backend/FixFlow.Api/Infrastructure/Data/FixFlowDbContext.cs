using Microsoft.EntityFrameworkCore;

namespace FixFlow.Api.Infrastructure.Data;

public class FixFlowDbContext(DbContextOptions<FixFlowDbContext> options) : DbContext(options)
{
    // Case- and accent-insensitive with Arabic-aware sorting. It does NOT normalize Arabic letters or tashkeel. See ADR 0003.
    public const string DatabaseCollation = "Arabic_100_CI_AI";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation(DatabaseCollation);
    }
}
