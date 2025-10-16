using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SnapDescribe.App.Models;

public partial class AgentSettings : ObservableObject
{
    private const string DefaultSystemPrompt =
        "You are SnapDescribe Agent. Combine the screenshot analysis with any provided tool outputs to produce a concise, actionable summary.";

    [ObservableProperty]
    private bool isEnabled;

    [ObservableProperty]
    private string systemPrompt = DefaultSystemPrompt;

    [ObservableProperty]
    private bool runToolsBeforeModel = true;

    [ObservableProperty]
    private bool includeToolOutputInResponse = true;

    [ObservableProperty]
    private ObservableCollection<AgentTool> tools = new();
}
