using FixFlow.Api.Features.Tenants;
using FixFlow.Api.Features.Users;
using FixFlow.Api.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FixFlow.Api.Tests;

/// <summary>Account rules from CLAUDE.md §7.4, run through the real UserManager against SQL Server.</summary>
[Collection("Api")]
public class UserRulesTests(ApiFactory factory) : IAsyncLifetime
{
    private const string ValidPassword = "Valid#Pass1";
    private AsyncServiceScope _scope;
    private UserManager<ApplicationUser> Users => _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    private FixFlowDbContext Db => _scope.ServiceProvider.GetRequiredService<FixFlowDbContext>();

    public Task InitializeAsync()
    {
        factory.CreateClient(); // starts the app, which applies migrations (Development)
        _scope = factory.Services.CreateAsyncScope();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _scope.DisposeAsync();

    [Theory]
    [InlineData("Sh0rt!x")] // 7 characters
    [InlineData("nouppercase1!")]
    [InlineData("NOLOWERCASE1!")]
    [InlineData("NoDigitsHere!")]
    [InlineData("NoSpecial123")]
    [InlineData(" Leading1!")]
    [InlineData("Trailing1! ")]
    public async Task Weak_passwords_are_rejected(string password)
    {
        var result = await Users.CreateAsync(NewUser(await NewTenantAsync()), password);

        Assert.Equal(["user.password_too_weak"], Codes(result));
    }

    [Fact]
    public async Task Password_longer_than_128_characters_is_rejected()
    {
        var result = await Users.CreateAsync(NewUser(await NewTenantAsync()), "Aa1!" + new string('x', 125));

        Assert.Equal(["user.password_too_weak"], Codes(result));
    }

    [Fact]
    public async Task Valid_user_is_created_with_a_hashed_password()
    {
        var user = NewUser(await NewTenantAsync());
        user.PhoneNumber = "+966 50 123 4567";

        var result = await Users.CreateAsync(user, "A middle space is fine1!");

        Assert.True(result.Succeeded, string.Join(", ", Codes(result)));
        Assert.NotNull(user.PasswordHash);
        Assert.DoesNotContain("middle space", user.PasswordHash);
    }

    [Theory]
    [InlineData("ab")] // too short
    [InlineData("1abc")] // must start with a letter
    [InlineData("_abc")]
    [InlineData("has space")]
    [InlineData("ahmed@site")]
    [InlineData("أحمد")] // ASCII only; the display name may be Arabic
    [InlineData("a234567890123456789012345678901")] // 31 characters
    public async Task Invalid_usernames_are_rejected(string username)
    {
        var user = NewUser(await NewTenantAsync());
        user.UserName = username;

        Assert.Contains("user.username_invalid", Codes(await Users.CreateAsync(user, ValidPassword)));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a b@example.com")]
    [InlineData("Name <a@example.com>")]
    public async Task Invalid_emails_are_rejected(string email)
    {
        var user = NewUser(await NewTenantAsync());
        user.Email = email;

        Assert.Contains("user.email_invalid", Codes(await Users.CreateAsync(user, ValidPassword)));
    }

    [Fact]
    public async Task Email_longer_than_254_characters_is_rejected()
    {
        var user = NewUser(await NewTenantAsync());
        user.Email = new string('a', 64) + "@" + new string('b', 186) + ".com"; // 64 + 1 + 186 + 4 = 255

        Assert.Contains("user.email_invalid", Codes(await Users.CreateAsync(user, ValidPassword)));
    }

    [Theory]
    [InlineData("A")]
    [InlineData(" Padded ")]
    public async Task Invalid_display_names_are_rejected(string displayName)
    {
        var user = NewUser(await NewTenantAsync());
        user.DisplayName = displayName;

        Assert.Equal(["user.display_name_invalid"], Codes(await Users.CreateAsync(user, ValidPassword)));
    }

    [Theory]
    [InlineData("12345")] // too few digits
    [InlineData("call me")]
    [InlineData("+966--501234567")]
    public async Task Invalid_phone_numbers_are_rejected(string phone)
    {
        var user = NewUser(await NewTenantAsync());
        user.PhoneNumber = phone;

        Assert.Equal(["user.phone_invalid"], Codes(await Users.CreateAsync(user, ValidPassword)));
    }

    [Fact]
    public async Task Username_and_email_are_unique_across_the_platform_ignoring_case()
    {
        var first = NewUser(await NewTenantAsync());
        Assert.True((await Users.CreateAsync(first, ValidPassword)).Succeeded);

        // Same username and email in different case, in a DIFFERENT tenant.
        var second = NewUser(await NewTenantAsync());
        second.UserName = first.UserName!.ToUpperInvariant();
        second.Email = first.Email!.ToUpperInvariant();

        var codes = Codes(await Users.CreateAsync(second, ValidPassword));
        Assert.Contains("user.username_taken", codes);
        Assert.Contains("user.email_taken", codes);
    }

    [Fact]
    public async Task Database_refuses_a_platform_admin_inside_a_tenant()
    {
        var user = NewUser(await NewTenantAsync(), UserRole.PlatformAdmin);

        await Assert.ThrowsAsync<DbUpdateException>(() => Users.CreateAsync(user, ValidPassword));
    }

    [Fact]
    public async Task Database_refuses_a_tenant_user_without_a_tenant()
    {
        var user = NewUser(tenantId: null, UserRole.Requester);

        await Assert.ThrowsAsync<DbUpdateException>(() => Users.CreateAsync(user, ValidPassword));
    }

    [Fact]
    public void Lockout_settings_come_from_configuration()
    {
        Assert.Equal(5, Users.Options.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(5), Users.Options.Lockout.DefaultLockoutTimeSpan);
        Assert.True(Users.Options.Lockout.AllowedForNewUsers);
    }

    private static string[] Codes(IdentityResult result) => [.. result.Errors.Select(e => e.Code).Distinct()];

    private async Task<Guid> NewTenantAsync()
    {
        var code = "t" + Guid.NewGuid().ToString("N")[..12];
        var tenant = new Tenant { Name = code, Code = code, DefaultLanguage = "en", TimeZone = "Asia/Riyadh" };
        Db.Tenants.Add(tenant);
        await Db.SaveChangesAsync();
        return tenant.Id;
    }

    private static ApplicationUser NewUser(Guid? tenantId, UserRole role = UserRole.Requester)
    {
        var name = "u" + Guid.NewGuid().ToString("N")[..12];
        return new ApplicationUser
        {
            TenantId = tenantId,
            Role = role,
            UserName = name,
            Email = $"{name}@example.com",
            DisplayName = "Test User",
        };
    }
}
