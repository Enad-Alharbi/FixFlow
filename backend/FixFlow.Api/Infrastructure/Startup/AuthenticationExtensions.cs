using System.Threading.RateLimiting;
using FixFlow.Api.Features.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FixFlow.Api.Infrastructure.Startup;

public static class AuthenticationExtensions
{
    /// <summary>JWT access tokens (ADR 0005), the named authorization policies, and the sign-in rate limit.</summary>
    public static IServiceCollection AddFixFlowAuthentication(this IServiceCollection services)
    {
        // Token settings; the API refuses to start with a missing or too-short signing key.
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
        services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.Section).ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                bearer.MapInboundClaims = false; // keep "sub", "role", "tenant_id" as written, not long XML claim URIs
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(jwt.Value.SigningKeyBytes),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256], // refuse "alg: none" and algorithm swaps
                    NameClaimType = FixFlowClaims.UserId,
                    RoleClaimType = FixFlowClaims.Role,
                    ClockSkew = TimeSpan.FromSeconds(30), // default is 5 minutes, a third of the token's life
                };
                // D17: a valid signature is not enough; the user and tenant must still be allowed in right now.
                bearer.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var status = context.HttpContext.RequestServices.GetRequiredService<IAccessStatus>();
                        if (!await status.IsAllowedAsync(context.Principal!, context.HttpContext.RequestAborted))
                            context.Fail("Access revoked."); // 401: the SPA refreshes, and refresh explains why
                    },
                };
            });

        AuthPolicies.Add(services.AddAuthorizationBuilder());

        // Slows password guessing on /api/auth/* (per client IP).
        // shortcut: behind a reverse proxy every client shares one IP; read X-Forwarded-For when the proxy arrives (Phase 18).
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AuthController.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = http.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimiting:AuthPermitsPerMinute", 20),
                    Window = TimeSpan.FromMinutes(1),
                }));
        });

        return services;
    }
}
