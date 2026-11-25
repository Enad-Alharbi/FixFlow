using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FixFlow.Api.Features.Auth;
using FixFlow.Api.Features.Tenants;
using FixFlow.Api.Features.Users;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace FixFlow.Api.Tests;

/// <summary>Sign-in, lockout, refresh rotation, sign-out, and "who am I" (CLAUDE.md §7.4, §9.2; ADR 0005).</summary>
[Collection("Api")]
public class AuthTests(ApiFactory factory)
{
    private readonly HttpClient _client = TestData.Client(factory);

    // ---- Sign-in ----

    [Fact]
    public async Task Sign_in_returns_an_access_token_and_a_locked_down_refresh_cookie()
    {
        var tenant = await TestData.CreateTenantAsync(factory.Services);
        var user = await TestData.CreateUserAsync(factory.Services, tenant.Id, UserRole.Supervisor);

        var response = await TestData.SignInAsync(_client, user.UserName!);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = (await response.Content.ReadFromJsonAsync<JsonElement>());
        var token = new JsonWebToken(session.GetProperty("accessToken").GetString());
        Assert.Equal(user.Id.ToString(), token.Subject);
        Assert.Equal("Supervisor", token.GetClaim("role").Value);
        Assert.Equal(tenant.Id.ToString(), token.GetClaim("tenant_id").Value);
        Assert.Equal("Supervisor", session.GetProperty("user").GetProperty("role").GetString());

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith(AuthEndpoints.RefreshCookie + "=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sign_in_accepts_the_email_in_any_case()
    {
        var user = await TestData.CreateUserAsync(factory.Services, (await TestData.CreateTenantAsync(factory.Services)).Id, UserRole.Requester);

        var response = await TestData.SignInAsync(_client, "  " + user.Email!.ToUpperInvariant() + " ");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Platform_admin_token_has_no_tenant_claim()
    {
        var admin = await TestData.CreateUserAsync(factory.Services, tenantId: null, UserRole.PlatformAdmin);

        var session = await (await TestData.SignInAsync(_client, admin.UserName!)).Content.ReadFromJsonAsync<JsonElement>();

        var token = new JsonWebToken(session.GetProperty("accessToken").GetString());
        Assert.False(token.TryGetClaim("tenant_id", out _));
        Assert.Equal(JsonValueKind.Null, session.GetProperty("user").GetProperty("tenant").ValueKind);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_account_get_the_same_answer()
    {
        var user = await TestData.CreateUserAsync(factory.Services, (await TestData.CreateTenantAsync(factory.Services)).Id, UserRole.Requester);

        var wrongPassword = await TestData.SignInAsync(_client, user.UserName!, "Wrong#Pass1");
        var unknownAccount = await TestData.SignInAsync(_client, "nobody" + Guid.NewGuid().ToString("N")[..8]);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownAccount.StatusCode);
        Assert.Equal("auth.invalid_credentials", await CodeAsync(wrongPassword));
        Assert.Equal("auth.invalid_credentials", await CodeAsync(unknownAccount));
    }

    [Fact]
    public async Task Sixth_attempt_fails_even_with_the_right_password_until_unlocked()
    {
        var user = await TestData.CreateUserAsync(factory.Services, (await TestData.CreateTenantAsync(factory.Services)).Id, UserRole.Requester);
        for (var i = 0; i < 5; i++)
            await TestData.SignInAsync(_client, user.UserName!, "Wrong#Pass1");

        var locked = await TestData.SignInAsync(_client, user.UserName!);

        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        var body = await locked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("auth.account_locked", body.GetProperty("code").GetString());
        var lockoutEnd = body.GetProperty("lockoutEnd").GetDateTimeOffset();
        Assert.InRange(lockoutEnd - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));

        // Manual unlock (what the Supervisor's Unlock button will do), then the right password works again.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await users.SetLockoutEndDateAsync((await users.FindByIdAsync(user.Id.ToString()))!, null);
        }
        Assert.Equal(HttpStatusCode.OK, (await TestData.SignInAsync(_client, user.UserName!)).StatusCode);
    }

    [Fact]
    public async Task Successful_sign_in_resets_the_failure_counter()
    {
        var user = await TestData.CreateUserAsync(factory.Services, (await TestData.CreateTenantAsync(factory.Services)).Id, UserRole.Requester);
        for (var i = 0; i < 4; i++)
            await TestData.SignInAsync(_client, user.UserName!, "Wrong#Pass1");
        Assert.Equal(HttpStatusCode.OK, (await TestData.SignInAsync(_client, user.UserName!)).StatusCode);

        // Without the reset, this single failure would be the 5th and lock the account.
        await TestData.SignInAsync(_client, user.UserName!, "Wrong#Pass1");

        Assert.Equal(HttpStatusCode.OK, (await TestData.SignInAsync(_client, user.UserName!)).StatusCode);
    }

    [Fact]
    public async Task Deactivated_and_deleted_users_cannot_sign_in()
    {
        var tenant = await TestData.CreateTenantAsync(factory.Services);
        var deactivated = await TestData.CreateUserAsync(factory.Services, tenant.Id, UserRole.Requester);
        var deleted = await TestData.CreateUserAsync(factory.Services, tenant.Id, UserRole.Requester);
        await TestData.UpdateUserAsync(factory.Services, deactivated.Id, u => u.IsActive = false);
        await TestData.UpdateUserAsync(factory.Services, deleted.Id, u => u.IsDeleted = true);

        var deactivatedResponse = await TestData.SignInAsync(_client, deactivated.UserName!);
        var deletedResponse = await TestData.SignInAsync(_client, deleted.UserName!);

        Assert.Equal(HttpStatusCode.Forbidden, deactivatedResponse.StatusCode);
        Assert.Equal("auth.account_disabled", await CodeAsync(deactivatedResponse));
        // A soft-deleted account looks exactly like one that doesn't exist.
        Assert.Equal(HttpStatusCode.Unauthorized, deletedResponse.StatusCode);
        Assert.Equal("auth.invalid_credentials", await CodeAsync(deletedResponse));
    }

    [Theory]
    [InlineData(TenantStatus.Suspended, "tenant.suspended")]
    [InlineData(TenantStatus.Archived, "tenant.archived")]
    public async Task Users_of_inactive_tenants_cannot_sign_in(TenantStatus status, string code)
    {
        var tenant = await TestData.CreateTenantAsync(factory.Services, status);
        var user = await TestData.CreateUserAsync(factory.Services, tenant.Id, UserRole.Supervisor);

        var response = await TestData.SignInAsync(_client, user.UserName!);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(code, await CodeAsync(response));
    }

    [Fact]
    public async Task Sign_in_is_rate_limited()
    {
        await using var limited = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:AuthPermitsPerMinute", "3"));
        var client = TestData.Client(limited);

        for (var i = 0; i < 3; i++)
            await TestData.SignInAsync(client, "nobody");
        var response = await TestData.SignInAsync(client, "nobody");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("http.too_many_requests", await CodeAsync(response));
    }

    // ---- Me / protected endpoints ----

    [Fact]
    public async Task Protected_endpoint_rejects_missing_and_tampered_tokens()
    {
        var user = await TestData.CreateUserAsync(factory.Services, (await TestData.CreateTenantAsync(factory.Services)).Id, UserRole.Requester);
        var token = (await SignInAsync(user)).AccessToken;
        var tampered = token[..^4] + (token[^4..] == "AAAA" ? "BBBB" : "AAAA"); // break the signature

        var missing = await _client.GetAsync("/api/auth/me");
        var bad = await GetMeAsync(tampered);

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal("auth.unauthenticated", await CodeAsync(missing));
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
    }

    [Fact]
    public async Task Me_returns_only_the_callers_own_user_and_tenant()
    {
        var tenants = await TestData.CreateTwoTenantsAsync(factory.Services);

        foreach (var (users, tenant) in new[] { (tenants.UsersA, tenants.A), (tenants.UsersB, tenants.B) })
        foreach (var user in users.Values)
        {
            var me = await (await GetMeAsync((await SignInAsync(user)).AccessToken)).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(user.Id, me.GetProperty("id").GetGuid());
            Assert.Equal(tenant.Id, me.GetProperty("tenant").GetProperty("id").GetGuid());
            Assert.Equal("Asia/Riyadh", me.GetProperty("tenant").GetProperty("timeZone").GetString());
        }
    }

    // ---- Refresh / sign-out ----

    [Fact]
    public async Task Refresh_rotates_the_token_and_reuse_ends_the_whole_session()
    {
        var user = await TestData.CreateUserAsync(factory.Services, (await TestData.CreateTenantAsync(factory.Services)).Id, UserRole.Requester);
        var first = (await SignInAsync(user)).Cookie;

        var refreshed = await RefreshAsync(first);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var second = RefreshCookieOf(refreshed);
        Assert.NotEqual(first, second);

        // The spent token comes back (a stolen copy): refused, and the new token dies with it.
        var reuse = await RefreshAsync(first);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        Assert.Equal("auth.session_expired", await CodeAsync(reuse));
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(second)).StatusCode);
    }

    [Fact]
    public async Task Refresh_without_a_cookie_is_refused()
    {
        var response = await _client.PostAsync("/api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("auth.session_expired", await CodeAsync(response));
    }

    [Fact]
    public async Task Refresh_is_refused_once_the_tenant_is_suspended()
    {
        var tenant = await TestData.CreateTenantAsync(factory.Services);
        var user = await TestData.CreateUserAsync(factory.Services, tenant.Id, UserRole.Requester);
        var cookie = (await SignInAsync(user)).Cookie;

        await TestData.SetTenantStatusAsync(factory.Services, tenant.Id, TenantStatus.Suspended);
        var response = await RefreshAsync(cookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("tenant.suspended", await CodeAsync(response));
    }

    [Fact]
    public async Task Refresh_is_refused_once_the_user_is_deactivated()
    {
        var user = await TestData.CreateUserAsync(factory.Services, (await TestData.CreateTenantAsync(factory.Services)).Id, UserRole.Requester);
        var cookie = (await SignInAsync(user)).Cookie;

        await TestData.UpdateUserAsync(factory.Services, user.Id, u => u.IsActive = false);

        Assert.Equal("auth.account_disabled", await CodeAsync(await RefreshAsync(cookie)));
    }

    [Fact]
    public async Task Refresh_from_a_foreign_origin_is_refused()
    {
        var user = await TestData.CreateUserAsync(factory.Services, (await TestData.CreateTenantAsync(factory.Services)).Id, UserRole.Requester);
        var cookie = (await SignInAsync(user)).Cookie;

        var response = await RefreshAsync(cookie, origin: "https://evil.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Sign_out_ends_the_session()
    {
        var user = await TestData.CreateUserAsync(factory.Services, (await TestData.CreateTenantAsync(factory.Services)).Id, UserRole.Requester);
        var cookie = (await SignInAsync(user)).Cookie;

        using var signOut = new HttpRequestMessage(HttpMethod.Post, "/api/auth/sign-out");
        signOut.Headers.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(signOut)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(cookie)).StatusCode);
    }

    // ---- helpers ----

    private async Task<(string AccessToken, string Cookie)> SignInAsync(ApplicationUser user)
    {
        var response = await TestData.SignInAsync(_client, user.UserName!);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (session.GetProperty("accessToken").GetString()!, RefreshCookieOf(response));
    }

    private Task<HttpResponseMessage> GetMeAsync(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> RefreshAsync(string cookie, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", cookie);
        if (origin is not null) request.Headers.Add("Origin", origin);
        return _client.SendAsync(request);
    }

    /// <summary>"fixflow_refresh=value" from the Set-Cookie header, ready to send back in a Cookie header.</summary>
    private static string RefreshCookieOf(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(AuthEndpoints.RefreshCookie + "=")).Split(';')[0];

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
}
