using System;
using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using SnapDescribe.App.Services;

namespace SnapDescribe.App.Models;

public partial class CaptureRecord : ObservableObject
{
    public required string Id { get; init; }

    public required string ImagePath { get; init; }

    public required string MarkdownPath { get; init; }

    public required DateTimeOffset CapturedAt { get; init; }

    public required string Prompt { get; init; }

    public string? ProcessName { get; init; }

    public string? WindowTitle { get; init; }

    [ObservableProperty]
    private string responseMarkdown = string.Empty;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private Bitmap? preview;

    public ObservableCollection<ChatMessage> Conversation { get; } = new();

    public byte[] ImageBytes { get; set; } = Array.Empty<byte>();

    public string DisplayTitle => $"{CapturedAt:HH:mm:ss} - {System.IO.Path.GetFileNameWithoutExtension(ImagePath)}";

    public string DisplayContext
    {
        get
        {
            var hasProcess = !string.IsNullOrWhiteSpace(ProcessName);
            var hasWindow = !string.IsNullOrWhiteSpace(WindowTitle);

            var localization = LocalizationService.Instance;

            if (!hasProcess && !hasWindow)
            {
                return localization?.GetString("History.Empty") ?? "Unknown context";
            }

            if (hasProcess && hasWindow)
            {
            return localization?.GetString("History.ProcessAndWindow", ProcessName ?? string.Empty, WindowTitle ?? string.Empty)
                       ?? $"{ProcessName} · {WindowTitle}";
        }

        return hasProcess
                ? localization?.GetString("History.ProcessOnly", ProcessName ?? string.Empty) ?? ProcessName!
                : localization?.GetString("History.WindowOnly", WindowTitle ?? string.Empty) ?? WindowTitle!;
        }
    }

    public void RefreshDisplayContext() => OnPropertyChanged(nameof(DisplayContext));
}
