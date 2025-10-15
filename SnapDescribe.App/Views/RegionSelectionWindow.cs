using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SnapDescribe.App.Models;

namespace SnapDescribe.App.Views;

public sealed class RegionSelectionWindow : Window
{
    private static readonly string[] IgnoredProcessNames =
    {
        "snapdescribe.exe",
        "snapdescribe.app.exe",
        "cgedata.exe",
        "hpaudiocontrol.exe",
        "hpaudiocontrol"
    };

    private readonly Canvas _overlayCanvas;
    private readonly Border _selectionBorder;
    private readonly Border _hoverBorder;
    private readonly TaskCompletionSource<SelectionResult?> _selectionCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly PixelPoint _screenOrigin;
    private PixelRect? _hoverWindowRect;
    private PixelRect? _hoverWindowScreenRect;
    private IntPtr? _hoverWindowHandle;
    private Point? _startPoint;
    private bool _isDragging;

    public RegionSelectionWindow(Bitmap background, PixelPoint screenPosition)
    {
        _screenOrigin = screenPosition;

        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        CanResize = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = screenPosition;
        Width = background.PixelSize.Width;
        Height = background.PixelSize.Height;

        Cursor = new Cursor(StandardCursorType.Cross);
        Background = Brushes.Transparent;

        var root = new Grid
        {
            ClipToBounds = true
        };

        var image = new Image
        {
            Source = background,
            Stretch = Stretch.None
        };

        _overlayCanvas = new Canvas
        {
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromArgb(48, 0, 0, 0))
        };

