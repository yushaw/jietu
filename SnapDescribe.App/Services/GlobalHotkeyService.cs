using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using SnapDescribe.App.Models;

namespace SnapDescribe.App.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int WmAppExecute = 0x8000 + 1;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly ConcurrentQueue<Action> _pendingActions = new();
    private readonly Dictionary<int, HotkeyRegistration> _registrations = new();
    private readonly object _syncRoot = new();
    private readonly Native.WndProc _wndProc;
    private Thread? _messageThread;
    private IntPtr _windowHandle;
    private TaskCompletionSource<IntPtr>? _windowReady;
    private int _nextId;
    private bool _disposed;

    public GlobalHotkeyService()
    {
        _wndProc = WindowProcedure;
    }

    public event EventHandler<HotkeyEventArgs>? HotkeyPressed;

    public int RegisterHotkey(string name, HotkeySetting setting)
    {
        if (!OperatingSystem.IsWindows())
        {
            return -1;
        }

        if (!setting.TryGetBinding(out var binding))
        {
            throw new InvalidOperationException($"Unable to parse shortcut: {setting.Shortcut}");
        }

        EnsureMessageWindow();

        return InvokeOnMessageThread(() =>
        {
            var id = Interlocked.Increment(ref _nextId);
            if (!Native.RegisterHotKey(_windowHandle, id, (uint)binding.Modifiers, binding.VirtualKey))
            {
                var error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"Failed to register hotkey (error {error}). Ensure the shortcut is not used by another application.");
            }

            _registrations[id] = new HotkeyRegistration(id, name, setting);
            return id;
        });
    }

    public void UnregisterHotkeys()
    {
        if (!OperatingSystem.IsWindows() || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        InvokeOnMessageThread(() =>
        {
            foreach (var registration in _registrations.Values)
            {
                Native.UnregisterHotKey(_windowHandle, registration.Id);
            }

            _registrations.Clear();
            return 0;
        });
    }

    private void EnsureMessageWindow()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_windowHandle != IntPtr.Zero)
            {
                return;
            }

            _windowReady = new TaskCompletionSource<IntPtr>(TaskCreationOptions.RunContinuationsAsynchronously);
            _messageThread = new Thread(MessageThreadMain)
            {
                Name = "GlobalHotkeyThread",
                IsBackground = true
            };
            _messageThread.Start();
        }

        _windowHandle = _windowReady!.Task.GetAwaiter().GetResult();
    }

    private void MessageThreadMain()
    {
        try
        {
            var moduleHandle = Native.GetModuleHandle(null);
            if (moduleHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Unable to acquire current module handle.");
            }
            var className = "SnapDescribeHotkeyWnd";

            var wndClass = new Native.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEX>(),
                lpfnWndProc = _wndProc,
                hInstance = moduleHandle,
                lpszClassName = className
            };

            var atom = Native.RegisterClassEx(ref wndClass);
            if (atom == 0)
            {
                var error = Marshal.GetLastWin32Error();
                _windowReady?.TrySetException(new InvalidOperationException($"RegisterClassEx failed ({error})."));
                return;
            }

            var handle = Native.CreateWindowEx(
                0,
                className,
                string.Empty,
                0,
                0,
                0,
                0,
                0,
                HwndMessage,
                IntPtr.Zero,
                moduleHandle,
                IntPtr.Zero);

            if (handle == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                _windowReady?.TrySetException(new InvalidOperationException($"CreateWindowEx failed ({error})."));
                return;
            }

            _windowReady?.TrySetResult(handle);

            while (Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                Native.TranslateMessage(ref msg);
                Native.DispatchMessage(ref msg);
            }

            Native.DestroyWindow(handle);
            Native.UnregisterClass(className, moduleHandle);
        }
        catch (Exception ex)
        {
            _windowReady?.TrySetException(ex);
        }
    }

    private T InvokeOnMessageThread<T>(Func<T> action)
    {
        if (_windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Hotkey window has not been initialized.");
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        _pendingActions.Enqueue(() =>
        {
            try
            {
                var result = action();
                completion.TrySetResult(result);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        if (!Native.PostMessage(_windowHandle, WmAppExecute, IntPtr.Zero, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            completion.TrySetException(new InvalidOperationException($"Failed to notify hotkey thread (error {error})."));
            return completion.Task.GetAwaiter().GetResult();
        }

        return completion.Task.GetAwaiter().GetResult();
    }

    private IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WmHotkey:
                HandleHotkey((int)wParam);
                break;
            case WmAppExecute:
                while (_pendingActions.TryDequeue(out var action))
                {
                    action();
                }
                break;
        }

        return Native.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void HandleHotkey(int id)
    {
        if (!_registrations.TryGetValue(id, out var registration))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            HotkeyPressed?.Invoke(this, new HotkeyEventArgs(registration.Name, registration.Setting));
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            UnregisterHotkeys();
        }
        catch
        {
            // ignored
        }

        if (_windowHandle != IntPtr.Zero)
        {
            Native.PostMessage(_windowHandle, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
            _windowHandle = IntPtr.Zero;
        }
    }

    private readonly record struct HotkeyRegistration(int Id, string Name, HotkeySetting Setting);
}

public sealed class HotkeyEventArgs : EventArgs
{
    public HotkeyEventArgs(string name, HotkeySetting setting)
    {
        Name = name;
        Setting = setting;
    }

    public string Name { get; }

    public HotkeySetting Setting { get; }
}

internal static class Native
{
    public delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProc? lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string? lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);
}
