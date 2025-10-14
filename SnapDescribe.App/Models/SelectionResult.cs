using System;
using Avalonia;

namespace SnapDescribe.App.Models;

public sealed class SelectionResult
{
    public SelectionResult(PixelRect overlayRect, PixelRect screenRect, bool isWindowSelection, IntPtr windowHandle)
    {
        OverlayRect = overlayRect;
        ScreenRect = screenRect;
        IsWindowSelection = isWindowSelection;
        WindowHandle = windowHandle;
    }

    public PixelRect OverlayRect { get; }

    public PixelRect ScreenRect { get; }

    public bool IsWindowSelection { get; }

    public IntPtr WindowHandle { get; }
}
