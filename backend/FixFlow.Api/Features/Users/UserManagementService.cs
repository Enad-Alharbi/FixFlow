using System.Linq.Expressions;
using FixFlow.Api.Features.Platform;
using FixFlow.Api.Infrastructure.Data;
using FixFlow.Api.Infrastructure.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace FixFlow.Api.Features.Users;

public class UserManagementService(
    UserManager<ApplicationUser> users, FixFlowDbContext db, IPlatformAudit audit, TimeProvider time) : IUserManagementService
{
    // EF's own SQL-Server-friendly sequential GUIDs, generated up front so the audit entry can name the new user.
    private static readonly SequentialGuidValueGenerator Ids = new();

    public async Task<Result<PagedResult<UserListItem>>> ListAsync(Guid? tenantId, UserQuery query, CancellationToken cancellationToken)
    {
        if (tenantId is { } routeTenant && !await db.Tenants.AnyAsync(t => t.Id == routeTenant, cancellationToken))
            return ServiceError.NotFound();

        // Tenant users only; deleted users are their own view (CLAUDE.md §7.4), never mixed into the normal list.
        var list = db.Users.AsNoTracking().Where(u => u.TenantId != null && u.IsDeleted == query.Deleted);
        if ((tenantId ?? query.TenantId) is { } id) // the route's tenant wins over the query string's
            list = list.Where(u => u.TenantId == id);
        if (query.Role is { Length: > 0 } roles)
            list = list.Where(u => roles.Contains(u.Role));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim(); // case- and accent-insensitive through the database collation
            list = list.Where(u => u.DisplayName.Contains(search) || u.UserName!.Contains(search) || u.Email!.Contains(search));
        }

        var (page, pageSize) = PagedResult<UserListItem>.Clamp(query.Page, query.PageSize);
        var total = await list.CountAsync(cancellationToken);
        var rows = await Sort(list, query.Sort)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new UserListItem(
                u.Id, u.TenantId!.Value, u.Tenant!.Name, u.Tenant.Code, u.UserName!, u.Email!, u.DisplayName, u.Role,
                u.IsActive, u.IsDeleted, u.DeletedAt, u.LockoutEnd, u.LastSignInAt))
            .ToListAsync(cancellationToken);
        return new PagedResult<UserListItem>(rows, page, pageSize, total);
    }

    public async Task<Result<UserDetails>> GetAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        await FindAsync(tenantId, userId, cancellationToken) is { } user ? ToDetails(user) : ServiceError.NotFound();

    public async Task<Result<UserDetails>> CreateAsync(Guid tenantId, CreateUserRequest request, CancellationToken cancellationToken)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, cancellationToken))
            return ServiceError.NotFound();

        var errors = new FieldErrors();
        if (request.Role is null or UserRole.PlatformAdmin) // a tenant user can never be given the platform role
            errors.Add("role", "user.role_invalid");
        if (string.IsNullOrEmpty(request.Password))
            errors.Add("password", "user.password_too_weak");
        if (errors.Any) return errors.ToError();

        var user = new ApplicationUser
        {
            Id = Ids.Next(null!),
            TenantId = tenantId,
            Role = request.Role!.Value,
            DisplayName = request.DisplayName?.Trim() ?? "",
            UserName = request.UserName?.Trim(),
            Email = request.Email?.Trim(),
            PhoneNumber = UserRules.NormalizePhone(request.Phone),
        };

        // Added before CreateAsync so Identity's own SaveChanges stores the user and its audit entry together.
        var entry = audit.Record("user.created", "User", user.Id, tenantId, after: Snapshot(user));
        var result = await users.CreateAsync(user, request.Password!);
        if (!result.Succeeded)
        {
            db.Entry(entry).State = EntityState.Detached; // nothing was created, so nothing to audit
            return ToFieldErrors(result.Errors);
        }

        return ToDetails(user);
    }

    public async Task<Result<UserDetails>> UpdateAsync(
        Guid tenantId, Guid userId, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await FindAsync(tenantId, userId, cancellationToken);
        if (user is null) return ServiceError.NotFound();
        if (user.IsDeleted) return ServiceError.Conflict("user.deleted");

        var before = Snapshot(user);
        user.DisplayName = request.DisplayName?.Trim() ?? "";
        user.UserName = request.UserName?.Trim();
        user.Email = request.Email?.Trim();
        user.PhoneNumber = UserRules.NormalizePhone(request.Phone);

        // UserManager.UpdateAsync runs the same validators as create (format, uniqueness), re-normalizes the
        // username/email, and saves: the audit entry added just before is stored by that same SaveChanges.
        var entry = audit.Record("user.updated", "User", user.Id, tenantId, before, Snapshot(user));
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            db.Entry(entry).State = EntityState.Detached;
            return ToFieldErrors(result.Errors);
        }
        return ToDetails(user);
    }

    public Task<Result<UserDetails>> ChangeRoleAsync(Guid tenantId, Guid userId, UserRole? role, CancellationToken cancellationToken) =>
        ChangeAsync(tenantId, userId, "user.role_changed", endSessions: true, cancellationToken: cancellationToken,
            change: async user =>
            {
                if (role is null or UserRole.PlatformAdmin)
                    return new FieldErrors().Add("role", "user.role_invalid").ToError();
                if (role != UserRole.Supervisor && await LastSupervisorAsync(user, cancellationToken))
                    return ServiceError.Conflict("user.last_supervisor");
                user.Role = role.Value;
                return null;
            });

    public Task<Result<UserDetails>> SetActiveAsync(Guid tenantId, Guid userId, bool active, CancellationToken cancellationToken) =>
        ChangeAsync(tenantId, userId, active ? "user.reactivated" : "user.deactivated", endSessions: !active,
            cancellationToken: cancellationToken,
            change: async user =>
            {
                if (!active && await LastSupervisorAsync(user, cancellationToken))
                    return ServiceError.Conflict("user.last_supervisor");
                user.IsActive = active;
                return null;
            });

    public Task<Result<UserDetails>> SetPasswordAsync(Guid tenantId, Guid userId, string? password, CancellationToken cancellationToken) =>
        ChangeAsync(tenantId, userId, "user.password_set", endSessions: true, alwaysAudit: true, cancellationToken: cancellationToken,
            change: async user =>
            {
                if (string.IsNullOrEmpty(password))
                    return new FieldErrors().Add("password", "user.password_too_weak").ToError();
                var failures = new List<IdentityError>();
                foreach (var validator in users.PasswordValidators)
                    failures.AddRange((await validator.ValidateAsync(users, user, password)).Errors);
                if (failures.Count > 0) return ToFieldErrors(failures);

                // Hash + new security stamp set directly, so the change and its audit entry share one SaveChanges.
                user.PasswordHash = users.PasswordHasher.HashPassword(user, password);
                user.SecurityStamp = Guid.NewGuid().ToString();
                return null;
            });

    public Task<Result<UserDetails>> UnlockAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        ChangeAsync(tenantId, userId, "user.unlocked", endSessions: false, alwaysAudit: true, cancellationToken: cancellationToken,
            change: user =>
            {
                user.LockoutEnd = null;
                user.AccessFailedCount = 0;
                return Task.FromResult<ServiceError?>(null);
            });

    public Task<Result<UserDetails>> SoftDeleteAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        ChangeAsync(tenantId, userId, "user.deleted", endSessions: true, cancellationToken: cancellationToken,
            change: async user =>
            {
                if (await LastSupervisorAsync(user, cancellationToken))
                    return ServiceError.Conflict("user.last_supervisor");
                user.IsDeleted = true;
                user.DeletedAt = time.GetUtcNow();
                user.DeletedById = audit.ActorId;
                return null;
            });

    public Task<Result<UserDetails>> RestoreAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        ChangeAsync(tenantId, userId, "user.restored", endSessions: false, forDeletedUser: true, cancellationToken: cancellationToken,
            change: user =>
            {
                user.IsDeleted = false;
                user.DeletedAt = null;
                user.DeletedById = null;
                user.IsActive = true; // "back as an active user" (CLAUDE.md §7.4)
                return Task.FromResult<ServiceError?>(null);
            });

    /// <summary>
    /// The shape every user action shares: find within the tenant, refuse on a deleted (or, for restore, a
    /// non-deleted) user, apply the change, audit it in the same SaveChanges, and end the user's sessions if asked.
    /// </summary>
    private async Task<Result<UserDetails>> ChangeAsync(
        Guid tenantId, Guid userId, string action, bool endSessions, Func<ApplicationUser, Task<ServiceError?>> change,
        CancellationToken cancellationToken, bool alwaysAudit = false, bool forDeletedUser = false)
    {
        var user = await FindAsync(tenantId, userId, cancellationToken);
        if (user is null) return ServiceError.NotFound();
        if (user.IsDeleted != forDeletedUser)
            return ServiceError.Conflict(user.IsDeleted ? "user.deleted" : "user.not_deleted");

        var before = Snapshot(user);
        if (await change(user) is { } error) return error;

        var after = Snapshot(user);
        if (!alwaysAudit && before.Equals(after)) return ToDetails(user); // already in that state: nothing to record

        audit.Record(action, "User", user.Id, tenantId, before, after);
        await db.SaveChangesAsync(cancellationToken);

        // Refresh is refused for inactive/deleted users anyway; revoking makes "signed out everywhere" explicit.
        // Access tokens already issued live up to 15 minutes until the per-request check (step 4c, D17).
        if (endSessions)
            await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, time.GetUtcNow()), cancellationToken);

        return ToDetails(user);
    }

    /// <summary>
    /// True when this user is the tenant's only active Supervisor, so removing them would leave none (D18:
    /// applies to everyone, including the Platform Admin).
    /// </summary>
    // shortcut: two admins removing the last two Supervisors at the same moment could both pass; add a locking
    // transaction if Supervisors (Phase 5) make concurrent user management common.
    private async Task<bool> LastSupervisorAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        user is { Role: UserRole.Supervisor, IsActive: true, IsDeleted: false }
        && !await db.Users.AnyAsync(u => u.TenantId == user.TenantId && u.Id != user.Id
            && u.Role == UserRole.Supervisor && u.IsActive && !u.IsDeleted, cancellationToken);

    // Users are outside the global tenant filter (ADR 0002), so the tenant condition is added here, every time.
    private Task<ApplicationUser?> FindAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId, cancellationToken);

    private static IQueryable<ApplicationUser> Sort(IQueryable<ApplicationUser> list, string? sort)
    {
        var descending = sort?.StartsWith('-') == true;
        Expression<Func<ApplicationUser, object?>> key = sort?.TrimStart('-') switch
        {
            "userName" => u => u.UserName,
            "email" => u => u.Email,
            "role" => u => u.Role,
            "lastSignInAt" => u => u.LastSignInAt,
            "tenant" => u => u.Tenant!.Name,
            _ => u => u.DisplayName,
        };
        return descending ? list.OrderByDescending(key).ThenBy(u => u.Id) : list.OrderBy(key).ThenBy(u => u.Id);
    }

    /// <summary>The audited view of a user: identity fields only, never the password hash or stamps.</summary>
    internal static object Snapshot(ApplicationUser u) =>
        new { u.UserName, u.Email, u.DisplayName, Phone = u.PhoneNumber, Role = u.Role.ToString(), u.IsActive, u.IsDeleted };

    internal static UserDetails ToDetails(ApplicationUser u) => new(
        u.Id, u.TenantId, u.UserName!, u.Email!, u.DisplayName, u.PhoneNumber, u.Role,
        u.IsActive, u.IsDeleted, u.DeletedAt, u.LockoutEnd, u.LastSignInAt);

    private static ServiceError ToFieldErrors(IEnumerable<IdentityError> identityErrors)
    {
        var errors = new FieldErrors();
        foreach (var error in identityErrors)
            errors.Add(FieldOf(error.Code), error.Code);
        return errors.ToError();
    }

    /// <summary>Which form field an Identity error code belongs to (codes come from UserErrors).</summary>
    private static string FieldOf(string code) => code switch
    {
        _ when code.StartsWith("user.password") => "password",
        _ when code.StartsWith("user.username") => "userName",
        _ when code.StartsWith("user.email") => "email",
        _ when code.StartsWith("user.display_name") => "displayName",
        _ when code.StartsWith("user.phone") => "phone",
        _ => "",
    };
}
