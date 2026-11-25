using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FixFlow.Api.Features.Tenants;

public enum TenantStatus { Active, Suspended, Archived }

/// <summary>An organization using FixFlow. Platform-scoped: it is the root of isolation, not tenant-owned.</summary>
public class Tenant
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    /// <summary>Short, unique, human-friendly slug (e.g. "acme").</summary>
    public required string Code { get; set; }
    public TenantStatus Status { get; set; } = TenantStatus.Active;
    /// <summary>"ar" or "en".</summary>
    public required string DefaultLanguage { get; set; }
    /// <summary>IANA id, e.g. "Asia/Riyadh". Display times are converted to this zone.</summary>
    public required string TimeZone { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.Property(t => t.Name).HasMaxLength(200);
        b.Property(t => t.Code).HasMaxLength(50);
        b.HasIndex(t => t.Code).IsUnique();
        b.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(t => t.DefaultLanguage).HasMaxLength(2).IsFixedLength();
        b.Property(t => t.TimeZone).HasMaxLength(64);
        b.Property(t => t.ContactName).HasMaxLength(100);
        b.Property(t => t.ContactEmail).HasMaxLength(254);
        b.Property(t => t.ContactPhone).HasMaxLength(20);
        b.Property(t => t.RowVersion).IsRowVersion();
    }
}
