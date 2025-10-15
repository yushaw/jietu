using System;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using SnapDescribe.App.Services;
using SnapDescribe.App.ViewModels;
using SnapDescribe.App.Views;

namespace SnapDescribe.App;

public partial class App : Application
{
    private static App? _current;
    private IServiceProvider? _services;
    private TrayIcon? _trayIcon;
    private WindowIcon? _trayIconIcon;

    public App()
    {
        _current = this;

        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
    }

    public static IServiceProvider Services => _current?._services
        ?? throw new InvalidOperationException("应用尚未初始化 ServiceProvider。");

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services ??= ConfigureServices();
            DiagnosticLogger.Log("应用启动，服务容器已创建。");
            var mainWindow = _services.GetRequiredService<MainWindow>();
            mainWindow.Icon = LoadWindowIcon();
            desktop.MainWindow = mainWindow;
            desktop.Exit += (_, _) => Shutdown();

            InitializeTray(desktop, mainWindow);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private IServiceProvider ConfigureServices()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("SnapDescribe 目前仅支持在 Windows 上运行。");
        }

        var services = new ServiceCollection();

        services.AddSingleton(new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        });
        services.AddSingleton<SettingsService>();
        services.AddSingleton<IScreenshotService, ScreenshotService>();
        services.AddSingleton<IAiClient, GlmClient>();
        services.AddSingleton<GlobalHotkeyService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    private WindowIcon LoadWindowIcon()
    {
        if (_trayIconIcon is not null)
        {
            return _trayIconIcon;
        }

        var assemblyName = typeof(App).Assembly.GetName().Name;
        var uri = new Uri($"avares://{assemblyName}/Assets/AppIcon.png");
        using var stream = AssetLoader.Open(uri);
        _trayIconIcon = new WindowIcon(stream);
        return _trayIconIcon;
    }

    private void InitializeTray(IClassicDesktopStyleApplicationLifetime desktop, MainWindow mainWindow)
    {
        var viewModel = _services!.GetRequiredService<MainWindowViewModel>();

        var openItem = new NativeMenuItem("打开主界面");
        openItem.Click += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            mainWindow.Show();
            mainWindow.Activate();
        });

        var captureItem = new NativeMenuItem("捕捉屏幕");
        captureItem.Click += async (_, _) =>
        {
            await viewModel.CaptureCommand.ExecuteAsync(null);
        };

        var exitItem = new NativeMenuItem("退出");
        exitItem.Click += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            mainWindow.ForceClose();
            desktop.Shutdown();
        });

        var menu = new NativeMenu();
        menu.Items.Add(openItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(captureItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exitItem);

        _trayIcon = new TrayIcon
        {
            Icon = LoadWindowIcon(),
            ToolTipText = "SnapDescribe 截图助手",
            Menu = menu
        };

        _trayIcon.Clicked += (_, _) =>
        {
            mainWindow.Show();
            mainWindow.Activate();
        };

        _trayIcon.IsVisible = true;
    }

    private void Shutdown()
    {
        DiagnosticLogger.Log("应用正在关闭。");
        _trayIcon?.Dispose();
        _trayIcon = null;
        _trayIconIcon = null;

        if (_services is null)
        {
            return;
        }

        if (_services.GetService<GlobalHotkeyService>() is { } hotkeys)
        {
            hotkeys.Dispose();
        }

        if (_services is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static void HandleUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            DiagnosticLogger.Log("未处理异常", exception);
        }
        else
        {
            DiagnosticLogger.Log($"未处理异常：{e.ExceptionObject}");
        }
    }

    private static void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        DiagnosticLogger.Log("未观察的任务异常", e.Exception);
        e.SetObserved();
    }
}
