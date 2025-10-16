using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using SnapDescribe.App.Models;
using SnapDescribe.App.Services;
using Xunit;

namespace SnapDescribe.Tests;

public class AgentExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_AppendsSystemToolAndAssistantMessages()
    {
        var localization = CreateLocalization();
        var aiClient = new FakeAiClient("assistant reply");
        var toolRunner = new FakeToolRunner();
        var service = new AgentExecutionService(aiClient, toolRunner, localization);

        var (settings, profile) = CreateSettings(includeToolOutput: true);
        var record = CreateRecord();
        record.AgentProfileId = profile.Id;

        var result = await service.ExecuteAsync(settings, profile, record, "describe screenshot", includeImage: true);

        Assert.Equal("assistant reply", result.Response);
        Assert.Collection(result.MessagesToAppend,
            message => Assert.Equal("system", message.Role),
            message => Assert.Equal("user", message.Role),
            message => Assert.Equal("tool", message.Role),
            message => Assert.Equal("assistant", message.Role));

        Assert.Contains("Tool:", result.MessagesToAppend[2].Content);
        Assert.Equal("describe screenshot", result.MessagesToAppend[1].Content);
        Assert.NotNull(aiClient.LastConversation);
        Assert.Contains(aiClient.LastConversation!, message => message.Role == "system" && message.Content.Contains("Tool:"));
    }

    [Fact]
    public async Task ExecuteAsync_WhenToolOutputExcluded_SkipsToolInConversationPayload()
    {
        var localization = CreateLocalization();
        var aiClient = new FakeAiClient("reply");
        var toolRunner = new FakeToolRunner();
        var service = new AgentExecutionService(aiClient, toolRunner, localization);

        var (settings, profile) = CreateSettings(includeToolOutput: false);
        var record = CreateRecord();
        record.AgentProfileId = profile.Id;

        await service.ExecuteAsync(settings, profile, record, "follow up", includeImage: true);

        Assert.NotNull(aiClient.LastConversation);
        Assert.DoesNotContain(aiClient.LastConversation!, message => message.Content.Contains("Tool:"));
    }

    private static (AppSettings Settings, AgentProfile Profile) CreateSettings(bool includeToolOutput)
    {
        var tool = new AgentTool
        {
            Name = "Echo",
            Description = "Returns predefined text",
            Command = "echo",
            ArgumentsTemplate = "hello",
            AutoRun = true,
            TimeoutSeconds = 10
        };

        var profile = new AgentProfile
        {
            Name = "Test Agent",
            SystemPrompt = "system prompt",
            RunToolsBeforeModel = true,
            IncludeToolOutputInResponse = includeToolOutput
        };
        profile.Tools.Clear();
        profile.Tools.Add(tool);

        var settings = new AppSettings
        {
            Agent = new AgentSettings
            {
                IsEnabled = true,
                Profiles = new ObservableCollection<AgentProfile> { profile },
                DefaultProfileId = profile.Id
            }
        };

        return (settings, profile);
    }

    private static CaptureRecord CreateRecord()
    {
        return new CaptureRecord
        {
            Id = "test",
            ImagePath = "image.png",
            MarkdownPath = "test.md",
            CapturedAt = DateTimeOffset.UtcNow,
            CapabilityId = CapabilityIds.Agent,
            Prompt = "",
            ImageBytes = new byte[] { 1, 2, 3 }
        };
    }

    private static LocalizationService CreateLocalization()
    {
        var localization = new LocalizationService();
        var cacheField = typeof(LocalizationService).GetField("_cache", BindingFlags.Instance | BindingFlags.NonPublic);
        if (cacheField?.GetValue(localization) is Dictionary<string, ResourceDictionary> cache)
        {
            cache["en-US"] = new ResourceDictionary
            {
                { "Error.MissingImageData", "Missing" },
                { "Agent.ToolHeading", "Tool: {0}" },
                { "Agent.ToolCommandLine", "Command: {0} {1}" },
                { "Agent.ToolTimeoutNotice", "Timed out after {0} seconds" },
                { "Agent.ToolExitCode", "Exit code: {0}" },
                { "Agent.ToolOutputHeading", "Captured output" },
                { "Agent.ToolErrorHeading", "Captured error" }
            };
        }

        localization.ApplyLanguage("en-US");
        return localization;
    }

    private sealed class FakeAiClient : IAiClient
    {
        private readonly string _response;

        public FakeAiClient(string response)
        {
            _response = response;
        }

        public IReadOnlyList<ChatMessage>? LastConversation { get; private set; }

        public Task<string> DescribeAsync(AppSettings settings, byte[] pngBytes, string prompt, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<string> ChatAsync(AppSettings settings, byte[] pngBytes, IReadOnlyList<ChatMessage> conversation, CancellationToken cancellationToken = default)
        {
            LastConversation = conversation;
            return Task.FromResult(_response);
        }
    }

    private sealed class FakeToolRunner : IAgentToolRunner
    {
        public Task<AgentToolRunResult> ExecuteAsync(AgentTool tool, string resolvedArguments, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentToolRunResult
            {
                ToolId = tool.Id,
                Name = tool.Name,
                Command = tool.Command,
                Arguments = resolvedArguments,
                ExitCode = 0,
                StandardOutput = "tool output",
                StandardError = string.Empty,
                TimedOut = false
            });
        }
    }
}
