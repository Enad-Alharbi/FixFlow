using FixFlow.Api.Features.Users;
using FixFlow.Api.Infrastructure.Startup;

// FixFlow API entry point. Each concern lives in its own extension file under Infrastructure/Startup.
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddFixFlowPersistence(builder.Configuration)    // SQL Server, EF Core, tenancy
    .AddFixFlowIdentity(builder.Configuration)       // users, passwords, lockout
    .AddFixFlowAuthentication()                      // JWT, policies, rate limiting
    .AddFixFlowApi(builder.Configuration)            // ProblemDetails, health, CORS, controllers
    .AddFixFlowFeatures();                           // feature services (IAuthService, ...)

var app = builder.Build();

// "FixFlow.Api admin bootstrap|recover": run a server-side admin command and exit, without starting the web server.
if (AdminCommands.IsAdminCommand(args))
    return await AdminCommands.RunAsync(app.Services, app.Configuration, args, Console.Out);

await app.InitializeDevelopmentDatabaseAsync(); // migrate + demo seed; Development only
app.UseFixFlowPipeline();
app.MapFixFlowEndpoints();

app.Run();
return 0;
