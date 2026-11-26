using Microsoft.Extensions.Logging;

namespace Klyroo.Agent;

internal static partial class AgentLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Agent bridge: the action {Action} on {Reference} threw. The run stopped and the model read a generic message; the exception follows.")]
    public static partial void ActionFailed(ILogger logger, string reference, string action, Exception exception);
}
