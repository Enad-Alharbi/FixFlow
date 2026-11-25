using FixFlow.Api.Features.Auth;
using FixFlow.Api.Infrastructure.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FixFlow.Api.Features.Tenants;

/// <summary>Control Panel: tenants. Platform Admin only; tenant users get 403 on every action.</summary>
[Route("api/platform/tenants")]
[Authorize(Policy = AuthPolicies.PlatformAdmin)]
public class TenantsController(ITenantService tenants) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] TenantQuery query, CancellationToken cancellationToken) =>
        Ok(await tenants.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        OkOrError(await tenants.GetAsync(id, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(CreateTenantRequest request, CancellationToken cancellationToken)
    {
        var result = await tenants.CreateAsync(request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value)
            : ErrorResult(result.Error!);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateTenantRequest request, CancellationToken cancellationToken) =>
        OkOrError(await tenants.UpdateAsync(id, request, cancellationToken));

    // Lifecycle actions are explicit endpoints, not a generic status PATCH (architecture.md §7).
    [HttpPost("{id:guid}/suspend")]
    public async Task<IActionResult> Suspend(Guid id, CancellationToken cancellationToken) =>
        OkOrError(await tenants.ChangeStatusAsync(id, TenantStatusAction.Suspend, null, cancellationToken));

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken) =>
        OkOrError(await tenants.ChangeStatusAsync(id, TenantStatusAction.Reactivate, null, cancellationToken));

    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken) =>
        OkOrError(await tenants.ChangeStatusAsync(id, TenantStatusAction.Archive, null, cancellationToken));

    [HttpPost("{id:guid}/unarchive")]
    public async Task<IActionResult> Unarchive(Guid id, UnarchiveTenantRequest request, CancellationToken cancellationToken) =>
        OkOrError(await tenants.ChangeStatusAsync(id, TenantStatusAction.Unarchive, request.Status, cancellationToken));
}
