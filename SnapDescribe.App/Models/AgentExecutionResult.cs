using System.Collections.Generic;

namespace SnapDescribe.App.Models;

public sealed class AgentExecutionResult
{
    public string Response { get; init; } = string.Empty;

    public IReadOnlyList<ChatMessage> MessagesToAppend { get; init; } = new List<ChatMessage>();

    public IReadOnlyList<AgentToolRunResult> ToolRuns { get; init; } = new List<AgentToolRunResult>();
}
