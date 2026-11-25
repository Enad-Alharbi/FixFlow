using FixFlow.Api.Features.Users;
using Microsoft.AspNetCore.Authorization;

namespace FixFlow.Api.Features.Auth;

/// <summary>
/// Named authorization policies (CLAUDE.md §7.3). Endpoints opt in with .RequireAuthorization(AuthPolicies.X).
/// Tenant policies also require the tenant_id claim, so a token without a tenant never passes them.
/// Resource checks (ownership, assignment) live in feature services on top of these.
/// </summary>
public static class AuthPolicies
{
    public const string PlatformAdmin = nameof(PlatformAdmin);
    public const string TenantMember = nameof(TenantMember);
    public const string TenantSupervisor = nameof(TenantSupervisor);
    public const string TenantTechnician = nameof(TenantTechnician);

    public static void Add(AuthorizationBuilder auth) => auth
        // Secure by default: every endpoint needs a signed-in user unless it says AllowAnonymous.
        .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
        .AddPolicy(PlatformAdmin, p => p.RequireClaim(FixFlowClaims.Role, nameof(UserRole.PlatformAdmin)))
        .AddPolicy(TenantMember, p => p.RequireClaim(FixFlowClaims.TenantId)
            .RequireClaim(FixFlowClaims.Role, nameof(UserRole.Requester), nameof(UserRole.Technician), nameof(UserRole.Supervisor)))
        .AddPolicy(TenantSupervisor, p => p.RequireClaim(FixFlowClaims.TenantId).RequireClaim(FixFlowClaims.Role, nameof(UserRole.Supervisor)))
        .AddPolicy(TenantTechnician, p => p.RequireClaim(FixFlowClaims.TenantId).RequireClaim(FixFlowClaims.Role, nameof(UserRole.Technician)));
}
