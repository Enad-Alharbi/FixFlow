using System.Text.Json.Serialization;
using FixFlow.Api.Features.Auth;
using FixFlow.Api.Features.Platform;
using FixFlow.Api.Features.Tenants;
using FixFlow.Api.Features.Users;
using FixFlow.Api.Infrastructure.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FixFlow.Api.Infrastructure.Startup;

public static class ApiExtensions
{
    /// <summary>HTTP API plumbing: ProblemDetails errors, health checks, CORS, controllers, and OpenAPI.</summary>
    public static IServiceCollection AddFixFlowApi(this IServiceCollection services, IConfiguration configuration)
    {
        // Errors: every error response is RFC 9457 ProblemDetails with a stable `code`.
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
                context.ProblemDetails.Extensions.TryAdd("code", context.ProblemDetails.Status switch
                {
                    // [ApiController] answers a missing or malformed body with 400 before our code runs.
                    StatusCodes.Status400BadRequest => "validation.failed",
                    StatusCodes.Status401Unauthorized => "auth.unauthenticated",
                    StatusCodes.Status403Forbidden => "auth.forbidden",
                    StatusCodes.Status404NotFound => "resource.not_found",
                    StatusCodes.Status405MethodNotAllowed => "http.method_not_allowed",
                    StatusCodes.Status429TooManyRequests => "http.too_many_requests",
                    >= 500 => "server.error",
                    _ => "http.error",
                }));

        // Health: live = the process answers; ready = it can also reach the database.
        services.AddHealthChecks()
            .AddDbContextCheck<FixFlowDbContext>("database", tags: ["ready"]);
        // EF's retry-on-failure would keep a failing check retrying for minutes; a probe must answer quickly.
        services.Configure<HealthCheckServiceOptions>(options =>
            options.Registrations.Single(r => r.Name == "database").Timeout = TimeSpan.FromSeconds(5));

        // CORS: only the origins listed in configuration may call the API from a browser.
        // AllowCredentials: the browser may send the refresh cookie cross-origin (dev: :4200 -> :8080). Needs explicit origins.
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options =>
            options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

        // Controllers (thin) calling feature services behind interfaces.
        // JSON conventions: enums as strings ("Supervisor", not 3). camelCase is already the default.
        services.AddControllers()
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddOpenApi();

        return services;
    }

    /// <summary>Feature services, one line per service. Each new feature registers its service here.</summary>
    public static IServiceCollection AddFixFlowFeatures(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);

        // Auth
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<TokenService>();

        // Platform (Control Panel)
        services.AddScoped<IPlatformAudit, PlatformAudit>();
        services.AddScoped<ITenantService, TenantService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddMemoryCache();
        services.AddScoped<IAccessStatus, AccessStatus>();
        services.AddScoped<IPlatformDashboardService, PlatformDashboardService>();

        return services;
    }
}
