using FixFlow.Api.Features.Auth;
using FixFlow.Api.Infrastructure.Http;
using Microsoft.AspNetCore.Mvc;

namespace FixFlow.Api.Features.Users;

/// <summary>
/// Your own profile, for every signed-in user, the Platform Admin included. Reading it is GET api/auth/me. The user
/// is always the token's: there is no user id in the route, so nobody can edit someone else's profile here.
/// </summary>
[Route("api/profile")]
public class ProfileController(IProfileService profile) : ApiControllerBase
{
    [HttpPut]
    public async Task<IActionResult> Update(UpdateProfileRequest request, CancellationToken cancellationToken) =>
        Guid.TryParse(User.FindFirst(FixFlowClaims.UserId)?.Value, out var userId)
            ? OkOrError(await profile.UpdateAsync(userId, request, cancellationToken))
            : ErrorResult(new ServiceError(StatusCodes.Status401Unauthorized, "auth.unauthenticated"));
}
