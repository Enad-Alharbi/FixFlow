using System.Linq.Expressions;
using System.Text.RegularExpressions;
using FixFlow.Api.Features.Auth;
using FixFlow.Api.Features.Platform;
using FixFlow.Api.Features.Users;
using FixFlow.Api.Infrastructure.Data;
using FixFlow.Api.Infrastructure.Http;
using Microsoft.EntityFrameworkCore;

namespace FixFlow.Api.Features.Tenants;

public partial class TenantService(
    FixFlowDbContext db, IUserManagementService userManagement, IPlatformAudit audit, IAccessStatus access, TimeProvider time)
    : ITenantService
{
    /// <summary>The tenant lifecycle (CLAUDE.md §9.4), in one place: which statuses each action may start from.</summary>
    private static readonly Dictionary<TenantStatusAction, TenantStatus[]> AllowedFrom = new()
    {
        [TenantStatusAction.Suspend] = [TenantStatus.Active],
        [TenantStatusAction.Reactivate] = [TenantStatus.Suspended],
        [TenantStatusAction.Archive] = [TenantStatus.Active, TenantStatus.Suspended],
        [TenantStatusAction.Unarchive] = [TenantStatus.Archived],
    };

    // 2–30 characters: lowercase letters, digits, "-"; starts with a letter. Fixed after creation (future URLs).
    [GeneratedRegex("^[a-z][a-z0-9-]{1,29}$")]
    private static partial Regex CodeFormat();

    public async Task<PagedResult<TenantListItem>> ListAsync(TenantQuery query, CancellationToken cancellationToken)
    {
        var tenants = db.Tenants.AsNoTracking();

        // Archived tenants are hidden unless the admin asks for them (CLAUDE.md §9.4).
        tenants = query.Status is { Length: > 0 } statuses
            ? tenants.Where(t => statuses.Contains(t.Status))
            : tenants.Where(t => t.Status != TenantStatus.Archived);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim(); // the Arabic_100_CI_AI collation makes this case- and accent-insensitive
            tenants = tenants.Where(t => t.Name.Contains(search) || t.Code.Contains(search));
        }

        var (page, pageSize) = PagedResult<TenantListItem>.Clamp(query.Page, query.PageSize);
        var total = await tenants.CountAsync(cancellationToken);
        // Sort and page on the entity, THEN project: EF can't sort on a record built through its constructor.
        var rows = await Sort(tenants, query.Sort)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(t => new TenantListItem(
                t.Id, t.Name, t.Code, t.Status, t.DefaultLanguage, t.TimeZone,
                db.Users.Count(u => u.TenantId == t.Id && !u.IsDeleted), t.CreatedAt))
            .ToListAsync(cancellationToken);
        return new PagedResult<TenantListItem>(rows, page, pageSize, total);
    }

    public async Task<Result<TenantDetails>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        return tenant is null ? ServiceError.NotFound() : await ToDetailsAsync(tenant, cancellationToken);
    }

    public async Task<Result<TenantDetails>> CreateAsync(CreateTenantRequest request, CancellationToken cancellationToken)
    {
        var errors = Validate(request.Name, request.DefaultLanguage, request.TimeZone,
            request.ContactName, request.ContactEmail, request.ContactPhone);
        var code = request.Code?.Trim() ?? "";
        if (!CodeFormat().IsMatch(code))
            errors.Add("code", "tenant.code_invalid");
        else if (await db.Tenants.AnyAsync(t => t.Code == code, cancellationToken))
            errors.Add("code", "tenant.code_taken");
        if (request.FirstSupervisor is null)
            errors.Add("firstSupervisor", "validation.required");
        if (errors.Any) return errors.ToError();

        var now = time.GetUtcNow();
        var tenant = new Tenant
        {
            Name = request.Name!.Trim(),
            Code = code,
            DefaultLanguage = request.DefaultLanguage!,
            TimeZone = request.TimeZone!,
            ContactName = Clean(request.ContactName),
            ContactEmail = Clean(request.ContactEmail),
            ContactPhone = UserRules.NormalizePhone(request.ContactPhone),
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Retry-on-failure (Program: EnableRetryOnFailure) may re-run work after a transient error, so a manual
        // transaction must run INSIDE the execution strategy: a retry then repeats the whole unit, never half of it.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<Result<TenantDetails>>(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            db.Tenants.Add(tenant);
            audit.Record("tenant.created", "Tenant", tenant.Id, tenant.Id, after: Snapshot(tenant));
            await db.SaveChangesAsync(cancellationToken);

            // The first Supervisor is always a Supervisor, whatever role the request carried.
            var supervisor = await userManagement.CreateAsync(
                tenant.Id, request.FirstSupervisor! with { Role = UserRole.Supervisor }, cancellationToken);
            if (!supervisor.IsSuccess)
            {
                await transaction.RollbackAsync(cancellationToken); // no tenant without its Supervisor
                db.ChangeTracker.Clear();
                return Prefixed(supervisor.Error!, "firstSupervisor");
            }

            await transaction.CommitAsync(cancellationToken);
            return await ToDetailsAsync(tenant, cancellationToken);
        });
    }

    public async Task<Result<TenantDetails>> UpdateAsync(Guid id, UpdateTenantRequest request, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tenant is null) return ServiceError.NotFound();

        var errors = Validate(request.Name, request.DefaultLanguage, request.TimeZone,
            request.ContactName, request.ContactEmail, request.ContactPhone);
        var rowVersion = DecodeRowVersion(request.RowVersion);
        if (rowVersion is null) errors.Add("rowVersion", "validation.required");
        if (errors.Any) return errors.ToError();

        var before = Snapshot(tenant);
        tenant.Name = request.Name!.Trim();
        tenant.DefaultLanguage = request.DefaultLanguage!;
        tenant.TimeZone = request.TimeZone!;
        tenant.ContactName = Clean(request.ContactName);
        tenant.ContactEmail = Clean(request.ContactEmail);
        tenant.ContactPhone = UserRules.NormalizePhone(request.ContactPhone);
        tenant.UpdatedAt = time.GetUtcNow();
        // Optimistic concurrency: the UPDATE only matches if the row still has the version the admin read.
        db.Entry(tenant).Property(t => t.RowVersion).OriginalValue = rowVersion!;
        audit.Record("tenant.updated", "Tenant", tenant.Id, tenant.Id, before, Snapshot(tenant));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceError.Concurrency(); // someone else saved first; the client reloads and retries
        }
        return await ToDetailsAsync(tenant, cancellationToken);
    }

    public async Task<Result<TenantDetails>> ChangeStatusAsync(
        Guid id, TenantStatusAction action, TenantStatus? unarchiveTo, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tenant is null) return ServiceError.NotFound();
        if (!AllowedFrom[action].Contains(tenant.Status))
            return ServiceError.Conflict("tenant.invalid_transition");

        var target = action switch
        {
            TenantStatusAction.Suspend => TenantStatus.Suspended,
            TenantStatusAction.Reactivate => TenantStatus.Active,
            TenantStatusAction.Archive => TenantStatus.Archived,
            _ => unarchiveTo,
        };
        if (target is not (TenantStatus.Active or TenantStatus.Suspended or TenantStatus.Archived)
            || (action == TenantStatusAction.Unarchive && target == TenantStatus.Archived))
            return new FieldErrors().Add("status", "tenant.status_invalid").ToError();

        var before = new { Status = tenant.Status.ToString() };
        tenant.Status = target.Value;
        tenant.UpdatedAt = time.GetUtcNow();
        audit.Record($"tenant.{ActionName(action)}", "Tenant", tenant.Id, tenant.Id, before, new { Status = tenant.Status.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        await access.ForgetTenantAsync(tenant.Id, cancellationToken); // existing access tokens stop (or work again) now

        // Suspended or archived: end every session now. Refresh is refused anyway, but revoking makes it explicit.
        if (tenant.Status != TenantStatus.Active)
            await db.RefreshTokens.Where(t => t.User.TenantId == tenant.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, time.GetUtcNow()), cancellationToken);

        return await ToDetailsAsync(tenant, cancellationToken);
    }

    private static FieldErrors Validate(
        string? name, string? language, string? timeZone, string? contactName, string? contactEmail, string? contactPhone)
    {
        var errors = new FieldErrors();
        if (name?.Trim() is not { Length: >= 2 and <= 200 })
            errors.Add("name", "tenant.name_invalid");
        if (language is not ("en" or "ar"))
            errors.Add("defaultLanguage", "tenant.language_invalid");
        // IANA ids only (e.g. "Asia/Riyadh"), not Windows ids, so the browser's Intl can use them too.
        if (string.IsNullOrWhiteSpace(timeZone) || !TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZone, out _))
            errors.Add("timeZone", "tenant.time_zone_invalid");
        if (contactName?.Trim().Length > 100)
            errors.Add("contactName", "tenant.contact_name_invalid");
        if (Clean(contactEmail) is { } email && !UserRules.IsValidEmail(email))
            errors.Add("contactEmail", "tenant.contact_email_invalid");
        if (UserRules.NormalizePhone(contactPhone) is { } phone && !UserRules.IsValidPhone(phone))
            errors.Add("contactPhone", "tenant.contact_phone_invalid");
        return errors;
    }

    private IQueryable<Tenant> Sort(IQueryable<Tenant> tenants, string? sort)
    {
        var descending = sort?.StartsWith('-') == true;
        Expression<Func<Tenant, object>> key = sort?.TrimStart('-') switch
        {
            "code" => t => t.Code,
            "status" => t => t.Status,
            "createdAt" => t => t.CreatedAt,
            "userCount" => t => db.Users.Count(u => u.TenantId == t.Id && !u.IsDeleted),
            _ => t => t.Name, // unknown keys fall back instead of erroring: sorting is a convenience
        };
        // Id as a tie-breaker keeps pages stable when many rows share the sort value.
        return descending ? tenants.OrderByDescending(key).ThenBy(t => t.Id) : tenants.OrderBy(key).ThenBy(t => t.Id);
    }

    private async Task<TenantDetails> ToDetailsAsync(Tenant t, CancellationToken cancellationToken) => new(
        t.Id, t.Name, t.Code, t.Status, t.DefaultLanguage, t.TimeZone, t.ContactName, t.ContactEmail, t.ContactPhone,
        await db.Users.CountAsync(u => u.TenantId == t.Id && !u.IsDeleted, cancellationToken),
        t.CreatedAt, t.UpdatedAt, Convert.ToBase64String(t.RowVersion));

    private static object Snapshot(Tenant t) => new
    {
        t.Name, t.Code, Status = t.Status.ToString(), t.DefaultLanguage, t.TimeZone, t.ContactName, t.ContactEmail, t.ContactPhone,
    };

    /// <summary>Puts the user's field errors under the request property they came from: "email" → "firstSupervisor.email".</summary>
    private static ServiceError Prefixed(ServiceError error, string prefix) => error with
    {
        Errors = error.Errors?.ToDictionary(e => $"{prefix}.{e.Key}", e => e.Value),
    };

    private static string ActionName(TenantStatusAction action) => action switch
    {
        TenantStatusAction.Suspend => "suspended",
        TenantStatusAction.Reactivate => "reactivated",
        TenantStatusAction.Archive => "archived",
        _ => "unarchived",
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static byte[]? DecodeRowVersion(string? value)
    {
        var buffer = new byte[8];
        return value is not null && Convert.TryFromBase64String(value, buffer, out var written) && written == 8 ? buffer : null;
    }
}
