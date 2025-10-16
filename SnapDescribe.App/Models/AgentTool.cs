using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SnapDescribe.App.Models;

public partial class AgentTool : ObservableObject
{
    public AgentTool()
    {
        id = Guid.NewGuid().ToString("N");
    }

    [ObservableProperty]
    private string id;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private string command = string.Empty;

    [ObservableProperty]
    private string argumentsTemplate = string.Empty;

    [ObservableProperty]
    private bool autoRun = true;

    [ObservableProperty]
    private int timeoutSeconds = 30;
}
