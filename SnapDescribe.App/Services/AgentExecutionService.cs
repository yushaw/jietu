using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SnapDescribe.App.Models;

namespace SnapDescribe.App.Services;

public class AgentExecutionService : IAgentExecutionService
{
    private readonly IAiClient _aiClient;
    private readonly IAgentToolRunner _toolRunner;
    private readonly LocalizationService _localization;

    public AgentExecutionService(IAiClient aiClient, IAgentToolRunner toolRunner, LocalizationService localization)
    {
        _aiClient = aiClient;
        _toolRunner = toolRunner;
        _localization = localization;
    }

    public Task<AgentExecutionResult> ExecuteAsync(AppSettings settings, CaptureRecord record, string userMessage, bool includeImage, CancellationToken cancellationToken = default)
        => RunAsync(settings, record, userMessage, includeImage, cancellationToken);

    public Task<AgentExecutionResult> ContinueAsync(AppSettings settings, CaptureRecord record, string userMessage, CancellationToken cancellationToken = default)
        => RunAsync(settings, record, userMessage, includeImage: false, cancellationToken);

    private async Task<AgentExecutionResult> RunAsync(AppSettings settings, CaptureRecord record, string userMessage, bool includeImage, CancellationToken cancellationToken)
    {
        if (record is null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        var agentSettings = settings?.Agent ?? new AgentSettings();
        if (!agentSettings.IsEnabled)
        {
            throw new InvalidOperationException("Agent execution is disabled in settings.");
        }

        if (record.ImageBytes is null || record.ImageBytes.Length == 0)
        {
            throw new InvalidOperationException(_localization.GetString("Error.MissingImageData"));
        }

        var workingConversation = new List<ChatMessage>(record.Conversation);
        var messagesToAppend = new List<ChatMessage>();
        var toolRuns = new List<AgentToolRunResult>();

        var systemPrompt = (agentSettings.SystemPrompt ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            var systemExists = workingConversation.Any(message => string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase)
                                                                  && string.Equals(Normalize(message.Content), systemPrompt, StringComparison.Ordinal));
            if (!systemExists)
            {
                var systemMessage = new ChatMessage("system", systemPrompt);
                workingConversation.Add(systemMessage);
                messagesToAppend.Add(systemMessage);
            }
        }

        var trimmedMessage = (userMessage ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(trimmedMessage))
        {
            var lastMessage = workingConversation.Count > 0 ? workingConversation[^1] : null;
            var userExists = lastMessage is not null
                             && string.Equals(lastMessage.Role, "user", StringComparison.OrdinalIgnoreCase)
                             && string.Equals(Normalize(lastMessage.Content), trimmedMessage, StringComparison.Ordinal);

            if (!userExists)
            {
                var userChat = new ChatMessage("user", trimmedMessage, includeImage: includeImage && !workingConversation.Any(m => m.IncludeImage));
                workingConversation.Add(userChat);
                messagesToAppend.Add(userChat);
            }
        }

        if (agentSettings.RunToolsBeforeModel && agentSettings.Tools is { Count: > 0 })
        {
            foreach (var tool in agentSettings.Tools.Where(t => t.AutoRun))
            {
                if (string.IsNullOrWhiteSpace(tool.Command))
                {
                    continue;
                }

                var resolvedArgs = ResolveArguments(tool.ArgumentsTemplate, record, trimmedMessage);
                var toolResult = await _toolRunner.ExecuteAsync(tool, resolvedArgs, cancellationToken).ConfigureAwait(false);
                toolRuns.Add(toolResult);

                var toolMessage = new ChatMessage("tool", BuildToolMessage(tool, toolResult));
                workingConversation.Add(toolMessage);
                messagesToAppend.Add(toolMessage);
            }
        }

        var conversationPayload = BuildConversationPayload(systemPrompt, workingConversation, agentSettings.IncludeToolOutputInResponse);
        var response = await _aiClient.ChatAsync(settings!, record.ImageBytes, conversationPayload, cancellationToken).ConfigureAwait(false);
        response = (response ?? string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(response))
        {
            var assistantMessage = new ChatMessage("assistant", response);
            messagesToAppend.Add(assistantMessage);
        }

        return new AgentExecutionResult
        {
            Response = response,
            MessagesToAppend = messagesToAppend,
            ToolRuns = toolRuns
        };
    }

    private string ResolveArguments(string? template, CaptureRecord record, string userMessage)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return string.Empty;
        }

        var resolved = template;
        resolved = resolved.Replace("{prompt}", record.Prompt ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        resolved = resolved.Replace("{message}", userMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        resolved = resolved.Replace("{imagePath}", record.ImagePath ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        resolved = resolved.Replace("{processName}", record.ProcessName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        resolved = resolved.Replace("{windowTitle}", record.WindowTitle ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        resolved = resolved.Replace("{timestamp}", record.CapturedAt.ToString("o", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        return resolved;
    }

    private string BuildToolMessage(AgentTool tool, AgentToolRunResult runResult)
    {
        var builder = new StringBuilder();
        builder.AppendLine(_localization.GetString("Agent.ToolHeading", tool.Name));
        builder.AppendLine(_localization.GetString("Agent.ToolCommandLine", tool.Command, runResult.Arguments));

        if (runResult.TimedOut)
        {
            builder.AppendLine(_localization.GetString("Agent.ToolTimeoutNotice", tool.TimeoutSeconds));
        }
        else
        {
            builder.AppendLine(_localization.GetString("Agent.ToolExitCode", runResult.ExitCode));
        }

        if (!string.IsNullOrWhiteSpace(runResult.StandardOutput))
        {
            builder.AppendLine();
            builder.AppendLine(_localization.GetString("Agent.ToolOutputHeading"));
            builder.AppendLine(runResult.StandardOutput.Trim());
        }

        if (!string.IsNullOrWhiteSpace(runResult.StandardError))
        {
            builder.AppendLine();
            builder.AppendLine(_localization.GetString("Agent.ToolErrorHeading"));
            builder.AppendLine(runResult.StandardError.Trim());
        }

        return builder.ToString().Trim();
    }

    private static IReadOnlyList<ChatMessage> BuildConversationPayload(string systemPrompt, List<ChatMessage> workingConversation, bool includeToolOutput)
    {
        var payload = new List<ChatMessage>();

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            payload.Add(new ChatMessage("system", systemPrompt));
        }

        foreach (var message in workingConversation)
        {
            if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase))
            {
                if (!payload.Any(existing => string.Equals(existing.Role, "system", StringComparison.OrdinalIgnoreCase) && string.Equals(existing.Content, message.Content, StringComparison.Ordinal)))
                {
                    payload.Add(new ChatMessage("system", message.Content));
                }
                continue;
            }

            if (string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                if (includeToolOutput)
                {
                    payload.Add(new ChatMessage("system", message.Content));
                }
                continue;
            }

            payload.Add(new ChatMessage(message.Role, message.Content, includeImage: message.IncludeImage));
        }

        return payload;
    }

    private static string Normalize(string? text) => text?.Trim() ?? string.Empty;
}
