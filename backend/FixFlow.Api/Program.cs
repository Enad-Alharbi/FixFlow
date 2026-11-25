using FixFlow.Api.Features.Users;
using FixFlow.Api.Infrastructure.Data;
using FixFlow.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --- Database ---------------------------------------------------------------
// The connection string comes from configuration (in Docker: the ConnectionStrings__Default env var).
// Not validated here so `dotnet ef migrations add` works without a database; a missing value fails on first use.
builder.Services.AddDbContext<FixFlowDbContext>((services, options) => options
    .UseSqlServer(builder.Configuration.GetConnectionString("Default"), sql => sql.EnableRetryOnFailure())
    .AddInterceptors(services.GetRequiredService<TenantStampingInterceptor>()));

// --- Tenancy: the current tenant comes only from the token (see TenantContext) ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<TenantStampingInterceptor>();

// --- Identity: user store, password hashing, and the account rules from CLAUDE.md §7.4 ---
// AddIdentityCore, not AddIdentity: the API authenticates with JWTs (Phase 3c), so no Identity cookies.
builder.Services.AddIdentityCore<ApplicationUser>(options =>
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
    .AddErrorDescriber<FixFlowIdentityErrorDescriber>()
    .AddUserValidator<UserRulesValidator>()
    .AddPasswordValidator<PasswordRulesValidator>();
// Lockout (attempts, duration) comes from configuration so it can change without a code change.
// Binds the "Identity" section onto IdentityOptions (only Lockout is set there today).
builder.Services.Configure<IdentityOptions>(builder.Configuration.GetSection("Identity"));

// --- Errors: every error response is RFC 9457 ProblemDetails with a stable `code` ---
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions.TryAdd("code", context.ProblemDetails.Status switch
        {
            StatusCodes.Status404NotFound => "resource.not_found",
            StatusCodes.Status405MethodNotAllowed => "http.method_not_allowed",
            >= 500 => "server.error",
            _ => "http.error",
        }));

// --- Health: live = the process answers; ready = it can also reach the database ---
builder.Services.AddHealthChecks()
    .AddDbContextCheck<FixFlowDbContext>("database", tags: ["ready"]);
// EF's retry-on-failure would keep a failing check retrying for minutes; a probe must answer quickly.
builder.Services.Configure<HealthCheckServiceOptions>(options =>
    options.Registrations.Single(r => r.Name == "database").Timeout = TimeSpan.FromSeconds(5));

// --- CORS: only the origins listed in configuration may call the API from a browser ---
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Turns unhandled exceptions into a 500 ProblemDetails (details go to the log, never to the client).
app.UseExceptionHandler();
// Turns empty 4xx responses (e.g. unknown route) into ProblemDetails.
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Development only: apply pending migrations at startup. Production uses a deliberate migration step.
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<FixFlowDbContext>().Database.MigrateAsync();
}

app.UseCors();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapControllers();

app.Run();
