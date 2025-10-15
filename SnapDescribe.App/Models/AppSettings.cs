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
        "请用简洁的语言描述这张截图的关键信息，并给出一个可能的解答或操作建议。";

    public string OutputDirectory { get; set; } = DefaultOutputDirectory;

    public HotkeySetting CaptureHotkey { get; set; } = HotkeySetting.ParseOrDefault("Alt+T");

    public int HistoryLimit { get; set; } = 30;

    public ObservableCollection<PromptRule> PromptRules { get; set; } = new();

    public bool HasSeededDefaultPromptRules { get; set; }
}
