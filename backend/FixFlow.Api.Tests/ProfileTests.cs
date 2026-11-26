using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FixFlow.Api.Features.Auth;
using FixFlow.Api.Features.Users;
using FixFlow.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FixFlow.Api.Tests;

/// <summary>Your own profile and password (CLAUDE.md §7.4, Phase 5).</summary>
[Collection("Api")]
public class ProfileTests(ApiFactory factory)
{
    private const string NewPassword = "Brand#New22";

    // ---- Profile ----

    [Fact]
    public async Task Updates_your_own_profile_and_me_shows_it()
    {
        var tenant = await TestData.CreateTenantAsync(factory.Services);
        var user = await TestData.CreateUserAsync(factory.Services, tenant.Id, UserRole.Technician);
        var client = await TestData.ClientAsAsync(factory, user);

        // Email and username in the body are not part of the contract (D22) and are ignored.
        var response = await client.PutAsJsonAsync("/api/profile", new
        {
            displayName = "  سارة  ", phone = "+966 50 000 0000", preferredLanguage = "en", email = "hijack@example.com",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("سارة", me.GetProperty("displayName").GetString());
        Assert.Equal("+966500000000", me.GetProperty("phone").GetString());
        Assert.Equal("en", me.GetProperty("preferredLanguage").GetString());
        Assert.Equal(user.Email, me.GetProperty("email").GetString());
        Assert.Single(await AuditAsync(user.Id, "user.profile_updated"));

        // Clearing the language means "use the tenant's default" again.
        await client.PutAsJsonAsync("/api/profile", new { displayName = "سارة", phone = "+966500000000", preferredLanguage = (string?)null });
        Assert.Equal(JsonValueKind.Null, (await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("preferredLanguage").ValueKind);
    }

    [Fact]
    public async Task Refuses_an_invalid_profile_and_changes_nothing()
    {
        var user = await TestData.CreateUserAsync(factory.Services, tenantId: null, UserRole.PlatformAdmin);
        var client = await TestData.ClientAsAsync(factory, user);

        var language = await client.PutAsJsonAsync("/api/profile", new { displayName = "Admin", preferredLanguage = "fr" });
        Assert.Equal(["user.language_invalid"], await FieldErrorsAsync(language, "preferredLanguage"));

        var name = await (await client.PutAsJsonAsync("/api/profile", new { displayName = "A", phone = "12" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("user.display_name_invalid", name.GetProperty("errors").GetProperty("displayName")[0].GetString());
        Assert.Equal("user.phone_invalid", name.GetProperty("errors").GetProperty("phone")[0].GetString());

        Assert.Equal("Test User", (await LoadAsync(user.Id)).DisplayName);
        Assert.Empty(await AuditAsync(user.Id, "user.profile_updated"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestData.Client(factory).PutAsJsonAsync("/api/profile", new { })).StatusCode);
    }

    // ---- Change own password ----

    [Fact]
    public async Task Changing_your_password_keeps_this_session_and_signs_out_the_others()
    {
        var user = await TestData.CreateUserAsync(factory.Services, (await TestData.CreateTenantAsync(factory.Services)).Id, UserRole.Requester);
        var other = await SignInAsync(user.UserName!); // e.g. the phone
        var here = await SignInAsync(user.UserName!);

        var response = await ChangeAsync(here, TestData.Password, NewPassword, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(here.Cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(other.Cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestData.SignInAsync(TestData.Client(factory), user.UserName!)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TestData.SignInAsync(TestData.Client(factory), user.UserName!, NewPassword)).StatusCode);

        var entry = Assert.Single(await AuditAsync(user.Id, "user.password_changed"));
        Assert.Equal(user.Id, entry.ActorId);
        Assert.Null(entry.Changes); // nothing about the password itself is stored
    }

    [Fact]
    public async Task A_wrong_current_password_is_refused_and_counts_toward_lockout()
    {
        var user = await TestData.CreateUserAsync(factory.Services, (await TestData.CreateTenantAsync(factory.Services)).Id, UserRole.Technician);
        var session = await SignInAsync(user.UserName!);

        var wrong = await ChangeAsync(session, "Wrong#Pass1", NewPassword, NewPassword);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(["user.current_password_incorrect"], await FieldErrorsAsync(wrong, "currentPassword"));
        Assert.Equal(1, (await LoadAsync(user.Id)).AccessFailedCount);

        for (var i = 0; i < 4; i++) await ChangeAsync(session, "Wrong#Pass1", NewPassword, NewPassword); // 5 in total: locked

        var locked = await ChangeAsync(session, TestData.Password, NewPassword, NewPassword);
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        Assert.Equal("auth.account_locked", (await locked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal("auth.account_locked", await SignInCodeAsync(user.UserName!));

        // D14: the lock blocks new sign-ins only; the session that is already open keeps working.
        var me = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        me.Headers.Authorization = new("Bearer", session.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await TestData.Client(factory).SendAsync(me)).StatusCode);
    }

    [Fact]
    public async Task Form_mistakes_are_refused_without_costing_a_lockout_attempt()
    {
        var user = await TestData.CreateUserAsync(factory.Services, tenantId: null, UserRole.PlatformAdmin);
        var session = await SignInAsync(user.UserName!);

        var same = await ChangeAsync(session, TestData.Password, TestData.Password, TestData.Password);
        Assert.Equal(["user.password_same"], await FieldErrorsAsync(same, "newPassword"));

        var mismatch = await ChangeAsync(session, "Wrong#Pass1", NewPassword, NewPassword + "x");
        Assert.Equal(["user.password_mismatch"], await FieldErrorsAsync(mismatch, "confirmPassword"));
        Assert.Equal(0, (await LoadAsync(user.Id)).AccessFailedCount);

        // The backend enforces the password rules even if the frontend is bypassed.
        var weak = await ChangeAsync(session, TestData.Password, "weakpass", "weakpass");
        Assert.Equal(["user.password_too_weak"], await FieldErrorsAsync(weak, "newPassword"));
        Assert.Equal(HttpStatusCode.OK, (await TestData.SignInAsync(TestData.Client(factory), user.UserName!)).StatusCode);
    }

    // ---- Helpers ----

    private async Task<(string AccessToken, string Cookie)> SignInAsync(string login)
    {
        var response = await TestData.SignInAsync(TestData.Client(factory), login);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<JsonElement>();
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(AuthController.RefreshCookie + "=")).Split(';')[0];
        return (session.GetProperty("accessToken").GetString()!, cookie);
    }

    private Task<HttpResponseMessage> ChangeAsync((string AccessToken, string Cookie) session, string current, string next, string confirm)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/password")
        {
            Content = JsonContent.Create(new { currentPassword = current, newPassword = next, confirmPassword = confirm }),
        };
        request.Headers.Authorization = new("Bearer", session.AccessToken);
        request.Headers.Add("Cookie", session.Cookie);
        return TestData.Client(factory).SendAsync(request);
    }

    private Task<HttpResponseMessage> RefreshAsync(string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", cookie);
        return TestData.Client(factory).SendAsync(request);
    }

    private async Task<string?> SignInCodeAsync(string login) =>
        (await (await TestData.SignInAsync(TestData.Client(factory), login)).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString();

    private async Task<ApplicationUser> LoadAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FixFlowDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    private async Task<List<Features.Platform.PlatformAuditEntry>> AuditAsync(Guid userId, string action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FixFlowDbContext>().PlatformAuditEntries
            .Where(e => e.TargetId == userId && e.Action == action).ToListAsync();
    }

    private static async Task<string[]> FieldErrorsAsync(HttpResponseMessage response, string field) =>
        [.. (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty(field)
            .EnumerateArray().Select(e => e.GetString()!)];
}
