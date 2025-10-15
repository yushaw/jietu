using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnapDescribe.App.Models;
using SnapDescribe.App.Services;

namespace SnapDescribe.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IScreenshotService _screenshotService;
    private readonly SettingsService _settingsService;
    private readonly IAiClient _aiClient;
    private readonly GlobalHotkeyService _hotkeyService;
    private readonly RelayCommand _removePromptRuleCommand;
    private readonly RelayCommand _movePromptRuleUpCommand;
    private readonly RelayCommand _movePromptRuleDownCommand;
    private readonly RelayCommand _savePromptRulesCommand;

    private bool _hotkeysRegistered;
    private int _captureHotkeyId = -1;
    private PromptRule? _subscribedPromptRule;

    public event EventHandler<CaptureRecord>? CaptureCompleted;

    [ObservableProperty]
    private CaptureRecord? selectedRecord;

    [ObservableProperty]
    private Bitmap? previewImage;

    [ObservableProperty]
    private string responseMarkdown = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "准备就绪";

    [ObservableProperty]
    private PromptRule? selectedPromptRule;

    [ObservableProperty]
    private bool canSavePromptRule;

    public MainWindowViewModel(
        IScreenshotService screenshotService,
        SettingsService settingsService,
        IAiClient aiClient,
        GlobalHotkeyService hotkeyService)
    {
        _screenshotService = screenshotService;
        _settingsService = settingsService;
        _aiClient = aiClient;
        _hotkeyService = hotkeyService;

        CaptureCommand = new AsyncRelayCommand(CaptureInteractiveAsync, () => !IsBusy);
        SaveSettingsCommand = new RelayCommand(SaveSettings);
        RefreshOutputFolderCommand = new RelayCommand(EnsureOutputDirectory);
        AddPromptRuleCommand = new RelayCommand(AddPromptRule);
        _removePromptRuleCommand = new RelayCommand(RemoveSelectedPromptRule, () => SelectedPromptRule is not null);
        _movePromptRuleUpCommand = new RelayCommand(() => MoveSelectedPromptRule(-1), () => CanMoveSelectedPromptRule(-1));
        _movePromptRuleDownCommand = new RelayCommand(() => MoveSelectedPromptRule(1), () => CanMoveSelectedPromptRule(1));
        _savePromptRulesCommand = new RelayCommand(SavePromptRules, CanSavePromptRules);
        RemovePromptRuleCommand = _removePromptRuleCommand;
        MovePromptRuleUpCommand = _movePromptRuleUpCommand;
        MovePromptRuleDownCommand = _movePromptRuleDownCommand;
        SavePromptRulesCommand = _savePromptRulesCommand;

        History = new ObservableCollection<CaptureRecord>();

        Settings.PromptRules.CollectionChanged += PromptRulesOnCollectionChanged;
        EnsureSelectedPromptRule();
        UpdatePromptRuleCommandStates();
        UpdatePromptRuleValidation();
    }

    public ObservableCollection<CaptureRecord> History { get; }

    public AppSettings Settings => _settingsService.Current;

    public IAsyncRelayCommand CaptureCommand { get; }

    public IRelayCommand SaveSettingsCommand { get; }

    public IRelayCommand RefreshOutputFolderCommand { get; }

    public IRelayCommand AddPromptRuleCommand { get; }

    public IRelayCommand RemovePromptRuleCommand { get; }

    public IRelayCommand MovePromptRuleUpCommand { get; }

    public IRelayCommand MovePromptRuleDownCommand { get; }

    public IRelayCommand SavePromptRulesCommand { get; }

    public string? SelectedMarkdownForCopy => SelectedRecord?.ResponseMarkdown;

    public bool HasSelectedPromptRule => SelectedPromptRule is not null;

    public bool NoPromptRuleSelected => SelectedPromptRule is null;

    partial void OnSelectedRecordChanged(CaptureRecord? value)
    {
        PreviewImage = value?.Preview;
        ResponseMarkdown = value?.ResponseMarkdown ?? string.Empty;
    }

    partial void OnSelectedPromptRuleChanged(PromptRule? value)
    {
        UpdatePromptRuleSubscription(value);
        UpdatePromptRuleCommandStates();
        OnPropertyChanged(nameof(HasSelectedPromptRule));
        OnPropertyChanged(nameof(NoPromptRuleSelected));
        UpdatePromptRuleValidation();
    }

    partial void OnIsBusyChanged(bool value)
    {
        CaptureCommand.NotifyCanExecuteChanged();
    }

    public void InitializeHotkeys()
    {
        if (_hotkeysRegistered)
        {
            return;
        }

        try
        {
            _captureHotkeyId = _hotkeyService.RegisterHotkey("capture", Settings.CaptureHotkey);
            if (_captureHotkeyId >= 0)
            {
                _hotkeyService.HotkeyPressed += HandleHotkeyPressed;
                _hotkeysRegistered = true;
            }
            else
            {
                SetStatus("全局快捷键不可用，已忽略。");
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log("注册全局快捷键失败", ex);
            SetStatus(ex.Message);
        }
    }

    public void UpdateCaptureHotkey(string shortcut)
    {
        Settings.CaptureHotkey = HotkeySetting.ParseOrDefault(shortcut);
        _hotkeyService.UnregisterHotkeys();
        _hotkeysRegistered = false;
        InitializeHotkeys();
        _settingsService.Save();
        OnPropertyChanged(nameof(Settings));
        SetStatus("快捷键已更新。");
    }

    private async void HandleHotkeyPressed(object? sender, HotkeyEventArgs e)
    {
        if (_hotkeysRegistered && _captureHotkeyId >= 0 && e.Name == "capture" && !IsBusy)
        {
            await CaptureCommand.ExecuteAsync(null);
        }
    }

    private async Task CaptureInteractiveAsync()
    {
        if (!BeginOperation("正在准备截图..."))
        {
            return;
        }

        try
        {
            var result = await _screenshotService.CaptureInteractiveAsync();
            if (result is null)
            {
                SetStatus("截图已取消。");
                return;
            }

            await ProcessScreenshotAsync(result);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log("执行截图流程时发生异常", ex);
            SetStatus($"截图失败：{ex.Message}");
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task ProcessScreenshotAsync(ScreenshotResult result)
    {
        EnsureOutputDirectory();

        var timestamp = DateTimeOffset.Now;
        var baseName = $"capture_{timestamp:yyyyMMdd_HHmmssfff}";
        var outputDirectory = Settings.OutputDirectory;
        var imagePath = Path.Combine(outputDirectory, $"{baseName}.png");
        var markdownPath = Path.Combine(outputDirectory, $"{baseName}.md");

        await File.WriteAllBytesAsync(imagePath, result.PngBytes);

        var prompt = ResolvePrompt(result.ProcessName, result.WindowTitle);
        var record = new CaptureRecord
        {
            Id = baseName,
            ImagePath = imagePath,
            MarkdownPath = markdownPath,
            CapturedAt = timestamp,
            Prompt = prompt,
            ProcessName = result.ProcessName,
            WindowTitle = result.WindowTitle,
            Preview = result.Preview,
            ResponseMarkdown = "模型正在生成回复...",
            IsLoading = true,
            ImageBytes = result.PngBytes
        };

        record.Conversation.Add(new ChatMessage("user", prompt, includeImage: true));

        SetStatus("正在向模型请求描述...");

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            History.Insert(0, record);
            TrimHistoryIfNeeded();
            SelectedRecord = record;
            CaptureCompleted?.Invoke(this, record);
        });

        var success = false;
        try
        {
            var response = await _aiClient.DescribeAsync(Settings, result.PngBytes, prompt);
            var sanitized = SanitizeResponse(response);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                record.ResponseMarkdown = sanitized;
                record.Conversation.Add(new ChatMessage("assistant", sanitized));
            });
            await File.WriteAllTextAsync(markdownPath, BuildConversationMarkdown(record), Encoding.UTF8);
            success = true;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log("调用模型生成描述失败", ex);
            var failure = SanitizeResponse($"模型调用失败：{ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                record.ResponseMarkdown = failure;
                record.Conversation.Add(new ChatMessage("assistant", failure));
            });
            await File.WriteAllTextAsync(markdownPath, BuildConversationMarkdown(record), Encoding.UTF8);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => record.IsLoading = false);
            SetStatus(success ? "完成。" : "生成失败");
        }
    }

    public async Task ContinueConversationAsync(CaptureRecord record, string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage) || record.IsLoading)
        {
            return;
        }

        userMessage = userMessage.Trim();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            record.Conversation.Add(new ChatMessage("user", userMessage));
            record.IsLoading = true;
            SetStatus("正在向模型请求描述...");
        });

        if (record.ImageBytes is null or { Length: 0 })
        {
            try
            {
                record.ImageBytes = File.Exists(record.ImagePath)
                    ? await File.ReadAllBytesAsync(record.ImagePath)
                    : Array.Empty<byte>();
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Log("重新加载截图文件失败", ex);
            }
        }

        var conversationSnapshot = record.Conversation.ToList();

        var success = false;
        string response;
        try
        {
            var imageBytes = record.ImageBytes;
            if (imageBytes is null || imageBytes.Length == 0)
            {
                throw new InvalidOperationException("无法读取截图数据。");
            }

            response = await _aiClient.ChatAsync(Settings, imageBytes, conversationSnapshot);
            response = SanitizeResponse(response);
            success = true;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log("继续对话失败", ex);
            response = SanitizeResponse($"模型调用失败：{ex.Message}");
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            record.Conversation.Add(new ChatMessage("assistant", response));
            record.ResponseMarkdown = response;
            record.IsLoading = false;
            SetStatus(success ? "完成。" : "生成失败");
        });

        try
        {
            await File.WriteAllTextAsync(record.MarkdownPath, BuildConversationMarkdown(record), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log("写入会话 Markdown 失败", ex);
        }
    }

    private void PromptRulesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        EnsureSelectedPromptRule();
        UpdatePromptRuleCommandStates();
        UpdatePromptRuleValidation();
    }

    private void EnsureSelectedPromptRule()
    {
        if (SelectedPromptRule is not null)
        {
            return;
        }

        if (Settings.PromptRules.Count > 0)
        {
            SelectedPromptRule = Settings.PromptRules[0];
        }
    }

    private void AddPromptRule()
    {
        var rule = new PromptRule
        {
            Prompt = Settings.DefaultPrompt
        };

        Settings.PromptRules.Add(rule);
        SelectedPromptRule = rule;
        SetStatus("已新增规则，请填写必填字段后保存。");
    }

    private void RemoveSelectedPromptRule()
    {
        if (SelectedPromptRule is null)
        {
            return;
        }

        var current = SelectedPromptRule;
        var index = Settings.PromptRules.IndexOf(current);
        if (index < 0)
        {
            return;
        }

        Settings.PromptRules.RemoveAt(index);
        if (Settings.PromptRules.Count == 0)
        {
            SelectedPromptRule = null;
        }
        else if (index < Settings.PromptRules.Count)
        {
            SelectedPromptRule = Settings.PromptRules[index];
        }
        else
        {
            SelectedPromptRule = Settings.PromptRules[^1];
        }

        PersistPromptRules();
    }

    private bool CanMoveSelectedPromptRule(int offset)
    {
        if (SelectedPromptRule is null)
        {
            return false;
        }

        var index = Settings.PromptRules.IndexOf(SelectedPromptRule);
        if (index < 0)
        {
            return false;
        }

        var targetIndex = index + offset;
        return targetIndex >= 0 && targetIndex < Settings.PromptRules.Count;
    }

    private void MoveSelectedPromptRule(int offset)
    {
        if (!CanMoveSelectedPromptRule(offset) || SelectedPromptRule is null)
        {
            return;
        }

        var index = Settings.PromptRules.IndexOf(SelectedPromptRule);
        if (index < 0)
        {
            return;
        }

        var newIndex = index + offset;
        Settings.PromptRules.Move(index, newIndex);
        PersistPromptRules();
    }

    private void UpdatePromptRuleCommandStates()
    {
        _removePromptRuleCommand.NotifyCanExecuteChanged();
        _movePromptRuleUpCommand.NotifyCanExecuteChanged();
        _movePromptRuleDownCommand.NotifyCanExecuteChanged();
        _savePromptRulesCommand.NotifyCanExecuteChanged();
    }

    private void SavePromptRules()
    {
        if (!CanSavePromptRules())
        {
            SetStatus("请先填写必填字段。");
            return;
        }

        PersistPromptRules();
    }

    private void PersistPromptRules()
    {
        try
        {
            _settingsService.Save();
            SetStatus("Prompt 模板已保存。");
        }
        catch (Exception ex)
        {
            SetStatus($"保存 Prompt 模板时出错：{ex.Message}");
        }
    }

    private bool CanSavePromptRules()
    {
        if (SelectedPromptRule is null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(SelectedPromptRule.ProcessName)
               && !string.IsNullOrWhiteSpace(SelectedPromptRule.Prompt);
    }

    private void UpdatePromptRuleValidation()
    {
        CanSavePromptRule = CanSavePromptRules();
        _savePromptRulesCommand.NotifyCanExecuteChanged();
    }

    private void UpdatePromptRuleSubscription(PromptRule? rule)
    {
        if (_subscribedPromptRule is not null)
        {
            _subscribedPromptRule.PropertyChanged -= PromptRuleOnPropertyChanged;
        }

        _subscribedPromptRule = rule;

        if (_subscribedPromptRule is not null)
        {
            _subscribedPromptRule.PropertyChanged += PromptRuleOnPropertyChanged;
        }
    }

    private void PromptRuleOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PromptRule.ProcessName) or nameof(PromptRule.Prompt))
        {
            UpdatePromptRuleValidation();
        }
    }

    private string ResolvePrompt(string? processName, string? windowTitle)
    {
        if (Settings.PromptRules.Count == 0)
        {
            return Settings.DefaultPrompt;
        }

        var normalizedProcess = Normalize(processName);
        string? normalizedProcessWithExe = normalizedProcess;
        if (!string.IsNullOrEmpty(normalizedProcess) && !normalizedProcess.EndsWith(".exe", StringComparison.Ordinal))
        {
            normalizedProcessWithExe = normalizedProcess + ".exe";
        }

        var normalizedTitle = Normalize(windowTitle);

        foreach (var rule in Settings.PromptRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Prompt) || string.IsNullOrWhiteSpace(rule.ProcessName))
            {
                continue;
            }

            var ruleProcess = Normalize(rule.ProcessName);
            if (string.IsNullOrEmpty(ruleProcess))
            {
                continue;
            }

            if (!ContainsNormalized(normalizedProcess, normalizedProcessWithExe, ruleProcess))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(rule.WindowTitle))
            {
                var ruleTitle = Normalize(rule.WindowTitle);
                if (string.IsNullOrEmpty(ruleTitle))
                {
                    continue;
                }

                if (!ContainsNormalized(normalizedTitle, null, ruleTitle))
                {
                    continue;
                }
            }

            return rule.Prompt;
        }

        return Settings.DefaultPrompt;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant();
    }

    private static bool ContainsNormalized(string? source, string? altSource, string target)
    {
        if (string.IsNullOrEmpty(target))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(source) && source.Contains(target, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(altSource) && altSource.Contains(target, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private void TrimHistoryIfNeeded()
    {
        while (History.Count > Math.Max(1, Settings.HistoryLimit))
        {
            var last = History[^1];
            last.Preview?.Dispose();
            History.RemoveAt(History.Count - 1);
        }
    }

    private string BuildConversationMarkdown(CaptureRecord record)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# 模型对话");
        builder.AppendLine();
        foreach (var message in record.Conversation)
        {
            var roleLabel = message.DisplayRole;
            builder.AppendLine($"## {roleLabel}");
            builder.AppendLine();
            builder.AppendLine(message.Content.Trim());
            builder.AppendLine();
        }
        builder.AppendLine("---");
        builder.AppendLine($"- 截图文件：`{Path.GetFileName(record.ImagePath)}`");
        builder.AppendLine($"- 生成时间：{record.CapturedAt:yyyy-MM-dd HH:mm:ss}");
        if (!string.IsNullOrWhiteSpace(record.ProcessName))
        {
            builder.AppendLine($"- 进程：{record.ProcessName}");
        }
        if (!string.IsNullOrWhiteSpace(record.WindowTitle))
        {
            builder.AppendLine($"- 窗口标题：{record.WindowTitle}");
        }
        builder.AppendLine($"- 使用 Prompt：{record.Prompt}");
        return builder.ToString();
    }

    private static string SanitizeResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return text
            .Replace("begin_of_box", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("end_of_box", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("<||>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private bool BeginOperation(string message)
    {
        if (IsBusy)
        {
            return false;
        }

        SetBusy(true);
        SetStatus(message);
        return true;
    }

    private void EndOperation() => SetBusy(false);

    private void EnsureOutputDirectory() => _settingsService.EnsureOutputDirectory();

    private void SaveSettings()
    {
        try
        {
            _settingsService.Save();
            SetStatus("设置已保存。");
        }
        catch (Exception ex)
        {
            SetStatus($"保存设置时出错：{ex.Message}");
        }
    }

    private void SetStatus(string message)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            StatusMessage = message;
        }
        else
        {
            Dispatcher.UIThread.Post(() => StatusMessage = message);
        }
    }

    private void SetBusy(bool value)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            IsBusy = value;
        }
        else
        {
            Dispatcher.UIThread.Post(() => IsBusy = value);
        }
    }
}
