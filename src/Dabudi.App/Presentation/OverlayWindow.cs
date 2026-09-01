using System.Windows.Interop;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ShapePath = System.Windows.Shapes.Path;

namespace Dabudi.Presentation;

public enum OverlayKind { Effects, Stopwatch, Crosshair, Performance }

public sealed class OverlayWindow : Window
{
    private readonly OverlayKind _kind;
    private readonly TextBlock _timerValue = Text("0:00.00");
    private readonly TextBlock _dsValue = Text("");
    private readonly TextBlock _endValue = Text("");
    private readonly ShapePath _dsMask = new() { Fill = Theme.Brush("#AA000000") };
    private readonly ShapePath _endMask = new() { Fill = Theme.Brush("#AA000000") };
    private readonly StackPanel? _dsPanel;
    private readonly StackPanel? _endPanel;
    private readonly TextBlock _cpuValue = Text("—");
    private readonly TextBlock _gpuValue = Text("—");
    private readonly TextBlock _ramValue = Text("—");
    private readonly Ellipse _crosshair = new() { StrokeThickness = 2 };
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
        switch (kind)
        {
            case OverlayKind.Effects:
                Width = 300;
                Height = 150;
                _dsPanel = EffectPanel("Decisive Strike", "decisive-strike.png", Brushes.Purple, _dsValue, _dsMask);
                _endPanel = EffectPanel("Endurance", "endurance.png", Brushes.Yellow, _endValue, _endMask);
                _endPanel.Margin = new(12, 0, 0, 0);
                var effects = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 8, 0, 0) };
                effects.Children.Add(_dsPanel);
                effects.Children.Add(_endPanel);
                var effectBody = new StackPanel();
                effectBody.Children.Add(new TextBlock { Text = "Активные эффекты", Foreground = Brushes.White, FontWeight = FontWeights.Bold });
                effectBody.Children.Add(effects);
                Content = new Border { Padding = new(10), Background = Brushes.Transparent, Child = effectBody };
                break;
            case OverlayKind.Stopwatch:
                SizeToContent = SizeToContent.WidthAndHeight;
                _timerValue.FontFamily = new("Consolas");
                _timerValue.FontSize = 52;
                _timerValue.FontWeight = FontWeights.Bold;
                _timerValue.Margin = new(12, 4, 12, 4);
                _timerValue.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                _timerValue.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 0, Opacity = .85 };
                Content = _timerValue;
                break;
            case OverlayKind.Crosshair:
                Content = _crosshair;
                break;
            case OverlayKind.Performance:
                Width = 244;
                Height = 126;
                Content = PerformanceGrid();
                break;
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

    private static TextBlock Text(string value)
    {
        var text = new TextBlock { Text = value, FontSize = 12, TextWrapping = TextWrapping.NoWrap };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        return text;
    }

    private Grid PerformanceGrid()
    {
        var grid = new Grid { Margin = new(4), Background = Brushes.Transparent,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 5, ShadowDepth = 1, Opacity = .9 } };
        for (var index = 0; index < 3; index++) grid.RowDefinitions.Add(new());
        grid.ColumnDefinitions.Add(new() { Width = new(66) });
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var index = 0; index < 2; index++)
        {
            var divider = new Border { BorderThickness = new(0, 0, 0, 1) };
            divider.SetResourceReference(Border.BorderBrushProperty, "DividerBrush");
            Grid.SetRow(divider, index);
            Grid.SetColumnSpan(divider, 2);
            grid.Children.Add(divider);
        }
        var vertical = new Border { BorderThickness = new(0, 0, 1, 0) };
        vertical.SetResourceReference(Border.BorderBrushProperty, "DividerBrush");
        Grid.SetRowSpan(vertical, 3);
        grid.Children.Add(vertical);
        var values = new[] { ("CPU", _cpuValue), ("GPU", _gpuValue), ("RAM", _ramValue) };
        for (var row = 0; row < values.Length; row++)
        {
            var (label, value) = values[row];
            var name = Text(label);
            name.Margin = new(9, 0, 9, 0);
            name.VerticalAlignment = VerticalAlignment.Center;
            name.FontWeight = FontWeights.SemiBold;
            name.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            Grid.SetRow(name, row);
            grid.Children.Add(name);
            value.Margin = new(12, 0, 4, 0);
            value.VerticalAlignment = VerticalAlignment.Center;
            value.TextAlignment = TextAlignment.Right;
            value.FontWeight = FontWeights.SemiBold;
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);
        }
        return grid;
    }

    private static StackPanel EffectPanel(string name, string asset, Brush background, TextBlock value, ShapePath mask)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 0, 0, 5) });
        var icon = new Grid { Width = 64, Height = 64 };
        icon.Children.Add(new Ellipse { Fill = background, Opacity = .3 });
        icon.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/" + asset)),
            Width = 64, Height = 64, Stretch = Stretch.UniformToFill, Clip = new EllipseGeometry(new Point(32, 32), 32, 32) });
        icon.Children.Add(mask);
        icon.Children.Add(new Ellipse { Stroke = Brushes.White, StrokeThickness = 1 });
        panel.Children.Add(icon);
        value.Foreground = Brushes.White;
        value.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(value);
        return panel;
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
            Width = Height = settings.CrosshairSize;
            _crosshair.Stroke = Theme.Brush(settings.CrosshairColor);
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
            var (anchor, x, y) = _kind switch
            {
                OverlayKind.Crosshair => (OverlayAnchor.Center, 0d, 0d),
                OverlayKind.Stopwatch => (OverlayAnchor.TopLeft, 16d, 16d),
                OverlayKind.Effects => (OverlayAnchor.TopRight, 10d, 100d),
                _ => (OverlayAnchor.TopRight, 18d, 18d)
            };
            WindowsDesktop.Position(new WindowInteropHelper(this).Handle, _device, anchor, x, y);
        }));
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        SchedulePosition();
    }

    public void Render(EffectsSnapshot state)
    {
        if (_dsPanel == null || _endPanel == null) return;
        _dsPanel.Visibility = state.DecisiveStrike.Enabled ? Visibility.Visible : Visibility.Collapsed;
        _endPanel.Visibility = state.Endurance.Enabled ? Visibility.Visible : Visibility.Collapsed;
        _endPanel.Margin = state.DecisiveStrike.Enabled ? new(12, 0, 0, 0) : new(0);
        _dsValue.Text = state.DecisiveStrike.RemainingSeconds > 0 ? $"{state.DecisiveStrike.RemainingSeconds:0.0} с" : "Завершён";
        _endValue.Text = state.Endurance.RemainingSeconds > 0 ? $"{state.Endurance.RemainingSeconds:0.0} с" : "Завершён";
        _dsMask.Data = ElapsedSector(state.DecisiveStrike.Fraction);
        _endMask.Data = ElapsedSector(state.Endurance.Fraction);
    }

    private static Geometry ElapsedSector(double remaining)
    {
        var elapsed = 1 - Math.Clamp(remaining, 0, 1);
        if (elapsed <= 0) return Geometry.Empty;
        if (elapsed >= 1) return new EllipseGeometry(new Point(32, 32), 32, 32);
        var angle = -Math.PI / 2 + elapsed * Math.PI * 2;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new(32, 32), isFilled: true, isClosed: true);
            context.LineTo(new(32, 0), isStroked: false, isSmoothJoin: false);
            context.ArcTo(new(32 + 32 * Math.Cos(angle), 32 + 32 * Math.Sin(angle)), new(32, 32), 0,
                elapsed > .5, SweepDirection.Clockwise, isStroked: false, isSmoothJoin: false);
        }
        geometry.Freeze();
        return geometry;
    }

    public void Render(ElapsedTimer timer) => _timerValue.Text = FormatTime(timer.Elapsed);

    public void Render(PerformanceSnapshot snapshot)
    {
        static string Value(double? n, string unit) => n.HasValue ? $"{n:0}{unit}" : "—";
        _cpuValue.Text = Value(snapshot.CpuPercent, "%") + "  ·  " + Value(snapshot.CpuTemperature, " °C");
        _gpuValue.Text = Value(snapshot.GpuPercent, "%") + "  ·  " + Value(snapshot.GpuTemperature, " °C");
        _ramValue.Text = snapshot.TotalMemoryGiB > 0 ? $"{snapshot.UsedMemoryGiB:0.0} / {snapshot.TotalMemoryGiB:0.0} ГиБ" : "—";
    }

    public static string FormatTime(TimeSpan time) => $"{(int)time.TotalMinutes}:{time.Seconds:00}.{time.Milliseconds / 10:00}";
}
