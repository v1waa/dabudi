using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Resources;
using dbdOverlay.Models;
using dbdOverlay.Services;

namespace dbdOverlay;

public partial class MainWindow : Window, IComponentConnector
{
	private enum CaptureMode
	{
		None,
		Start,
		Close,
		Exit,
		Crosshair,
		Timer,
		Performance,
		Clicker
	}

	private sealed class HotkeyBinding
	{
		public int Id { get; }

		public ModifierKeys Modifiers { get; set; }

		public Key Key { get; set; }

		public HotkeyBinding(int id, ModifierKeys modifiers, Key key)
		{
			Id = id;
			Modifiers = modifiers;
			Key = key;
		}
	}

	private const int WmHotkey = 786;

	private const uint ModNoRepeat = 16384u;

	private const int StartHotkeyId = 9000;

	private const int ExitHotkeyId = 9002;

	private const int CrosshairHotkeyId = 9003;

	private const int TimerHotkeyId = 9004;

	private const int PerformanceHotkeyId = 9005;

	private const int ClickerHotkeyId = 9010;

	private HwndSource? _source;

	private NotifyIcon? _notifyIcon;

	private CrosshairWindow? _crosshairWindow;

	private OverlayWindow? _overlayWindow;

	private TimerWindow? _timerWindow;

	private PerformanceWindow? _performanceWindow;

	private readonly HotkeyBinding _startHotkey = new HotkeyBinding(9000, ModifierKeys.None, Key.F9);

	private readonly HotkeyBinding _closeHotkey = new HotkeyBinding(0, ModifierKeys.None, Key.Escape);

	private readonly HotkeyBinding _exitHotkey = new HotkeyBinding(9002, ModifierKeys.None, Key.None);

	private readonly HotkeyBinding _crosshairHotkey = new HotkeyBinding(9003, ModifierKeys.None, Key.F8);

	private readonly HotkeyBinding _timerHotkey = new HotkeyBinding(9004, ModifierKeys.None, Key.F7);

	private readonly HotkeyBinding _performanceHotkey = new HotkeyBinding(9005, ModifierKeys.None, Key.F10);

	private readonly HotkeyBinding _clickerHotkey = new HotkeyBinding(9010, ModifierKeys.None, Key.F6);

	private ClickerBinding _clickerBinding = ClickerBinding.ForMouse(MouseButtonKind.Left);

	private readonly AutoClickerService _autoClicker = new AutoClickerService();

	private CaptureMode _captureMode;

	private bool _capturingClickerInput;

	private bool _updatingUi = true;

	private bool _isExiting;

	public MainWindow()
	{
		InitializeComponent();
		base.SourceInitialized += OnSourceInitialized;
		base.Closing += OnWindowClosing;
		base.Closed += OnWindowClosed;
		base.Loaded += OnLoaded;
		base.PreviewKeyDown += OnPreviewKeyDown;
		base.PreviewMouseDown += OnPreviewMouseDown;
		LoadSettings();
		_updatingUi = false;
		UpdateHotkeyButtons();
		UpdateClickerUi();
		UpdatePerformanceUi();
	}

	private void NavGeneral_Checked(object sender, RoutedEventArgs e)
	{
		if (mainTabs != null)
		{
			mainTabs.SelectedIndex = 0;
			SetStatus("Раздел: Общие");
		}
	}

	private void NavDbd_Checked(object sender, RoutedEventArgs e)
	{
		if (mainTabs != null)
		{
			mainTabs.SelectedIndex = 1;
			SetStatus("Раздел: Dead by Daylight");
		}
	}

	private void NavInterface_Checked(object sender, RoutedEventArgs e)
	{
		if (mainTabs != null)
		{
			mainTabs.SelectedIndex = 2;
			SetStatus("Раздел: Интерфейс");
		}
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		ApplyGridColor(txtGridColor.Text);
		ApplyBorderColor(txtBorderColor.Text);
		ApplyAccentColor(txtAccentColor.Text);
		ApplyTextColor(txtTextColor.Text);
		ShowTrayIcon();
		SetStatus("Готово");
	}

	private void OnSourceInitialized(object? sender, EventArgs e)
	{
		nint handle = new WindowInteropHelper(this).Handle;
		_source = HwndSource.FromHwnd(handle);
		_source?.AddHook(WndProc);
		RegisterGlobalHotkeys();
	}

	private void OnWindowClosing(object? sender, CancelEventArgs e)
	{
		if (!_isExiting)
		{
			e.Cancel = true;
			CancelHotkeyCapture();
			CancelClickerInputCapture();
			SaveSettings();
			Hide();
			ShowTrayIcon();
		}
	}

	private void OnWindowClosed(object? sender, EventArgs e)
	{
		SaveSettings();
		_autoClicker.Dispose();
		ClosePerformance();
		UnregisterGlobalHotkeys();
		if (_source != null)
		{
			try
			{
				_source.RemoveHook(WndProc);
			}
			catch
			{
			}
			_source = null;
		}
		DisposeTrayIcon();
	}

	private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
	{
		if (msg != 786)
		{
			return IntPtr.Zero;
		}
		handled = true;
		switch (((IntPtr)wParam).ToInt32())
		{
		case 9000:
			StartOverlayFromDurations();
			break;
		case 9002:
			ExitApplication();
			break;
		case 9003:
			ToggleCrosshair();
			break;
		case 9004:
			ToggleTimer();
			break;
		case 9005:
			TogglePerformance();
			break;
		case 9010:
			ToggleClicker();
			break;
		default:
			handled = false;
			break;
		}
		return IntPtr.Zero;
	}

