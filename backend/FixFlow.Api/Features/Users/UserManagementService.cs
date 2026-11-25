using FixFlow.Api.Features.Platform;
using FixFlow.Api.Infrastructure.Data;
using FixFlow.Api.Infrastructure.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace FixFlow.Api.Features.Users;

public class UserManagementService(UserManager<ApplicationUser> users, FixFlowDbContext db, IPlatformAudit audit)
    : IUserManagementService
{
    // EF's own SQL-Server-friendly sequential GUIDs, generated up front so the audit entry can name the new user.
    private static readonly SequentialGuidValueGenerator Ids = new();

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
            foreach (var error in result.Errors)
                errors.Add(FieldOf(error.Code), error.Code);
            return errors.ToError();
        }

        return ToDetails(user);
    }

    /// <summary>The audited view of a user: identity fields only, never the password hash or stamps.</summary>
    internal static object Snapshot(ApplicationUser u) =>
        new { u.UserName, u.Email, u.DisplayName, Phone = u.PhoneNumber, Role = u.Role.ToString(), u.IsActive, u.IsDeleted };

    internal static UserDetails ToDetails(ApplicationUser u) => new(
        u.Id, u.TenantId, u.UserName!, u.Email!, u.DisplayName, u.PhoneNumber, u.Role,
        u.IsActive, u.IsDeleted, u.LockoutEnd, u.LastSignInAt);

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
