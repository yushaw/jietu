using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SnapDescribe.App.Models;
using SnapDescribe.App.Services;

namespace SnapDescribe.App.Views;

public partial class ResultDialog : Window
{
    private readonly CaptureRecord? _record;
    private readonly Func<CaptureRecord, string, Task>? _continueChat;
    private readonly LocalizationService _localization;
    private readonly bool _supportsChat;

    public ResultDialog()
    {
        InitializeComponent();
        _record = null;
        _localization = LocalizationService.Instance;
        _supportsChat = false;
    }

    public ResultDialog(CaptureRecord record, Func<CaptureRecord, string, Task>? continueChat) : this()
    {
        _record = record;
        _continueChat = continueChat;
        _supportsChat = continueChat is not null;
        DataContext = record;

        if (!record.SupportsOcr)
        {
            PreviewImage.Source = record.Preview;
        }

        TitleText.Text = record.DisplayTitle;
        UpdateLocalizedTexts();

        if (_supportsChat)
        {
            SendButton.Click += OnSendClicked;
            ChatInput.KeyDown += ChatInputOnKeyDown;
        }
        else
        {
            ChatContainer.IsVisible = false;
        }

        if (record.SupportsOcr)
        {
            OcrCanvas.SegmentClicked += OnOcrSegmentClicked;
            OcrCanvas.SelectionChanged += OnOcrSelectionChanged;
            KeyDown += OnOcrDialogKeyDown;
        }

        Closed += OnClosed;

        UpdateLoadingState();
        _record.PropertyChanged += OnRecordPropertyChanged;
        _localization.LanguageChanged += LocalizationOnLanguageChanged;

        if (_supportsChat && !_record.IsLoading)
        {
            ChatInput.Focus();
        }
    }

    private async void OnSendClicked(object? sender, RoutedEventArgs e)
    {
        await SendMessageAsync();
    }

    private async void ChatInputOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Shift) == 0)
        {
            e.Handled = true;
            await SendMessageAsync();
        }
    }

    private async Task SendMessageAsync()
    {
        if (_record is null || _continueChat is null)
        {
            return;
        }

        var text = ChatInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text) || _record.IsLoading)
        {
            return;
        }

        await _continueChat(_record, text);
        ChatInput.Text = string.Empty;
    }

    private void OnRecordPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_record is null)
        {
            return;
        }

        if (e.PropertyName == nameof(CaptureRecord.IsLoading))
        {
            UpdateLoadingState();
        }

        if (e.PropertyName == nameof(CaptureRecord.ProcessName) || e.PropertyName == nameof(CaptureRecord.WindowTitle))
        {
            UpdateLocalizedTexts();
        }
    }

    private void UpdateLoadingState()
    {
        var isLoading = _record?.IsLoading ?? false;
        LoadingPanel.IsVisible = isLoading;

        if (_supportsChat)
        {
            SendButton.IsEnabled = !isLoading;
            ChatInput.IsEnabled = !isLoading;
            if (!isLoading)
            {
                ChatInput.Focus();
            }
        }
        else
        {
            SendButton.IsEnabled = false;
            ChatInput.IsEnabled = false;
            if (OcrActions is not null)
            {
                OcrActions.IsEnabled = !isLoading;
            }
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_supportsChat)
        {
            SendButton.Click -= OnSendClicked;
            ChatInput.KeyDown -= ChatInputOnKeyDown;
        }

        if (_record is not null)
        {
            _record.PropertyChanged -= OnRecordPropertyChanged;

            if (_record.SupportsOcr)
            {
                OcrCanvas.SegmentClicked -= OnOcrSegmentClicked;
                OcrCanvas.SelectionChanged -= OnOcrSelectionChanged;
                KeyDown -= OnOcrDialogKeyDown;
            }
        }

        _localization.LanguageChanged -= LocalizationOnLanguageChanged;
    }

    private void OnOcrDialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (_record?.SupportsOcr != true) return;

        if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            OcrCanvas.SelectAll();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            OcrCanvas.ClearSelection();
        }
    }

    private async void OnOcrSegmentClicked(object? sender, OcrSegment segment)
    {
        // Single click: copy just this segment
        await CopyTextAsync(segment.Text);
        ShowCopyFeedback();
    }

    private async void OnOcrSelectionChanged(object? sender, System.Collections.Generic.IReadOnlyList<OcrSegment> segments)
    {
        // Multiple selection: copy all selected text
        if (segments.Count == 0) return;

        var combinedText = string.Join(Environment.NewLine, segments.Select(s => s.Text));
        await CopyTextAsync(combinedText);
        ShowCopyFeedback(segments.Count);
    }

    private void ShowCopyFeedback(int count = 1)
    {
        // Update copy button text temporarily as visual feedback
        if (OcrActions is not null && OcrActions.Child is StackPanel stackPanel)
        {
            if (stackPanel.Children.Count > 0 && stackPanel.Children[0] is Button copyButton)
            {
                var originalText = copyButton.Content;
                copyButton.Content = count > 1
                    ? _localization.GetString("Button.CopiedMultiple", count)
                    : _localization.GetString("Button.Copied");

                var timer = new System.Timers.Timer(1500);
                timer.Elapsed += (s, e) =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        copyButton.Content = originalText;
                    });
                    timer.Dispose();
                };
                timer.Start();
            }
        }
    }

    private void OnOcrSegmentItemClicked(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not OcrSegment segment)
        {
            return;
        }

        OcrCanvas.HighlightSegment(segment);
    }

    private void UpdateLocalizedTexts()
    {
        if (_record is null)
        {
            return;
        }

        SubTitleText.Text = $"{_localization.GetString("Dialog.GeneratedAt")} {_record.CapturedAt:yyyy-MM-dd HH:mm:ss}";
        WindowTitleValue.Text = string.IsNullOrWhiteSpace(_record.WindowTitle)
            ? _localization.GetString("Dialog.WindowTitleMissing")
            : _record.WindowTitle;
        ProcessNameValue.Text = string.IsNullOrWhiteSpace(_record.ProcessName)
            ? _localization.GetString("Dialog.ProcessNameMissing")
            : _record.ProcessName;
    }

    private void LocalizationOnLanguageChanged(object? sender, EventArgs e)
    {
        UpdateLocalizedTexts();
    }

    private async void OnCopyAllClicked(object? sender, RoutedEventArgs e)
    {
        if (_record is null)
        {
            return;
        }

        var text = _record.ResponseMarkdown;

        await CopyTextAsync(text);
    }

    private async Task CopyTextAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        await clipboard.SetTextAsync(text);
    }
}
