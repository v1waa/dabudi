using System.Windows.Data;
using System.Windows.Interop;
using Microsoft.Win32;

namespace Dabudi.Presentation;

public partial class MainWindow : Window
{
    private readonly AppController _controller;
    private readonly MainViewModel _viewModel;
    private HwndSource? _source;
    private HotkeyRow? _capturingHotkey;
    private bool _capturingInput;
    private Button? _captureButton;

    public MainWindow(AppController controller)
    {
        _controller = controller;
        _viewModel = new(controller);
        InitializeComponent();
        DataContext = _viewModel;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            WindowsDesktop.SetDarkTitleBar(handle);
            _source = HwndSource.FromHwnd(handle);
            _source?.AddHook(WndProc);
            controller.Attach(handle);
        };
        Loaded += (_, _) =>
        {
            Width = Math.Min(Width, Math.Max(MinWidth, SystemParameters.WorkArea.Width - 40));
            Height = Math.Min(Height, Math.Max(MinHeight, SystemParameters.WorkArea.Height - 40));
        };
        PreviewKeyDown += CaptureKey;
        PreviewMouseDown += CaptureMouse;
        Deactivated += (_, _) => CancelCapture();
        SystemEvents.DisplaySettingsChanged += DisplaysChanged;
    }

    public void Restore()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private nint WndProc(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312)
        {
            _controller.OnHotkey(wParam, lParam);
            handled = true;
        }
        return 0;
    }

    private void CaptureHotkey_Click(object sender, RoutedEventArgs e)
    {
        CancelCapture();
        if (sender is not Button { DataContext: HotkeyRow row } button) return;
        _capturingHotkey = row;
        BeginCapture(button, "Нажмите сочетание. Esc — отмена.");
    }

    private void CaptureInput_Click(object sender, RoutedEventArgs e)
    {
        CancelCapture();
        _capturingInput = true;
        BeginCapture((Button)sender, "Нажмите клавишу или кнопку мыши для автокликера. Esc — отмена.");
    }

    private void BeginCapture(Button button, string hint)
    {
        _captureButton = button;
        _controller.SuspendHotkeys();
        button.SetCurrentValue(ContentControl.ContentProperty, "Нажмите…");
        button.Focus();
        _controller.Report(hint);
    }

    private void CaptureKey(object sender, KeyEventArgs e)
    {
        if (!_capturingInput && _capturingHotkey == null) return;
        e.Handled = true;
        if (e.IsRepeat) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            CancelCapture();
            _controller.Report("Запись отменена");
            return;
        }
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0 || Shortcut.IsModifier(virtualKey)) return;
        var row = _capturingHotkey;
        var input = _capturingInput;
        var modifiers = (ShortcutModifiers)Keyboard.Modifiers;
        CancelCapture();
        if (input) _controller.ChangeClickTarget(new(InputKind.Keyboard, Core.MouseButton.Left, virtualKey));
        else if (row != null) _controller.ChangeShortcut(row.Action, new(virtualKey, modifiers));
    }

    private void CaptureMouse(object sender, MouseButtonEventArgs e)
    {
        if (!_capturingInput) return;
        var button = e.ChangedButton switch
        {
            System.Windows.Input.MouseButton.Left => Core.MouseButton.Left,
            System.Windows.Input.MouseButton.Right => Core.MouseButton.Right,
            System.Windows.Input.MouseButton.Middle => Core.MouseButton.Middle,
            System.Windows.Input.MouseButton.XButton1 => Core.MouseButton.X1,
            System.Windows.Input.MouseButton.XButton2 => Core.MouseButton.X2,
            _ => (Core.MouseButton?)null
        };
        if (button == null) return;
        e.Handled = true;
        CancelCapture();
        _controller.ChangeClickTarget(new(InputKind.Mouse, button.Value));
    }

    private void CancelCapture()
    {
        if (!_capturingInput && _capturingHotkey == null) return;
        _capturingInput = false;
        _capturingHotkey = null;
        if (_captureButton != null)
            BindingOperations.GetBindingExpression(_captureButton, ContentControl.ContentProperty)?.UpdateTarget();
        _captureButton = null;
        _controller.ResumeHotkeys();
    }

    private void HideToTray_Click(object sender, RoutedEventArgs e) { CancelCapture(); Hide(); }
    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender)) CancelCapture();
    }
    private void DisplaysChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(new Action(() => { _viewModel.RefreshDisplays(); _controller.RefreshDisplays(); }));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_controller.IsExiting && !(Application.Current is App { IsClosing: true }))
        {
            e.Cancel = true;
            CancelCapture();
            Hide();
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
        _source?.RemoveHook(WndProc);
        base.OnClosed(e);
    }

    internal void SelectTabForSmoke(int index) => NavigationTabs.SelectedIndex = index;
}