        _selectionBorder = new Border
        {
            BorderBrush = Brushes.DeepSkyBlue,
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromArgb(40, 30, 144, 255)),
            IsVisible = false
        };

        _hoverBorder = new Border
        {
            BorderBrush = Brushes.Orange,
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromArgb(30, 255, 140, 0)),
            IsVisible = false
        };

        root.Children.Add(image);
        root.Children.Add(_overlayCanvas);
        _overlayCanvas.Children.Add(_hoverBorder);
        _overlayCanvas.Children.Add(_selectionBorder);

        Content = root;

        KeyDown += HandleKeyDown;
        PointerPressed += HandlePointerPressed;
        PointerMoved += HandlePointerMoved;
        PointerReleased += HandlePointerReleased;
        Closed += (_, _) => _selectionCompletion.TrySetResult(null);
    }

    public Task<SelectionResult?> GetSelectionAsync()
    {
        Show();
        Activate();
        Focus();
        return _selectionCompletion.Task;
    }

    private void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _selectionCompletion.TrySetResult(null);
            Close();
        }
    }

    private void HandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_selectionCompletion.Task.IsCompleted)
        {
            return;
        }

        _startPoint = e.GetPosition(this);
        _isDragging = false;
        _selectionBorder.IsVisible = true;
        UpdateSelection(_startPoint.Value, _startPoint.Value);
        _hoverBorder.IsVisible = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void HandlePointerMoved(object? sender, PointerEventArgs e)
    {
        var position = e.GetPosition(this);

        if (_startPoint.HasValue && e.Pointer.Captured == this)
        {
            if (!_isDragging && Distance(_startPoint.Value, position) >= 5)
            {
                _isDragging = true;
            }

            if (_isDragging)
            {
                UpdateSelection(_startPoint.Value, position);
            }

            e.Handled = true;
            return;
        }

        UpdateHoverBorder(position);
    }

    private void HandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_startPoint.HasValue && e.Pointer.Captured == this)
        {
            var endPoint = e.GetPosition(this);
            e.Pointer.Capture(null);

            SelectionResult? result = null;

            if (_isDragging)
            {
                var rect = BuildPixelRect(_startPoint.Value, endPoint);
                if (rect.Width >= 4 && rect.Height >= 4)
                {
                    var screenRect = new PixelRect(_screenOrigin.X + rect.X, _screenOrigin.Y + rect.Y, rect.Width, rect.Height);
                    var handle = TryGetDominantWindow(screenRect, out var matchedHandle)
                        ? matchedHandle
                        : IntPtr.Zero;
                    result = new SelectionResult(rect, screenRect, false, handle);
                }
            }
            else if (_hoverWindowRect is { Width: >= 4, Height: >= 4 } hoverRect &&
                     _hoverWindowScreenRect is { Width: >= 4, Height: >= 4 } hoverScreenRect &&
                     _hoverWindowHandle is { } handle && handle != IntPtr.Zero)
            {
                result = new SelectionResult(hoverRect, hoverScreenRect, true, handle);
            }

            _startPoint = null;
            _isDragging = false;
            _selectionBorder.IsVisible = false;

            if (result is null)
            {
                _selectionCompletion.TrySetResult(null);
            }
            else
            {
                _selectionCompletion.TrySetResult(result);
            }

            Close();
            e.Handled = true;
        }
    }

    private void UpdateSelection(Point start, Point end)
    {
        var x = Math.Min(start.X, end.X);
        var y = Math.Min(start.Y, end.Y);
        var width = Math.Abs(end.X - start.X);
        var height = Math.Abs(end.Y - start.Y);

        Canvas.SetLeft(_selectionBorder, x);
        Canvas.SetTop(_selectionBorder, y);
        _selectionBorder.Width = Math.Max(1, width);
        _selectionBorder.Height = Math.Max(1, height);
    }

    private void UpdateHoverBorder(Point position)
    {
        if (TryGetWindowRect(position, out var overlayRect, out var screenRect, out var handle))
        {
            _hoverWindowRect = overlayRect;
            _hoverWindowScreenRect = screenRect;
            _hoverWindowHandle = handle;
            _hoverBorder.IsVisible = true;
            Canvas.SetLeft(_hoverBorder, overlayRect.X);
            Canvas.SetTop(_hoverBorder, overlayRect.Y);
            _hoverBorder.Width = overlayRect.Width;
            _hoverBorder.Height = overlayRect.Height;
        }
        else
        {
            _hoverWindowRect = null;
            _hoverWindowScreenRect = null;
            _hoverWindowHandle = null;
            _hoverBorder.IsVisible = false;
        }
    }

    private bool TryGetWindowRect(Point pointerPosition, out PixelRect overlayRect, out PixelRect screenRect, out IntPtr handle)
    {
        overlayRect = default;
        screenRect = default;
        handle = IntPtr.Zero;
        var screenX = _screenOrigin.X + (int)Math.Round(pointerPosition.X);
        var screenY = _screenOrigin.Y + (int)Math.Round(pointerPosition.Y);

        PixelRect? candidate = null;
        PixelRect? candidateScreen = null;
        var bestArea = double.MaxValue;
        var overlayHandle = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        var bestHandle = IntPtr.Zero;

        Native.EnumWindows((hwnd, _) =>
        {
            if (hwnd == overlayHandle)
            {
                return true;
            }

            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd))
            {
                return true;
            }

            if (!Native.GetWindowRect(hwnd, out var nativeRect))
            {
                return true;
            }

            var width = nativeRect.Right - nativeRect.Left;
            var height = nativeRect.Bottom - nativeRect.Top;
            if (width <= 0 || height <= 0)
            {
                return true;
            }

            if (screenX >= nativeRect.Left && screenX <= nativeRect.Right &&
                screenY >= nativeRect.Top && screenY <= nativeRect.Bottom)
            {
                var className = Native.GetWindowClassName(hwnd);
                if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                {
                    return true;
                }

                if (ShouldSkipWindow(hwnd))
                {
                    return true;
                }

                var area = (double)width * height;
                if (area >= bestArea)
                {
                    return true;
                }

                var localX = nativeRect.Left - _screenOrigin.X;
                var localY = nativeRect.Top - _screenOrigin.Y;

                var overlayCandidate = new PixelRect(localX, localY, width, height);
                var adjustedOverlay = ClampToOverlay(overlayCandidate);
                var deltaX = adjustedOverlay.X - localX;
                var deltaY = adjustedOverlay.Y - localY;
                var adjustedScreen = new PixelRect(nativeRect.Left + deltaX, nativeRect.Top + deltaY, adjustedOverlay.Width, adjustedOverlay.Height);

                candidate = adjustedOverlay;
                candidateScreen = adjustedScreen;
                bestArea = area;
                bestHandle = hwnd;
            }

            return true;
        }, IntPtr.Zero);

        if (candidate is { Width: >= 4, Height: >= 4 } overlayResult &&
            candidateScreen is { Width: >= 4, Height: >= 4 } screenResult &&
            bestHandle != IntPtr.Zero)
        {
            overlayRect = overlayResult;
            screenRect = screenResult;
            handle = bestHandle;
            return true;
        }

        return false;
    }

    private bool TryGetDominantWindow(PixelRect screenRect, out IntPtr handle)
    {
        handle = IntPtr.Zero;

        if (screenRect.Width <= 0 || screenRect.Height <= 0)
        {
            return false;
        }

        var selectionLeft = screenRect.X;
        var selectionTop = screenRect.Y;
        var selectionRight = screenRect.X + screenRect.Width;
        var selectionBottom = screenRect.Y + screenRect.Height;
        var overlayHandle = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        var bestArea = 0L;
        var bestHandle = IntPtr.Zero;

        Native.EnumWindows((hwnd, _) =>
        {
            if (hwnd == overlayHandle)
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

            if (ShouldSkipWindow(hwnd))
            {
                return true;
            }

            var overlapLeft = Math.Max(selectionLeft, windowRect.Left);
            var overlapTop = Math.Max(selectionTop, windowRect.Top);
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

            bestArea = overlapArea;
            bestHandle = hwnd;
            return true;
        }, IntPtr.Zero);

        handle = bestHandle;
        return handle != IntPtr.Zero;
    }

    private PixelRect ClampToOverlay(PixelRect rect)
    {
        var maxWidth = Math.Max(0, (int)Math.Round(Bounds.Width));
        var maxHeight = Math.Max(0, (int)Math.Round(Bounds.Height));

        var x = Math.Clamp(rect.X, 0, maxWidth);
        var y = Math.Clamp(rect.Y, 0, maxHeight);
        var right = Math.Clamp(rect.X + rect.Width, 0, maxWidth);
        var bottom = Math.Clamp(rect.Y + rect.Height, 0, maxHeight);

        var width = Math.Max(0, right - x);
        var height = Math.Max(0, bottom - y);
        return new PixelRect(x, y, width, height);
    }

    private static PixelRect BuildPixelRect(Point start, Point end)
    {
        var x = (int)Math.Round(Math.Min(start.X, end.X));
        var y = (int)Math.Round(Math.Min(start.Y, end.Y));
        var width = (int)Math.Round(Math.Abs(end.X - start.X));
        var height = (int)Math.Round(Math.Abs(end.Y - start.Y));
        return new PixelRect(x, y, width, height);
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool ShouldSkipWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return true;
        }

        if (!TryGetProcessName(hwnd, out var processName) || string.IsNullOrWhiteSpace(processName))
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

    private static bool TryGetProcessName(IntPtr hwnd, out string? processName)
    {
        processName = null;

        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        if (Native.GetWindowThreadProcessId(hwnd, out var processId) == 0 || processId == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            try
            {
                var fileName = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    processName = Path.GetFileName(fileName);
                }
            }
            catch
            {
                // Accessing some modules may fail due to permissions; fall back to ProcessName.
            }

            if (string.IsNullOrWhiteSpace(processName))
            {
                var name = process.ProcessName;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    processName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        ? name
                        : name + ".exe";
                }
            }
        }
        catch
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(processName);
    }

    private static class Native
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        public static string GetWindowClassName(IntPtr hWnd)
        {
            var builder = new StringBuilder(256);
            return GetClassName(hWnd, builder, builder.Capacity) > 0
                ? builder.ToString()
                : string.Empty;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
