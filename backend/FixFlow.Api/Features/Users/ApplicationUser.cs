using FixFlow.Api.Features.Tenants;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FixFlow.Api.Features.Users;

/// <summary>Exactly one role per user (CLAUDE.md §7.2), so a column rather than Identity's many-to-many role tables.</summary>
public enum UserRole { PlatformAdmin, Requester, Technician, Supervisor }

/// <summary>
/// Identity user. Tenant-owned, except the Platform Admin (TenantId null). Deliberately NOT under the global
/// tenant filter: sign-in must find a user before any tenant is known (ADR 0002).
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public Guid? TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public UserRole Role { get; set; }
    public required string DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedById { get; set; }
    public DateTimeOffset? LastSignInAt { get; set; }
}

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
        b.Property(u => u.DisplayName).HasMaxLength(UserRules.DisplayNameMaxLength);
        b.Property(u => u.PhoneNumber).HasMaxLength(20);

        // Identity's email index is not unique; email is unique across the whole platform.
        b.HasIndex(u => u.NormalizedEmail).IsUnique();
        b.HasIndex(u => u.TenantId);

        // Tenants are never cascade-deleted by accident; permanent tenant deletion is its own operation (Phase 12c).
        b.HasOne(u => u.Tenant).WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Restrict);

        // The database itself refuses a Platform Admin inside a tenant or a tenant user without one.
        b.ToTable(t => t.HasCheckConstraint("CK_AspNetUsers_TenantRole",
            "([TenantId] IS NULL AND [Role] = 'PlatformAdmin') OR ([TenantId] IS NOT NULL AND [Role] <> 'PlatformAdmin')"));
    }
}
