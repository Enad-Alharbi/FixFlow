using FixFlow.Api.Features.Auth;
using FixFlow.Api.Infrastructure.Http;
using FixFlow.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FixFlow.Api.Features.Users;

/// <summary>
/// Tenant app: a Supervisor manages their own tenant's users (CLAUDE.md §7.2). The tenant comes ONLY from the token
/// (§9.2), never from the route or body. Soft-deleted users are not found here, and there is no restore: both are
/// Platform Admin only (the service hides deleted users from any caller with a tenant).
/// </summary>
[Route("api/users")]
[Authorize(Policy = AuthPolicies.TenantSupervisor)]
public class UsersController(IUserManagementService users, ITenantContext tenant) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] UserQuery query, CancellationToken cancellationToken) =>
        OkOrError(await users.ListAsync(tenant.TenantId, query, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        OkOrError(await users.GetAsync(tenant.TenantId, id, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var result = await users.CreateAsync(tenant.TenantId, request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value)
            : ErrorResult(result.Error!);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateUserRequest request, CancellationToken cancellationToken) =>
        OkOrError(await users.UpdateAsync(tenant.TenantId, id, request, cancellationToken));

    [HttpPost("{id:guid}/role")]
    public async Task<IActionResult> ChangeRole(Guid id, ChangeRoleRequest request, CancellationToken cancellationToken) =>
        OkOrError(await users.ChangeRoleAsync(tenant.TenantId, id, request.Role, cancellationToken));

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        OkOrError(await users.SetActiveAsync(tenant.TenantId, id, false, cancellationToken));

    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken) =>
        OkOrError(await users.SetActiveAsync(tenant.TenantId, id, true, cancellationToken));

    [HttpPost("{id:guid}/password")]
    public async Task<IActionResult> SetPassword(Guid id, SetPasswordRequest request, CancellationToken cancellationToken) =>
        OkOrError(await users.SetPasswordAsync(tenant.TenantId, id, request.Password, cancellationToken));

    [HttpPost("{id:guid}/unlock")]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken cancellationToken) =>
        OkOrError(await users.UnlockAsync(tenant.TenantId, id, cancellationToken));

    /// <summary>Soft delete only; a Supervisor can never permanently delete (CLAUDE.md §7.2).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        OkOrError(await users.SoftDeleteAsync(tenant.TenantId, id, cancellationToken));
}
