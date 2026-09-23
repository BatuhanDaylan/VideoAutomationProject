using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using VideoAutomation.Domain.Entities;

namespace VideoAutomation.API.Security;

/// <summary>
/// Issues signed JWTs carrying the user's id. Long-lived (30 days) on purpose — this is a locally-run
/// desktop-style app, so re-logging-in constantly would be annoying; the token lives in the browser and the
/// SPA sends it as a Bearer header. Signed with JWT_SECRET (HMAC-SHA256).
/// </summary>
public class JwtTokenService
{
    public const string Issuer = "VideoAutomation";
    public const string Audience = "VideoAutomation";

    private readonly SymmetricSecurityKey _key;

    public JwtTokenService(IConfiguration config)
    {
        var secret = config["Auth:JwtSecret"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            throw new InvalidOperationException("JWT_SECRET yapılandırılmamış veya çok kısa (en az 32 karakter).");
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }

    public string CreateToken(User user)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Expires = DateTime.UtcNow.AddDays(30),
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("username", user.Username),
            }),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public TokenValidationParameters ValidationParameters => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = _key,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
    };
}
