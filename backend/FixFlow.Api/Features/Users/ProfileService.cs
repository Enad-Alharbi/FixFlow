using FixFlow.Api.Features.Auth;
using FixFlow.Api.Features.Platform;
using FixFlow.Api.Infrastructure.Data;
using FixFlow.Api.Infrastructure.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FixFlow.Api.Features.Users;

/// <summary>
/// What you may change about yourself (CLAUDE.md §17 Profiles). Email and username are read-only here (D22): a
/// Supervisor or the Platform Admin changes them. Your own password has its own endpoint (POST api/auth/password).
/// </summary>
public record UpdateProfileRequest(string? DisplayName, string? Phone, string? PreferredLanguage);

public interface IProfileService
{
    /// <summary>Updates the caller's own profile and returns it in the same shape as GET api/auth/me.</summary>
    Task<Result<MeResponse>> UpdateAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken);
}

public class ProfileService(UserManager<ApplicationUser> users, FixFlowDbContext db, IPlatformAudit audit, IAuthService auth)
    : IProfileService
{
    public async Task<Result<MeResponse>> UpdateAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return new ServiceError(StatusCodes.Status401Unauthorized, "auth.session_expired");

        var language = string.IsNullOrWhiteSpace(request.PreferredLanguage) ? null : request.PreferredLanguage.Trim();
        if (language is not (null or "ar" or "en"))
            return new FieldErrors().Add("preferredLanguage", "user.language_invalid").ToError();

        var before = Snapshot(user);
        user.DisplayName = request.DisplayName?.Trim() ?? "";
        user.PhoneNumber = UserRules.NormalizePhone(request.Phone);
        user.PreferredLanguage = language;
        var after = Snapshot(user);

        if (!before.Equals(after))
        {
            // UpdateAsync runs the same user validators as the management screens (display name, phone format)
            // and its SaveChanges stores the audit entry added just before.
            var entry = audit.Record("user.profile_updated", "User", user.Id, user.TenantId, before, after);
            var result = await users.UpdateAsync(user);
            if (!result.Succeeded)
            {
                db.Entry(entry).State = EntityState.Detached;
                var errors = new FieldErrors();
                foreach (var error in result.Errors)
                    errors.Add(error.Code.StartsWith("user.phone") ? "phone" : "displayName", error.Code);
                return errors.ToError();
            }
        }

        return (await auth.GetMeAsync(user.Id, cancellationToken))!;
    }

    private static object Snapshot(ApplicationUser u) => new { u.DisplayName, Phone = u.PhoneNumber, u.PreferredLanguage };
}
