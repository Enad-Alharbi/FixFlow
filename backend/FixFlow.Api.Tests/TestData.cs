using System.Net.Http.Json;
using FixFlow.Api.Features.Auth;
using FixFlow.Api.Features.Tenants;
using FixFlow.Api.Features.Users;
using FixFlow.Api.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FixFlow.Api.Tests;

/// <summary>Tenant A and Tenant B, each with one user per role: the reusable isolation fixture (CLAUDE.md §9.6).</summary>
public record TwoTenants(Tenant A, Tenant B, IReadOnlyDictionary<UserRole, ApplicationUser> UsersA, IReadOnlyDictionary<UserRole, ApplicationUser> UsersB);

/// <summary>Creates real rows through the app's own services, with unique names so tests never collide.</summary>
public static class TestData
{
    public const string Password = "Valid#Pass1";

    private static readonly UserRole[] TenantRoles = [UserRole.Requester, UserRole.Technician, UserRole.Supervisor];

    /// <summary>HTTPS (the refresh cookie is Secure) and manual cookies, so tests control exactly what is sent.</summary>
    public static HttpClient Client(WebApplicationFactory<Program> factory) => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = false,
    });

    public static async Task<TwoTenants> CreateTwoTenantsAsync(IServiceProvider services)
    {
        var a = await CreateTenantAsync(services);
        var b = await CreateTenantAsync(services);
        var usersA = new Dictionary<UserRole, ApplicationUser>();
        var usersB = new Dictionary<UserRole, ApplicationUser>();
        foreach (var role in TenantRoles)
        {
            usersA[role] = await CreateUserAsync(services, a.Id, role);
            usersB[role] = await CreateUserAsync(services, b.Id, role);
        }
        return new TwoTenants(a, b, usersA, usersB);
    }

    public static async Task<Tenant> CreateTenantAsync(IServiceProvider services, TenantStatus status = TenantStatus.Active)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FixFlowDbContext>();
        var code = "t" + Guid.NewGuid().ToString("N")[..12];
        var tenant = new Tenant { Name = code, Code = code, Status = status, DefaultLanguage = "ar", TimeZone = "Asia/Riyadh" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    public static async Task<ApplicationUser> CreateUserAsync(IServiceProvider services, Guid? tenantId, UserRole role)
    {
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var name = "u" + Guid.NewGuid().ToString("N")[..12];
        var user = new ApplicationUser { TenantId = tenantId, Role = role, UserName = name, Email = $"{name}@example.com", DisplayName = "Test User" };
        var result = await users.CreateAsync(user, Password);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Code)));
        return user;
    }

    /// <summary>Changes a stored user (deactivate, delete, …) in its own scope, the way an admin action would.</summary>
    public static async Task UpdateUserAsync(IServiceProvider services, Guid userId, Action<ApplicationUser> change)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FixFlowDbContext>();
        change(await db.Users.FindAsync(userId) ?? throw new InvalidOperationException("No such user"));
        await db.SaveChangesAsync();
    }

    public static async Task SetTenantStatusAsync(IServiceProvider services, Guid tenantId, TenantStatus status)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FixFlowDbContext>();
        (await db.Tenants.FindAsync(tenantId))!.Status = status;
        await db.SaveChangesAsync();
    }

    public static Task<HttpResponseMessage> SignInAsync(HttpClient client, string login, string password = Password) =>
        client.PostAsJsonAsync("/api/auth/sign-in", new SignInRequest(login, password));
}
