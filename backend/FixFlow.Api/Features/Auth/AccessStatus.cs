using System.Security.Claims;
using FixFlow.Api.Features.Tenants;
using FixFlow.Api.Features.Users;
using FixFlow.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FixFlow.Api.Features.Auth;

/// <summary>
/// The per-request status check (D17). A JWT stays cryptographically valid for 15 minutes, but a suspended tenant,
/// a deactivated/deleted user, or a changed role must stop it sooner. Each authenticated request compares the token
/// with the user's current state, read from the database at most every ~30 seconds per user.
/// </summary>
public interface IAccessStatus
{
    Task<bool> IsAllowedAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);

    /// <summary>Drops cached state so an admin's change applies on the user's very next request.</summary>
    void Forget(Guid userId);

    /// <summary>Same, for every user of a tenant (suspend, archive).</summary>
    Task ForgetTenantAsync(Guid tenantId, CancellationToken cancellationToken);
}

// shortcut: an in-process cache, so Forget only reaches this API instance; other instances catch up within the
// 30-second lifetime. Use a shared cache or a message when the API runs on more than one instance (Phase 18).
public class AccessStatus(FixFlowDbContext db, IMemoryCache cache) : IAccessStatus
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private record UserState(bool IsActive, bool IsDeleted, UserRole Role, TenantStatus? TenantStatus);

    public async Task<bool> IsAllowedAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(FixFlowClaims.UserId), out var userId)) return false;

        var state = await cache.GetOrCreateAsync(Key(userId), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Lifetime;
            return db.Users.AsNoTracking().Where(u => u.Id == userId)
                .Select(u => new UserState(u.IsActive, u.IsDeleted, u.Role, u.Tenant == null ? null : u.Tenant.Status))
                .SingleOrDefaultAsync(cancellationToken);
        });

        // A role change also fails the old token: the client refreshes (or signs in) and gets the new role.
        return state is { IsActive: true, IsDeleted: false }
            && state.TenantStatus is null or TenantStatus.Active
            && state.Role.ToString() == principal.FindFirstValue(FixFlowClaims.Role);
    }

    public void Forget(Guid userId) => cache.Remove(Key(userId));

    public async Task ForgetTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        foreach (var id in await db.Users.Where(u => u.TenantId == tenantId).Select(u => u.Id).ToListAsync(cancellationToken))
            Forget(id);
    }

    private static string Key(Guid userId) => $"access-status:{userId}";
}
