namespace FixFlow.Api.Features.Users;

/// <summary>The shared Create User form (CLAUDE.md §7.4). Used by the Platform Admin now and by Supervisors in Phase 5.</summary>
public record CreateUserRequest(
    string? DisplayName,
    string? UserName,
    string? Email,
    string? Phone,
    UserRole? Role,
    string? Password);

/// <summary>Editable identity fields. Role, status, and password have their own explicit actions.</summary>
public record UpdateUserRequest(string? DisplayName, string? UserName, string? Email, string? Phone);

public record ChangeRoleRequest(UserRole? Role);

/// <summary>A new password set by an admin (no current password needed). Never returned or logged.</summary>
public record SetPasswordRequest(string? Password);

/// <summary>
/// ?search=ali&amp;role=Technician&amp;deleted=true&amp;tenantId=…&amp;sort=-lastSignInAt&amp;page=1&amp;pageSize=20.
/// Deleted users are a separate view: deleted=true lists ONLY them, otherwise they are hidden.
/// </summary>
public record UserQuery(
    string? Search, UserRole[]? Role, bool Deleted, Guid? TenantId, string? Sort, int Page = 1, int PageSize = 20);

/// <summary>A row in the user lists; carries the tenant so the global search can show where each user belongs.</summary>
public record UserListItem(
    Guid Id, Guid TenantId, string TenantName, string TenantCode,
    string UserName, string Email, string DisplayName, UserRole Role,
    bool IsActive, bool IsDeleted, DateTimeOffset? DeletedAt, DateTimeOffset? LockoutEnd, DateTimeOffset? LastSignInAt);

/// <summary>A user as the management screens see it. Never includes the password hash or tokens.</summary>
public record UserDetails(
    Guid Id,
    Guid? TenantId,
    string UserName,
    string Email,
    string DisplayName,
    string? Phone,
    UserRole Role,
    bool IsActive,
    bool IsDeleted,
    DateTimeOffset? DeletedAt,
    DateTimeOffset? LockoutEnd, // in the future = locked until then (the "Locked" indicator)
    DateTimeOffset? LastSignInAt);
