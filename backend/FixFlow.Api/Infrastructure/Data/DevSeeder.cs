using FixFlow.Api.Features.Tenants;
using FixFlow.Api.Features.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FixFlow.Api.Infrastructure.Data;

/// <summary>
/// Development-only demo data: a Platform Admin and two tenants with one user per role, so tenant isolation
/// is visible from the first sign-in. Fake people, reserved ".example" domains — never real data.
/// Safe to run on every startup: anything that already exists is left alone.
/// The shared demo password comes from configuration (SEED_DEMO_PASSWORD in .env); it is never committed.
/// </summary>
public static class DevSeeder
{
    private record DemoUser(string UserName, string DisplayName, UserRole Role);
    private record DemoTenant(string Code, string Name, string Language, string Domain, DemoUser[] Users);

    private static readonly DemoTenant[] Tenants =
    [
        new("acme", "Acme Facilities", "en", "acme.example",
        [
            new("acme.supervisor", "Alice Morgan", UserRole.Supervisor),
            new("acme.tech", "Tom Baker", UserRole.Technician),
            new("acme.requester", "Rita Hayes", UserRole.Requester),
        ]),
        new("nahda", "مدارس النهضة", "ar", "nahda.example",
        [
            new("nahda.supervisor", "نورة العتيبي", UserRole.Supervisor),
            new("nahda.tech", "خالد الحربي", UserRole.Technician),
            new("nahda.requester", "ريم القحطاني", UserRole.Requester),
        ]),
    ];

    public static async Task SeedAsync(IServiceProvider services, string? demoPassword, ILogger logger)
    {
        if (string.IsNullOrEmpty(demoPassword))
        {
            logger.LogWarning("Demo data not seeded: set SEED_DEMO_PASSWORD in .env to create the demo tenants and users.");
            return;
        }

        var db = services.GetRequiredService<FixFlowDbContext>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var now = DateTimeOffset.UtcNow;

        await EnsureUserAsync(users, new ApplicationUser
        {
            UserName = "admin", Email = "admin@fixflow.example", DisplayName = "Platform Admin", Role = UserRole.PlatformAdmin,
        }, demoPassword);

        foreach (var demo in Tenants)
        {
            var tenant = await db.Tenants.SingleOrDefaultAsync(t => t.Code == demo.Code);
            if (tenant is null)
            {
                tenant = new Tenant
                {
                    Code = demo.Code, Name = demo.Name, DefaultLanguage = demo.Language, TimeZone = "Asia/Riyadh",
                    ContactEmail = $"contact@{demo.Domain}", CreatedAt = now, UpdatedAt = now,
                };
                db.Tenants.Add(tenant);
                await db.SaveChangesAsync();
            }

            foreach (var user in demo.Users)
                await EnsureUserAsync(users, new ApplicationUser
                {
                    TenantId = tenant.Id, UserName = user.UserName, Email = $"{user.UserName}@{demo.Domain}",
                    DisplayName = user.DisplayName, Role = user.Role,
                }, demoPassword);
        }

        logger.LogInformation("Demo data ready: admin + {Count} tenants (acme, nahda).", Tenants.Length);
    }

    private static async Task EnsureUserAsync(UserManager<ApplicationUser> users, ApplicationUser user, string password)
    {
        if (await users.FindByNameAsync(user.UserName!) is not null) return;

        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded) // e.g. a SEED_DEMO_PASSWORD that breaks the password rules: fail loudly, don't half-seed silently
            throw new InvalidOperationException(
                $"Seeding '{user.UserName}' failed: {string.Join(", ", result.Errors.Select(e => e.Code).Distinct())}");
    }
}
