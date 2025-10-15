using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SnapDescribe.App.Models;

public partial class PromptRule : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string processName = string.Empty;

    [ObservableProperty]
    private string windowTitle = string.Empty;

    [ObservableProperty]
    private string prompt = string.Empty;
}
