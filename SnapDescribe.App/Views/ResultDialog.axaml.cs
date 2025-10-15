using System;
using System.ComponentModel;
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

        PreviewImage.Source = record.Preview;
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
        }

        _localization.LanguageChanged -= LocalizationOnLanguageChanged;
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
}
