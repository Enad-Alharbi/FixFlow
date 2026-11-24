using FixFlow.Api.Infrastructure.Data;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --- Database ---------------------------------------------------------------
// The connection string comes from configuration (in Docker: the ConnectionStrings__Default env var).
// Not validated here so `dotnet ef migrations add` works without a database; a missing value fails on first use.
builder.Services.AddDbContext<FixFlowDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default"), sql => sql.EnableRetryOnFailure()));

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
