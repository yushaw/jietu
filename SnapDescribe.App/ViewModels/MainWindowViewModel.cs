using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading;
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
    private readonly LocalizationService _localization;
    private readonly RelayCommand _removePromptRuleCommand;
    private readonly RelayCommand _movePromptRuleUpCommand;
    private readonly RelayCommand _movePromptRuleDownCommand;
    private readonly RelayCommand _savePromptRulesCommand;
    private readonly StartupRegistrationService _startupService;
    private readonly CapabilityResolver _capabilityResolver;
    private string? _lastStatusResourceKey;
    private object[] _lastStatusArgs = Array.Empty<object>();
    private readonly DispatcherTimer _statusTimer;
    private bool _historyLoaded;
    private const string MetadataExtension = ".json";
    private static readonly JsonSerializerOptions HistoryJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private bool _hotkeysRegistered;
    private int _captureHotkeyId = -1;
    private PromptRule? _subscribedPromptRule;

    public event EventHandler<CaptureRecord>? CaptureCompleted;
    public event EventHandler<bool>? RequestMainWindowVisibility;

    [ObservableProperty]
    private CaptureRecord? selectedRecord;

    [ObservableProperty]
    private Bitmap? previewImage;

    [ObservableProperty]
    private string responseMarkdown = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private PromptRule? selectedPromptRule;

    [ObservableProperty]
    private bool canSavePromptRule;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public MainWindowViewModel(
        IScreenshotService screenshotService,
        SettingsService settingsService,
        IAiClient aiClient,
        GlobalHotkeyService hotkeyService,
        LocalizationService localizationService,
        StartupRegistrationService startupRegistrationService,
        CapabilityResolver capabilityResolver)
    {
        _screenshotService = screenshotService;
        _settingsService = settingsService;
        _aiClient = aiClient;
        _hotkeyService = hotkeyService;
        _localization = localizationService;
        _startupService = startupRegistrationService;
        _capabilityResolver = capabilityResolver;
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _statusTimer.Tick += (_, _) =>
        {
                _statusTimer.Stop();
                StatusMessage = string.Empty;
                _lastStatusResourceKey = null;
                _lastStatusArgs = Array.Empty<object>();
        };

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
        CapabilityOptions = new ObservableCollection<CapabilityOption>();
        RefreshCapabilityOptions();

        try
        {
            var launchOnStartup = _startupService.IsEnabled();
            if (Settings.LaunchOnStartup != launchOnStartup)
            {
                Settings.LaunchOnStartup = launchOnStartup;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log("Failed to load launch-on-startup status.", ex);
        }

        Settings.PromptRules.CollectionChanged += PromptRulesOnCollectionChanged;
        EnsureSelectedPromptRule();
        UpdatePromptRuleCommandStates();
        UpdatePromptRuleValidation();

        StatusMessage = _localization.GetString("Status.Ready");
        _lastStatusResourceKey = "Status.Ready";
        _localization.LanguageChanged += (_, _) => OnLanguageChanged();
    }

    public ObservableCollection<CaptureRecord> History { get; }

    public ObservableCollection<CapabilityOption> CapabilityOptions { get; }

    public AppSettings Settings => _settingsService.Current;

    public CapabilityOption? SelectedCapabilityOption
    {
        get
        {
            if (SelectedPromptRule is null)
            {
                return null;
            }

            return CapabilityOptions.FirstOrDefault(option => string.Equals(option.Id, SelectedPromptRule.CapabilityId, StringComparison.OrdinalIgnoreCase));
        }
        set
        {
            if (SelectedPromptRule is null || value is null)
            {
                return;
            }

            if (!string.Equals(SelectedPromptRule.CapabilityId, value.Id, StringComparison.OrdinalIgnoreCase))
            {
                SelectedPromptRule.CapabilityId = value.Id;
            }
        }
    }

    public bool SelectedCapabilityIsAvailable => SelectedCapabilityOption?.IsAvailable ?? false;

    public bool IsSelectedCapabilityLanguageModel => SelectedPromptRule is not null
        && string.Equals(SelectedPromptRule.CapabilityId, CapabilityIds.LanguageModel, StringComparison.OrdinalIgnoreCase);

    public bool ShowCapabilityPlaceholder => SelectedPromptRule is not null && !SelectedCapabilityIsAvailable;

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
        OnPropertyChanged(nameof(SelectedCapabilityOption));
        OnPropertyChanged(nameof(SelectedCapabilityIsAvailable));
        OnPropertyChanged(nameof(IsSelectedCapabilityLanguageModel));
        OnPropertyChanged(nameof(ShowCapabilityPlaceholder));
        UpdatePromptRuleValidation();
    }

    partial void OnStatusMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasStatusMessage));
    }

    partial void OnIsBusyChanged(bool value)
    {
        CaptureCommand.NotifyCanExecuteChanged();
    }

    public async Task LoadHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (_historyLoaded)
        {
            return;
        }

        _historyLoaded = true;

        try
        {
            EnsureOutputDirectory();
            var directory = Settings.OutputDirectory;
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            var limit = Math.Max(1, Settings.HistoryLimit);
            var persisted = await Task.Run(() => LoadPersistedCaptures(directory, limit, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (persisted.Count == 0)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var existingIds = new HashSet<string>(History.Select(record => record.Id), StringComparer.OrdinalIgnoreCase);

                foreach (var data in persisted)
                {
                    if (existingIds.Contains(data.Id))
                    {
                        continue;
                    }

                    var record = CreateCaptureRecord(data);
                    if (record is not null)
                    {
                        History.Add(record);
                        existingIds.Add(record.Id);
                    }
                }

                if (History.Count > 0 && SelectedRecord is null)
                {
                    SelectedRecord = History[0];
                }
            });
        }
        catch (OperationCanceledException)
        {
            // ignore cancellation
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log("Failed to load capture history.", ex);
        }
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
                SetStatusFromResource("Status.HotkeyUnavailable");
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log("Failed to register global hotkey", ex);
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
        SetStatusFromResource("Status.HotkeyUpdated");
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
        if (!BeginOperationWithResource("Status.PreparingCapture"))
        {
            return;
        }

        var requestedHide = false;
        try
        {
            RequestMainWindowVisibility?.Invoke(this, false);
            requestedHide = true;

            await Task.Delay(200);

            var result = await _screenshotService.CaptureInteractiveAsync();
            if (result is null)
            {
                SetStatusFromResource("Status.CaptureCancelled");
                return;
            }

            await ProcessScreenshotAsync(result);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log("An exception occurred during the capture workflow", ex);
            SetStatusFromResource("Status.CaptureFailed", ex.Message);
        }
        finally
        {
            if (requestedHide)
            {
                RequestMainWindowVisibility?.Invoke(this, true);
            }

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

        var plan = _capabilityResolver.Resolve(Settings, result.ProcessName, result.WindowTitle);
        var prompt = plan.GetParameter("prompt") ?? Settings.DefaultPrompt;
        var isLanguageModel = string.Equals(plan.CapabilityId, CapabilityIds.LanguageModel, StringComparison.OrdinalIgnoreCase);
        var initialResponse = isLanguageModel
            ? _localization.GetString("Response.Generating")
            : _localization.GetString("Response.CapabilityPending");

        var record = new CaptureRecord
        {
            Id = baseName,
            ImagePath = imagePath,
            MarkdownPath = markdownPath,
            CapturedAt = timestamp,
            CapabilityId = plan.CapabilityId,
            Prompt = prompt,
            ProcessName = result.ProcessName,
            WindowTitle = result.WindowTitle,
            Preview = result.Preview,
            ResponseMarkdown = initialResponse,
            IsLoading = isLanguageModel,
            ImageBytes = result.PngBytes
        };

        if (isLanguageModel)
        {
            record.Conversation.Add(new ChatMessage("user", prompt, includeImage: true));
            SetStatusFromResource("Status.RequestingModel", autoClear: false);
        }
        else
        {
            SetStatusFromResource("Status.CapabilityPending");
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            History.Insert(0, record);
            TrimHistoryIfNeeded();
            SelectedRecord = record;
            CaptureCompleted?.Invoke(this, record);
        });

        if (isLanguageModel)
        {
            await ExecuteLanguageModelCapabilityAsync(record, markdownPath);
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(() => record.IsLoading = false);
            await SaveRecordMetadataAsync(record);
        }
    }

    private async Task ExecuteLanguageModelCapabilityAsync(CaptureRecord record, string markdownPath)
    {
        var success = false;
        try
        {
            var response = await _aiClient.DescribeAsync(Settings, record.ImageBytes, record.Prompt);
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
            DiagnosticLogger.Log("Model invocation failed while generating description", ex);
            var failure = SanitizeResponse(_localization.GetString("Status.ModelInvokeFailed", ex.Message));
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
            if (success)
            {
                SetStatusFromResource("Status.Completed");
            }
        }

        await SaveRecordMetadataAsync(record);
    }

    public async Task ContinueConversationAsync(CaptureRecord record, string userMessage)
    {
        if (record is null || !record.SupportsChat)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(userMessage) || record.IsLoading)
        {
            return;
        }

        userMessage = userMessage.Trim();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            record.Conversation.Add(new ChatMessage("user", userMessage));
            record.IsLoading = true;
            SetStatusFromResource("Status.RequestingModel", autoClear: false);
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
                DiagnosticLogger.Log("Failed to reload screenshot file", ex);
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
                throw new InvalidOperationException(_localization.GetString("Error.MissingImageData"));
            }

            response = await _aiClient.ChatAsync(Settings, imageBytes, conversationSnapshot);
            response = SanitizeResponse(response);
            success = true;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log("Continuing the conversation failed", ex);
            response = SanitizeResponse(_localization.GetString("Status.ModelInvokeFailed", ex.Message));
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            record.Conversation.Add(new ChatMessage("assistant", response));
            record.ResponseMarkdown = response;
            record.IsLoading = false;
            if (success)
            {
                SetStatusFromResource("Status.Completed");
            }
        });

        try
        {
            await File.WriteAllTextAsync(record.MarkdownPath, BuildConversationMarkdown(record), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log("Failed to write conversation markdown", ex);
        }

        await SaveRecordMetadataAsync(record);
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
        SetStatusFromResource("Status.RuleAdded");
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
        var rule = SelectedPromptRule;
        Settings.PromptRules.Move(index, newIndex);
        PersistPromptRules();

        if (rule is not null)
        {
            SelectedPromptRule = null;
            SelectedPromptRule = rule;
        }
    }

    private void UpdatePromptRuleCommandStates()
    {
        _removePromptRuleCommand.NotifyCanExecuteChanged();
        _movePromptRuleUpCommand.NotifyCanExecuteChanged();
        _movePromptRuleDownCommand.NotifyCanExecuteChanged();
        _savePromptRulesCommand.NotifyCanExecuteChanged();
    }

    private void RefreshCapabilityOptions()
    {
        var options = new List<CapabilityOption>
        {
            new CapabilityOption(CapabilityIds.LanguageModel, _localization.GetString("Capability.LanguageModel"), isAvailable: true),
            new CapabilityOption(CapabilityIds.ExternalTool, _localization.GetString("Capability.ExternalTool"), isAvailable: false),
            new CapabilityOption(CapabilityIds.Ocr, _localization.GetString("Capability.Ocr"), isAvailable: false)
        };

        CapabilityOptions.Clear();
        foreach (var option in options)
        {
            CapabilityOptions.Add(option);
        }

        OnPropertyChanged(nameof(SelectedCapabilityOption));
        OnPropertyChanged(nameof(SelectedCapabilityIsAvailable));
        OnPropertyChanged(nameof(IsSelectedCapabilityLanguageModel));
        OnPropertyChanged(nameof(ShowCapabilityPlaceholder));
    }

    private void SavePromptRules()
    {
        if (!CanSavePromptRules())
        {
            SetStatusFromResource("Status.FillRequired");
            return;
        }

        PersistPromptRules();
    }

    private void PersistPromptRules()
    {
        try
        {
            _settingsService.Save();
            SetStatusFromResource("Status.PromptSaved");
        }
        catch (Exception ex)
        {
            SetStatusFromResource("Status.PromptSaveFailed", ex.Message);
        }
    }

    private bool CanSavePromptRules()
    {
        if (SelectedPromptRule is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SelectedPromptRule.ProcessName))
        {
            return false;
        }

        return !string.Equals(SelectedPromptRule.CapabilityId, CapabilityIds.LanguageModel, StringComparison.OrdinalIgnoreCase)
               || !string.IsNullOrWhiteSpace(SelectedPromptRule.Prompt);
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
        if (e.PropertyName is nameof(PromptRule.ProcessName) or nameof(PromptRule.Prompt) or nameof(PromptRule.CapabilityId))
        {
            UpdatePromptRuleValidation();
        }

        if (e.PropertyName == nameof(PromptRule.CapabilityId))
        {
            OnPropertyChanged(nameof(SelectedCapabilityOption));
            OnPropertyChanged(nameof(SelectedCapabilityIsAvailable));
            OnPropertyChanged(nameof(IsSelectedCapabilityLanguageModel));
            OnPropertyChanged(nameof(ShowCapabilityPlaceholder));
        }
    }

    private List<PersistedCapture> LoadPersistedCaptures(string directory, int limit, CancellationToken cancellationToken)
    {
        var captures = new List<PersistedCapture>();

        foreach (var jsonPath in Directory.EnumerateFiles(directory, $"*{MetadataExtension}", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var capture = LoadCaptureFromJson(jsonPath);
            if (capture is not null)
            {
                captures.Add(capture);
            }
        }

        foreach (var markdownPath in Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadataPath = Path.ChangeExtension(markdownPath, MetadataExtension);
            if (File.Exists(metadataPath))
            {
                continue;
            }

            var capture = LoadCaptureFromMarkdown(markdownPath);
            if (capture is not null)
            {
                captures.Add(capture);
            }
        }

        var ordered = captures
            .Where(capture => File.Exists(capture.ImagePath) && File.Exists(capture.MarkdownPath))
            .OrderByDescending(capture => capture.CapturedAt)
            .Take(limit)
            .ToList();

        foreach (var capture in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                capture.ImageBytes = File.ReadAllBytes(capture.ImagePath);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Log($"Failed to read screenshot bytes for {capture.ImagePath}", ex);
                capture.ImageBytes = Array.Empty<byte>();
            }
        }

        return ordered;
    }

    private PersistedCapture? LoadCaptureFromJson(string jsonPath)
    {
        try
        {
            var json = File.ReadAllText(jsonPath);
            var capture = JsonSerializer.Deserialize<PersistedCapture>(json, HistoryJsonOptions);
            if (capture is null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(capture.Id))
            {
                capture.Id = Path.GetFileNameWithoutExtension(jsonPath);
            }

            var directory = Path.GetDirectoryName(jsonPath) ?? Settings.OutputDirectory;
            if (string.IsNullOrWhiteSpace(capture.ImagePath))
            {
                capture.ImagePath = Path.Combine(directory, $"{capture.Id}.png");
            }

            if (string.IsNullOrWhiteSpace(capture.MarkdownPath))
            {
                capture.MarkdownPath = Path.Combine(directory, $"{capture.Id}.md");
            }

            if (capture.CapturedAt == default)
            {
                capture.CapturedAt = File.GetCreationTimeUtc(jsonPath);
            }

            if (string.IsNullOrWhiteSpace(capture.Prompt))
            {
                capture.Prompt = Settings.DefaultPrompt;
            }

            return capture;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"Failed to deserialize capture metadata: {jsonPath}", ex);
            return null;
        }
    }

    private PersistedCapture? LoadCaptureFromMarkdown(string markdownPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(markdownPath) ?? Settings.OutputDirectory;
            var baseName = Path.GetFileNameWithoutExtension(markdownPath);
            var imagePath = Path.Combine(directory, $"{baseName}.png");
            if (!File.Exists(imagePath))
            {
                return null;
            }

            var markdown = File.ReadAllText(markdownPath);
            var sections = ParseConversationSections(markdown);
            var metadata = ParseMetadata(markdown);

            var prompt = metadata.TryGetValue("Prompt Used", out var promptValue)
                ? promptValue
                : metadata.TryGetValue("使用 Prompt", out var promptZh)
                    ? promptZh
                    : Settings.DefaultPrompt;

            var processName = metadata.TryGetValue("Process", out var processValue)
                ? processValue
                : metadata.TryGetValue("进程", out var processZh)
                    ? processZh
                    : null;

            var windowTitle = metadata.TryGetValue("Window Title", out var titleValue)
                ? titleValue
                : metadata.TryGetValue("窗口标题", out var titleZh)
                    ? titleZh
                    : null;

            var capturedAt = ParseTimestamp(metadata, baseName, markdownPath);

            var messages = new List<PersistedMessage>();
            for (var i = 0; i < sections.Count; i++)
            {
                var role = i % 2 == 0 ? "user" : "assistant";
                messages.Add(new PersistedMessage
                {
                    Role = role,
                    Content = sections[i],
                    IncludeImage = i == 0
                });
            }

            var response = messages.LastOrDefault(m => m.Role == "assistant")?.Content ?? string.Empty;

            return new PersistedCapture
            {
                Id = baseName,
                ImagePath = imagePath,
                MarkdownPath = markdownPath,
                CapturedAt = capturedAt,
                Prompt = prompt,
                ProcessName = string.IsNullOrWhiteSpace(processName) ? null : processName,
                WindowTitle = string.IsNullOrWhiteSpace(windowTitle) ? null : windowTitle,
                ResponseMarkdown = response,
                Conversation = messages.ToArray()
            };
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"Failed to parse capture markdown: {markdownPath}", ex);
            return null;
        }
    }

    private static List<string> ParseConversationSections(string markdown)
    {
        var sections = new List<string>();
        using var reader = new StringReader(markdown);
        string? line;
        var builder = new StringBuilder();
        var inConversation = false;

        while ((line = reader.ReadLine()) is not null)
        {
            if (line.StartsWith("---", StringComparison.Ordinal))
            {
                if (builder.Length > 0)
                {
                    sections.Add(builder.ToString().Trim());
                }
                break;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (builder.Length > 0)
                {
                    sections.Add(builder.ToString().Trim());
                }

                builder.Clear();
                inConversation = true;
                continue;
            }

            if (!inConversation)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(line);
        }

        if (builder.Length > 0)
        {
            sections.Add(builder.ToString().Trim());
        }

        return sections.Where(section => !string.IsNullOrWhiteSpace(section)).ToList();
    }

    private static Dictionary<string, string> ParseMetadata(string markdown)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var separatorIndex = markdown.IndexOf("---", StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return metadata;
        }

        var metadataContent = markdown[(separatorIndex + 3)..];
        using var reader = new StringReader(metadataContent);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            trimmed = trimmed.TrimStart('-').Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var colonIndex = trimmed.IndexOfAny(new[] { ':', '：' });
            if (colonIndex <= 0 || colonIndex >= trimmed.Length - 1)
            {
                continue;
            }

            var key = trimmed[..colonIndex].Trim();
            var value = trimmed[(colonIndex + 1)..].Trim();
            if (value.StartsWith('`') && value.EndsWith('`'))
            {
                value = value.Trim('`');
            }

            metadata[key] = value;
        }

        return metadata;
    }

    private static DateTimeOffset ParseTimestamp(Dictionary<string, string> metadata, string baseName, string markdownPath)
    {
        if (metadata.TryGetValue("Generated at", out var value) || metadata.TryGetValue("生成时间", out value))
        {
            if (DateTimeOffset.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces, out var parsed))
            {
                return parsed;
            }
        }

        if (baseName.StartsWith("capture_", StringComparison.OrdinalIgnoreCase))
        {
            var timestamp = baseName["capture_".Length..];
            if (DateTimeOffset.TryParseExact(timestamp, "yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out var parsedFromName))
            {
                return parsedFromName;
            }
        }

        return File.GetCreationTime(markdownPath);
    }

    private CaptureRecord? CreateCaptureRecord(PersistedCapture data)
    {
        if (data.ImageBytes is null || data.ImageBytes.Length == 0)
        {
            return null;
        }

        Bitmap? preview = null;
        try
        {
            using var stream = new MemoryStream(data.ImageBytes);
            preview = new Bitmap(stream);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log("Failed to create preview bitmap.", ex);
        }

        var capabilityId = string.IsNullOrWhiteSpace(data.CapabilityId)
            ? CapabilityIds.LanguageModel
            : data.CapabilityId;

        var record = new CaptureRecord
        {
            Id = data.Id,
            ImagePath = data.ImagePath,
            MarkdownPath = data.MarkdownPath,
            CapturedAt = data.CapturedAt,
            CapabilityId = capabilityId,
            Prompt = string.IsNullOrWhiteSpace(data.Prompt) ? Settings.DefaultPrompt : data.Prompt,
            ProcessName = string.IsNullOrWhiteSpace(data.ProcessName) ? null : data.ProcessName,
            WindowTitle = string.IsNullOrWhiteSpace(data.WindowTitle) ? null : data.WindowTitle,
            Preview = preview,
            ResponseMarkdown = data.ResponseMarkdown ?? string.Empty,
            ImageBytes = data.ImageBytes,
            IsLoading = false
        };

        if (data.Conversation is { Length: > 0 })
        {
            foreach (var message in data.Conversation)
            {
                var role = string.IsNullOrWhiteSpace(message.Role) ? "assistant" : message.Role;
                record.Conversation.Add(new ChatMessage(role, message.Content ?? string.Empty, message.IncludeImage));
            }
        }
        else if (string.Equals(capabilityId, CapabilityIds.LanguageModel, StringComparison.OrdinalIgnoreCase))
        {
            var prompt = record.Prompt ?? Settings.DefaultPrompt;
            record.Conversation.Add(new ChatMessage("user", prompt, includeImage: true));
            if (!string.IsNullOrWhiteSpace(record.ResponseMarkdown))
            {
                record.Conversation.Add(new ChatMessage("assistant", record.ResponseMarkdown));
            }
        }

        return record;
    }

    private async Task SaveRecordMetadataAsync(CaptureRecord record)
    {
        try
        {
            var metadata = new PersistedCapture
            {
                Id = record.Id,
                ImagePath = record.ImagePath,
                MarkdownPath = record.MarkdownPath,
                CapturedAt = record.CapturedAt,
                CapabilityId = record.CapabilityId,
                Prompt = record.Prompt,
                ProcessName = record.ProcessName,
                WindowTitle = record.WindowTitle,
                ResponseMarkdown = record.ResponseMarkdown,
                Conversation = record.Conversation
                    .Select(message => new PersistedMessage
                    {
                        Role = message.Role,
                        Content = message.Content,
                        IncludeImage = message.IncludeImage
                    })
                    .ToArray()
            };

            var jsonPath = Path.ChangeExtension(record.MarkdownPath, MetadataExtension);
            var directory = Path.GetDirectoryName(jsonPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var json = JsonSerializer.Serialize(metadata, HistoryJsonOptions);
            await File.WriteAllTextAsync(jsonPath, json, Encoding.UTF8).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"Failed to save capture metadata for {record.Id}", ex);
        }
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
        builder.AppendLine(_localization.GetString("Markdown.ConversationTitle"));
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
        builder.AppendLine(_localization.GetString("Markdown.ScreenshotFile", Path.GetFileName(record.ImagePath)));
        builder.AppendLine(_localization.GetString("Markdown.GeneratedAt", record.CapturedAt));
        if (!string.IsNullOrWhiteSpace(record.ProcessName))
        {
            builder.AppendLine(_localization.GetString("Markdown.Process", record.ProcessName));
        }
        if (!string.IsNullOrWhiteSpace(record.WindowTitle))
        {
            builder.AppendLine(_localization.GetString("Markdown.WindowTitle", record.WindowTitle));
        }
        builder.AppendLine(_localization.GetString("Markdown.PromptUsed", record.Prompt));
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
        SetStatus(message, autoClear: false);
        return true;
    }

    private void EndOperation() => SetBusy(false);

    private void EnsureOutputDirectory() => _settingsService.EnsureOutputDirectory();

    private void SaveSettings()
    {
        string? startupError = null;

        try
        {
            _startupService.Apply(Settings.LaunchOnStartup);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log("Failed to update launch-on-startup preference.", ex);
            startupError = ex.Message;

            try
            {
                var actualState = _startupService.IsEnabled();
                if (Settings.LaunchOnStartup != actualState)
                {
                    Settings.LaunchOnStartup = actualState;
                }
            }
            catch (Exception syncEx)
            {
                DiagnosticLogger.Log("Failed to synchronize launch-on-startup state after error.", syncEx);
            }
        }

        try
        {
            _settingsService.Save();
        }
        catch (Exception ex)
        {
            SetStatusFromResource("Status.SettingsSaveFailed", ex.Message);
            return;
        }

        if (startupError is not null)
        {
            SetStatusFromResource("Status.StartupUpdateFailed", startupError);
            return;
        }

        SetStatusFromResource("Status.SettingsSaved");
    }

    private void SetStatus(string message, bool autoClear = true)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            StatusMessage = message;
        }
        else
        {
            Dispatcher.UIThread.Post(() => StatusMessage = message);
        }

        var shouldClear = string.IsNullOrWhiteSpace(message);

        if (!autoClear || shouldClear)
        {
            _statusTimer.Stop();
        }
        else
        {
            _statusTimer.Stop();
            _statusTimer.Start();
        }

        if (shouldClear)
        {
            _lastStatusResourceKey = null;
            _lastStatusArgs = Array.Empty<object>();
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

    private void SetStatusFromResource(string key, params object[] args) => SetStatusFromResourceInternal(key, true, args);

    private void SetStatusFromResource(string key, bool autoClear, params object[] args) => SetStatusFromResourceInternal(key, autoClear, args);

    private void SetStatusFromResourceInternal(string key, bool autoClear, params object[] args)
    {
        var message = _localization.GetString(key, args);
        SetStatus(message, autoClear);
        if (autoClear)
        {
            _lastStatusResourceKey = null;
            _lastStatusArgs = Array.Empty<object>();
        }
        else
        {
            RememberStatus(key, args);
        }
    }

    private bool BeginOperationWithResource(string key, params object[] args)
    {
        var message = _localization.GetString(key, args);
        if (!BeginOperation(message))
        {
            return false;
        }

        RememberStatus(key, args);
        return true;
    }

    private void RememberStatus(string key, params object[] args)
    {
        _lastStatusResourceKey = key;
        _lastStatusArgs = args is { Length: > 0 } ? args.ToArray() : Array.Empty<object>();
    }

    private void OnLanguageChanged()
    {
        if (!string.IsNullOrWhiteSpace(_lastStatusResourceKey))
        {
            SetStatus(_localization.GetString(_lastStatusResourceKey, _lastStatusArgs), autoClear: false);
        }

        foreach (var record in History)
        {
            record.RefreshDisplayContext();
        }

        RefreshCapabilityOptions();
    }

    public void SetStatusMessage(string resourceKey, params object[] args) => SetStatusFromResource(resourceKey, args);

    public void ChangeLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return;
        }

        var changed = !string.Equals(Settings.Language, language, StringComparison.OrdinalIgnoreCase);
        if (changed)
        {
            Settings.Language = language;
            _settingsService.Save();
        }

        _localization.ApplyLanguage(language);

        if (changed)
        {
            SetStatusFromResource("Status.SettingsSaved");
        }
    }

    private sealed class PersistedCapture
    {
        public string Id { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public string MarkdownPath { get; set; } = string.Empty;
        public DateTimeOffset CapturedAt { get; set; }
        public string CapabilityId { get; set; } = CapabilityIds.LanguageModel;
        public string Prompt { get; set; } = string.Empty;
        public string? ProcessName { get; set; }
        public string? WindowTitle { get; set; }
        public string ResponseMarkdown { get; set; } = string.Empty;
        public PersistedMessage[] Conversation { get; set; } = Array.Empty<PersistedMessage>();

        [JsonIgnore]
        public byte[] ImageBytes { get; set; } = Array.Empty<byte>();
    }

    private sealed class PersistedMessage
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public bool IncludeImage { get; set; }
    }
}
