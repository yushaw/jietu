using System;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SnapDescribe.App.Models;
using SnapDescribe.App.Views;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;
using DrawingCopyPixelOperation = System.Drawing.CopyPixelOperation;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace SnapDescribe.App.Services;

[SupportedOSPlatform("windows")]
public class ScreenshotService : IScreenshotService
{
    public async Task<ScreenshotResult?> CaptureInteractiveAsync()
    {
        EnsureWindows();

        try
        {
            var virtualLeft = Native.GetSystemMetrics(Native.SystemMetric.SM_XVIRTUALSCREEN);
            var virtualTop = Native.GetSystemMetrics(Native.SystemMetric.SM_YVIRTUALSCREEN);
            var virtualWidth = Native.GetSystemMetrics(Native.SystemMetric.SM_CXVIRTUALSCREEN);
            var virtualHeight = Native.GetSystemMetrics(Native.SystemMetric.SM_CYVIRTUALSCREEN);

            if (virtualWidth <= 0 || virtualHeight <= 0)
            {
                return null;
            }

            using var fullBitmap = new DrawingBitmap(virtualWidth, virtualHeight);
            using (var graphics = DrawingGraphics.FromImage(fullBitmap))
            {
                graphics.CopyFromScreen(virtualLeft, virtualTop, 0, 0, new DrawingSize(virtualWidth, virtualHeight), DrawingCopyPixelOperation.SourceCopy);
            }

            using var previewStream = new MemoryStream();
            fullBitmap.Save(previewStream, ImageFormat.Png);
            var pngBytes = previewStream.ToArray();
            previewStream.Position = 0;
            var previewBitmap = new AvaloniaBitmap(previewStream);

            var selection = await ShowOverlayAsync(previewBitmap, new PixelPoint(virtualLeft, virtualTop)).ConfigureAwait(false);
            previewBitmap.Dispose();

            if (selection is null || selection.OverlayRect.Width <= 5 || selection.OverlayRect.Height <= 5)
            {
                return null;
            }

            DrawingBitmap? capturedBitmap = null;
            if (selection.IsWindowSelection && selection.WindowHandle != IntPtr.Zero)
            {
                capturedBitmap = CaptureWindow(selection.WindowHandle, selection.ScreenRect);
            }

            var finalBitmap = capturedBitmap ?? CropFromFull(fullBitmap, selection.ScreenRect, virtualLeft, virtualTop);

            return CreateResult(finalBitmap);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log("交互式截图失败", ex);
            throw;
        }
    }

    private static async Task<SelectionResult?> ShowOverlayAsync(AvaloniaBitmap previewBitmap, PixelPoint origin)
    {
        Task<SelectionResult?> selectionTask = Task.FromResult<SelectionResult?>(null);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var overlay = new RegionSelectionWindow(previewBitmap, origin);
            selectionTask = overlay.GetSelectionAsync();
        });

        return await selectionTask.ConfigureAwait(false);
    }

    private static ScreenshotResult CreateResult(DrawingBitmap bitmap)
    {
        using var memory = new MemoryStream();
        bitmap.Save(memory, ImageFormat.Png);
        var pngBytes = memory.ToArray();
        using var previewStream = new MemoryStream(pngBytes);
        var avaloniaBitmap = new AvaloniaBitmap(previewStream);
        bitmap.Dispose();
        return new ScreenshotResult(pngBytes, avaloniaBitmap);
    }

    private static DrawingBitmap CropFromFull(DrawingBitmap source, PixelRect screenRect, int virtualLeft, int virtualTop)
    {
        var crop = new DrawingBitmap(screenRect.Width, screenRect.Height);
        using var graphics = DrawingGraphics.FromImage(crop);
        var sourceRect = new DrawingRectangle(screenRect.X - virtualLeft, screenRect.Y - virtualTop, screenRect.Width, screenRect.Height);
        graphics.DrawImage(source, new DrawingRectangle(0, 0, crop.Width, crop.Height), sourceRect, System.Drawing.GraphicsUnit.Pixel);
        return crop;
    }

    private static DrawingBitmap? CaptureWindow(IntPtr hwnd, PixelRect screenRect)
    {
        try
        {
            var width = Math.Max(1, screenRect.Width);
            var height = Math.Max(1, screenRect.Height);

            var previousForeground = Native.GetForegroundWindow();
            var windowThread = Native.GetWindowThreadProcessId(hwnd, out _);
            var currentThread = Native.GetCurrentThreadId();
            var attached = false;

            if (windowThread != 0 && currentThread != windowThread)
            {
                attached = Native.AttachThreadInput(currentThread, windowThread, true);
            }

            if (Native.IsIconic(hwnd))
            {
                Native.ShowWindow(hwnd, Native.SW_RESTORE);
            }

            Native.SetForegroundWindow(hwnd);

            if (attached)
            {
                Native.AttachThreadInput(currentThread, windowThread, false);
            }

            Thread.Sleep(50);

            var bitmap = new DrawingBitmap(width, height);
            using (var graphics = DrawingGraphics.FromImage(bitmap))
            {
                var hdc = graphics.GetHdc();
                var printed = Native.PrintWindow(hwnd, hdc, 0);
                graphics.ReleaseHdc(hdc);

                if (!printed)
                {
                    graphics.CopyFromScreen(screenRect.X, screenRect.Y, 0, 0, new DrawingSize(width, height), DrawingCopyPixelOperation.SourceCopy);
                }
            }

            if (previousForeground != IntPtr.Zero && previousForeground != hwnd)
            {
                Native.SetForegroundWindow(previousForeground);
            }

            return bitmap;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log("窗口截图失败，回退到裁剪整屏", ex);
            return null;
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("截图功能仅支持在 Windows 上运行。");
        }
    }

    private static class Native
    {
        public enum SystemMetric
        {
            SM_XVIRTUALSCREEN = 76,
            SM_YVIRTUALSCREEN = 77,
            SM_CXVIRTUALSCREEN = 78,
            SM_CYVIRTUALSCREEN = 79
        }

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(SystemMetric metric);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, int nFlags);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        public const int SW_RESTORE = 9;
    }
}
