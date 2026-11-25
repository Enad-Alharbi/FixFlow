using FixFlow.Api.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FixFlow.Api.Features.Users;

/// <summary>
/// Server-side Platform Admin commands (CLAUDE.md §7.4, §10.2). They run from a shell on the server, never over HTTP:
///   FixFlow.Api admin bootstrap   create the FIRST Platform Admin
///   FixFlow.Api admin recover     unlock a Platform Admin, and set a new password if one is given
/// Values come from environment variables, so no password lands in the command line or shell history:
///   FIXFLOW_ADMIN_USERNAME, FIXFLOW_ADMIN_EMAIL, FIXFLOW_ADMIN_DISPLAYNAME, FIXFLOW_ADMIN_PASSWORD
/// </summary>
public static class AdminCommands
{
    public static bool IsAdminCommand(string[] args) => args is ["admin", ..];

    /// <returns>Process exit code: 0 success, 1 refused or failed.</returns>
    public static async Task<int> RunAsync(IServiceProvider services, IConfiguration config, string[] args, TextWriter output)
    {
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<FixFlowDbContext>();

        return args switch
        {
            ["admin", "bootstrap"] => await BootstrapAsync(users, db, config, output),
            ["admin", "recover"] => await RecoverAsync(users, db, config, output),
            _ => Fail(output, "Usage: admin bootstrap | admin recover"),
        };
    }

    private static async Task<int> BootstrapAsync(UserManager<ApplicationUser> users, FixFlowDbContext db, IConfiguration config, TextWriter output)
    {
        if (await db.Users.AnyAsync(u => u.Role == UserRole.PlatformAdmin))
            return Fail(output, "A Platform Admin already exists. Use 'admin recover' to regain access.");

        if (Missing(config, "FIXFLOW_ADMIN_USERNAME", "FIXFLOW_ADMIN_EMAIL", "FIXFLOW_ADMIN_DISPLAYNAME", "FIXFLOW_ADMIN_PASSWORD") is { } missing)
            return Fail(output, $"Set {missing}.");

        var admin = new ApplicationUser
        {
            TenantId = null,
            Role = UserRole.PlatformAdmin,
            UserName = config["FIXFLOW_ADMIN_USERNAME"]!.Trim(),
            Email = config["FIXFLOW_ADMIN_EMAIL"]!.Trim(),
            DisplayName = config["FIXFLOW_ADMIN_DISPLAYNAME"]!.Trim(),
        };
        // Same rules as every other account: the password and username validators run here too.
        var result = await users.CreateAsync(admin, config["FIXFLOW_ADMIN_PASSWORD"]!);
        if (!result.Succeeded)
            return Fail(output, "Refused: " + string.Join(", ", result.Errors.Select(e => e.Code).Distinct()));

        output.WriteLine($"Platform Admin '{admin.UserName}' created.");
        return 0;
    }

    private static async Task<int> RecoverAsync(UserManager<ApplicationUser> users, FixFlowDbContext db, IConfiguration config, TextWriter output)
    {
        if (Missing(config, "FIXFLOW_ADMIN_USERNAME") is { } missing)
            return Fail(output, $"Set {missing}.");

        var admin = await users.FindByNameAsync(config["FIXFLOW_ADMIN_USERNAME"]!.Trim());
        if (admin is not { Role: UserRole.PlatformAdmin })
            return Fail(output, "No Platform Admin with that username."); // recovery never touches tenant users

        if (config["FIXFLOW_ADMIN_PASSWORD"] is { Length: > 0 } newPassword)
        {
            // Validate first, so a rejected password never leaves the account without one.
            var errors = new List<string>();
            foreach (var validator in users.PasswordValidators)
                errors.AddRange((await validator.ValidateAsync(users, admin, newPassword)).Errors.Select(e => e.Code));
            if (errors.Count > 0)
                return Fail(output, "Refused: " + string.Join(", ", errors.Distinct()));

            await users.RemovePasswordAsync(admin);
            await users.AddPasswordAsync(admin, newPassword);
            // A new password ends every existing session.
            await db.RefreshTokens.Where(t => t.UserId == admin.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow));
            output.WriteLine("New password set; all sessions signed out.");
        }

        await users.SetLockoutEndDateAsync(admin, null);
        await users.ResetAccessFailedCountAsync(admin);
        output.WriteLine($"Platform Admin '{admin.UserName}' unlocked.");
        return 0;
    }

    private static string? Missing(IConfiguration config, params string[] keys) =>
        keys.FirstOrDefault(k => string.IsNullOrWhiteSpace(config[k]));

    private static int Fail(TextWriter output, string message)
    {
        output.WriteLine(message);
        return 1;
    }
}
