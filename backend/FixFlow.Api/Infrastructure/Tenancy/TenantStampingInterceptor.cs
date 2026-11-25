using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FixFlow.Api.Infrastructure.Tenancy;

/// <summary>
/// Runs before every SaveChanges. New tenant-owned rows get the current TenantId; any write that would touch
/// another tenant's row, or move a row between tenants, throws and nothing is saved.
/// </summary>
public sealed class TenantStampingInterceptor(ITenantContext tenant) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries<ITenantOwned>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.TenantId == Guid.Empty)
                        entry.Entity.TenantId = tenant.TenantId; // throws without a tenant: fail closed
                    else if (entry.Entity.TenantId != tenant.TenantId)
                        throw new TenantIsolationException($"A new {entry.Metadata.ClrType.Name} belongs to a different tenant.");
                    break;

                case EntityState.Modified or EntityState.Deleted:
                    var property = entry.Property(e => e.TenantId);
                    if (property.IsModified && !Equals(property.OriginalValue, property.CurrentValue))
                        throw new TenantIsolationException($"The TenantId of a {entry.Metadata.ClrType.Name} cannot change.");
                    if (entry.Entity.TenantId != tenant.TenantId)
                        throw new TenantIsolationException($"A {entry.Metadata.ClrType.Name} of a different tenant cannot be changed.");
                    break;
            }
        }
    }
}
