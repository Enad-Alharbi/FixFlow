using System.Security.Claims;
using FixFlow.Api.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace FixFlow.Api.Tests;

/// <summary>
/// The named policies, evaluated by the app's real IAuthorizationService. Endpoint-level checks (a tenant user
/// calling /api/platform/...) are added with the first platform endpoints in Phase 4.
/// </summary>
[Collection("Api")]
public class PolicyTests(ApiFactory factory)
{
    [Theory]
    // role,          has tenant, PlatformAdmin, TenantMember, TenantSupervisor, TenantTechnician
    [InlineData("PlatformAdmin", false, true, false, false, false)]
    [InlineData("Requester", true, false, true, false, false)]
    [InlineData("Technician", true, false, true, false, true)]
    [InlineData("Supervisor", true, false, true, true, false)]
    [InlineData("Supervisor", false, false, false, false, false)] // tenant role without tenant claim: nothing
    [InlineData("PlatformAdmin", true, true, false, false, false)] // a tenant claim never makes an admin a tenant member
    public async Task Policies_grant_exactly_the_expected_access(
        string role, bool hasTenant, bool platformAdmin, bool tenantMember, bool tenantSupervisor, bool tenantTechnician)
    {
        var auth = factory.Services.GetRequiredService<IAuthorizationService>();
        var claims = new List<Claim> { new(FixFlowClaims.UserId, Guid.NewGuid().ToString()), new(FixFlowClaims.Role, role) };
        if (hasTenant) claims.Add(new(FixFlowClaims.TenantId, Guid.NewGuid().ToString()));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        Assert.Equal(platformAdmin, (await auth.AuthorizeAsync(user, AuthPolicies.PlatformAdmin)).Succeeded);
        Assert.Equal(tenantMember, (await auth.AuthorizeAsync(user, AuthPolicies.TenantMember)).Succeeded);
        Assert.Equal(tenantSupervisor, (await auth.AuthorizeAsync(user, AuthPolicies.TenantSupervisor)).Succeeded);
        Assert.Equal(tenantTechnician, (await auth.AuthorizeAsync(user, AuthPolicies.TenantTechnician)).Succeeded);
    }
}
