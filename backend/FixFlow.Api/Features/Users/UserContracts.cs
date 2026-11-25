namespace FixFlow.Api.Features.Users;

/// <summary>The shared Create User form (CLAUDE.md §7.4). Used by the Platform Admin now and by Supervisors in Phase 5.</summary>
public record CreateUserRequest(
    string? DisplayName,
    string? UserName,
    string? Email,
    string? Phone,
    UserRole? Role,
    string? Password);

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
    DateTimeOffset? LockoutEnd,
    DateTimeOffset? LastSignInAt);
