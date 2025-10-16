using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SnapDescribe.App.Models;

public partial class AgentProfile : ObservableObject
{
    private const string DefaultSystemPrompt =
        "You are SnapDescribe Agent. Combine the screenshot analysis with any provided tool outputs to produce a concise, actionable summary.";

    public AgentProfile()
    {
        id = Guid.NewGuid().ToString("N");
        name = "Default Agent";
        systemPrompt = DefaultSystemPrompt;
        tools = new ObservableCollection<AgentTool>();
    }

    [ObservableProperty]
    private string id;

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private string systemPrompt;

    [ObservableProperty]
    private bool runToolsBeforeModel = true;

    [ObservableProperty]
    private bool includeToolOutputInResponse = true;

    [ObservableProperty]
    private ObservableCollection<AgentTool> tools;
}
