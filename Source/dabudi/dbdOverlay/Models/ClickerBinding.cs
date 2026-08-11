using System.Windows.Input;

namespace dbdOverlay.Models;

public readonly record struct ClickerBinding(ClickerInputKind Kind, MouseButtonKind MouseButton, int VirtualKey)
{
	public string DisplayName
	{
		get
		{
			if (Kind != ClickerInputKind.KeyboardKey)
			{
				return GetMouseDisplayName(MouseButton);
			}
			return GetKeyboardDisplayName(VirtualKey);
		}
	}

	public static ClickerBinding ForMouse(MouseButtonKind button)
	{
		return new ClickerBinding(ClickerInputKind.MouseButton, button, 0);
	}

	public static ClickerBinding ForKeyboard(int virtualKey)
	{
		return new ClickerBinding(ClickerInputKind.KeyboardKey, MouseButtonKind.Left, virtualKey);
	}

	private static string GetKeyboardDisplayName(int virtualKey)
	{
		Key key = KeyInterop.KeyFromVirtualKey(virtualKey);
		if (key != Key.None)
		{
			return key.ToString();
		}
		return $"VK {virtualKey}";
	}

	private static string GetMouseDisplayName(MouseButtonKind button)
	{
		return button switch
		{
			MouseButtonKind.Left => "Левая кнопка мыши",
			MouseButtonKind.Right => "Правая кнопка мыши",
			MouseButtonKind.Middle => "Средняя кнопка мыши",
			MouseButtonKind.X1 => "Боковая кнопка 1",
			MouseButtonKind.X2 => "Боковая кнопка 2",
			_ => "Левая кнопка мыши",
		};
	}
}
