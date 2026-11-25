using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FixFlow.Api.Features.Users;
using FixFlow.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FixFlow.Api.Tests;

/// <summary>Control Panel user management (CLAUDE.md §7.4, §10.1): isolation, guardrails (D18), lifecycle, audit.</summary>
[Collection("Api")]
public class PlatformUsersTests(ApiFactory factory) : IAsyncLifetime
{
    private HttpClient _admin = null!;
    private ApplicationUser _adminUser = null!;

    public async Task InitializeAsync()
    {
        _adminUser = await TestData.CreateUserAsync(factory.Services, tenantId: null, UserRole.PlatformAdmin);
        _admin = await TestData.ClientAsAsync(factory, _adminUser);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string UserUrl(Guid tenantId, Guid userId, string action = "") =>
        $"/api/platform/tenants/{tenantId}/users/{userId}{action}";

    // ---- Access and isolation ----

    [Fact]
    public async Task Tenant_users_get_403_and_anonymous_gets_401()
    {
        var t = await TestData.CreateTwoTenantsAsync(factory.Services);
        var target = UserUrl(t.A.Id, t.UsersA[UserRole.Requester].Id);

        foreach (var user in t.UsersA.Values)
        {
            var client = await TestData.ClientAsAsync(factory, user);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/platform/users")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/platform/tenants/{t.A.Id}/users")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"{target}/deactivate", null)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestData.Client(factory).GetAsync("/api/platform/users")).StatusCode);
    }

    [Fact]
    public async Task A_user_addressed_through_another_tenant_is_404_and_untouched()
    {
        var t = await TestData.CreateTwoTenantsAsync(factory.Services);
        var userOfB = t.UsersB[UserRole.Technician];
        var wrong = UserUrl(t.A.Id, userOfB.Id); // tenant A in the route, tenant B's user id

        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync(wrong)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.PutAsJsonAsync(wrong, Edit("Hijacked", userOfB))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.PostAsJsonAsync($"{wrong}/role", new { role = "Supervisor" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.PostAsync($"{wrong}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.PostAsJsonAsync($"{wrong}/password", new { password = "Other#Pass2" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync(wrong)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/api/platform/tenants/{Guid.NewGuid()}/users")).StatusCode);

        var stored = await LoadAsync(userOfB.Id);
        Assert.Equal(("Test User", UserRole.Technician, true, false), (stored.DisplayName, stored.Role, stored.IsActive, stored.IsDeleted));
        Assert.Equal(HttpStatusCode.OK, (await TestData.SignInAsync(TestData.Client(factory), userOfB.UserName!)).StatusCode);
    }

    // ---- Lists ----

    [Fact]
    public async Task Tenant_list_shows_only_that_tenant_and_hides_deleted_users()
    {
        var t = await TestData.CreateTwoTenantsAsync(factory.Services);
        var gone = await TestData.CreateUserAsync(factory.Services, t.A.Id, UserRole.Requester);
        await TestData.UpdateUserAsync(factory.Services, gone.Id, u => u.IsDeleted = true);

        var page = await _admin.GetFromJsonAsync<JsonElement>($"/api/platform/tenants/{t.A.Id}/users");
        Assert.Equal(t.UsersA.Values.Select(u => u.Id).Order(), Ids(page).Order());
        Assert.Equal(3, page.GetProperty("totalCount").GetInt32());

        var technicians = await _admin.GetFromJsonAsync<JsonElement>($"/api/platform/tenants/{t.A.Id}/users?role=Technician");
        Assert.Equal([t.UsersA[UserRole.Technician].Id], Ids(technicians));

        var deleted = await _admin.GetFromJsonAsync<JsonElement>($"/api/platform/tenants/{t.A.Id}/users?deleted=true");
        Assert.Equal([gone.Id], Ids(deleted));
    }

    [Fact]
    public async Task Global_search_finds_users_in_any_tenant_with_their_tenant_and_never_the_admin()
    {
        var t = await TestData.CreateTwoTenantsAsync(factory.Services);
        var userOfB = t.UsersB[UserRole.Requester];

        var found = await _admin.GetFromJsonAsync<JsonElement>($"/api/platform/users?search={userOfB.UserName!.ToUpperInvariant()}");
        var row = Assert.Single(found.GetProperty("items").EnumerateArray());
        Assert.Equal(userOfB.Id, row.GetProperty("id").GetGuid());
        Assert.Equal(t.B.Code, row.GetProperty("tenantCode").GetString());

        var admin = await _admin.GetFromJsonAsync<JsonElement>($"/api/platform/users?search={_adminUser.UserName}");
        Assert.Equal(0, admin.GetProperty("totalCount").GetInt32());

        var byTenant = await _admin.GetFromJsonAsync<JsonElement>($"/api/platform/users?tenantId={t.B.Id}");
        Assert.Equal(t.UsersB.Values.Select(u => u.Id).Order(), Ids(byTenant).Order());
    }

    // ---- Create and edit ----

    [Fact]
    public async Task Creates_a_user_in_the_route_tenant()
    {
        var tenant = await TestData.CreateTenantAsync(factory.Services);
        var name = TestData.Unique("n");

        var response = await _admin.PostAsJsonAsync($"/api/platform/tenants/{tenant.Id}/users",
            new { displayName = "فني جديد", userName = name, email = $"{name}@example.com", role = "Technician", password = "Fresh#Tech1" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(tenant.Id, user.GetProperty("tenantId").GetGuid());
        Assert.EndsWith(UserUrl(tenant.Id, user.GetProperty("id").GetGuid()), response.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await TestData.SignInAsync(TestData.Client(factory), name, "Fresh#Tech1")).StatusCode);
    }

    [Fact]
    public async Task Edits_identity_fields_with_the_same_rules_as_create_and_audits_before_and_after()
    {
        var t = await TestData.CreateTwoTenantsAsync(factory.Services);
        var user = t.UsersA[UserRole.Requester];
        var newName = TestData.Unique("e");

        var taken = await _admin.PutAsJsonAsync(UserUrl(t.A.Id, user.Id),
            new { displayName = "X", userName = newName, email = t.UsersB[UserRole.Requester].Email });
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        var body = await taken.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["user.email_taken"], FieldErrors(body, "email"));
        Assert.Equal(["user.display_name_invalid"], FieldErrors(body, "displayName"));

        var ok = await _admin.PutAsJsonAsync(UserUrl(t.A.Id, user.Id),
            new { displayName = "Renamed", userName = newName, email = $"{newName}@example.com", phone = "+966 55 000 1111" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TestData.SignInAsync(TestData.Client(factory), newName)).StatusCode);

        var entry = Assert.Single(await AuditAsync(user.Id));
        Assert.Equal("user.updated", entry.Action);
        Assert.Contains("Test User", entry.Changes);
        Assert.Contains("Renamed", entry.Changes);
        Assert.Contains("+966550001111", entry.Changes);
    }

    // ---- Last-Supervisor rule (D18) ----

    [Fact]
    public async Task The_last_active_supervisor_cannot_be_deactivated_deleted_or_demoted_even_by_the_admin()
    {
        var t = await TestData.CreateTwoTenantsAsync(factory.Services);
        var only = UserUrl(t.A.Id, t.UsersA[UserRole.Supervisor].Id);

        Assert.Equal("user.last_supervisor", await CodeAsync(await _admin.PostAsync($"{only}/deactivate", null)));
        Assert.Equal("user.last_supervisor", await CodeAsync(await _admin.DeleteAsync(only)));
        Assert.Equal("user.last_supervisor", await CodeAsync(await _admin.PostAsJsonAsync($"{only}/role", new { role = "Technician" })));

        // The documented way out: promote someone else first; then the old Supervisor can be changed.
        var promote = await _admin.PostAsJsonAsync($"{UserUrl(t.A.Id, t.UsersA[UserRole.Technician].Id)}/role", new { role = "Supervisor" });
        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _admin.PostAsJsonAsync($"{only}/role", new { role = "Requester" })).StatusCode);
    }

    [Fact]
    public async Task Role_change_rejects_the_platform_role()
    {
        var t = await TestData.CreateTwoTenantsAsync(factory.Services);
        var response = await _admin.PostAsJsonAsync($"{UserUrl(t.A.Id, t.UsersA[UserRole.Requester].Id)}/role", new { role = "PlatformAdmin" });
        Assert.Equal(["user.role_invalid"], await FieldErrorsAsync(response, "role"));
    }

    // ---- Status, password, unlock ----

    [Fact]
    public async Task Deactivate_blocks_sign_in_and_ends_sessions_and_reactivate_restores_access()
    {
        var t = await TestData.CreateTwoTenantsAsync(factory.Services);
        var user = t.UsersA[UserRole.Technician];
        await TestData.SignInAsync(TestData.Client(factory), user.UserName!); // leaves a live refresh token

        Assert.Equal(HttpStatusCode.OK, (await _admin.PostAsync($"{UserUrl(t.A.Id, user.Id)}/deactivate", null)).StatusCode);
        Assert.Equal("auth.account_disabled", await CodeAsync(await TestData.SignInAsync(TestData.Client(factory), user.UserName!)));
        Assert.Equal(0, await LiveRefreshTokensAsync(user.Id));

        Assert.Equal(HttpStatusCode.OK, (await _admin.PostAsync($"{UserUrl(t.A.Id, user.Id)}/reactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TestData.SignInAsync(TestData.Client(factory), user.UserName!)).StatusCode);
        Assert.Equal(["user.deactivated", "user.reactivated"], (await AuditAsync(user.Id)).Select(e => e.Action));
    }

    [Fact]
    public async Task Set_password_enforces_the_rules_replaces_the_old_one_and_never_reaches_the_audit()
    {
        var t = await TestData.CreateTwoTenantsAsync(factory.Services);
        var user = t.UsersA[UserRole.Requester];
        var url = $"{UserUrl(t.A.Id, user.Id)}/password";
        await TestData.SignInAsync(TestData.Client(factory), user.UserName!);

        Assert.Equal(["user.password_too_weak"], await FieldErrorsAsync(await _admin.PostAsJsonAsync(url, new { password = "short" }), "password"));
        Assert.Equal(HttpStatusCode.OK, (await _admin.PostAsJsonAsync(url, new { password = "Brand#New22" })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await TestData.SignInAsync(TestData.Client(factory), user.UserName!)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TestData.SignInAsync(TestData.Client(factory), user.UserName!, "Brand#New22")).StatusCode);
        var entry = Assert.Single(await AuditAsync(user.Id));
        Assert.Equal("user.password_set", entry.Action);
        Assert.DoesNotContain("Brand#New22", entry.Changes);
        Assert.Equal(1, await LiveRefreshTokensAsync(user.Id)); // only the sign-in made after the change
    }

    [Fact]
    public async Task Unlock_clears_a_lockout_immediately()
    {
        var t = await TestData.CreateTwoTenantsAsync(factory.Services);
        var user = t.UsersA[UserRole.Requester];
        await TestData.UpdateUserAsync(factory.Services, user.Id, u => { u.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(5); u.AccessFailedCount = 5; });
        Assert.Equal("auth.account_locked", await CodeAsync(await TestData.SignInAsync(TestData.Client(factory), user.UserName!)));

        var response = await _admin.PostAsync($"{UserUrl(t.A.Id, user.Id)}/unlock", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lockoutEnd").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await TestData.SignInAsync(TestData.Client(factory), user.UserName!)).StatusCode);
        Assert.Equal(["user.unlocked"], (await AuditAsync(user.Id)).Select(e => e.Action));
    }

    // ---- Soft delete and restore ----

    [Fact]
    public async Task Soft_delete_hides_and_blocks_the_user_only_restore_works_on_them_and_restore_brings_them_back_active()
    {
        var t = await TestData.CreateTwoTenantsAsync(factory.Services);
        var user = t.UsersA[UserRole.Requester];
        var url = UserUrl(t.A.Id, user.Id);
        await TestData.UpdateUserAsync(factory.Services, user.Id, u => u.IsActive = false); // restore must also reactivate

        Assert.Equal(HttpStatusCode.OK, (await _admin.DeleteAsync(url)).StatusCode);
        var stored = await LoadAsync(user.Id);
        Assert.True(stored.IsDeleted);
        Assert.Equal(_adminUser.Id, stored.DeletedById);
        Assert.NotNull(stored.DeletedAt);
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestData.SignInAsync(TestData.Client(factory), user.UserName!)).StatusCode);

        Assert.Equal("user.deleted", await CodeAsync(await _admin.PostAsync($"{url}/reactivate", null)));
        Assert.Equal("user.deleted", await CodeAsync(await _admin.PutAsJsonAsync(url, Edit("New", user))));
        Assert.Equal("user.deleted", await CodeAsync(await _admin.DeleteAsync(url)));

        var restored = await _admin.PostAsync($"{url}/restore", null);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        var body = await restored.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.False(body.GetProperty("isDeleted").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await TestData.SignInAsync(TestData.Client(factory), user.UserName!)).StatusCode);
        Assert.Equal("user.not_deleted", await CodeAsync(await _admin.PostAsync($"{url}/restore", null)));
        Assert.Equal(["user.deleted", "user.restored"], (await AuditAsync(user.Id)).Select(e => e.Action));
    }

    // ---- Helpers ----

    private static object Edit(string displayName, ApplicationUser u) =>
        new { displayName, userName = u.UserName, email = u.Email };

    private async Task<ApplicationUser> LoadAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FixFlowDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    private async Task<List<Features.Platform.PlatformAuditEntry>> AuditAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FixFlowDbContext>().PlatformAuditEntries
            .Where(e => e.TargetId == userId).OrderBy(e => e.At).ToListAsync();
    }

    private async Task<int> LiveRefreshTokensAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FixFlowDbContext>().RefreshTokens
            .CountAsync(r => r.UserId == userId && r.RevokedAt == null);
    }

    private static Guid[] Ids(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("id").GetGuid())];

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private static async Task<string[]> FieldErrorsAsync(HttpResponseMessage response, string field) =>
        FieldErrors(await response.Content.ReadFromJsonAsync<JsonElement>(), field);

    private static string[] FieldErrors(JsonElement body, string field) =>
        [.. body.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString()!)];
}
