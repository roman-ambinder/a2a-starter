using Microsoft.Extensions.Logging;

namespace Ambinder.A2A.AspNetCore.Hosting;

/// <summary>Log messages of the hosting classes. EventIds are public API; this assembly owns 2000 to 2999.</summary>
internal static partial class HostingLog
{
    [LoggerMessage(EventId = 2010, EventName = nameof(AgentAnonymousInDevelopment), Level = LogLevel.Warning,
        Message = "Agent '{AgentName}' has no access mode and no authentication scheme is registered, so it accepts anonymous callers because the environment is Development. " +
                  "Register authentication, or call AddAgent(..., access: AgentAccess.Anonymous) to keep this on purpose. Other environments refuse to start.")]
    public static partial void AgentAnonymousInDevelopment(ILogger logger, string agentName);
}
