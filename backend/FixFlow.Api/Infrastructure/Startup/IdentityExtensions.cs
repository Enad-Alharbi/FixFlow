using FixFlow.Api.Features.Users;
using FixFlow.Api.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;

namespace FixFlow.Api.Infrastructure.Startup;

public static class IdentityExtensions
{
    /// <summary>ASP.NET Core Identity: user store, password hashing, and the account rules from CLAUDE.md §7.4.</summary>
    public static IServiceCollection AddFixFlowIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        // AddIdentityCore, not AddIdentity: the API authenticates with JWTs, so no Identity cookies.
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = UserRules.PasswordMinLength;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = true; // "special": anything except A–Z, a–z, 0–9
                options.Password.RequiredUniqueChars = 1;
                options.User.AllowedUserNameCharacters = UserRules.UsernameAllowedCharacters;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<FixFlowDbContext>()
            .AddSignInManager() // only for CheckPasswordSignInAsync (password check + lockout counting)
            .AddErrorDescriber<FixFlowIdentityErrorDescriber>()
            .AddUserValidator<UserRulesValidator>()
            .AddPasswordValidator<PasswordRulesValidator>();

        // Lockout (attempts, duration) comes from configuration so it can change without a code change.
        // Binds the "Identity" section onto IdentityOptions (only Lockout is set there today).
        services.Configure<IdentityOptions>(configuration.GetSection("Identity"));

        return services;
    }
}
