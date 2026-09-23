using VideoAutomation.Application.Interfaces;

namespace VideoAutomation.API.Security;

/// <summary>In-memory active-user holder (singleton). Set on login/register; read by background jobs.</summary>
public class ActiveUserProvider : IActiveUserProvider
{
    public int? ActiveUserId { get; set; }
}
