using FixFlow.Api.Features.Tenants;
using FixFlow.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FixFlow.Api.Features.Platform;

public record TenantCounts(int Active, int Suspended, int Archived);

/// <summary>Tenant users only (the Platform Admin is not counted). Locked = lockout ends in the future.</summary>
public record UserCounts(int Active, int Deactivated, int Locked, int Deleted);

/// <summary>One audit entry with readable names; a name is null when that user/tenant no longer exists.</summary>
public record RecentActivityItem(
    Guid Id, DateTimeOffset At, string Action, string? ActorName,
    Guid? TenantId, string? TenantName, string TargetType, Guid? TargetId, string? TargetName);

/// <summary>The basic platform dashboard (Phase 4): real counts and recent platform activity, nothing invented.</summary>
public record PlatformDashboard(TenantCounts Tenants, UserCounts Users, IReadOnlyList<RecentActivityItem> RecentActivity);

public interface IPlatformDashboardService
{
    Task<PlatformDashboard> GetAsync(CancellationToken cancellationToken);
}

public class PlatformDashboardService(FixFlowDbContext db, TimeProvider time) : IPlatformDashboardService
{
    private const int RecentActivityCount = 10;

    public async Task<PlatformDashboard> GetAsync(CancellationToken cancellationToken)
    {
        var byStatus = await db.Tenants.GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);

        var now = time.GetUtcNow();
        var tenantUsers = db.Users.Where(u => u.TenantId != null);
        var users = new UserCounts(
            await tenantUsers.CountAsync(u => u.IsActive && !u.IsDeleted, cancellationToken),
            await tenantUsers.CountAsync(u => !u.IsActive && !u.IsDeleted, cancellationToken),
            await tenantUsers.CountAsync(u => u.LockoutEnd > now && !u.IsDeleted, cancellationToken),
            await tenantUsers.CountAsync(u => u.IsDeleted, cancellationToken));

        // Audit entries have no foreign keys (they outlive what they describe), so names are looked up, not joined.
        var recent = await db.PlatformAuditEntries.AsNoTracking()
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
            .Take(RecentActivityCount)
            .Select(e => new RecentActivityItem(
                e.Id, e.At, e.Action,
                db.Users.Where(u => u.Id == e.ActorId).Select(u => u.DisplayName).FirstOrDefault(),
                e.TenantId,
                db.Tenants.Where(t => t.Id == e.TenantId).Select(t => t.Name).FirstOrDefault(),
                e.TargetType, e.TargetId,
                e.TargetType == "User"
                    ? db.Users.Where(u => u.Id == e.TargetId).Select(u => u.DisplayName).FirstOrDefault()
                    : db.Tenants.Where(t => t.Id == e.TargetId).Select(t => t.Name).FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new PlatformDashboard(
            new TenantCounts(
                byStatus.GetValueOrDefault(TenantStatus.Active),
                byStatus.GetValueOrDefault(TenantStatus.Suspended),
                byStatus.GetValueOrDefault(TenantStatus.Archived)),
            users, recent);
    }
}
