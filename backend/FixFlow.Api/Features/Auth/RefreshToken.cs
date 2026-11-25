using FixFlow.Api.Features.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FixFlow.Api.Features.Auth;

/// <summary>
/// One refresh token. Only its SHA-256 hash is stored, so a database leak doesn't hand out sessions.
/// Not ITenantOwned: refresh runs before any tenant is known, and the Platform Admin has no tenant (ADR 0005).
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    /// <summary>All tokens rotated from one sign-in share a family; reuse of a rotated token revokes the family.</summary>
    public Guid FamilyId { get; set; }
    public required byte[] TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.Property(t => t.TokenHash).HasMaxLength(32).IsFixedLength();
        b.HasIndex(t => t.TokenHash).IsUnique();
        b.HasIndex(t => t.FamilyId);
        b.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
