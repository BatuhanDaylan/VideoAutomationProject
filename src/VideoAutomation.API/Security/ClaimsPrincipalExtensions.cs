using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace VideoAutomation.API.Security;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The logged-in user's id, read from the JWT 'sub' claim (which JwtBearer may map to
    /// NameIdentifier). Returns null if there's no valid authenticated user.</summary>
    public static int? GetUserId(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? principal.FindFirstValue("sub");
        return int.TryParse(raw, out var id) ? id : null;
    }
}
