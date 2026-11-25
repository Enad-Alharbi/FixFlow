using FixFlow.Api.Features.Auth;
using FixFlow.Api.Infrastructure.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FixFlow.Api.Features.Users;

/// <summary>
/// Control Panel: the users of one tenant. The tenant comes from the route (CLAUDE.md §9.2), accepted only for the
/// Platform Admin. A user id from another tenant is 404, never 403.
/// </summary>
[Route("api/platform/tenants/{tenantId:guid}/users")]
[Authorize(Policy = AuthPolicies.PlatformAdmin)]
public class TenantUsersController(IUserManagementService users) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid tenantId, [FromQuery] UserQuery query, CancellationToken cancellationToken) =>
        OkOrError(await users.ListAsync(tenantId, query, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        OkOrError(await users.GetAsync(tenantId, id, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(Guid tenantId, CreateUserRequest request, CancellationToken cancellationToken)
    {
        var result = await users.CreateAsync(tenantId, request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { tenantId, id = result.Value!.Id }, result.Value)
            : ErrorResult(result.Error!);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid tenantId, Guid id, UpdateUserRequest request, CancellationToken cancellationToken) =>
        OkOrError(await users.UpdateAsync(tenantId, id, request, cancellationToken));

    [HttpPost("{id:guid}/role")]
    public async Task<IActionResult> ChangeRole(Guid tenantId, Guid id, ChangeRoleRequest request, CancellationToken cancellationToken) =>
        OkOrError(await users.ChangeRoleAsync(tenantId, id, request.Role, cancellationToken));

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        OkOrError(await users.SetActiveAsync(tenantId, id, false, cancellationToken));

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        OkOrError(await users.SetActiveAsync(tenantId, id, true, cancellationToken));

    [HttpPost("{id:guid}/password")]
    public async Task<IActionResult> SetPassword(Guid tenantId, Guid id, SetPasswordRequest request, CancellationToken cancellationToken) =>
        OkOrError(await users.SetPasswordAsync(tenantId, id, request.Password, cancellationToken));

    [HttpPost("{id:guid}/unlock")]
    public async Task<IActionResult> Unlock(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        OkOrError(await users.UnlockAsync(tenantId, id, cancellationToken));

    /// <summary>Soft delete. Permanent deletion is a separate, later action (Phase 5, D13).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        OkOrError(await users.SoftDeleteAsync(tenantId, id, cancellationToken));

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        OkOrError(await users.RestoreAsync(tenantId, id, cancellationToken));
}

/// <summary>Control Panel: search users across every tenant; ?deleted=true is the Deleted users view.</summary>
[Route("api/platform/users")]
[Authorize(Policy = AuthPolicies.PlatformAdmin)]
public class PlatformUsersController(IUserManagementService users) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] UserQuery query, CancellationToken cancellationToken) =>
        OkOrError(await users.ListAsync(null, query, cancellationToken));
}
