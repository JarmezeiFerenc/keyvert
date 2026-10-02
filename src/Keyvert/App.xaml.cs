using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Keyvert.Core;
using Keyvert.Services;
using Keyvert.ViewModels;
using Keyvert.Views;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace Keyvert;

public partial class App : Application
{
    private const string Title = "Keyvert";

    private SingleInstance? _instance;
    private AppLog? _log;
    private EmulationEngine? _engine;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private MainViewModel? _viewModel;
    private bool _trayHintShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instance = SingleInstance.TryAcquire();
        if (_instance is null)
        {
            Shutdown();
            return;
        }

        string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Keyvert");
        _log = new AppLog(Path.Combine(dataDir, "Logs"));
        _log.Info($"Starting version {Assembly.GetExecutingAssembly().GetName().Version} on {Environment.OSVersion}.");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _log.Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };

        ApplicationThemeManager.ApplySystemTheme();

        var settingsStore = new SettingsStore(Path.Combine(dataDir, "settings.json"), _log);
        var settings = settingsStore.Load();
        var profiles = new ProfileStore(Path.Combine(dataDir, "Profiles"));

        KeyNames.TryParse(settings.ToggleKey, out int toggleKey, out _);
        _engine = new EmulationEngine(new MappingProfile { ToggleKey = toggleKey, Bindings = new Dictionary<int, ControllerAction>() }, _log);
        try
        {
            _engine.Start();
        }
        catch (Win32Exception ex)
        {
            _log.Error("Could not install the keyboard hook.", ex);
            MessageBox.Show($"Keyvert couldn't start listening to the keyboard.\n\n{ex.Message}", Title,
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _viewModel = new MainViewModel(_engine, profiles, settingsStore, settings, _log);
        _window = new MainWindow(_viewModel);
        _window.ExitRequested += ExitApp;
        _window.HiddenToTray += OnHiddenToTray;

        _tray = new TrayIcon(profiles.List);
        _tray.OpenRequested += _window.ShowFromTray;
        _tray.ToggleRequested += _viewModel.ToggleEmulation;
        _tray.ProfileSelected += _viewModel.SelectProfileFromTray;
        _tray.ExitRequested += ExitApp;
        _viewModel.TrayStateChanged += UpdateTray;
        UpdateTray();
        _engine.EnableRefused += () => Dispatcher.BeginInvoke(() =>
        {
            if (!_window.IsVisible)
                _tray.ShowBalloon(Title, "Install the ViGEmBus driver first. Emulation can't start without it.");
        });

        _instance.ListenForShowRequests(() => Dispatcher.BeginInvoke(_window.ShowFromTray));
        SystemEvents.SessionSwitch += OnSessionSwitch;

        if (!settings.StartMinimized)
            _window.Show();

        _ = _viewModel.ConnectAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _engine?.Dispose();
        _tray?.Dispose();
        _instance?.Dispose();
        _log?.Info("Exited.");
        base.OnExit(e);
    }

    private void ExitApp()
    {
        if (_window is not null)
            _window.AllowClose = true;
        Shutdown();
    }

    private void UpdateTray()
    {
        if (_tray is not null && _engine is not null && _viewModel is not null)
            _tray.Update(_engine.Enabled, _viewModel.CurrentProfile, _viewModel.ToggleKeyLabel);
    }

    private void OnHiddenToTray()
    {
        if (_trayHintShown)
            return;
        _trayHintShown = true;
        _tray?.ShowBalloon(Title, "Still running here. Right-click the icon to exit.");
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        // Key-ups pressed before locking the PC may never arrive, so drop everything that's held.
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            _engine?.ReleaseAll();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log?.Error("Unhandled UI exception.", e.Exception);
        e.Handled = true;
        MessageBox.Show($"Something went wrong:\n\n{e.Exception.Message}\n\nDetails were written to the log.", Title,
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _log?.Error("Fatal exception.", e.ExceptionObject as Exception);

        // Unplug the virtual controller so nothing stays pressed after the crash.
        _engine?.Dispose();
    }
}
