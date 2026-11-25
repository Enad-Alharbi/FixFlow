using FixFlow.Api.Features.Auth;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace FixFlow.Api.Infrastructure.Startup;

public static class PipelineExtensions
{
    /// <summary>
    /// The middleware every request passes through. Order matters: errors first (so they catch everything),
    /// then CORS, then who you are (authentication), what you may do (authorization), and how often (rate limit).
    /// </summary>
    public static WebApplication UseFixFlowPipeline(this WebApplication app)
    {
        // Turns unhandled exceptions into a 500 ProblemDetails (details go to the log, never to the client).
        app.UseExceptionHandler();
        // Turns empty 4xx responses (e.g. unknown route, 401 challenge) into ProblemDetails.
        app.UseStatusCodePages();

        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseRequestLogScope();

        return app;
    }

    /// <summary>Health probes, controllers, and (Development only) the OpenAPI document.</summary>
    public static WebApplication MapFixFlowEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
            app.MapOpenApi().AllowAnonymous();

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();
        app.MapControllers();

        return app;
    }

    /// <summary>Every log line written while handling a signed-in request carries who and which tenant (never the token).</summary>
    private static void UseRequestLogScope(this WebApplication app) => app.Use(async (context, next) =>
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
}
