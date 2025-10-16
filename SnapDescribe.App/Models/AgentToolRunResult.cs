namespace SnapDescribe.App.Models;

public sealed class AgentToolRunResult
{
    public required string ToolId { get; init; }

    public required string Name { get; init; }

    public required string Command { get; init; }

    public required string Arguments { get; init; }

    public int ExitCode { get; init; }

    public string StandardOutput { get; init; } = string.Empty;

    public string StandardError { get; init; } = string.Empty;

    public bool TimedOut { get; init; }
}