	private void BtnRecordStartKey_Click(object sender, RoutedEventArgs e)
	{
		BeginHotkeyCapture(CaptureMode.Start, btnRecordKey);
	}

	private void BtnRecordCloseKey_Click(object sender, RoutedEventArgs e)
	{
		BeginHotkeyCapture(CaptureMode.Close, btnRecordCloseKey);
	}

	private void BtnRecordExitKey_Click(object sender, RoutedEventArgs e)
	{
		BeginHotkeyCapture(CaptureMode.Exit, btnRecordExitKey);
	}

	private void BtnRecordCrosshairKey_Click(object sender, RoutedEventArgs e)
	{
		BeginHotkeyCapture(CaptureMode.Crosshair, btnRecordCrosshairKey);
	}

	private void BtnRecordTimerKey_Click(object sender, RoutedEventArgs e)
	{
		BeginHotkeyCapture(CaptureMode.Timer, btnRecordTimerKey);
	}

	private void BtnRecordPerformanceKey_Click(object sender, RoutedEventArgs e)
	{
		BeginHotkeyCapture(CaptureMode.Performance, btnRecordPerformanceKey);
	}

	private void BtnRecordClickerKey_Click(object sender, RoutedEventArgs e)
	{
		BeginHotkeyCapture(CaptureMode.Clicker, btnRecordClickerKey);
	}

	private void BtnRecordClickerInput_Click(object sender, RoutedEventArgs e)
	{
		StopClicker();
		CancelHotkeyCapture();
		_capturingClickerInput = true;
		UnregisterGlobalHotkeys();
		btnRecordClickerInput.Content = "Нажмите клавишу или кнопку мыши...";
		SetStatus("Ожидаю клавишу автокликера", active: true);
		Activate();
		Keyboard.Focus(btnRecordClickerInput);
	}

	private void BeginHotkeyCapture(CaptureMode mode, System.Windows.Controls.Button targetButton)
	{
		if (_capturingClickerInput)
		{
			_capturingClickerInput = false;
			UpdateClickerInputButton();
		}
		_captureMode = mode;
		UnregisterGlobalHotkeys();
		UpdateHotkeyButtons();
		targetButton.Content = "Нажмите сочетание...";
		SetStatus("Ожидаю горячую клавишу", active: true);
		Activate();
		Keyboard.Focus(targetButton);
	}

	private void CancelHotkeyCapture()
	{
		if (_captureMode != CaptureMode.None)
		{
			_captureMode = CaptureMode.None;
			UpdateHotkeyButtons();
			RegisterGlobalHotkeys();
		}
	}

