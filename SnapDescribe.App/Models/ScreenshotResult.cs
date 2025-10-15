using Avalonia.Media.Imaging;

namespace SnapDescribe.App.Models;

public class ScreenshotResult
{
    public ScreenshotResult(byte[] pngBytes, Bitmap preview, string? processName = null, string? windowTitle = null)
    {
        PngBytes = pngBytes;
        Preview = preview;
        ProcessName = processName;
        WindowTitle = windowTitle;
    }

    public byte[] PngBytes { get; }

    public Bitmap Preview { get; }

    public string? ProcessName { get; }

    public string? WindowTitle { get; }
}
