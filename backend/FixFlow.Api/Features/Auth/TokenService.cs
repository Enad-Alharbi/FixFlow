using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FixFlow.Api.Features.Users;
using FixFlow.Api.Infrastructure.Data;
using FixFlow.Api.Infrastructure.Tenancy;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FixFlow.Api.Features.Auth;

/// <summary>Claim names inside FixFlow access tokens (kept short and unmapped: MapInboundClaims = false).</summary>
public static class FixFlowClaims
{
    public const string UserId = JwtRegisteredClaimNames.Sub;
    public const string Role = "role";
    public const string TenantId = TenantContext.ClaimType;
}

public class TokenService(IOptions<JwtOptions> options, TimeProvider time, FixFlowDbContext db)
{
    private readonly JwtOptions _jwt = options.Value;

    /// <summary>A signed JWT. Carries identity, role, and tenant — no personal data (it is only base64, not encrypted).</summary>
    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(ApplicationUser user)
    {
        var now = time.GetUtcNow();
        var expiresAt = now + _jwt.AccessTokenLifetime;
        var claims = new List<Claim>
        {
            new(FixFlowClaims.UserId, user.Id.ToString()),
            new(FixFlowClaims.Role, user.Role.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        // No tenant_id claim at all for the Platform Admin.
        if (user.TenantId is { } tenantId) claims.Add(new(FixFlowClaims.TenantId, tenantId.ToString()));

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(_jwt.SigningKeyBytes), SecurityAlgorithms.HmacSha256),
        });
        return (token, expiresAt);
    }

    /// <summary>Adds a new refresh token to the context (the caller saves) and returns the raw value for the cookie.</summary>
    public (string Raw, DateTimeOffset ExpiresAt) AddRefreshToken(Guid userId, Guid familyId)
    {
        var raw = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)); // 256 bits from a CSPRNG
        var now = time.GetUtcNow();
        var token = new RefreshToken
        {
            UserId = userId,
            FamilyId = familyId,
            TokenHash = Hash(raw),
            CreatedAt = now,
            ExpiresAt = now + _jwt.RefreshTokenLifetime,
        };
        db.RefreshTokens.Add(token);
        return (raw, token.ExpiresAt);
    }

    // A plain SHA-256 is enough here (unlike passwords): the input is 256 random bits, so it can't be guessed.
    public static byte[] Hash(string raw) => SHA256.HashData(Encoding.UTF8.GetBytes(raw));
}
