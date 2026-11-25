using FixFlow.Api.Infrastructure.Data;
using FixFlow.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace FixFlow.Api.Infrastructure.Startup;

public static class PersistenceExtensions
{
    /// <summary>SQL Server through EF Core, plus the tenancy services every DbContext depends on (ADR 0002).</summary>
    public static IServiceCollection AddFixFlowPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        // The connection string comes from configuration (in Docker: the ConnectionStrings__Default env var).
        // Not validated here so `dotnet ef migrations add` works without a database; a missing value fails on first use.
        services.AddDbContext<FixFlowDbContext>((provider, options) => options
            .UseSqlServer(configuration.GetConnectionString("Default"), sql => sql.EnableRetryOnFailure())
            .AddInterceptors(provider.GetRequiredService<TenantStampingInterceptor>()));

        // Tenancy: the current tenant comes only from the token (see TenantContext).
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<TenantStampingInterceptor>();

        return services;
    }

    /// <summary>
    /// Development only: apply pending migrations and seed the demo data at startup.
    /// Production uses a deliberate migration step. Does nothing in other environments.
    /// </summary>
    public static async Task InitializeDevelopmentDatabaseAsync(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;

        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FixFlowDbContext>().Database.MigrateAsync();
        // Demo tenants and users (skipped when Seed:DemoPassword is not set).
        await DevSeeder.SeedAsync(scope.ServiceProvider, app.Configuration["Seed:DemoPassword"],
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FixFlow.Seed"));
    }
}