	private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (_capturingClickerInput)
		{
			Key key = NormalizeKey(e);
			if (key == Key.Escape)
			{
				CancelClickerInputCapture();
				SetStatus("Запись клавиши отменена");
				e.Handled = true;
				return;
			}
			if (IsModifierKey(key))
			{
				SetStatus("Нажмите обычную клавишу, не модификатор", active: false, isError: true);
				e.Handled = true;
				return;
			}
			if (_clickerHotkey.Modifiers == ModifierKeys.None && _clickerHotkey.Key == key)
			{
				SetStatus("Эта клавиша переключает автокликер — выберите другую", active: false, isError: true);
				e.Handled = true;
				return;
			}
			int num = KeyInterop.VirtualKeyFromKey(key);
			if (num > 0)
			{
				CompleteClickerInputCapture(ClickerBinding.ForKeyboard(num));
			}
			e.Handled = true;
		}
		else if (_captureMode != CaptureMode.None)
		{
			Key key2 = NormalizeKey(e);
			ModifierKeys currentModifiers = GetCurrentModifiers();
			HotkeyBinding binding = GetBinding(_captureMode);
			if (IsModifierKey(key2))
			{
				binding.Modifiers = currentModifiers;
				UpdateHotkeyButton(_captureMode, Key.None);
				e.Handled = true;
				return;
			}
			binding.Modifiers = currentModifiers;
			binding.Key = key2;
			CaptureMode captureMode = _captureMode;
			_captureMode = CaptureMode.None;
			ApplyHotkeyChanges(captureMode);
			e.Handled = true;
		}
	}

	private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (_capturingClickerInput)
		{
			MouseButtonKind? mouseButtonKind = e.ChangedButton switch
			{
				MouseButton.Left => MouseButtonKind.Left,
				MouseButton.Right => MouseButtonKind.Right,
				MouseButton.Middle => MouseButtonKind.Middle,
				MouseButton.XButton1 => MouseButtonKind.X1,
				MouseButton.XButton2 => MouseButtonKind.X2,
				_ => null,
			};
			if (mouseButtonKind.HasValue)
			{
				CompleteClickerInputCapture(ClickerBinding.ForMouse(mouseButtonKind.Value));
				e.Handled = true;
			}
		}
	}

	private void CompleteClickerInputCapture(ClickerBinding binding)
	{
		_clickerBinding = binding;
		_capturingClickerInput = false;
		UpdateClickerInputButton();
		RegisterGlobalHotkeys();
		SaveSettings();
		SetStatus("Клавиша автокликера: " + binding.DisplayName);
	}

	private void CancelClickerInputCapture()
	{
		if (_capturingClickerInput)
		{
			_capturingClickerInput = false;
			UpdateClickerInputButton();
			RegisterGlobalHotkeys();
		}
	}

	private void ApplyHotkeyChanges(CaptureMode mode)
	{
		UpdateHotkeyButtons();
		SaveSettings();
		RegisterGlobalHotkeys();
		if (mode == CaptureMode.Close)
		{
			ApplyCloseHotkeyToOverlay();
		}
		SetStatus("Горячая клавиша сохранена");
	}

	private void RegisterGlobalHotkeys()
	{
		if (_source != null && _captureMode == CaptureMode.None && !_capturingClickerInput)
		{
			UnregisterGlobalHotkeys();
			int num = 0;
			num += ((!RegisterHotkey(_startHotkey)) ? 1 : 0);
			num += ((!RegisterHotkey(_exitHotkey, optional: true)) ? 1 : 0);
			num += ((!RegisterHotkey(_crosshairHotkey)) ? 1 : 0);
			num += ((!RegisterHotkey(_timerHotkey)) ? 1 : 0);
			num += ((!RegisterHotkey(_performanceHotkey)) ? 1 : 0);
			num += ((!RegisterHotkey(_clickerHotkey)) ? 1 : 0);
			if (num > 0 && base.IsLoaded)
			{
				SetStatus($"Не удалось назначить горячих клавиш: {num}", active: false, isError: true);
			}
		}
	}

	private void UnregisterGlobalHotkeys()
	{
		if (_source != null)
		{
			UnregisterHotkey(_startHotkey.Id);
			UnregisterHotkey(_exitHotkey.Id);
			UnregisterHotkey(_crosshairHotkey.Id);
			UnregisterHotkey(_timerHotkey.Id);
			UnregisterHotkey(_performanceHotkey.Id);
			UnregisterHotkey(_clickerHotkey.Id);
		}
	}

	private bool RegisterHotkey(HotkeyBinding binding, bool optional = false)
	{
		if (binding.Key == Key.None)
		{
			return optional;
		}
		return RegisterHotkey(binding.Id, binding.Modifiers, binding.Key);
	}

	private bool RegisterHotkey(int id, ModifierKeys modifiers, Key key)
	{
		if (_source == null || id <= 0 || key == Key.None)
		{
			return false;
		}
		try
		{
			nint handle = new WindowInteropHelper(this).Handle;
			uint virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
			return RegisterHotKey(handle, id, (uint)(modifiers | (ModifierKeys)16384), virtualKey);
		}
		catch
		{
			return false;
		}
	}

	private void UnregisterHotkey(int id)
	{
		if (_source == null || id <= 0)
		{
			return;
		}
		try
		{
			UnregisterHotKey(new WindowInteropHelper(this).Handle, id);
		}
		catch
		{
		}
	}

	private void TxtClickerCps_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (!_updatingUi && txtClickerCps != null && int.TryParse(txtClickerCps.Text, out var result) && result >= 1 && result <= 50)
		{
			ApplyClickerSettingsChange();
		}
	}

	private void TxtClickerCps_LostFocus(object sender, RoutedEventArgs e)
	{
		int clickerCps = GetClickerCps();
		_updatingUi = true;
		txtClickerCps.Text = clickerCps.ToString();
		_updatingUi = false;
		ApplyClickerSettingsChange();
	}

	private void ApplyClickerSettingsChange()
	{
		if (_autoClicker.IsRunning)
		{
			_autoClicker.Start(_clickerBinding, GetClickerCps());
		}
		UpdateClickerUi();
		SaveSettings();
	}

	private void BtnClickerToggle_Click(object sender, RoutedEventArgs e)
	{
		ToggleClicker();
	}

	private void ToggleClicker()
	{
		if (_autoClicker.IsRunning)
		{
			_autoClicker.Stop();
			UpdateClickerUi();
			SetStatus("Автокликер остановлен");
			return;
		}
		_autoClicker.Start(_clickerBinding, GetClickerCps());
		UpdateClickerUi();
		SetStatus($"Автокликер: {_clickerBinding.DisplayName} · {GetClickerCps()}/с", active: true);
	}

	private void StopClicker()
	{
		if (_autoClicker.IsRunning)
		{
			_autoClicker.Stop();
			UpdateClickerUi();
		}
	}

	private int GetClickerCps()
	{
		if (!int.TryParse(txtClickerCps?.Text, out var result))
		{
			return 10;
		}
		return Math.Clamp(result, 1, 50);
	}

	private void UpdateClickerUi()
	{
		if (txtClickerState != null)
		{
			bool isRunning = _autoClicker.IsRunning;
			UpdateClickerInputButton();
			txtClickerState.Text = (isRunning ? "Работает" : "Остановлен");
			btnClickerToggle.Content = (isRunning ? "Остановить" : "Запустить");
			if (isRunning)
			{
				txtClickerState.Foreground = GetBrush("AccentBrush", System.Windows.Media.Brushes.HotPink);
				btnClickerToggle.Background = GetBrush("DangerBrush", System.Windows.Media.Brushes.IndianRed);
				btnClickerToggle.Foreground = GetBrush("AccentTextBrush", CreateBrush(System.Windows.Media.Color.FromRgb(34, 34, 34)));
			}
			else
			{
				txtClickerState.ClearValue(TextBlock.ForegroundProperty);
				btnClickerToggle.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
				btnClickerToggle.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
			}
		}
	}

	private void UpdateClickerInputButton()
	{
		if (btnRecordClickerInput != null)
		{
			btnRecordClickerInput.Content = (_capturingClickerInput ? "Нажмите клавишу или кнопку мыши..." : _clickerBinding.DisplayName);
		}
	}

	private void BtnStartOverlay_Click(object sender, RoutedEventArgs e)
	{
		StartOverlayFromDurations();
	}

	private void BtnCloseOverlay_Click(object sender, RoutedEventArgs e)
	{
		if (_overlayWindow == null)
		{
			SetStatus("Оверлей Dead by Daylight уже закрыт");
			return;
		}
		try
		{
			_overlayWindow.Close();
			SetStatus("Оверлей Dead by Daylight закрыт");
		}
		catch
		{
			SetStatus("Не удалось закрыть оверлей", active: false, isError: true);
		}
	}

	private void StartOverlayFromDurations()
	{
		int dsSec = ParseOrDefault(txtDsDuration.Text, 60, 1);
		int endSec = ParseOrDefault(txtEndDuration.Text, 15, 0);
		try
		{
			base.Dispatcher.Invoke(delegate
			{
				ShowOrRestartOverlay(dsSec, endSec);
			});
			SetStatus("Оверлей Dead by Daylight запущен", active: true);
		}
		catch
		{
			SetStatus("Не удалось запустить оверлей", active: false, isError: true);
		}
	}

	private void ShowOrRestartOverlay(int dsSec, int endSec)
	{
		if (_overlayWindow == null || !_overlayWindow.IsVisible)
		{
			_overlayWindow = new OverlayWindow();
			_overlayWindow.Closed += delegate
			{
				_overlayWindow = null;
			};
		}
		ApplyCloseHotkeyToOverlay();
		_overlayWindow.Start(dsSec, endSec);
	}

	private void ApplyCloseHotkeyToOverlay()
	{
		_overlayWindow?.SetCloseHotkey(_closeHotkey.Key, _closeHotkey.Modifiers);
	}

	private void BtnToggleTimer_Click(object sender, RoutedEventArgs e)
	{
		ToggleTimer();
	}

	private void ToggleTimer()
	{
		try
		{
			base.Dispatcher.Invoke(ToggleTimerInternal);
		}
		catch
		{
			SetStatus("Не удалось открыть секундомер", active: false, isError: true);
		}
	}

	private void ToggleTimerInternal()
	{
		if (_timerWindow != null && _timerWindow.IsVisible)
		{
			_timerWindow.HandleHotkey();
			return;
		}
		_timerWindow = new TimerWindow();
		_timerWindow.Closed += delegate
		{
			_timerWindow = null;
		};
		_timerWindow.StartFresh();
		SetStatus("Секундомер запущен", active: true);
	}

	private void BtnPerformanceToggle_Click(object sender, RoutedEventArgs e)
	{
		TogglePerformance();
	}

	private void TogglePerformance()
	{
		try
		{
			base.Dispatcher.Invoke(delegate
			{
				if (_performanceWindow != null && _performanceWindow.IsVisible)
				{
					ClosePerformance();
				}
				else
				{
					OpenPerformance();
				}
			});
		}
		catch
		{
			SetStatus("Не удалось открыть монитор производительности", active: false, isError: true);
		}
	}

	private void OpenPerformance()
	{
		if (_performanceWindow == null || !_performanceWindow.IsVisible)
		{
			_performanceWindow = new PerformanceWindow();
			_performanceWindow.Closed += delegate
			{
				_performanceWindow = null;
				UpdatePerformanceUi();
			};
			_performanceWindow.Show();
			UpdatePerformanceUi();
			SetStatus("Монитор производительности включён", active: true);
		}
	}

	private void ClosePerformance()
	{
		if (_performanceWindow != null)
		{
			PerformanceWindow performanceWindow = _performanceWindow;
			_performanceWindow = null;
			try
			{
				performanceWindow.Close();
			}
			catch
			{
			}
		}
		UpdatePerformanceUi();
	}

	private void UpdatePerformanceUi()
	{
		if (txtPerformanceState != null && btnPerformanceToggle != null)
		{
			bool flag = _performanceWindow != null && _performanceWindow.IsVisible;
			txtPerformanceState.Text = (flag ? "Показано" : "Скрыто");
			txtPerformanceState.Foreground = (flag ? GetBrush("SuccessBrush", System.Windows.Media.Brushes.LightGreen) : GetBrush("MutedTextBrush", System.Windows.Media.Brushes.Gray));
			btnPerformanceToggle.Content = (flag ? "Скрыть" : "Показать");
		}
	}

	private void OpenCrosshair()
	{
		if (_crosshairWindow == null || !_crosshairWindow.IsVisible)
		{
			_crosshairWindow = new CrosshairWindow();
			_crosshairWindow.Closed += delegate
			{
				_crosshairWindow = null;
				chkCrosshair.IsChecked = false;
			};
			_crosshairWindow.Show();
			chkCrosshair.IsChecked = true;
			SetStatus("Прицел включён", active: true);
		}
	}

	private void CloseCrosshair()
	{
		if (_crosshairWindow != null)
		{
			CrosshairWindow crosshairWindow = _crosshairWindow;
			_crosshairWindow = null;
			try
			{
				crosshairWindow.Close();
			}
			catch
			{
			}
			chkCrosshair.IsChecked = false;
			SetStatus("Прицел выключен");
		}
	}

	private void ToggleCrosshair()
	{
		if (_crosshairWindow == null || !_crosshairWindow.IsVisible)
		{
			OpenCrosshair();
		}
		else
		{
			CloseCrosshair();
		}
	}

	private void chkCrosshair_Checked(object sender, RoutedEventArgs e)
	{
		OpenCrosshair();
	}

	private void chkCrosshair_Unchecked(object sender, RoutedEventArgs e)
	{
		CloseCrosshair();
	}

	private void BtnExitNow_Click(object sender, RoutedEventArgs e)
	{
		ExitApplication();
	}

	private void ExitApplication()
	{
		_isExiting = true;
		_captureMode = CaptureMode.None;
		_autoClicker.Stop();
		ClosePerformance();
		SaveSettings();
		DisposeTrayIcon();
		System.Windows.Application.Current.Shutdown();
	}

	private void ChkRunAtStartup_Changed(object sender, RoutedEventArgs e)
	{
		if (!_updatingUi && chkRunAtStartup != null)
		{
			bool valueOrDefault = chkRunAtStartup.IsChecked == true;
			if (!StartupService.SetEnabled(valueOrDefault))
			{
				_updatingUi = true;
				chkRunAtStartup.IsChecked = !valueOrDefault;
				_updatingUi = false;
				SaveSettings();
				SetStatus("Не удалось изменить автозапуск", active: false, isError: true);
			}
			else
			{
				SaveSettings();
				SetStatus(valueOrDefault ? "Автозапуск включён" : "Автозапуск выключен");
			}
		}
	}

	private void TxtGridColor_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (!_updatingUi)
		{
			ApplyGridColor(txtGridColor.Text);
			SaveSettings();
		}
	}

	private void TxtBorderColor_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (!_updatingUi)
		{
			ApplyBorderColor(txtBorderColor.Text);
			ApplyTextColor(txtTextColor.Text);
			SaveSettings();
		}
	}

	private void TxtAccentColor_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (!_updatingUi)
		{
			ApplyAccentColor(txtAccentColor.Text);
			ApplyBorderColor(txtBorderColor.Text);
			SaveSettings();
		}
	}

	private void TxtTextColor_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (!_updatingUi)
		{
			ApplyTextColor(txtTextColor.Text);
			SaveSettings();
		}
	}

	private void BtnResetTheme_Click(object sender, RoutedEventArgs e)
	{
		AppThemePalette theme = AppThemeService.GetTheme("SageGraphite");
		_updatingUi = true;
		txtGridColor.Text = theme.AppBackground;
		txtBorderColor.Text = theme.PanelBackground;
		txtAccentColor.Text = theme.Accent;
		txtTextColor.Text = theme.TextPrimary;
		_updatingUi = false;
		AppThemeService.Apply("SageGraphite");
		ApplyGridColor(theme.AppBackground);
		ApplyBorderColor(theme.PanelBackground);
		ApplyAccentColor(theme.Accent);
		ApplyTextColor(theme.TextPrimary);
		SaveSettings();
		SetStatus("Тема «Шалфейный графит» восстановлена");
	}

	private void ApplyGridColor(string hex)
	{
		if (TryParseHex(hex, out var color))
		{
			SolidColorBrush solidColorBrush = new SolidColorBrush(color);
			System.Windows.Application.Current.Resources["AppBackgroundBrush"] = solidColorBrush;
			rootGrid.Background = solidColorBrush;
			rectGridPreview.Fill = solidColorBrush;
		}
		else
		{
			rectGridPreview.Fill = System.Windows.Media.Brushes.Transparent;
		}
	}

	private void ApplyBorderColor(string hex)
	{
		if (TryParseHex(hex, out var color))
		{
			System.Windows.Media.Color color3;
			System.Windows.Media.Color color2 = ((txtAccentColor != null && TryParseHex(txtAccentColor.Text, out color3)) ? color3 : System.Windows.Media.Color.FromRgb(194, 216, 196));
			ResourceDictionary resources = System.Windows.Application.Current.Resources;
			SolidColorBrush solidColorBrush = (SolidColorBrush)(resources["PanelBackgroundBrush"] = CreateBrush(color));
			AppThemePalette theme = AppThemeService.GetTheme("SageGraphite");
			if (TryParseHex(theme.PanelBackground, out var color4) && TryParseHex(theme.Accent, out var color5) && color == color4 && color2 == color5)
			{
				resources["SurfaceBrush"] = CreateBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.Surface));
				resources["SurfaceAltBrush"] = CreateBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.SurfaceAlt));
				resources["ControlBackgroundBrush"] = CreateBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.ControlBackground));
				resources["ControlHoverBrush"] = CreateBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.ControlHover));
				resources["ControlPressedBrush"] = CreateBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.ControlPressed));
				resources["BorderBrush"] = CreateBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.Border));
				resources["DividerBrush"] = CreateBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.Divider));
			}
			else if (GetRelativeLuminance(color) >= 0.55)
			{
				resources["SurfaceBrush"] = CreateBrush(BlendColor(color, Colors.White, 0.66));
				resources["SurfaceAltBrush"] = CreateBrush(BlendColor(color, Colors.Black, 0.05));
				resources["ControlBackgroundBrush"] = CreateBrush(BlendColor(color, Colors.White, 0.82));
				resources["ControlHoverBrush"] = CreateBrush(BlendColor(color, Colors.White, 0.36));
				resources["ControlPressedBrush"] = CreateBrush(BlendColor(color, color2, 0.1));
				resources["BorderBrush"] = CreateBrush(BlendColor(color, Colors.Black, 0.28));
				resources["DividerBrush"] = CreateBrush(BlendColor(color, Colors.Black, 0.14));
			}
			else
			{
				resources["SurfaceBrush"] = CreateBrush(BlendColor(color, Colors.White, 0.06));
				resources["SurfaceAltBrush"] = CreateBrush(BlendColor(color, color2, 0.12));
				resources["ControlBackgroundBrush"] = CreateBrush(BlendColor(color, Colors.Black, 0.13));
				resources["ControlHoverBrush"] = CreateBrush(BlendColor(color, color2, 0.2));
				resources["ControlPressedBrush"] = CreateBrush(BlendColor(color, color2, 0.3));
				resources["BorderBrush"] = CreateBrush(BlendColor(color, color2, 0.35));
				resources["DividerBrush"] = CreateBrush(BlendColor(color, color2, 0.46));
			}
			rootBorder.Background = solidColorBrush;
			rectBorderPreview.Fill = solidColorBrush;
		}
		else
		{
			rectBorderPreview.Fill = System.Windows.Media.Brushes.Transparent;
		}
	}

	private void ApplyAccentColor(string hex)
	{
		if (TryParseHex(hex, out var color))
		{
			ResourceDictionary resources = System.Windows.Application.Current.Resources;
			resources["AccentBrush"] = CreateBrush(color);
			resources["AccentHoverBrush"] = CreateBrush(BlendColor(color, Colors.White, 0.15));
			resources["AccentPressedBrush"] = CreateBrush(BlendColor(color, Colors.Black, 0.18));
			resources["SuccessBrush"] = CreateBrush(color);
			resources["AccentTextBrush"] = CreateBrush(GetContrastingTextColor(color));
			rectAccentPreview.Fill = CreateBrush(color);
		}
		else
		{
			rectAccentPreview.Fill = System.Windows.Media.Brushes.Transparent;
		}
	}

	private void ApplyTextColor(string hex)
	{
		if (TryParseHex(hex, out var color))
		{
			System.Windows.Media.Color color2;
			System.Windows.Media.Color target = (TryParseHex(txtBorderColor.Text, out color2) ? color2 : System.Windows.Media.Color.FromRgb(34, 34, 34));
			ResourceDictionary resources = System.Windows.Application.Current.Resources;
			resources["TextBrush"] = CreateBrush(color);
			resources["MutedTextBrush"] = CreateBrush(BlendColor(color, target, 0.34));
			rectTextPreview.Fill = CreateBrush(color);
		}
		else
		{
			rectTextPreview.Fill = System.Windows.Media.Brushes.Transparent;
		}
	}

	private static SolidColorBrush CreateBrush(System.Windows.Media.Color color)
	{
		SolidColorBrush solidColorBrush = new SolidColorBrush(color);
		if (solidColorBrush.CanFreeze)
		{
			solidColorBrush.Freeze();
		}
		return solidColorBrush;
	}

	private static System.Windows.Media.Color BlendColor(System.Windows.Media.Color source, System.Windows.Media.Color target, double amount)
	{
		amount = Math.Clamp(amount, 0.0, 1.0);
		return System.Windows.Media.Color.FromArgb(Mix(source.A, target.A), Mix(source.R, target.R), Mix(source.G, target.G), Mix(source.B, target.B));
		byte Mix(byte first, byte second)
		{
			return (byte)Math.Round((double)(int)first + (double)(second - first) * amount);
		}
	}

	private static System.Windows.Media.Color GetContrastingTextColor(System.Windows.Media.Color background)
	{
		if (!((0.2126 * (double)(int)background.R + 0.7152 * (double)(int)background.G + 0.0722 * (double)(int)background.B) / 255.0 >= 0.58))
		{
			return Colors.White;
		}
		return System.Windows.Media.Color.FromRgb(34, 34, 34);
	}

	private static double GetRelativeLuminance(System.Windows.Media.Color color)
	{
		return (0.2126 * (double)(int)color.R + 0.7152 * (double)(int)color.G + 0.0722 * (double)(int)color.B) / 255.0;
	}

	private static bool TryParseHex(string? hex, out System.Windows.Media.Color color)
	{
		color = default(System.Windows.Media.Color);
		if (string.IsNullOrWhiteSpace(hex))
		{
			return false;
		}
		hex = hex.Trim();
		if (!hex.StartsWith("#", StringComparison.Ordinal))
		{
			hex = "#" + hex;
		}
		try
		{
			color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.LeftButton != MouseButtonState.Pressed)
		{
			return;
		}
		if (e.ClickCount == 2)
		{
			base.WindowState = ((base.WindowState != WindowState.Maximized) ? WindowState.Maximized : WindowState.Normal);
			return;
		}
		try
		{
			DragMove();
		}
		catch
		{
		}
	}

	private void BtnWindowMinimize_Click(object sender, RoutedEventArgs e)
	{
		base.WindowState = WindowState.Minimized;
	}

	private void BtnWindowMaximize_Click(object sender, RoutedEventArgs e)
	{
		base.WindowState = ((base.WindowState != WindowState.Maximized) ? WindowState.Maximized : WindowState.Normal);
	}

	private void BtnWindowClose_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void BtnMinimizeToTray_Click(object sender, RoutedEventArgs e)
	{
		CancelHotkeyCapture();
		SaveSettings();
		Hide();
		ShowTrayIcon();
	}

	private void ShowTrayIcon()
	{
		if (_notifyIcon == null)
		{
			_notifyIcon = new NotifyIcon
			{
				Icon = LoadApplicationIcon(),
				Text = "dabudi helper",
				Visible = true,
				ContextMenuStrip = BuildTrayMenu()
			};
			_notifyIcon.DoubleClick += delegate
			{
				RestoreFromTray();
			};
		}
		else
		{
			_notifyIcon.Visible = true;
		}
	}

	private ContextMenuStrip BuildTrayMenu()
	{
		ContextMenuStrip contextMenuStrip = new ContextMenuStrip();
		ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem("Открыть");
		toolStripMenuItem.Click += delegate
		{
			RestoreFromTray();
		};
		ToolStripMenuItem toolStripMenuItem2 = new ToolStripMenuItem("Остановить инструменты");
		toolStripMenuItem2.Click += delegate
		{
			base.Dispatcher.BeginInvoke(new Action(StopAutomation));
		};
		ToolStripMenuItem toolStripMenuItem3 = new ToolStripMenuItem("Выход");
		toolStripMenuItem3.Click += delegate
		{
			base.Dispatcher.BeginInvoke(new Action(ExitApplication));
		};
		contextMenuStrip.Items.Add(toolStripMenuItem);
		contextMenuStrip.Items.Add(toolStripMenuItem2);
		contextMenuStrip.Items.Add(new ToolStripSeparator());
		contextMenuStrip.Items.Add(toolStripMenuItem3);
		return contextMenuStrip;
	}

	private static Icon LoadApplicationIcon()
	{
		try
		{
			StreamResourceInfo resourceStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/imgs/icon.ico", UriKind.Absolute));
			if (resourceStream != null)
			{
				using (Stream stream = resourceStream.Stream)
				{
					using Icon icon = new Icon(stream);
					return (Icon)icon.Clone();
				}
			}
		}
		catch
		{
		}
		return (Icon)SystemIcons.Application.Clone();
	}

	private void StopAutomation()
	{
		_autoClicker.Stop();
		ClosePerformance();
		UpdateClickerUi();
		SetStatus("Все инструменты остановлены");
	}

	private void RestoreFromTray()
	{
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			Show();
			base.WindowState = WindowState.Normal;
			Activate();
		});
	}

	private void DisposeTrayIcon()
	{
		if (_notifyIcon != null)
		{
			Icon? icon = _notifyIcon.Icon;
			_notifyIcon.Visible = false;
			_notifyIcon.Dispose();
			_notifyIcon = null;
			icon?.Dispose();
		}
	}

	private void LoadSettings()
	{
		SettingsModel settingsModel = SettingsModel.Load();
		AppThemeService.Apply("SageGraphite");
		txtDsDuration.Text = settingsModel.DsDuration.ToString();
		txtEndDuration.Text = settingsModel.EndDuration.ToString();
		_startHotkey.Modifiers = (ModifierKeys)settingsModel.Modifiers;
		_closeHotkey.Modifiers = (ModifierKeys)settingsModel.CloseModifiers;
		_exitHotkey.Modifiers = (ModifierKeys)settingsModel.ExitModifiers;
		_crosshairHotkey.Modifiers = (ModifierKeys)settingsModel.CrosshairModifiers;
		_timerHotkey.Modifiers = (ModifierKeys)settingsModel.TimerModifiers;
		_performanceHotkey.Modifiers = (ModifierKeys)settingsModel.PerformanceModifiers;
		_clickerHotkey.Modifiers = (ModifierKeys)settingsModel.ClickerModifiers;
		_startHotkey.Key = ParseKeyOrDefault(settingsModel.Key, Key.F9);
		_closeHotkey.Key = ParseKeyOrDefault(settingsModel.CloseKey, Key.Escape);
		_exitHotkey.Key = ParseKeyOrDefault(settingsModel.ExitKey, Key.None);
		_crosshairHotkey.Key = ParseKeyOrDefault(settingsModel.CrosshairKey, Key.F8);
		_timerHotkey.Key = ParseKeyOrDefault(settingsModel.TimerKey, Key.F7);
		_performanceHotkey.Key = ParseKeyOrDefault(settingsModel.PerformanceKey, Key.F10);
		_clickerHotkey.Key = ParseKeyOrDefault(settingsModel.ClickerKey, Key.F6);
		txtClickerCps.Text = Math.Clamp(settingsModel.ClickerCps, 1, 50).ToString();
		_clickerBinding = ((settingsModel.ClickerTargetKind == ClickerInputKind.KeyboardKey && settingsModel.ClickerVirtualKey > 0) ? ClickerBinding.ForKeyboard(settingsModel.ClickerVirtualKey) : ClickerBinding.ForMouse(settingsModel.ClickerButton));
		chkRunAtStartup.IsChecked = settingsModel.RunAtStartup && StartupService.SetEnabled(enabled: true);
		AppThemePalette theme = AppThemeService.GetTheme(settingsModel.ThemeName);
		txtGridColor.Text = (string.IsNullOrWhiteSpace(settingsModel.GridColor) ? theme.AppBackground : settingsModel.GridColor);
		txtBorderColor.Text = (string.IsNullOrWhiteSpace(settingsModel.BorderColor) ? theme.PanelBackground : settingsModel.BorderColor);
		txtAccentColor.Text = (string.IsNullOrWhiteSpace(settingsModel.AccentColor) ? theme.Accent : settingsModel.AccentColor);
		txtTextColor.Text = (string.IsNullOrWhiteSpace(settingsModel.TextColor) ? theme.TextPrimary : settingsModel.TextColor);
	}

	private void SaveSettings()
	{
		if (!_updatingUi && txtDsDuration != null && txtGridColor != null)
		{
			SettingsModel settingsModel = new SettingsModel();
			settingsModel.ThemeName = "SageGraphite";
			settingsModel.DsDuration = ParseOrDefault(txtDsDuration.Text, 60, 1);
			settingsModel.EndDuration = ParseOrDefault(txtEndDuration.Text, 15, 0);
			settingsModel.Modifiers = (uint)_startHotkey.Modifiers;
			settingsModel.Key = KeyToSetting(_startHotkey.Key);
			settingsModel.CloseModifiers = (uint)_closeHotkey.Modifiers;
			settingsModel.CloseKey = KeyToSetting(_closeHotkey.Key);
			settingsModel.ExitModifiers = (uint)_exitHotkey.Modifiers;
			settingsModel.ExitKey = KeyToSetting(_exitHotkey.Key);
			settingsModel.CrosshairModifiers = (uint)_crosshairHotkey.Modifiers;
			settingsModel.CrosshairKey = KeyToSetting(_crosshairHotkey.Key);
			settingsModel.TimerModifiers = (uint)_timerHotkey.Modifiers;
			settingsModel.TimerKey = KeyToSetting(_timerHotkey.Key);
			settingsModel.PerformanceModifiers = (uint)_performanceHotkey.Modifiers;
			settingsModel.PerformanceKey = KeyToSetting(_performanceHotkey.Key);
			settingsModel.ClickerCps = GetClickerCps();
			settingsModel.ClickerButton = _clickerBinding.MouseButton;
			settingsModel.ClickerTargetKind = _clickerBinding.Kind;
			settingsModel.ClickerVirtualKey = _clickerBinding.VirtualKey;
			settingsModel.ClickerModifiers = (uint)_clickerHotkey.Modifiers;
			settingsModel.ClickerKey = KeyToSetting(_clickerHotkey.Key);
			settingsModel.RunAtStartup = chkRunAtStartup.IsChecked == true;
			settingsModel.GridColor = txtGridColor.Text.Trim();
			settingsModel.BorderColor = txtBorderColor.Text.Trim();
			settingsModel.AccentColor = txtAccentColor.Text.Trim();
			settingsModel.TextColor = txtTextColor.Text.Trim();
			settingsModel.Save();
		}
	}

	private void UpdateHotkeyButtons()
	{
		UpdateHotkeyButton(CaptureMode.Start);
		UpdateHotkeyButton(CaptureMode.Close);
		UpdateHotkeyButton(CaptureMode.Exit);
		UpdateHotkeyButton(CaptureMode.Crosshair);
		UpdateHotkeyButton(CaptureMode.Timer);
		UpdateHotkeyButton(CaptureMode.Performance);
		UpdateHotkeyButton(CaptureMode.Clicker);
	}

	private void UpdateHotkeyButton(CaptureMode mode, Key? keyOverride = null)
	{
		HotkeyBinding binding = GetBinding(mode);
		Key key = keyOverride ?? binding.Key;
		string content = FormatHotkeyDisplay(binding.Modifiers, key);
		switch (mode)
		{
		case CaptureMode.Start:
			btnRecordKey.Content = content;
			break;
		case CaptureMode.Close:
			btnRecordCloseKey.Content = content;
			break;
		case CaptureMode.Exit:
			btnRecordExitKey.Content = content;
			break;
		case CaptureMode.Crosshair:
			btnRecordCrosshairKey.Content = content;
			break;
		case CaptureMode.Timer:
			btnRecordTimerKey.Content = content;
			break;
		case CaptureMode.Performance:
			btnRecordPerformanceKey.Content = content;
			break;
		case CaptureMode.Clicker:
			btnRecordClickerKey.Content = content;
			break;
		}
	}

	private HotkeyBinding GetBinding(CaptureMode mode)
	{
		return mode switch
		{
			CaptureMode.Start => _startHotkey,
			CaptureMode.Close => _closeHotkey,
			CaptureMode.Exit => _exitHotkey,
			CaptureMode.Crosshair => _crosshairHotkey,
			CaptureMode.Timer => _timerHotkey,
			CaptureMode.Performance => _performanceHotkey,
			CaptureMode.Clicker => _clickerHotkey,
			_ => throw new InvalidOperationException("Unknown capture mode."),
		};
	}

	private static ModifierKeys GetCurrentModifiers()
	{
		return Keyboard.Modifiers & (ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Windows);
	}

	private static Key NormalizeKey(System.Windows.Input.KeyEventArgs e)
	{
		if (e.Key != Key.System)
		{
			return e.Key;
		}
		return e.SystemKey;
	}

	private static bool IsModifierKey(Key key)
	{
		if ((uint)(key - 70) <= 1u || (uint)(key - 116) <= 5u || key == Key.System)
		{
			return true;
		}
		return false;
	}

	private static string FormatHotkeyDisplay(ModifierKeys modifiers, Key key)
	{
		string text = string.Empty;
		if ((modifiers & ModifierKeys.Control) != ModifierKeys.None)
		{
			text += "Ctrl+";
		}
		if ((modifiers & ModifierKeys.Shift) != ModifierKeys.None)
		{
			text += "Shift+";
		}
		if ((modifiers & ModifierKeys.Alt) != ModifierKeys.None)
		{
			text += "Alt+";
		}
		if ((modifiers & ModifierKeys.Windows) != ModifierKeys.None)
		{
			text += "Win+";
		}
		if (key == Key.None)
		{
			if (!string.IsNullOrEmpty(text))
			{
				return text;
			}
			return "Не назначено";
		}
		return text + key;
	}

	private static int ParseOrDefault(string? text, int fallback, int min)
	{
		if (!int.TryParse(text, out var result) || result < min)
		{
			return fallback;
		}
		return result;
	}

	private static int ParseClamped(string? text, int fallback, int min, int max)
	{
		if (!int.TryParse(text, out var result))
		{
			return Math.Clamp(fallback, min, max);
		}
		return Math.Clamp(result, min, max);
	}

	private static Key ParseKeyOrDefault(string? value, Key fallback)
	{
		if (!Enum.TryParse<Key>(value, ignoreCase: true, out var result))
		{
			return fallback;
		}
		return result;
	}

	private static string KeyToSetting(Key key)
	{
		if (key != Key.None)
		{
			return key.ToString();
		}
		return string.Empty;
	}

	private System.Windows.Media.Brush GetBrush(string resourceKey, System.Windows.Media.Brush fallback)
	{
		return (TryFindResource(resourceKey) as System.Windows.Media.Brush) ?? fallback;
	}

	private void SetStatus(string text, bool active = false, bool isError = false)
	{
	}

	[DllImport("user32.dll")]
	private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

	[DllImport("user32.dll")]
	private static extern bool UnregisterHotKey(nint window, int id);
}
