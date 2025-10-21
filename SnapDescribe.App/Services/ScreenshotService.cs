using System;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
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
    private static readonly string[] IgnoredProcessNames =
    {
        "snapdescribe.exe",
        "snapdescribe.app.exe",
        "cgedata.exe",
        "hpaudiocontrol.exe",
        "hpaudiocontrol",
        "textinputhost.exe",
        "textinputhost"
    };

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

            var metadata = ResolveWindowMetadata(selection);

            DrawingBitmap? capturedBitmap = null;
            if (selection.IsWindowSelection && selection.WindowHandle != IntPtr.Zero)
            {
                capturedBitmap = CaptureWindow(selection.WindowHandle, selection.ScreenRect);
                if (capturedBitmap is not null && IsMostlyBlack(capturedBitmap))
                {
                    capturedBitmap.Dispose();
                    capturedBitmap = null;
                }
            }

            var finalBitmap = capturedBitmap ?? CropFromFullWithBringToFront(fullBitmap, selection, virtualLeft, virtualTop);

            return CreateResult(finalBitmap, metadata.ProcessName, metadata.WindowTitle);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log("Interactive capture failed", ex);
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

    private static (string? ProcessName, string? WindowTitle) ResolveWindowMetadata(SelectionResult selection)
    {
        if (selection.WindowHandle != IntPtr.Zero)
        {
            if (TryGetWindowMetadata(selection.WindowHandle, out var metadata) && !IsIgnoredProcess(metadata.ProcessName))
            {
                return metadata;
            }
        }

        var rect = selection.ScreenRect;
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return default;
        }

        if (TryResolveMetadataFromPoint(rect, out var metadataFromPoint))
        {
            return metadataFromPoint;
        }

        if (TryResolveMetadataFromOverlap(rect, out var metadataFromOverlap))
        {
            return metadataFromOverlap;
        }

        return default;
    }

    private static bool TryResolveMetadataFromPoint(PixelRect rect, out (string? ProcessName, string? WindowTitle) metadata)
    {
        metadata = default;

        var centerX = rect.X + rect.Width / 2;
        var centerY = rect.Y + rect.Height / 2;
        var point = new Native.POINT(centerX, centerY);
        var handle = Native.WindowFromPoint(point);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        handle = Native.GetAncestor(handle, Native.GA_ROOT);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        if (!TryGetWindowMetadata(handle, out metadata))
        {
            return false;
        }

        if (IsIgnoredProcess(metadata.ProcessName))
        {
            metadata = default;
            return false;
        }

        return true;
    }

    private static bool TryResolveMetadataFromOverlap(PixelRect rect, out (string? ProcessName, string? WindowTitle) metadata)
    {
        metadata = default;

        var bestArea = 0L;
        var bestHandle = IntPtr.Zero;
        (string? ProcessName, string? WindowTitle)? bestMetadata = null;
        var selectionRight = rect.X + rect.Width;
        var selectionBottom = rect.Y + rect.Height;

        Native.EnumWindows((hwnd, _) =>
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd))
            {
                return true;
            }

            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd))
            {
                return true;
            }

            if (!Native.GetWindowRect(hwnd, out var windowRect))
            {
                return true;
            }

            var width = windowRect.Right - windowRect.Left;
            var height = windowRect.Bottom - windowRect.Top;
            if (width <= 0 || height <= 0)
            {
                return true;
            }

            var className = Native.GetWindowClassName(hwnd);
            if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            {
                return true;
            }

            var overlapLeft = Math.Max(rect.X, windowRect.Left);
            var overlapTop = Math.Max(rect.Y, windowRect.Top);
            var overlapRight = Math.Min(selectionRight, windowRect.Right);
            var overlapBottom = Math.Min(selectionBottom, windowRect.Bottom);

            var overlapWidth = overlapRight - overlapLeft;
            var overlapHeight = overlapBottom - overlapTop;
            if (overlapWidth <= 0 || overlapHeight <= 0)
            {
                return true;
            }

            var overlapArea = (long)overlapWidth * overlapHeight;
            if (overlapArea <= bestArea)
            {
                return true;
            }

            if (!TryGetWindowMetadata(hwnd, out var candidateMetadata))
            {
                return true;
            }

            if (IsIgnoredProcess(candidateMetadata.ProcessName))
            {
                return true;
            }

            bestArea = overlapArea;
            bestHandle = hwnd;
            bestMetadata = candidateMetadata;
            return true;
        }, IntPtr.Zero);

        if (bestHandle == IntPtr.Zero || bestMetadata is null)
        {
            return false;
        }

        metadata = bestMetadata.Value;
        return true;
    }

    private static bool IsIgnoredProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        foreach (var ignored in IgnoredProcessNames)
        {
            if (processName.Contains(ignored, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetWindowMetadata(IntPtr hwnd, out (string? ProcessName, string? WindowTitle) metadata)
    {
        metadata = default;

        if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd))
        {
            return false;
        }

        string? windowTitle = null;
        try
        {
            var capacity = Native.GetWindowTextLength(hwnd);
            if (capacity > 0)
            {
                var builder = new StringBuilder(capacity + 1);
                if (Native.GetWindowText(hwnd, builder, builder.Capacity) > 0)
                {
                    windowTitle = builder.ToString().Trim();
                }
            }
        }
        catch
        {
            // ignored
        }

        string? processName = null;
        try
        {
            Native.GetWindowThreadProcessId(hwnd, out var processId);
            if (processId != 0)
            {
                using var process = Process.GetProcessById((int)processId);
                processName = TryResolveProcessName(process);
            }
        }
        catch
        {
            // ignored
        }

        if (string.IsNullOrWhiteSpace(processName) && string.IsNullOrWhiteSpace(windowTitle))
        {
            return false;
        }

        metadata = (processName, windowTitle);
        return true;
    }

    private static string? TryResolveProcessName(Process process)
    {
        try
        {
            var fileName = process.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                var name = Path.GetFileName(fileName);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
        }
        catch
        {
            // ignored: accessing MainModule can fail for protected processes.
        }

        return process.ProcessName;
    }

    private static ScreenshotResult CreateResult(DrawingBitmap bitmap, string? processName, string? windowTitle)
    {
        using var memory = new MemoryStream();
        bitmap.Save(memory, ImageFormat.Png);
        var pngBytes = memory.ToArray();
        using var previewStream = new MemoryStream(pngBytes);
        var avaloniaBitmap = new AvaloniaBitmap(previewStream);
        bitmap.Dispose();
        return new ScreenshotResult(pngBytes, avaloniaBitmap, processName, windowTitle);
    }

    private static DrawingBitmap CropFromFull(DrawingBitmap source, PixelRect screenRect, int virtualLeft, int virtualTop)
    {
        var crop = new DrawingBitmap(screenRect.Width, screenRect.Height);
        using var graphics = DrawingGraphics.FromImage(crop);
        var sourceRect = new DrawingRectangle(screenRect.X - virtualLeft, screenRect.Y - virtualTop, screenRect.Width, screenRect.Height);
        graphics.DrawImage(source, new DrawingRectangle(0, 0, crop.Width, crop.Height), sourceRect, System.Drawing.GraphicsUnit.Pixel);
        return crop;
    }

    private static DrawingBitmap CropFromFullWithBringToFront(DrawingBitmap originalFullScreen, SelectionResult selection, int virtualLeft, int virtualTop)
    {
        // If this is a window selection with a valid handle, bring it to front and re-capture
        if (selection.IsWindowSelection && selection.WindowHandle != IntPtr.Zero)
        {
            try
            {
                // Save current foreground window
                var previousForeground = Native.GetForegroundWindow();

                // Restore if minimized
                if (Native.IsIconic(selection.WindowHandle))
                {
                    Native.ShowWindow(selection.WindowHandle, Native.SW_RESTORE);
                }

                // Bring target window to front
                Native.SetForegroundWindow(selection.WindowHandle);

                // Wait 300ms for window to fully render on top
                Thread.Sleep(300);

                // Re-capture full screen
                var virtualWidth = Native.GetSystemMetrics(Native.SystemMetric.SM_CXVIRTUALSCREEN);
                var virtualHeight = Native.GetSystemMetrics(Native.SystemMetric.SM_CYVIRTUALSCREEN);
                using var newFullBitmap = new DrawingBitmap(virtualWidth, virtualHeight);
                using (var graphics = DrawingGraphics.FromImage(newFullBitmap))
                {
                    graphics.CopyFromScreen(virtualLeft, virtualTop, 0, 0,
                        new DrawingSize(virtualWidth, virtualHeight),
                        DrawingCopyPixelOperation.SourceCopy);
                }

                // Restore previous foreground window
                if (previousForeground != IntPtr.Zero && previousForeground != selection.WindowHandle)
                {
                    Native.SetForegroundWindow(previousForeground);
                }

                // Crop from the new full screenshot
                return CropFromFull(newFullBitmap, selection.ScreenRect, virtualLeft, virtualTop);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Log("Bring-to-front re-capture failed, using original screenshot", ex);
                // Fall back to cropping from original full screen
            }
        }

        // If not a window selection, or if bring-to-front logic failed, use original full screen
        return CropFromFull(originalFullScreen, selection.ScreenRect, virtualLeft, virtualTop);
    }

    private static bool IsMostlyBlack(DrawingBitmap bitmap)
    {
        try
        {
            var sampleRect = new DrawingRectangle(
                Math.Max(0, bitmap.Width / 2 - 20),
                Math.Max(0, bitmap.Height / 2 - 20),
                Math.Min(bitmap.Width, 40),
                Math.Min(bitmap.Height, 40));

            if (sampleRect.Width <= 0 || sampleRect.Height <= 0)
            {
                return false;
            }

            using var clone = bitmap.Clone(sampleRect, bitmap.PixelFormat);
            var data = clone.LockBits(new DrawingRectangle(0, 0, clone.Width, clone.Height),
                ImageLockMode.ReadOnly, clone.PixelFormat);

            try
            {
                var stride = Math.Abs(data.Stride);
                var bytes = stride * data.Height;
                var buffer = new byte[bytes];
                Marshal.Copy(data.Scan0, buffer, 0, bytes);

                var bpp = System.Drawing.Image.GetPixelFormatSize(data.PixelFormat) / 8;
                if (bpp <= 0)
                {
                    return false;
                }

                var darkPixels = 0;
                var totalPixels = data.Height * data.Width;

                for (var y = 0; y < data.Height; y++)
                {
                    for (var x = 0; x < data.Width; x++)
                    {
                        var offset = y * stride + x * bpp;
                        var b = buffer[offset];
                        var g = buffer[offset + 1];
                        var r = buffer[offset + 2];
                        var luminance = (0.299 * r) + (0.587 * g) + (0.114 * b);
                        if (luminance < 8)
                        {
                            darkPixels++;
                        }
                    }
                }

                return darkPixels / (double)totalPixels > 0.95;
            }
            finally
            {
                clone.UnlockBits(data);
            }
        }
        catch
        {
            return false;
        }
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

            Thread.Sleep(300);

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
            DiagnosticLogger.Log("Window capture failed, falling back to screen crop", ex);
            return null;
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Screenshot capture is only supported on Windows.");
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
        public static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        public const uint GA_ROOT = 2;
        public const int SW_RESTORE = 9;

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public POINT(int x, int y)
            {
                X = x;
                Y = y;
            }

            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public static string GetWindowClassName(IntPtr hWnd)
        {
            var builder = new StringBuilder(256);
            return GetClassName(hWnd, builder, builder.Capacity) > 0
                ? builder.ToString()
                : string.Empty;
        }
    }
}
