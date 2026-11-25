using System.Text.Json;
using FixFlow.Api.Features.Auth;
using FixFlow.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FixFlow.Api.Features.Platform;

/// <summary>
/// One Platform Admin action (CLAUDE.md §10.1): who, what, which tenant, which record, before/after values.
/// Platform-scoped (not tenant-owned). No foreign keys on purpose: the entry must outlive a deleted user or tenant.
/// Never contains passwords, hashes, or tokens.
/// </summary>
public class PlatformAuditEntry
{
    public Guid Id { get; set; }
    public DateTimeOffset At { get; set; }
    public Guid ActorId { get; set; }
    /// <summary>Stable action name, e.g. "tenant.created", "tenant.suspended", "user.created".</summary>
    public required string Action { get; set; }
    public Guid? TenantId { get; set; }
    /// <summary>"Tenant" or "User".</summary>
    public required string TargetType { get; set; }
    public Guid? TargetId { get; set; }
    /// <summary>JSON: { "before": {...}, "after": {...} } with only the fields that matter.</summary>
    public string? Changes { get; set; }
}

public class PlatformAuditEntryConfiguration : IEntityTypeConfiguration<PlatformAuditEntry>
{
    public void Configure(EntityTypeBuilder<PlatformAuditEntry> b)
    {
        b.Property(e => e.Action).HasMaxLength(64);
        b.Property(e => e.TargetType).HasMaxLength(32);
        b.HasIndex(e => new { e.TenantId, e.At }); // a tenant's history, newest first
        b.HasIndex(e => e.At); // the platform-wide log
    }
}

/// <summary>Adds audit entries to the DbContext. The caller's SaveChanges stores them in the same transaction as the change.</summary>
public interface IPlatformAudit
{
    /// <returns>The added entry, so a caller whose change fails validation can detach it again.</returns>
    PlatformAuditEntry Record(string action, string targetType, Guid? targetId, Guid? tenantId, object? before = null, object? after = null);
}

public class PlatformAudit(FixFlowDbContext db, IHttpContextAccessor http, TimeProvider time) : IPlatformAudit
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public PlatformAuditEntry Record(string action, string targetType, Guid? targetId, Guid? tenantId, object? before = null, object? after = null)
    {
        // Platform endpoints all require a signed-in Platform Admin, so the actor is always known here.
        var actorId = Guid.Parse(http.HttpContext?.User.FindFirst(FixFlowClaims.UserId)?.Value
            ?? throw new InvalidOperationException("Platform audit needs a signed-in actor."));

        var entry = new PlatformAuditEntry
        {
            At = time.GetUtcNow(),
            ActorId = actorId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            TenantId = tenantId,
            Changes = before is null && after is null ? null : JsonSerializer.Serialize(new { before, after }, Json),
        };
        db.PlatformAuditEntries.Add(entry);
        return entry;
    }
}
