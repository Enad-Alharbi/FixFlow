using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FixFlow.Api.Features.Users;
using FixFlow.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FixFlow.Api.Tests;

/// <summary>Supervisor user management (CLAUDE.md §7.2, Phase 5): tenant from the token only, guardrails, isolation.</summary>
[Collection("Api")]
public class SupervisorUsersTests(ApiFactory factory)
{
    [Fact]
    public async Task Only_Supervisors_get_in()
    {
        var t = await TestData.CreateTwoTenantsAsync(factory.Services);
        var admin = await TestData.CreateUserAsync(factory.Services, tenantId: null, UserRole.PlatformAdmin);

        foreach (var user in new[] { t.UsersA[UserRole.Requester], t.UsersA[UserRole.Technician], admin })
        {
            var client = await TestData.ClientAsAsync(factory, user);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestData.Client(factory).GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task The_list_is_the_token_tenant_only_and_never_shows_deleted_users()
    {
        var t = await TestData.CreateTwoTenantsAsync(factory.Services);
        var gone = await TestData.CreateUserAsync(factory.Services, t.A.Id, UserRole.Requester);
        await TestData.UpdateUserAsync(factory.Services, gone.Id, u => u.IsDeleted = true);
        var supervisor = await TestData.ClientAsAsync(factory, t.UsersA[UserRole.Supervisor]);
        var mine = t.UsersA.Values.Select(u => u.Id).Order();

        Assert.Equal(mine, Ids(await supervisor.GetFromJsonAsync<JsonElement>("/api/users")).Order());
        // Neither query string can widen the view: the tenant comes from the token, deleted users stay hidden.
        Assert.Equal(mine, Ids(await supervisor.GetFromJsonAsync<JsonElement>($"/api/users?tenantId={t.B.Id}")).Order());
        Assert.Equal(mine, Ids(await supervisor.GetFromJsonAsync<JsonElement>("/api/users?deleted=true")).Order());
    }

    [Fact]
    public async Task Another_tenants_user_is_404_and_untouched()
    {
        var t = await TestData.CreateTwoTenantsAsync(factory.Services);
        var supervisor = await TestData.ClientAsAsync(factory, t.UsersA[UserRole.Supervisor]);
        var userOfB = t.UsersB[UserRole.Supervisor];
        var url = $"/api/users/{userOfB.Id}";

        Assert.Equal(HttpStatusCode.NotFound, (await supervisor.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await supervisor.PutAsJsonAsync(url, new
        {
            displayName = "Hijacked", userName = userOfB.UserName, email = userOfB.Email,
        })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await supervisor.PostAsJsonAsync($"{url}/role", new { role = "Requester" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await supervisor.PostAsync($"{url}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await supervisor.PostAsJsonAsync($"{url}/password", new { password = "Other#Pass2" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await supervisor.PostAsync($"{url}/unlock", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await supervisor.DeleteAsync(url)).StatusCode);

        var stored = await LoadAsync(userOfB.Id);
        Assert.Equal(("Test User", UserRole.Supervisor, true, false), (stored.DisplayName, stored.Role, stored.IsActive, stored.IsDeleted));
        Assert.Equal(HttpStatusCode.OK, (await TestData.SignInAsync(TestData.Client(factory), userOfB.UserName!)).StatusCode);
    }

    [Fact]
    public async Task A_soft_deleted_user_is_not_found_and_cannot_be_restored()
    {
        var tenant = await TestData.CreateTenantAsync(factory.Services);
        var supervisor = await TestData.ClientAsAsync(factory,
            await TestData.CreateUserAsync(factory.Services, tenant.Id, UserRole.Supervisor));
        var target = await TestData.CreateUserAsync(factory.Services, tenant.Id, UserRole.Technician);

        Assert.Equal(HttpStatusCode.OK, (await supervisor.DeleteAsync($"/api/users/{target.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await supervisor.GetAsync($"/api/users/{target.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await supervisor.PostAsync($"/api/users/{target.Id}/reactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await supervisor.PostAsync($"/api/users/{target.Id}/restore", null)).StatusCode);
        Assert.True((await LoadAsync(target.Id)).IsDeleted);
    }

    [Fact]
    public async Task Creates_users_in_the_token_tenant_but_never_a_Platform_Admin()
    {
        var t = await TestData.CreateTwoTenantsAsync(factory.Services);
        var supervisor = await TestData.ClientAsAsync(factory, t.UsersA[UserRole.Supervisor]);
        var name = TestData.Unique("n");

        // A tenantId in the body is not part of the contract and is ignored.
        var created = await supervisor.PostAsJsonAsync("/api/users", new
        {
            tenantId = t.B.Id, displayName = "مشرف ثان", userName = name, email = $"{name}@example.com",
            role = "Supervisor", password = "Fresh#Sup1",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(t.A.Id, (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid());

        var admin = TestData.Unique("n");
        var refused = await supervisor.PostAsJsonAsync("/api/users", new
        {
            displayName = "Nope", userName = admin, email = $"{admin}@example.com", role = "PlatformAdmin", password = "Fresh#Sup1",
        });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var promote = await supervisor.PostAsJsonAsync($"/api/users/{t.UsersA[UserRole.Requester].Id}/role", new { role = "PlatformAdmin" });
        Assert.Equal(HttpStatusCode.BadRequest, promote.StatusCode);
    }

    [Fact]
    public async Task A_Supervisor_cannot_deactivate_or_delete_themselves()
    {
        var tenant = await TestData.CreateTenantAsync(factory.Services);
        var me = await TestData.CreateUserAsync(factory.Services, tenant.Id, UserRole.Supervisor);
        await TestData.CreateUserAsync(factory.Services, tenant.Id, UserRole.Supervisor); // so "last Supervisor" is not the reason
        var client = await TestData.ClientAsAsync(factory, me);

        Assert.Equal("user.self", await CodeAsync(await client.PostAsync($"/api/users/{me.Id}/deactivate", null)));
        Assert.Equal("user.self", await CodeAsync(await client.DeleteAsync($"/api/users/{me.Id}")));
        var stored = await LoadAsync(me.Id);
        Assert.True(stored.IsActive && !stored.IsDeleted);
    }

    [Fact]
    public async Task The_only_Supervisor_cannot_demote_themselves()
    {
        var tenant = await TestData.CreateTenantAsync(factory.Services);
        var me = await TestData.CreateUserAsync(factory.Services, tenant.Id, UserRole.Supervisor);
        var client = await TestData.ClientAsAsync(factory, me);

        var response = await client.PostAsJsonAsync($"/api/users/{me.Id}/role", new { role = "Requester" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("user.last_supervisor", await CodeAsync(response));
    }

    [Fact]
    public async Task Two_Supervisors_demoting_each_other_at_once_leave_exactly_one()
    {
        // Several tenants at once make a real overlap likely; the tenant-row lock must serialize each pair.
        for (var round = 0; round < 5; round++)
        {
            var tenant = await TestData.CreateTenantAsync(factory.Services);
            var first = await TestData.CreateUserAsync(factory.Services, tenant.Id, UserRole.Supervisor);
            var second = await TestData.CreateUserAsync(factory.Services, tenant.Id, UserRole.Supervisor);
            var firstClient = await TestData.ClientAsAsync(factory, first);
            var secondClient = await TestData.ClientAsAsync(factory, second);

            var results = await Task.WhenAll(
                firstClient.PostAsJsonAsync($"/api/users/{second.Id}/role", new { role = "Technician" }),
                secondClient.PostAsJsonAsync($"/api/users/{first.Id}/role", new { role = "Technician" }));

            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], results.Select(r => r.StatusCode).Order());
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FixFlowDbContext>();
            Assert.Equal(1, await db.Users.CountAsync(u => u.TenantId == tenant.Id && u.Role == UserRole.Supervisor && u.IsActive));
        }
    }

    [Fact]
    public async Task Supervisor_actions_are_audited_with_the_Supervisor_as_actor()
    {
        var t = await TestData.CreateTwoTenantsAsync(factory.Services);
        var me = t.UsersA[UserRole.Supervisor];
        var client = await TestData.ClientAsAsync(factory, me);
        var target = t.UsersA[UserRole.Technician];

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/users/{target.Id}/password", new { password = "Reset#Tech9" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TestData.SignInAsync(TestData.Client(factory), target.UserName!, "Reset#Tech9")).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var entry = await scope.ServiceProvider.GetRequiredService<FixFlowDbContext>().PlatformAuditEntries
            .SingleAsync(e => e.TargetId == target.Id && e.Action == "user.password_set");
        Assert.Equal((me.Id, t.A.Id), (entry.ActorId, entry.TenantId));
    }

    private async Task<ApplicationUser> LoadAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FixFlowDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    private static Guid[] Ids(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("id").GetGuid())];

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
}
