using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FixFlow.Api.Features.Auth;
using FixFlow.Api.Features.Users;
using FixFlow.Api.Infrastructure.Data;
using FixFlow.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

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
// AddIdentityCore, not AddIdentity: the API authenticates with JWTs, so no Identity cookies.
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
    .AddSignInManager() // only for CheckPasswordSignInAsync (password check + lockout counting)
    .AddErrorDescriber<FixFlowIdentityErrorDescriber>()
    .AddUserValidator<UserRulesValidator>()
    .AddPasswordValidator<PasswordRulesValidator>();
// Lockout (attempts, duration) comes from configuration so it can change without a code change.
// Binds the "Identity" section onto IdentityOptions (only Lockout is set there today).
builder.Services.Configure<IdentityOptions>(builder.Configuration.GetSection("Identity"));

// --- Authentication: JWT access tokens (ADR 0005) ---
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
builder.Services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.Section).ValidateOnStart();
builder.Services.AddScoped<TokenService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
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
    });
AuthPolicies.Add(builder.Services.AddAuthorizationBuilder());

// --- Rate limiting: slows password guessing on /api/auth/* (per client IP) ---
// shortcut: behind a reverse proxy every client shares one IP; read X-Forwarded-For when the proxy arrives (Phase 18).
builder.Services.AddRateLimiter(options =>
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

// --- Errors: every error response is RFC 9457 ProblemDetails with a stable `code` ---
builder.Services.AddProblemDetails(options =>
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

// --- Health: live = the process answers; ready = it can also reach the database ---
builder.Services.AddHealthChecks()
    .AddDbContextCheck<FixFlowDbContext>("database", tags: ["ready"]);
// EF's retry-on-failure would keep a failing check retrying for minutes; a probe must answer quickly.
builder.Services.Configure<HealthCheckServiceOptions>(options =>
    options.Registrations.Single(r => r.Name == "database").Timeout = TimeSpan.FromSeconds(5));

// --- CORS: only the origins listed in configuration may call the API from a browser ---
// AllowCredentials: the browser may send the refresh cookie cross-origin (dev: :4200 -> :8080). Needs explicit origins.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// --- API: controllers (thin) calling feature services behind interfaces ---
// JSON conventions: enums as strings ("Supervisor", not 3). camelCase is already the default.
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddOpenApi();

var app = builder.Build();

// "FixFlow.Api admin bootstrap|recover": run a server-side admin command and exit, without starting the web server.
if (AdminCommands.IsAdminCommand(args))
    return await AdminCommands.RunAsync(app.Services, app.Configuration, args, Console.Out);

// Turns unhandled exceptions into a 500 ProblemDetails (details go to the log, never to the client).
app.UseExceptionHandler();
// Turns empty 4xx responses (e.g. unknown route, 401 challenge) into ProblemDetails.
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();

    // Development only: apply pending migrations at startup. Production uses a deliberate migration step.
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<FixFlowDbContext>().Database.MigrateAsync();
    // Development only: demo tenants and users (skipped when Seed:DemoPassword is not set).
    await DevSeeder.SeedAsync(scope.ServiceProvider, app.Configuration["Seed:DemoPassword"],
        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FixFlow.Seed"));
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Every log line written while handling a signed-in request carries who and which tenant (never the token).
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated != true)
    {
        await next();
        return;
    }
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("FixFlow.Request");
    using (logger.BeginScope(new Dictionary<string, object?>
           {
               ["UserId"] = context.User.FindFirst(FixFlowClaims.UserId)?.Value,
               ["TenantId"] = context.User.FindFirst(FixFlowClaims.TenantId)?.Value,
           }))
        await next();
});

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();
app.MapControllers();

app.Run();
return 0;
