using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Dabudi.Presentation;

public enum OverlayKind { Effects, Stopwatch, Crosshair, Performance }

public sealed class OverlayWindow : Window
{
    private readonly OverlayKind _kind;
    private readonly TextBlock _primary = Text("", 24);
    private readonly TextBlock _secondary = Text("", 13);
    private readonly TextBlock _dsValue = Text("", 23);
    private readonly TextBlock _endValue = Text("", 23);
    private readonly ProgressBar _dsProgress = new() { Maximum = 1, Height = 3, Margin = new(0, 7, 0, 0) };
    private readonly ProgressBar _endProgress = new() { Maximum = 1, Height = 3, Margin = new(0, 7, 0, 0) };
    private readonly StackPanel _dsPanel;
    private readonly StackPanel _endPanel;
    private readonly TextBlock _cpuValue = Text("—", 14);
    private readonly TextBlock _gpuValue = Text("—", 14);
    private readonly TextBlock _ramValue = Text("—", 14);
    private readonly Canvas _crosshair = new() { Width = 80, Height = 80 };
    private AppSettings _settings;
    private string _device;

    public OverlayWindow(OverlayKind kind, AppSettings settings)
    {
        _kind = kind;
        _settings = settings;
        _device = string.IsNullOrEmpty(settings.MonitorDevice) ? WindowsDesktop.ForegroundDisplay() : settings.MonitorDevice;
        Title = "dabudi — " + kind;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Width = kind == OverlayKind.Crosshair ? 80 : 280;
        SizeToContent = SizeToContent.Height;
        _dsPanel = EffectRow("Decisive Strike", "decisive-strike.png", _dsValue, _dsProgress);
        _endPanel = EffectRow("Endurance", "endurance.png", _endValue, _endProgress);
        var body = new StackPanel();
        switch (kind)
        {
            case OverlayKind.Effects:
                body.Children.Add(_dsPanel);
                body.Children.Add(_endPanel);
                break;
            case OverlayKind.Stopwatch:
                body.Children.Add(_primary);
                body.Children.Add(_secondary);
                break;
            case OverlayKind.Performance:
                body.Children.Add(Metric("CPU", _cpuValue));
                body.Children.Add(Metric("GPU", _gpuValue));
                body.Children.Add(Metric("RAM", _ramValue));
                break;
        }
        if (kind == OverlayKind.Crosshair) Content = _crosshair;
        else
        {
            var frame = new Border { CornerRadius = new(9), BorderThickness = new(1), Padding = new(14), Child = body };
            frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            frame.Background = new SolidColorBrush(Color.FromArgb(232, 28, 31, 31));
            Content = frame;
        }
        SourceInitialized += (_, _) => ApplyNativeStyles();
        Loaded += (_, _) => SchedulePosition();
        MouseLeftButtonDown += (_, e) =>
        {
            if (_settings.AllowOverlayDragging && _kind != OverlayKind.Crosshair && e.LeftButton == MouseButtonState.Pressed)
            {
                try { DragMove(); }
                catch (InvalidOperationException) { } // The button can be released before the native drag begins.
            }
        };
        Configure(settings);
    }

    private static TextBlock Text(string value, double size)
    {
        var text = new TextBlock { Text = value, FontSize = size, FontFamily = new("Segoe UI"), TextWrapping = TextWrapping.NoWrap };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        return text;
    }

