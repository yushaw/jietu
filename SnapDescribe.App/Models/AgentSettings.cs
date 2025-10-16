using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SnapDescribe.App.Models;

public partial class AgentSettings : ObservableObject
{
    [ObservableProperty]
    private bool isEnabled;

    [ObservableProperty]
    private string? defaultProfileId;

    public ObservableCollection<AgentProfile> Profiles { get; set; } = new();

    // Legacy fields (pre-multi-agent). Used to migrate existing settings.
    public string? SystemPrompt { get; set; }

    public bool? RunToolsBeforeModel { get; set; }

    public bool? IncludeToolOutputInResponse { get; set; }

    public ObservableCollection<AgentTool>? Tools { get; set; }
}
