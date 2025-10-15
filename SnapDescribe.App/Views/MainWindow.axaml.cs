using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Media.Imaging;
using SnapDescribe.App.Models;
using SnapDescribe.App.ViewModels;
using SnapDescribe.App.Services;
using System.Linq;

namespace SnapDescribe.App.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();

        if (Design.IsDesignMode)
        {
            return;
        }

        var services = App.Services;
        var viewModel = services.GetService(typeof(MainWindowViewModel)) as MainWindowViewModel
                         ?? throw new InvalidOperationException("MainWindowViewModel is not registered.");
        AttachViewModel(viewModel);
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        AttachViewModel(viewModel);
    }

    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void AttachViewModel(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;

        var browseButton = this.FindControl<Button>("BrowseFolderButton");
        browseButton?.AddHandler(Button.ClickEvent, OnBrowseFolderClicked);

        var hotkeyBox = this.FindControl<TextBox>("HotkeyTextBox");
        if (hotkeyBox is not null)
        {
            hotkeyBox.Text = _viewModel.Settings.CaptureHotkey.Shortcut;
            hotkeyBox.IsTabStop = true;
            hotkeyBox.AddHandler(KeyDownEvent, OnHotkeyBoxKeyDown, RoutingStrategies.Tunnel);
            hotkeyBox.PointerPressed += (_, _) => hotkeyBox.Focus();
        }

        RegisterSettingsFieldHandlers();

        var historyList = this.FindControl<ListBox>("HistoryList");
        if (historyList is not null)
        {
            historyList.DoubleTapped += OnHistoryDoubleTapped;
        }

        if (this.FindControl<ComboBox>("LanguageComboBox") is { } languageCombo)
        {
            SelectLanguageItem(languageCombo, _viewModel.Settings.Language);
            languageCombo.SelectionChanged += OnLanguageSelectionChanged;
        }

        _viewModel.CaptureCompleted += OnCaptureCompleted;
        _ = _viewModel.LoadHistoryAsync();
        Opened += HandleOpened;
        Closed += HandleClosed;
    }

    private async void OnBrowseFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (!StorageProvider.CanPickFolder)
        {
            _viewModel.SetStatusMessage("Status.FolderPickerNotSupported");
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = LocalizationService.Instance.GetString("Dialog.SelectOutputFolder")
        });

        var folder = folders?.Count > 0 ? folders[0] : null;
        if (folder is null)
        {
            return;
        }

        var path = folder.Path.LocalPath ?? folder.Path.ToString();
        if (string.IsNullOrWhiteSpace(path))
        {
            _viewModel.SetStatusMessage("Status.ReadSelectedPathFailed");
            return;
        }

        _viewModel.Settings.OutputDirectory = path;
        _viewModel.RefreshOutputFolderCommand.Execute(null);
        _viewModel.SetStatusMessage("Status.OutputDirectoryUpdated");
    }

    private void HandleOpened(object? sender, EventArgs e)
    {
        _viewModel?.InitializeHotkeys();
    }

    private void HandleClosed(object? sender, EventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.CaptureCompleted -= OnCaptureCompleted;

        var historyList = this.FindControl<ListBox>("HistoryList");
        if (historyList is not null)
        {
            historyList.DoubleTapped -= OnHistoryDoubleTapped;
        }

        var hotkeyBox = this.FindControl<TextBox>("HotkeyTextBox");
        if (hotkeyBox is not null)
        {
            hotkeyBox.RemoveHandler(KeyDownEvent, OnHotkeyBoxKeyDown);
        }

        if (this.FindControl<ComboBox>("LanguageComboBox") is { } languageCombo)
        {
            languageCombo.SelectionChanged -= OnLanguageSelectionChanged;
        }

        UnregisterSettingsFieldHandlers();
    }

    private async void OnHistoryDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedRecord is null)
        {
            return;
        }

        await OpenResultDialogAsync(_viewModel.SelectedRecord);
    }

    private async void OnCaptureCompleted(object? sender, CaptureRecord record)
    {
        await CopyImageToClipboardAsync(record);
        await OpenResultDialogAsync(record);
    }

    private async Task OpenResultDialogAsync(CaptureRecord record)
    {
        var continueHandler = _viewModel is null
            ? null
            : new Func<CaptureRecord, string, Task>(_viewModel.ContinueConversationAsync);

        if (!IsVisible)
        {
            Show();
        }

        Activate();

        var dialog = new ResultDialog(record, continueHandler)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        await dialog.ShowDialog(this);
    }

    private void OnHotkeyBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null || sender is not TextBox hotkeyBox)
        {
            return;
        }

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            _viewModel.UpdateCaptureHotkey(string.Empty);
            hotkeyBox.Text = string.Empty;
            return;
        }

        if (IsModifierKey(e.Key))
        {
            return;
        }

        var shortcut = FormatShortcut(e.Key, e.KeyModifiers);
        if (string.IsNullOrEmpty(shortcut))
        {
            return;
        }

        _viewModel.UpdateCaptureHotkey(shortcut);
        hotkeyBox.Text = shortcut;
    }

    private static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static string FormatShortcut(Key key, KeyModifiers modifiers)
    {
        var parts = new List<string>();

        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }
        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("Alt");
        }
        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("Shift");
        }
        if (modifiers.HasFlag(KeyModifiers.Meta))
        {
            parts.Add("Win");
        }

        if (key == Key.Oem1 || key == Key.OemPlus || key == Key.OemComma || key == Key.OemMinus || key == Key.OemPeriod || key == Key.Oem2 || key == Key.Oem3 || key == Key.Oem4 || key == Key.Oem5 || key == Key.Oem6 || key == Key.Oem7)
        {
            parts.Add(key switch
            {
                Key.OemPlus => "+",
                Key.OemComma => ",",
                Key.OemMinus => "-",
                Key.OemPeriod => ".",
                Key.Oem1 => ";",
                Key.Oem2 => "/",
                Key.Oem3 => "`",
                Key.Oem4 => "[",
                Key.Oem5 => "\\",
                Key.Oem6 => "]",
                Key.Oem7 => "'",
                _ => key.ToString()
            });
        }
        else
        {
            var keyText = key switch
            {
                >= Key.A and <= Key.Z => key.ToString().ToUpperInvariant(),
                >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
                >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (key - Key.NumPad0),
                >= Key.F1 and <= Key.F24 => key.ToString().ToUpperInvariant(),
                Key.Space => "Space",
                Key.Enter => "Enter",
                Key.Tab => "Tab",
                Key.Up => "Up",
                Key.Down => "Down",
                Key.Left => "Left",
                Key.Right => "Right",
                Key.Home => "Home",
                Key.End => "End",
                Key.PageUp => "PageUp",
                Key.PageDown => "PageDown",
                Key.Insert => "Insert",
                Key.Delete => "Delete",
                _ => key.ToString().ToUpperInvariant()
            };

            if (string.IsNullOrWhiteSpace(keyText))
            {
                return string.Empty;
            }

            parts.Add(keyText);
        }

        return parts.Count == 0 ? string.Empty : string.Join("+", parts);
    }

    private void RegisterSettingsFieldHandlers()
    {
        AttachLostFocus("BaseUrlTextBox");
        AttachLostFocus("ModelTextBox");
        AttachLostFocus("ApiKeyTextBox");
        AttachLostFocus("DefaultPromptTextBox");
        AttachLostFocus("OutputDirectoryTextBox");
        AttachLostFocus("HistoryLimitTextBox");
        AttachToggle("LaunchOnStartupCheckBox");
    }

    private void UnregisterSettingsFieldHandlers()
    {
        DetachLostFocus("BaseUrlTextBox");
        DetachLostFocus("ModelTextBox");
        DetachLostFocus("ApiKeyTextBox");
        DetachLostFocus("DefaultPromptTextBox");
        DetachLostFocus("OutputDirectoryTextBox");
        DetachLostFocus("HistoryLimitTextBox");
        DetachToggle("LaunchOnStartupCheckBox");
    }

    private void AttachLostFocus(string name)
    {
        if (this.FindControl<TextBox>(name) is { } box)
        {
            box.LostFocus += OnSettingsFieldLostFocus;
        }
    }

    private void DetachLostFocus(string name)
    {
        if (this.FindControl<TextBox>(name) is { } box)
        {
            box.LostFocus -= OnSettingsFieldLostFocus;
        }
    }

    private void OnSettingsFieldLostFocus(object? sender, RoutedEventArgs e)
    {
        _viewModel?.SaveSettingsCommand.Execute(null);
    }

    private void AttachToggle(string name)
    {
        if (this.FindControl<CheckBox>(name) is { } checkBox)
        {
            checkBox.PropertyChanged += OnTogglePropertyChanged;
        }
    }

    private void DetachToggle(string name)
    {
        if (this.FindControl<CheckBox>(name) is { } checkBox)
        {
            checkBox.PropertyChanged -= OnTogglePropertyChanged;
        }
    }

    private void OnTogglePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ToggleButton.IsCheckedProperty && !Equals(e.OldValue, e.NewValue))
        {
            _viewModel?.SaveSettingsCommand.Execute(null);
        }
    }

    private void OnLanguageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null || sender is not ComboBox combo)
        {
            return;
        }

        if (combo.SelectedItem is not ComboBoxItem item || item.Tag is not string language)
        {
            return;
        }

        _viewModel.ChangeLanguage(language);
        SelectLanguageItem(combo, language);
    }


    private static void SelectLanguageItem(ComboBox combo, string language)
    {
        var match = combo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is string tag && string.Equals(tag, language, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            combo.SelectedItem = match;
        }
    }

    private async Task CopyImageToClipboardAsync(CaptureRecord record)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        if (record.Preview is Bitmap preview)
        {
            var data = new DataObject();
            data.Set("Bitmap", preview);
            await clipboard.SetDataObjectAsync(data);
            return;
        }

        if (record.ImageBytes.Length > 0)
        {
            using var ms = new MemoryStream(record.ImageBytes);
            using var bitmap = new Bitmap(ms);
            var data = new DataObject();
            data.Set("Bitmap", bitmap);
            await clipboard.SetDataObjectAsync(data);
        }
    }
}
