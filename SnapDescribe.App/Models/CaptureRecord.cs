using System;
using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SnapDescribe.App.Models;

public partial class CaptureRecord : ObservableObject
{
    public required string Id { get; init; }

    public required string ImagePath { get; init; }

    public required string MarkdownPath { get; init; }

    public required DateTimeOffset CapturedAt { get; init; }

    public required string Prompt { get; init; }

    [ObservableProperty]
    private string responseMarkdown = string.Empty;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private Bitmap? preview;

    public ObservableCollection<ChatMessage> Conversation { get; } = new();

    public byte[] ImageBytes { get; set; } = Array.Empty<byte>();

    public string DisplayTitle => $"{CapturedAt:HH:mm:ss} - {System.IO.Path.GetFileNameWithoutExtension(ImagePath)}";
}