    private static Grid Metric(string label, TextBlock value)
    {
        var row = new Grid { Margin = new(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new() { Width = new(42) });
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var name = Text(label, 12);
        name.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        row.Children.Add(name);
        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        return row;
    }

    private static StackPanel EffectRow(string name, string asset, TextBlock value, ProgressBar progress)
    {
        var row = new StackPanel { Margin = new(0, 4, 0, 8) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new() { Width = new(50) });
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/" + asset)),
            Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Left });
        var detail = new StackPanel();
        detail.Children.Add(Text(name, 12));
        detail.Children.Add(value);
        Grid.SetColumn(detail, 1);
        grid.Children.Add(detail);
        row.Children.Add(grid);
        progress.SetResourceReference(ProgressBar.ForegroundProperty, "AccentBrush");
        progress.SetResourceReference(ProgressBar.BackgroundProperty, "BorderBrush");
        row.Children.Add(progress);
        return row;
    }

    public void Configure(AppSettings settings)
    {
        var monitorChanged = settings.MonitorDevice != _settings.MonitorDevice;
        _settings = settings;
        if (!string.IsNullOrEmpty(settings.MonitorDevice)) _device = settings.MonitorDevice;
        else if (monitorChanged) _device = WindowsDesktop.ForegroundDisplay();
        ApplyNativeStyles();
        if (_kind == OverlayKind.Crosshair)
        {
            _crosshair.Children.Clear();
            var half = settings.CrosshairSize / 2d;
            var color = Theme.Brush(settings.CrosshairColor);
            foreach (var (x1, y1, x2, y2) in new[]
            {
                (40 - half, 40d, 37d, 40d), (43d, 40d, 40 + half, 40d),
                (40d, 40 - half, 40d, 37d), (40d, 43d, 40d, 40 + half)
            })
            {
                _crosshair.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = Brushes.Black, StrokeThickness = 4 });
                _crosshair.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = color, StrokeThickness = 2 });
            }
        }
        if (IsVisible) SchedulePosition();
    }

    private void ApplyNativeStyles()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != 0) WindowsDesktop.SetOverlayStyles(handle, _settings.AllowOverlayDragging && _kind != OverlayKind.Crosshair);
    }

    public void SchedulePosition()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (!IsVisible) return;
            var anchor = _kind switch
            {
                OverlayKind.Crosshair => OverlayAnchor.Center,
                OverlayKind.Stopwatch => OverlayAnchor.TopLeft,
                OverlayKind.Effects => OverlayAnchor.RightCenter,
                _ => OverlayAnchor.TopRight
            };
            WindowsDesktop.Position(new WindowInteropHelper(this).Handle, _device, anchor);
        }));
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        SchedulePosition();
    }

    public void Render(EffectsSnapshot state)
    {
        _dsPanel.Visibility = state.DecisiveStrike.Enabled ? Visibility.Visible : Visibility.Collapsed;
        _endPanel.Visibility = state.Endurance.Enabled ? Visibility.Visible : Visibility.Collapsed;
        _dsValue.Text = state.DecisiveStrike.RemainingSeconds > 0 ? $"{state.DecisiveStrike.RemainingSeconds:0.0} с" : "Завершён";
        _endValue.Text = state.Endurance.RemainingSeconds > 0 ? $"{state.Endurance.RemainingSeconds:0.0} с" : "Завершён";
        _dsProgress.Value = state.DecisiveStrike.Fraction;
        _endProgress.Value = state.Endurance.Fraction;
    }

    public void Render(ElapsedTimer timer)
    {
        _primary.Text = FormatTime(timer.Elapsed);
        _primary.SetResourceReference(TextBlock.ForegroundProperty, timer.State == StopwatchState.Paused ? "MutedBrush" : "AccentBrush");
        _secondary.Text = timer.State == StopwatchState.Paused ? "Пауза" : "Секундомер";
    }

    public void Render(PerformanceSnapshot snapshot)
    {
        static string Value(double? n, string unit) => n.HasValue ? $"{n:0}{unit}" : "—";
        _cpuValue.Text = Value(snapshot.CpuPercent, "%") + "  ·  " + Value(snapshot.CpuTemperature, " °C");
        _gpuValue.Text = Value(snapshot.GpuPercent, "%") + "  ·  " + Value(snapshot.GpuTemperature, " °C");
        _ramValue.Text = snapshot.TotalMemoryGiB > 0 ? $"{snapshot.UsedMemoryGiB:0.0} / {snapshot.TotalMemoryGiB:0.0} ГиБ" : "—";
    }

    public static string FormatTime(TimeSpan time) => $"{(int)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds / 10:00}";
}
