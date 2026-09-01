using System.Text.Json;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Dabudi.Presentation;

namespace Dabudi;

public partial class App : Application
{
    private SingleInstance? _instance;
    private AppController? _controller;
    private TrayIcon? _tray;
    private AppLog? _log;
    private bool _smokeTest;
    private string? _smokeOutput;
    public bool IsClosing { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _smokeTest = e.Args.Contains("--smoke-test");
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _smokeOutput = _smokeTest ? Environment.GetEnvironmentVariable("DABUDI_SMOKE_OUTPUT")
            ?? Path.Combine(Path.GetTempPath(), "dabudi-smoke-" + Guid.NewGuid().ToString("N")) : null;
        var settingsDirectory = _smokeOutput == null ? Path.Combine(roaming, "dabudi") : Path.Combine(_smokeOutput, "settings");
        _log = new(_smokeOutput == null ? Path.Combine(local, "dabudi", "logs") : Path.Combine(_smokeOutput, "logs"));
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            FailStartup(args.Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) => { _log.Write("Background task error", args.Exception); args.SetObserved(); };
        try
        {
            if (!_smokeTest)
            {
                _instance = new();
                if (!_instance.IsPrimary) { Shutdown(0); return; }
            }
            var store = new SettingsStore(settingsDirectory, _log, _smokeTest ? null : Path.Combine(roaming, "dbdOverlay"));
            _controller = new(store, store.Load(KeyNames.ParseLegacy), _log, _smokeTest);
            _controller.ExitRequested += () => { IsClosing = true; Shutdown(0); };
            BindingErrorListener? bindingListener = null;
            if (_smokeTest)
            {
                bindingListener = new();
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
                PresentationTraceSources.DataBindingSource.Listeners.Add(bindingListener);
            }
            var window = new Presentation.MainWindow(_controller);
            MainWindow = window;
            _instance?.Listen(() => Dispatcher.BeginInvoke(new Action(() => { if (!IsClosing) window.Restore(); })));
            if (!_smokeTest) _tray = new(window, _controller);
            if (e.Args.Contains("--tray") && !_smokeTest) window.Loaded += (_, _) => window.Hide();
            window.Show();
            if (_smokeTest) _ = RunSmokeAsync(window, _controller, bindingListener!);
        }
        catch (Exception exception) { FailStartup(exception); }
    }

