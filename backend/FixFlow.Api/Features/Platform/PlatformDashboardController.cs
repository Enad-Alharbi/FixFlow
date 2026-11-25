using FixFlow.Api.Features.Auth;
using FixFlow.Api.Infrastructure.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FixFlow.Api.Features.Platform;

[Route("api/platform/dashboard")]
[Authorize(Policy = AuthPolicies.PlatformAdmin)]
public class PlatformDashboardController(IPlatformDashboardService dashboard) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) => Ok(await dashboard.GetAsync(cancellationToken));
}
