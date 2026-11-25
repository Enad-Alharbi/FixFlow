using Microsoft.Extensions.Options;

namespace FixFlow.Api.Features.Auth;

/// <summary>Token settings, from the "Jwt" configuration section. The signing key comes only from the environment.</summary>
public class JwtOptions
{
    public const string Section = "Jwt";

    /// <summary>Base64, at least 32 bytes (HS256 needs a 256-bit key). Never in appsettings or source control.</summary>
    public string SigningKey { get; set; } = "";

    public string Issuer { get; set; } = "fixflow";
    public string Audience { get; set; } = "fixflow-api";
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);
    /// <summary>Sliding: each refresh issues a new token with a fresh lifetime.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(7);

    public byte[] SigningKeyBytes => Convert.FromBase64String(SigningKey);
}

/// <summary>Fails startup on a missing, non-base64, or too-short key, instead of failing on the first sign-in.</summary>
public class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        Span<byte> buffer = stackalloc byte[512];
        if (!Convert.TryFromBase64String(options.SigningKey, buffer, out var length) || length < 32)
            return ValidateOptionsResult.Fail("Jwt:SigningKey must be base64 for at least 32 random bytes (set JWT_SIGNING_KEY).");
        return ValidateOptionsResult.Success;
    }
}
