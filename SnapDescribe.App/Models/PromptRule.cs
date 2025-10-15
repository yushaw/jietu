using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    [ObservableProperty]
    private string capabilityId = CapabilityIds.LanguageModel;

    [ObservableProperty]
    private ObservableCollection<CapabilityParameter> parameters = new();

    public IReadOnlyDictionary<string, string> BuildParameterMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in Parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Key) || parameter.Value is null)
            {
                continue;
            }

            map[parameter.Key.Trim()] = parameter.Value;
        }

        if (string.Equals(CapabilityId, CapabilityIds.LanguageModel, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(Prompt))
        {
            map["prompt"] = Prompt;
        }

        return map;
    }
}
