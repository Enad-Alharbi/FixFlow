using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FixFlow.Api.Features.Auth;
using FixFlow.Api.Features.Tenants;
using FixFlow.Api.Features.Users;
using Microsoft.Extensions.DependencyInjection;

namespace FixFlow.Api.Tests;

/// <summary>D17: an access token that is still valid on paper stops working once the user or tenant is blocked.</summary>
[Collection("Api")]
public class AccessStatusTests(ApiFactory factory) : IAsyncLifetime
{
    private HttpClient _admin = null!;

    public async Task InitializeAsync() =>
        _admin = await TestData.ClientAsAsync(factory, await TestData.CreateUserAsync(factory.Services, null, UserRole.PlatformAdmin));

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Deactivating_a_user_stops_their_current_token_at_once_and_reactivating_restores_it()
    {
        var (tenant, user, client) = await SignedInUserAsync(UserRole.Technician);
        var url = $"/api/platform/tenants/{tenant.Id}/users/{user.Id}";

        await _admin.PostAsync($"{url}/deactivate", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        await _admin.PostAsync($"{url}/reactivate", null);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Soft_deleting_a_user_stops_their_current_token()
    {
        var (tenant, user, client) = await SignedInUserAsync(UserRole.Requester);
        await _admin.DeleteAsync($"/api/platform/tenants/{tenant.Id}/users/{user.Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Suspending_the_tenant_stops_its_users_tokens_and_reactivating_restores_them()
    {
        var (tenant, _, client) = await SignedInUserAsync(UserRole.Requester);

        await _admin.PostAsync($"/api/platform/tenants/{tenant.Id}/suspend", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        await _admin.PostAsync($"/api/platform/tenants/{tenant.Id}/reactivate", null);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task A_role_change_retires_the_token_that_carries_the_old_role()
    {
        var (tenant, user, client) = await SignedInUserAsync(UserRole.Requester);
        await _admin.PostAsJsonAsync($"/api/platform/tenants/{tenant.Id}/users/{user.Id}/role", new { role = "Technician" });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task State_is_cached_per_user_until_it_expires_or_is_forgotten()
    {
        var (_, user, client) = await SignedInUserAsync(UserRole.Requester);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode); // state now cached

        // A change made behind the service's back (no Forget) is not seen until the ~30 s entry expires...
        await TestData.UpdateUserAsync(factory.Services, user.Id, u => u.IsActive = false);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        // ...which is why every admin action calls Forget.
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IAccessStatus>().Forget(user.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    private async Task<(Tenant Tenant, ApplicationUser User, HttpClient Client)> SignedInUserAsync(UserRole role)
    {
        var tenant = await TestData.CreateTenantAsync(factory.Services);
        await TestData.CreateUserAsync(factory.Services, tenant.Id, UserRole.Supervisor); // keeps D18 out of the way
        var user = await TestData.CreateUserAsync(factory.Services, tenant.Id, role);
        var client = await TestData.ClientAsAsync(factory, user);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        return (tenant, user, client);
    }
}

/// <summary>The basic platform dashboard: real counts and recent activity with readable names.</summary>
[Collection("Api")]
public class PlatformDashboardTests(ApiFactory factory)
{
    [Fact]
    public async Task Counts_follow_real_changes_and_recent_activity_names_actor_tenant_and_target()
    {
        var adminUser = await TestData.CreateUserAsync(factory.Services, null, UserRole.PlatformAdmin);
        var admin = await TestData.ClientAsAsync(factory, adminUser);
        var before = await admin.GetFromJsonAsync<JsonElement>("/api/platform/dashboard");

        var tenant = await TestData.CreateTenantAsync(factory.Services);
        await TestData.CreateUserAsync(factory.Services, tenant.Id, UserRole.Supervisor);
        var tech = await TestData.CreateUserAsync(factory.Services, tenant.Id, UserRole.Technician);
        await admin.PostAsync($"/api/platform/tenants/{tenant.Id}/users/{tech.Id}/deactivate", null);
        await admin.PostAsync($"/api/platform/tenants/{tenant.Id}/suspend", null);

        var after = await admin.GetFromJsonAsync<JsonElement>("/api/platform/dashboard");
        Assert.Equal(1, Count(after, "tenants", "suspended") - Count(before, "tenants", "suspended"));
        Assert.Equal(1, Count(after, "users", "active") - Count(before, "users", "active"));
        Assert.Equal(1, Count(after, "users", "deactivated") - Count(before, "users", "deactivated"));

        var latest = after.GetProperty("recentActivity").EnumerateArray().Take(2).ToArray();
        Assert.Equal(["tenant.suspended", "user.deactivated"], latest.Select(e => e.GetProperty("action").GetString()));
        Assert.All(latest, e => Assert.Equal(adminUser.DisplayName, e.GetProperty("actorName").GetString()));
        Assert.All(latest, e => Assert.Equal(tenant.Name, e.GetProperty("tenantName").GetString()));
        Assert.Equal(tenant.Name, latest[0].GetProperty("targetName").GetString());
        Assert.Equal(tech.DisplayName, latest[1].GetProperty("targetName").GetString());
    }

    [Fact]
    public async Task Tenant_users_cannot_see_it()
    {
        var t = await TestData.CreateTwoTenantsAsync(factory.Services);
        var supervisor = await TestData.ClientAsAsync(factory, t.UsersA[UserRole.Supervisor]);
        Assert.Equal(HttpStatusCode.Forbidden, (await supervisor.GetAsync("/api/platform/dashboard")).StatusCode);
    }

    private static int Count(JsonElement dashboard, string group, string name) =>
        dashboard.GetProperty(group).GetProperty(name).GetInt32();
}
