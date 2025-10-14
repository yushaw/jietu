using Avalonia.Media.Imaging;

namespace SnapDescribe.App.Models;

public class ScreenshotResult
{
    public ScreenshotResult(byte[] pngBytes, Bitmap preview)
    {
        PngBytes = pngBytes;
        Preview = preview;
    }

    public byte[] PngBytes { get; }

    public Bitmap Preview { get; }
}
