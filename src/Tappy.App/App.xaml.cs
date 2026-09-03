using System.Windows;
using System.Windows.Threading;
using Tappy.App.Runtime;
using Tappy.App.Services;
using Tappy.App.ViewModels;
using Tappy.Windows;
using Tappy.Windows.Input;
using Tappy.Windows.Lifecycle;

namespace Tappy.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private SessionMarker? _sessionMarker;
    private MainWindow? _mainWindow;
    private ApplicationLifecycleSignalSource? _applicationLifecycle;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, _) => _mainWindow?.FailSafeStop("unhandled UI failure");
        AppDomain.CurrentDomain.UnhandledException += (_, _) => _mainWindow?.FailSafeStop("unhandled process failure");
        if (e.Args.Any(argument => string.Equals(argument, "--readiness-smoke", StringComparison.OrdinalIgnoreCase)))
        {
            var code = await ReadinessSmokeRunner.RunAsync(e.Args).ConfigureAwait(true);
            Shutdown(code);
            return;
        }

        try
        {
            ApplicationIdentityService.Apply(ProductIdentity.AppUserModelId);
            ThemeService.Apply(light: false);
            _singleInstance = new Mutex(initiallyOwned: true, ProductIdentity.SingleInstanceMutexName, out var created);
            if (!created)
            {
                System.Windows.MessageBox.Show(
                    "Tappy is already running. Use its window or notification-area icon.",
                    "Tappy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                _singleInstance.Dispose();
                _singleInstance = null;
                Shutdown(0);
                return;
            }

            _sessionMarker = new SessionMarker(ProductIdentity.LocalDataRoot);
            var previous = _sessionMarker.Begin();
            await ShowSplashAsync().ConfigureAwait(true);
            if (previous is not null)
            {
                System.Windows.MessageBox.Show(
                    "Tappy's previous session did not close cleanly. No controller will be armed automatically. " +
                    "The saved profile will be validated and its last-known-good copy used if recovery is needed. " +
                    "Keep Rehearsal Mode on while reviewing the setup.",
                    "Tappy recovery",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            _applicationLifecycle = new ApplicationLifecycleSignalSource();
            var runtime = new DeviceAwareControllerRuntime(
                applicationLifecycleSource: _applicationLifecycle);
            var viewModel = new MainViewModel(runtime, action =>
                Dispatcher.BeginInvoke(action, DispatcherPriority.Render), ScheduleUiAfter);
            var placement = new WindowPlacementStore(ProductIdentity.LocalDataRoot);
            _mainWindow = new MainWindow(viewModel, placement, _sessionMarker);
            MainWindow = _mainWindow;
            _mainWindow.Show();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"Tappy could not start safely. Nothing was armed.\n\n{exception.Message}",
                "Tappy — Needs attention",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _applicationLifecycle?.Report(WindowsLifecycleSignal.ShutdownRequested);
        _mainWindow?.PrepareForSystemShutdown();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _applicationLifecycle?.Report(WindowsLifecycleSignal.Shutdown);
        _applicationLifecycle = null;
        _singleInstance?.Dispose();
        _singleInstance = null;
        base.OnExit(e);
    }

    private static async Task ShowSplashAsync()
    {
        var dismissed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var splash = new SplashWindow();
        splash.Dismissed += (_, _) => dismissed.TrySetResult();
        splash.Closed += (_, _) => dismissed.TrySetResult();
        splash.Show();
        await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(10)), dismissed.Task).ConfigureAwait(true);
        if (splash.IsVisible)
        {
            splash.Close();
        }
    }

    private void ScheduleUiAfter(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = delay <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : delay
        };
        EventHandler? tick = null;
        tick = (_, _) =>
        {
            timer.Stop();
            timer.Tick -= tick;
            action();
        };
        timer.Tick += tick;
        timer.Start();
    }
}
