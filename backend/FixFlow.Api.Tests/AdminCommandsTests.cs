using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FixFlow.Api.Features.Users;
using FixFlow.Api.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FixFlow.Api.Tests;

/// <summary>Platform Admin bootstrap and recovery commands (CLAUDE.md §7.4, §10.2), plus the development seed.</summary>
[Collection("Api")]
public class AdminCommandsTests(ApiFactory factory)
{
    private const string AdminPassword = "Admin#Pass1";

    private static readonly Dictionary<string, string?> BootstrapValues = new()
    {
        ["FIXFLOW_ADMIN_USERNAME"] = "owner",
        ["FIXFLOW_ADMIN_EMAIL"] = "owner@fixflow.example",
        ["FIXFLOW_ADMIN_DISPLAYNAME"] = "Owner",
        ["FIXFLOW_ADMIN_PASSWORD"] = AdminPassword,
    };

    [Fact]
    public async Task Bootstrap_creates_the_first_admin_and_refuses_a_second_time()
    {
        await using var app = FreshDatabase();

        Assert.Equal(0, await RunAsync(app, BootstrapValues, "bootstrap"));
        Assert.Equal(1, await RunAsync(app, BootstrapValues, "bootstrap"));

        await using var scope = app.Services.CreateAsyncScope();
        var admin = await scope.ServiceProvider.GetRequiredService<FixFlowDbContext>().Users.SingleAsync();
        Assert.Equal(UserRole.PlatformAdmin, admin.Role);
        Assert.Null(admin.TenantId);
        Assert.Equal(HttpStatusCode.OK, (await TestData.SignInAsync(TestData.Client(app), "owner", AdminPassword)).StatusCode);
    }

    [Theory]
    [InlineData("FIXFLOW_ADMIN_PASSWORD", "weak")] // password rules apply to the admin too
    [InlineData("FIXFLOW_ADMIN_PASSWORD", "")] // missing value
    [InlineData("FIXFLOW_ADMIN_USERNAME", "9starts-with-digit")]
    public async Task Bootstrap_refuses_invalid_values_and_creates_nothing(string key, string value)
    {
        await using var app = FreshDatabase();

        Assert.Equal(1, await RunAsync(app, new(BootstrapValues) { [key] = value }, "bootstrap"));

        await using var scope = app.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<FixFlowDbContext>().Users.AnyAsync());
    }

    [Fact]
    public async Task Recover_unlocks_sets_a_new_password_and_ends_sessions()
    {
        await using var app = FreshDatabase();
        await RunAsync(app, BootstrapValues, "bootstrap");
        var client = TestData.Client(app);
        var signIn = await TestData.SignInAsync(client, "owner", AdminPassword);
        var oldCookie = signIn.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        for (var i = 0; i < 5; i++)
            await TestData.SignInAsync(client, "owner", "Wrong#Pass1"); // now locked

        var exitCode = await RunAsync(app, new() { ["FIXFLOW_ADMIN_USERNAME"] = "owner", ["FIXFLOW_ADMIN_PASSWORD"] = "Brand#New2" }, "recover");

        Assert.Equal(0, exitCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestData.SignInAsync(client, "owner", AdminPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TestData.SignInAsync(client, "owner", "Brand#New2")).StatusCode);
        using var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refresh.Headers.Add("Cookie", oldCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(refresh)).StatusCode);
    }

    [Fact]
    public async Task Recover_with_a_weak_password_keeps_the_old_one()
    {
        await using var app = FreshDatabase();
        await RunAsync(app, BootstrapValues, "bootstrap");

        Assert.Equal(1, await RunAsync(app, new() { ["FIXFLOW_ADMIN_USERNAME"] = "owner", ["FIXFLOW_ADMIN_PASSWORD"] = "weak" }, "recover"));

        Assert.Equal(HttpStatusCode.OK, (await TestData.SignInAsync(TestData.Client(app), "owner", AdminPassword)).StatusCode);
    }

    [Fact]
    public async Task Recover_never_touches_tenant_users()
    {
        var supervisor = await TestData.CreateUserAsync(factory.Services, (await TestData.CreateTenantAsync(factory.Services)).Id, UserRole.Supervisor);

        var exitCode = await RunAsync(factory, new() { ["FIXFLOW_ADMIN_USERNAME"] = supervisor.UserName, ["FIXFLOW_ADMIN_PASSWORD"] = "Brand#New2" }, "recover");

        Assert.Equal(1, exitCode);
        Assert.Equal(HttpStatusCode.OK, (await TestData.SignInAsync(TestData.Client(factory), supervisor.UserName!)).StatusCode);
    }

    [Fact]
    public async Task Dev_seed_creates_two_demo_tenants_once_and_their_users_can_sign_in()
    {
        await using var app = FreshDatabase();
        for (var run = 0; run < 2; run++) // the second run must change nothing
        {
            await using var scope = app.Services.CreateAsyncScope();
            await DevSeeder.SeedAsync(scope.ServiceProvider, TestData.Password, NullLogger.Instance);
        }

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FixFlowDbContext>();
            Assert.Equal(["acme", "nahda"], await db.Tenants.OrderBy(t => t.Code).Select(t => t.Code).ToListAsync());
            Assert.Equal(7, await db.Users.CountAsync()); // admin + 3 per tenant
        }

        var response = await TestData.SignInAsync(TestData.Client(app), "nahda.supervisor");
        var me = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("user");
        Assert.Equal("نورة العتيبي", me.GetProperty("displayName").GetString());
        Assert.Equal("ar", me.GetProperty("tenant").GetProperty("defaultLanguage").GetString());
    }

    [Fact]
    public async Task Dev_seed_is_skipped_without_a_demo_password()
    {
        await using var app = FreshDatabase();
        await using var scope = app.Services.CreateAsyncScope();

        await DevSeeder.SeedAsync(scope.ServiceProvider, demoPassword: null, NullLogger.Instance);

        Assert.False(await scope.ServiceProvider.GetRequiredService<FixFlowDbContext>().Users.AnyAsync());
    }

    /// <summary>The app against its own new, empty database (migrated on startup), so "no admin yet" holds.</summary>
    private WebApplicationFactory<Program> FreshDatabase()
    {
        var connectionString = new SqlConnectionStringBuilder(factory.ConnectionString)
        {
            InitialCatalog = "FixFlow_Cmd_" + Guid.NewGuid().ToString("N")[..8],
        }.ConnectionString;
        var app = factory.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Default", connectionString));
        app.CreateClient(); // start it: Development startup applies migrations
        return app;
    }

    private static Task<int> RunAsync(WebApplicationFactory<Program> app, Dictionary<string, string?> values, string command) =>
        AdminCommands.RunAsync(app.Services, new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            ["admin", command], TextWriter.Null);
}
