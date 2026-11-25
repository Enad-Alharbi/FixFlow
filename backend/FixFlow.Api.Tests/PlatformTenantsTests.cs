using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FixFlow.Api.Features.Users;
using FixFlow.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FixFlow.Api.Tests;

/// <summary>Control Panel tenant management (CLAUDE.md §9.4, §10.1): access, create with first Supervisor, edit, lifecycle, audit.</summary>
[Collection("Api")]
public class PlatformTenantsTests(ApiFactory factory) : IAsyncLifetime
{
    private HttpClient _admin = null!;
    private ApplicationUser _adminUser = null!;

    public async Task InitializeAsync()
    {
        _adminUser = await TestData.CreateUserAsync(factory.Services, tenantId: null, UserRole.PlatformAdmin);
        _admin = await TestData.ClientAsAsync(factory, _adminUser);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ---- Access ----

    [Fact]
    public async Task Every_tenant_role_gets_403_and_anonymous_gets_401()
    {
        var tenants = await TestData.CreateTwoTenantsAsync(factory.Services);
        var someTenant = $"/api/platform/tenants/{tenants.B.Id}";

        foreach (var user in tenants.UsersA.Values)
        {
            var client = await TestData.ClientAsAsync(factory, user);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/platform/tenants")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(someTenant)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"{someTenant}/suspend", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/platform/tenants", NewTenant(TestData.Unique()))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestData.Client(factory).GetAsync("/api/platform/tenants")).StatusCode);
    }

    // ---- Create ----

    [Fact]
    public async Task Creates_the_tenant_and_its_first_supervisor_together_and_audits_both()
    {
        var code = TestData.Unique();

        var response = await _admin.PostAsJsonAsync("/api/platform/tenants", NewTenant(code, phone: "+966 50-123 4567"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var tenant = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, tenant.GetProperty("code").GetString());
        Assert.Equal("Active", tenant.GetProperty("status").GetString());
        Assert.Equal(1, tenant.GetProperty("userCount").GetInt32());
        Assert.EndsWith($"/api/platform/tenants/{tenant.GetProperty("id").GetGuid()}", response.Headers.Location!.ToString());

        // The Supervisor exists, has the Supervisor role, and can sign in right away (no forced password change).
        var signIn = await TestData.SignInAsync(TestData.Client(factory), $"{code}.boss", "First#Boss1");
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Assert.Equal("Supervisor", (await signIn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("user").GetProperty("role").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FixFlowDbContext>();
        var tenantId = tenant.GetProperty("id").GetGuid();
        var entries = await db.PlatformAuditEntries.Where(e => e.TenantId == tenantId).ToListAsync();
        Assert.Equal(["tenant.created", "user.created"], entries.Select(e => e.Action).Order());
        Assert.All(entries, e => Assert.Equal(_adminUser.Id, e.ActorId));
        Assert.All(entries, e => Assert.DoesNotContain("First#Boss1", e.Changes)); // never a password in the audit
        var supervisor = await db.Users.SingleAsync(u => u.TenantId == tenantId);
        Assert.Equal("+966501234567", supervisor.PhoneNumber); // stored normalized
    }

    [Fact]
    public async Task A_rejected_supervisor_means_no_tenant_is_created()
    {
        var existing = await TestData.CreateUserAsync(factory.Services, (await TestData.CreateTenantAsync(factory.Services)).Id, UserRole.Requester);
        var code = TestData.Unique();

        var response = await _admin.PostAsJsonAsync("/api/platform/tenants", NewTenant(code, email: existing.Email!.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["user.email_taken"], await FieldErrorsAsync(response, "firstSupervisor.email"));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FixFlowDbContext>();
        Assert.False(await db.Tenants.AnyAsync(t => t.Code == code)); // the transaction rolled back
        Assert.False(await db.PlatformAuditEntries.AnyAsync(e => e.Changes!.Contains(code))); // and so did its audit entry
    }

    [Fact]
    public async Task Invalid_settings_are_reported_per_field()
    {
        var request = NewTenant("9-bad-code") with
        {
            DefaultLanguage = "fr",
            TimeZone = "Arab Standard Time", // a Windows id, not IANA
            FirstSupervisor = NewTenant("x").FirstSupervisor! with { Password = "weak" },
        };

        var response = await _admin.PostAsJsonAsync("/api/platform/tenants", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["tenant.code_invalid"], FieldErrors(body, "code"));
        Assert.Equal(["tenant.language_invalid"], FieldErrors(body, "defaultLanguage"));
        Assert.Equal(["tenant.time_zone_invalid"], FieldErrors(body, "timeZone"));
    }

    [Fact]
    public async Task A_taken_code_is_refused()
    {
        var code = TestData.Unique();
        await CreateAsync(code);

        var response = await _admin.PostAsJsonAsync("/api/platform/tenants", NewTenant(code) with
        {
            FirstSupervisor = NewTenant(TestData.Unique()).FirstSupervisor,
        });

        Assert.Equal(["tenant.code_taken"], await FieldErrorsAsync(response, "code"));
    }

    // ---- Read ----

    [Fact]
    public async Task List_searches_filters_hides_archived_and_pages()
    {
        var prefix = TestData.Unique("s");
        var a = await CreateAsync(prefix + "a");
        await CreateAsync(prefix + "b");
        var archived = await CreateAsync(prefix + "c");
        await _admin.PostAsync($"/api/platform/tenants/{archived.GetProperty("id").GetGuid()}/archive", null);

        var visible = await ListAsync($"search={prefix}&sort=code");
        var onlyArchived = await ListAsync($"search={prefix}&status=Archived");
        var page2 = await ListAsync($"search={prefix}&sort=code&pageSize=1&page=2");

        Assert.Equal([prefix + "a", prefix + "b"], Codes(visible));
        Assert.Equal(2, visible.GetProperty("totalCount").GetInt32());
        Assert.Equal([prefix + "c"], Codes(onlyArchived));
        Assert.Equal([prefix + "b"], Codes(page2));
        Assert.Equal(1, visible.GetProperty("items")[0].GetProperty("userCount").GetInt32());
        Assert.Equal(a.GetProperty("id").GetGuid(), visible.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Unknown_tenant_is_404()
    {
        var response = await _admin.GetAsync($"/api/platform/tenants/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("resource.not_found", await CodeAsync(response));
    }

    // ---- Edit ----

    [Fact]
    public async Task Edit_with_a_stale_version_is_a_conflict_and_a_fresh_one_saves_and_audits()
    {
        var tenant = await CreateAsync(TestData.Unique());
        var id = tenant.GetProperty("id").GetGuid();
        var staleVersion = tenant.GetProperty("rowVersion").GetString();

        var first = await _admin.PutAsJsonAsync($"/api/platform/tenants/{id}", Edit("First rename", staleVersion));
        var second = await _admin.PutAsJsonAsync($"/api/platform/tenants/{id}", Edit("Second rename", staleVersion));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("concurrency.conflict", await CodeAsync(second));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FixFlowDbContext>();
        Assert.Equal("First rename", (await db.Tenants.SingleAsync(t => t.Id == id)).Name);
        var update = await db.PlatformAuditEntries.SingleAsync(e => e.TenantId == id && e.Action == "tenant.updated");
        Assert.Contains("First rename", update.Changes);
        Assert.Contains(tenant.GetProperty("name").GetString()!, update.Changes); // the "before" value
    }

    // ---- Lifecycle ----

    [Fact]
    public async Task Lifecycle_follows_the_transition_table_and_suspension_blocks_sign_in()
    {
        var code = TestData.Unique();
        var id = (await CreateAsync(code)).GetProperty("id").GetGuid();
        var supervisor = TestData.Client(factory);
        var cookie = (await TestData.SignInAsync(supervisor, $"{code}.boss", "First#Boss1")).Headers
            .GetValues("Set-Cookie").Single().Split(';')[0];

        Assert.Equal("Suspended", await StatusAfterAsync($"{id}/suspend"));
        var blocked = await TestData.SignInAsync(supervisor, $"{code}.boss", "First#Boss1");
        Assert.Equal("tenant.suspended", await CodeAsync(blocked));
        using (var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh"))
        {
            refresh.Headers.Add("Cookie", cookie);
            Assert.NotEqual(HttpStatusCode.OK, (await supervisor.SendAsync(refresh)).StatusCode); // session ended
        }

        Assert.Equal("Active", await StatusAfterAsync($"{id}/reactivate"));
        Assert.Equal(HttpStatusCode.OK, (await TestData.SignInAsync(supervisor, $"{code}.boss", "First#Boss1")).StatusCode);

        var again = await _admin.PostAsync($"/api/platform/tenants/{id}/reactivate", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("tenant.invalid_transition", await CodeAsync(again));

        Assert.Equal("Archived", await StatusAfterAsync($"{id}/archive"));
        Assert.Equal("tenant.archived", await CodeAsync(await TestData.SignInAsync(supervisor, $"{code}.boss", "First#Boss1")));

        var noTarget = await _admin.PostAsJsonAsync($"/api/platform/tenants/{id}/unarchive", new { });
        Assert.Equal(HttpStatusCode.BadRequest, noTarget.StatusCode);
        Assert.Equal("Suspended", await StatusAfterAsync($"{id}/unarchive", new { status = "Suspended" }));

        await using var scope = factory.Services.CreateAsyncScope();
        var actions = await scope.ServiceProvider.GetRequiredService<FixFlowDbContext>().PlatformAuditEntries
            .Where(e => e.TenantId == id && e.Action != "user.created").OrderBy(e => e.At).Select(e => e.Action).ToListAsync();
        Assert.Equal(["tenant.created", "tenant.suspended", "tenant.reactivated", "tenant.archived", "tenant.unarchived"], actions);
    }

    // ---- helpers ----

    private record SupervisorBody(string? DisplayName, string? UserName, string? Email, string? Phone, string? Role, string? Password);
    private record TenantBody(
        string? Name, string? Code, string? DefaultLanguage, string? TimeZone,
        string? ContactName, string? ContactEmail, string? ContactPhone, SupervisorBody? FirstSupervisor);

    private static TenantBody NewTenant(string code, string? email = null, string? phone = null) => new(
        $"Tenant {code}", code, "ar", "Asia/Riyadh", "Contact Person", $"contact@{code}.example", null,
        new SupervisorBody("First Boss", $"{code}.boss", email ?? $"boss@{code}.example", phone, "Requester", "First#Boss1"));

    private static object Edit(string name, string? rowVersion) =>
        new { name, defaultLanguage = "en", timeZone = "Asia/Riyadh", rowVersion };

    private async Task<JsonElement> CreateAsync(string code)
    {
        var response = await _admin.PostAsJsonAsync("/api/platform/tenants", NewTenant(code));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> ListAsync(string query) =>
        await _admin.GetFromJsonAsync<JsonElement>($"/api/platform/tenants?{query}");

    private async Task<string?> StatusAfterAsync(string action, object? body = null)
    {
        var response = await _admin.PostAsJsonAsync($"/api/platform/tenants/{action}", body ?? new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString();
    }

    private static string[] Codes(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("code").GetString()!)];

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private static async Task<string[]> FieldErrorsAsync(HttpResponseMessage response, string field) =>
        FieldErrors(await response.Content.ReadFromJsonAsync<JsonElement>(), field);

    private static string[] FieldErrors(JsonElement body, string field) =>
        [.. body.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString()!)];
}