    private async Task RunSmokeAsync(Presentation.MainWindow window, AppController controller, BindingErrorListener listener)
    {
        try
        {
            Directory.CreateDirectory(_smokeOutput!);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            controller.Run(AppAction.ToggleStopwatch);
            controller.Run(AppAction.ToggleCrosshair);
            controller.Run(AppAction.TogglePerformance);
            controller.Run(AppAction.RestartEffects);
            await Task.Delay(180);
            if (controller.Overlays.Count != 4 || controller.Elapsed.Elapsed <= TimeSpan.Zero)
                throw new InvalidOperationException("Smoke check: tools did not start.");
            controller.Run(AppAction.ToggleStopwatch);
            var paused = controller.Elapsed.Elapsed;
            await Task.Delay(100);
            if (controller.Elapsed.Elapsed != paused) throw new InvalidOperationException("Smoke check: pause drift.");
            controller.Run(AppAction.ToggleStopwatch);
            await Task.Delay(80);
            if (controller.Elapsed.Elapsed <= paused) throw new InvalidOperationException("Smoke check: resume failed.");
            for (var index = 0; index < 3; index++)
            {
                window.SelectTabForSmoke(index);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                SaveScreenshot(window, Path.Combine(_smokeOutput!, $"tab-{index}.png"));
            }
            window.ScrollTabForSmoke(toEnd: true);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            SaveScreenshot(window, Path.Combine(_smokeOutput!, "interface-overlays.png"));
            foreach (var kind in Enum.GetValues<OverlayKind>())
                if (controller.Overlays.Get(kind) is { } overlay)
                    SaveScreenshot(overlay, Path.Combine(_smokeOutput!, $"overlay-{kind}.png"));
            window.SelectTabForSmoke(0);
            if (!window.SetDelayForSmoke("5")) throw new InvalidOperationException("Smoke check: delay save failed.");
            window.ToggleClickerForSmoke();
            await Task.Delay(80);
            if (!controller.IsClickerRunning || controller.ClickerRemainingDelay <= TimeSpan.Zero)
                throw new InvalidOperationException("Smoke check: delayed click did not start.");
            window.UpdateLayout();
            SaveScreenshot(window, Path.Combine(_smokeOutput!, "clicker-delay.png"));
            window.HideToTray();
            if (window.IsVisible || controller.IsExiting || !controller.IsClickerRunning || controller.Overlays.Count != 4)
                throw new InvalidOperationException("Smoke check: hiding the window stopped the application.");
            window.Restore();
            window.ToggleClickerForSmoke();
            if (controller.IsClickerRunning) throw new InvalidOperationException("Smoke check: repeat command did not cancel the delay.");
            if (!window.SetDelayForSmoke("0,1") || controller.Settings.ClickDelaySeconds != .1)
                throw new InvalidOperationException("Smoke check: fractional delay did not save.");
            window.ToggleClickerForSmoke();
            await Task.Delay(400);
            if (controller.IsClickerRunning || controller.Status != "Отложенное нажатие выполнено")
                throw new InvalidOperationException("Smoke check: delayed click did not complete and refresh the UI.");
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            SaveScreenshot(window, Path.Combine(_smokeOutput!, "general-minimum-size.png"));
            window.ScrollTabForSmoke(toEnd: true);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            SaveScreenshot(window, Path.Combine(_smokeOutput!, "general-minimum-bottom.png"));
            controller.Run(AppAction.StopAll);
            if (controller.Overlays.Count != 0 || controller.Elapsed.State != StopwatchState.Idle || controller.Effects.IsActive || controller.IsClickerRunning)
                throw new InvalidOperationException("Smoke check: Stop All left a tool running.");
            if (!controller.Save(controller.Settings with { EnduranceSeconds = 0 }))
                throw new InvalidOperationException("Smoke check: settings save failed.");
            controller.Run(AppAction.RestartEffects);
            if (controller.Effects.Snapshot().Endurance.Enabled)
                throw new InvalidOperationException("Smoke check: zero-duration effect was enabled.");
            controller.Run(AppAction.StopAll);
            if (!window.SetDelayForSmoke("5")) throw new InvalidOperationException("Smoke check: closing setup failed.");
            window.ToggleClickerForSmoke();
            controller.Run(AppAction.ToggleStopwatch);
            window.Close();
            if (!controller.IsExiting || !IsClosing || controller.IsClickerRunning || controller.Overlays.Count != 0)
                throw new InvalidOperationException("Smoke check: closing the window did not exit and stop all tools.");
            if (listener.Errors.Count != 0) throw new InvalidOperationException("Binding errors: " + string.Join("\n", listener.Errors));
            File.WriteAllText(Path.Combine(_smokeOutput!, "smoke-result.json"), JsonSerializer.Serialize(new
            {
                passed = true, overlayCountAfterStop = controller.Overlays.Count,
                bindingErrors = listener.Errors.Count, settingsSave = true,
                delayedClick = true, hideKeepsRunning = true, closeExits = true
            }));
        }
        catch (Exception exception) { FailStartup(exception); }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); }
    }

    private static void SaveScreenshot(Window window, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private void FailStartup(Exception exception)
    {
        _log?.Write("Application failure", exception);
        if (_smokeTest && _smokeOutput != null)
        {
            Directory.CreateDirectory(_smokeOutput);
            File.WriteAllText(Path.Combine(_smokeOutput, "smoke-failure.txt"), exception.ToString());
        }
        else MessageBox.Show("Не удалось продолжить работу dabudi.\n\n" + exception.Message
            + "\n\nПодробности: %LOCALAPPDATA%\\dabudi\\logs", "dabudi", MessageBoxButton.OK, MessageBoxImage.Error);
        IsClosing = true;
        Shutdown(1);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsClosing = true;
        _controller?.Dispose();
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private sealed class BindingErrorListener : TraceListener
    {
        public List<string> Errors { get; } = new();
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Errors.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
