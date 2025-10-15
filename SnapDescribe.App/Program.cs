using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using SnapDescribe.App.Views;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SnapDescribe.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "SnapDescribe.SingleInstance", out var createdNew);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "SnapDescribe.ShowWindow");

        if (!createdNew)
        {
            showEvent.Set();
            return;
        }

        App.RegisterActivationEvent(showEvent);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

internal static class NativeWindowHelper
{
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public static void BringExistingToFront()
    {
        var hwnd = FindWindow(null, "SnapDescribe");
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
        }

        SetForegroundWindow(hwnd);
    }
}
