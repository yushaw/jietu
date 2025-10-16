using System;
using System.Collections.ObjectModel;
using System.IO;

namespace SnapDescribe.App.Models;

public class AppSettings
{
    private static readonly string DefaultOutputDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "SnapDescribe");

    public string BaseUrl { get; set; } = "https://open.bigmodel.cn/api/paas/v4/";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "glm-4.5v";

    public string DefaultPrompt { get; set; } =
        "Describe the key information shown in this screenshot and provide a concise answer or actionable suggestion.";

    public string OutputDirectory { get; set; } = DefaultOutputDirectory;

    public HotkeySetting CaptureHotkey { get; set; } = HotkeySetting.ParseOrDefault("Alt+T");

    public int HistoryLimit { get; set; } = 30;

    public bool LaunchOnStartup { get; set; }

    public ObservableCollection<PromptRule> PromptRules { get; set; } = new();

    public bool HasSeededDefaultPromptRules { get; set; }

    public string Language { get; set; } = "en-US";

    public string OcrTessDataPath { get; set; } = string.Empty;

    public string OcrDefaultLanguages { get; set; } = "chi_sim+eng";

    public AgentSettings Agent { get; set; } = new();
}
