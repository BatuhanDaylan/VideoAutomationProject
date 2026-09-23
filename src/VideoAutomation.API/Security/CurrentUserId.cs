using System.Security.Claims;
using VideoAutomation.Application.Interfaces;

namespace VideoAutomation.API.Security;

/// <summary>Resolves the current user id from the JWT (HTTP request) or the active user (background job).</summary>
public class CurrentUserId : ICurrentUserId
{
    private readonly IHttpContextAccessor _http;
    private readonly IActiveUserProvider _active;

    private int? _override;

    public CurrentUserId(IHttpContextAccessor http, IActiveUserProvider active)
    {
        _http = http; _active = active;
    }

    public void Override(int userId) => _override = userId;

    public int? Value
    {
        get
        {
            if (_override is { } o) return o;  // background jobs pin the work's owner
            var principal = _http.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated == true)
            {
                var sub = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value;
                if (int.TryParse(sub, out var id)) return id;
            }
            return _active.ActiveUserId;
        }
    }
}
