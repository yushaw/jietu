using System;
using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using SnapDescribe.App.Services;

namespace SnapDescribe.App.Models;

public partial class CaptureRecord : ObservableObject
{
    public CaptureRecord()
    {
        OcrSegments.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasOcrResults));
            OnPropertyChanged(nameof(NoOcrResults));
        };
    }

    public required string Id { get; init; }

    public required string ImagePath { get; init; }

    public required string MarkdownPath { get; init; }

    public required DateTimeOffset CapturedAt { get; init; }

    public required string CapabilityId { get; init; }

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

    public ObservableCollection<OcrSegment> OcrSegments { get; } = new();

    [ObservableProperty]
    private string? ocrLanguages;

    public string DisplayTitle => $"{CapturedAt:HH:mm:ss} - {System.IO.Path.GetFileNameWithoutExtension(ImagePath)}";

    public bool SupportsChat => string.Equals(CapabilityId, CapabilityIds.LanguageModel, StringComparison.OrdinalIgnoreCase)
        || string.Equals(CapabilityId, CapabilityIds.Agent, StringComparison.OrdinalIgnoreCase);

    public bool SupportsOcr => string.Equals(CapabilityId, CapabilityIds.Ocr, StringComparison.OrdinalIgnoreCase);

    public bool HasOcrResults => SupportsOcr && !string.IsNullOrWhiteSpace(ResponseMarkdown);

    public bool NoOcrResults => SupportsOcr && !HasOcrResults;

    public bool HasOcrLanguages => !string.IsNullOrWhiteSpace(OcrLanguages);

    public bool ShowPromptDetails => !SupportsOcr;

    public bool ShowOcrDetails => SupportsOcr;

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

    partial void OnOcrLanguagesChanged(string? value)
    {
        OnPropertyChanged(nameof(HasOcrLanguages));
    }

    partial void OnResponseMarkdownChanged(string value)
    {
        if (SupportsOcr)
        {
            OnPropertyChanged(nameof(HasOcrResults));
            OnPropertyChanged(nameof(NoOcrResults));
        }
    }
}
