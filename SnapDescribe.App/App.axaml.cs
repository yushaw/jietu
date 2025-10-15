using System;
using System.Net.Http;
using System.Threading;
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
    private LocalizationService? _localization;
    private NativeMenuItem? _trayOpenItem;
    private NativeMenuItem? _trayCaptureItem;
    private NativeMenuItem? _trayExitItem;
    private EventHandler? _localizationChangedHandler;
    private EventHandler<bool>? _visibilityHandler;
    private bool _shouldRestoreMainWindow;
    private static EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationWaitHandle;

    public App()
    {
        _current = this;

        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
    }

    public static IServiceProvider Services => _current?._services
        ?? throw new InvalidOperationException("Service provider has not been initialized.");

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services ??= ConfigureServices();
            DiagnosticLogger.Log("Application started, service container created.");
            _localization = _services.GetRequiredService<LocalizationService>();
            var settingsService = _services.GetRequiredService<SettingsService>();
            _localization.ApplyLanguage(settingsService.Current.Language);
            _localizationChangedHandler = (_, _) => UpdateTrayTexts();
            _localization.LanguageChanged += _localizationChangedHandler;
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
            throw new PlatformNotSupportedException("SnapDescribe currently supports Windows only.");
        }

        var services = new ServiceCollection();

        services.AddSingleton(new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        });
        services.AddSingleton<SettingsService>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<CapabilityResolver>();
        services.AddSingleton<IScreenshotService, ScreenshotService>();
        services.AddSingleton<IAiClient, GlmClient>();
        services.AddSingleton<GlobalHotkeyService>();
        services.AddSingleton<StartupRegistrationService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    public static void RegisterActivationEvent(EventWaitHandle handle)
    {
        _activationEvent = handle;
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

        _trayOpenItem = new NativeMenuItem();
        _trayOpenItem.Click += (_, _) => Dispatcher.UIThread.Post(() => ShowMainWindow(mainWindow));

        _trayCaptureItem = new NativeMenuItem();
        _trayCaptureItem.Click += async (_, _) =>
        {
            await viewModel.CaptureCommand.ExecuteAsync(null);
        };

        _trayExitItem = new NativeMenuItem();
        _trayExitItem.Click += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            mainWindow.ForceClose();
            desktop.Shutdown();
        });

        var menu = new NativeMenu();
        menu.Items.Add(_trayOpenItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_trayCaptureItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_trayExitItem);

        _trayIcon = new TrayIcon
        {
            Icon = LoadWindowIcon(),
            ToolTipText = L("App.Tray.Tooltip"),
            Menu = menu
        };

        _trayIcon.Clicked += (_, _) => ShowMainWindow(mainWindow);

        UpdateTrayTexts();
        _trayIcon.IsVisible = true;

        _visibilityHandler = (_, visible) =>
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                ToggleMainWindow(mainWindow, visible);
            }
            else
            {
                Dispatcher.UIThread.Post(() => ToggleMainWindow(mainWindow, visible));
            }
        };
        viewModel.RequestMainWindowVisibility += _visibilityHandler;

        if (_activationEvent is not null)
        {
            _activationWaitHandle = ThreadPool.RegisterWaitForSingleObject(
                _activationEvent,
                (_, _) => Dispatcher.UIThread.Post(() => ShowMainWindow(mainWindow)),
                null,
                -1,
                false);
        }
    }

    private void UpdateTrayTexts()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.ToolTipText = L("App.Tray.Tooltip");
        }

        if (_trayOpenItem is not null)
        {
            _trayOpenItem.Header = L("Tray.Open");
        }

        if (_trayCaptureItem is not null)
        {
            _trayCaptureItem.Header = L("Tray.Capture");
        }

        if (_trayExitItem is not null)
        {
            _trayExitItem.Header = L("Tray.Exit");
        }
    }

    private string L(string key) => _localization?.GetString(key) ?? key;

    private void ToggleMainWindow(MainWindow mainWindow, bool visible)
    {
        if (visible)
        {
            if (_shouldRestoreMainWindow)
            {
                _shouldRestoreMainWindow = false;
                ShowMainWindow(mainWindow);
            }
        }
        else if (mainWindow.IsVisible)
        {
            _shouldRestoreMainWindow = true;
            mainWindow.Hide();
        }
    }

    private void ShowMainWindow(MainWindow mainWindow)
    {
        if (!mainWindow.IsVisible)
        {
            mainWindow.Show();
        }

        mainWindow.WindowState = WindowState.Normal;
        mainWindow.Activate();
    }

    private void Shutdown()
    {
        DiagnosticLogger.Log("Application is shutting down.");
        _trayIcon?.Dispose();
        _trayIcon = null;
        _trayIconIcon = null;

        if (_localization is not null && _localizationChangedHandler is not null)
        {
            _localization.LanguageChanged -= _localizationChangedHandler;
            _localizationChangedHandler = null;
        }

        if (_services is not null && _visibilityHandler is not null)
        {
            var vm = _services.GetService<MainWindowViewModel>();
            if (vm is not null)
            {
                vm.RequestMainWindowVisibility -= _visibilityHandler;
            }

            _visibilityHandler = null;
        }

        _activationWaitHandle?.Unregister(null);
        _activationWaitHandle = null;

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
            DiagnosticLogger.Log("Unhandled exception", exception);
        }
        else
        {
            DiagnosticLogger.Log($"Unhandled exception: {e.ExceptionObject}");
        }
    }

    private static void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        DiagnosticLogger.Log("Unobserved task exception", e.Exception);
        e.SetObserved();
    }
}
