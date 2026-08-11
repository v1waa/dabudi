using System.Runtime.InteropServices;
using dbdOverlay.Models;

namespace dbdOverlay.Services;

#pragma warning disable CS0649 // Unset Win32 INPUT fields are intentionally zero-initialized.

internal static class InputAutomationService
{
	private struct Input
	{
		public uint Type;

		public InputUnion Union;
	}

	[StructLayout(LayoutKind.Explicit)]
	private struct InputUnion
	{
		[FieldOffset(0)]
		public MouseInput Mouse;

		[FieldOffset(0)]
		public KeyboardInput Keyboard;
	}

	private struct MouseInput
	{
		public int X;

		public int Y;

		public uint MouseData;

		public uint Flags;

		public uint Time;

		public nint ExtraInfo;
	}

	private struct KeyboardInput
	{
		public ushort VirtualKey;

		public ushort ScanCode;

		public uint Flags;

		public uint Time;

		public nint ExtraInfo;
	}

	private const uint InputMouse = 0u;

	private const uint InputKeyboard = 1u;

	private const uint KeyEventExtendedKey = 1u;

	private const uint KeyEventKeyUp = 2u;

	private const uint MouseEventLeftDown = 2u;

	private const uint MouseEventLeftUp = 4u;

	private const uint MouseEventRightDown = 8u;

	private const uint MouseEventRightUp = 16u;

	private const uint MouseEventMiddleDown = 32u;

	private const uint MouseEventMiddleUp = 64u;

	private const uint MouseEventWheel = 2048u;

	private const uint MouseEventXDown = 128u;

	private const uint MouseEventXUp = 256u;

	public static void Click(MouseButtonKind button)
	{
		Input[] array = new Input[2]
		{
			CreateMouseButtonInput(button, isDown: true),
			CreateMouseButtonInput(button, isDown: false)
		};
		SendInput((uint)array.Length, array, Marshal.SizeOf<Input>());
	}

	public static void SendKey(int virtualKey, bool isDown)
	{
		if (virtualKey > 0 && virtualKey <= 65535)
		{
			Input[] inputs = new Input[1]
			{
				new Input
				{
					Type = 1u,
					Union = new InputUnion
					{
						Keyboard = new KeyboardInput
						{
							VirtualKey = (ushort)virtualKey,
							Flags = ((uint)((!isDown) ? 2 : 0) | (IsExtendedKey(virtualKey) ? 1u : 0u))
						}
					}
				}
			};
			SendInput(1u, inputs, Marshal.SizeOf<Input>());
		}
	}

	public static void PressKey(int virtualKey)
	{
		if (virtualKey > 0 && virtualKey <= 65535)
		{
			Input[] array = new Input[2]
			{
				CreateKeyboardInput(virtualKey, isDown: true),
				CreateKeyboardInput(virtualKey, isDown: false)
			};
			SendInput((uint)array.Length, array, Marshal.SizeOf<Input>());
		}
	}

	private static bool IsExtendedKey(int virtualKey)
	{
		switch (virtualKey)
		{
		case 3:
		case 33:
		case 34:
		case 35:
		case 36:
		case 37:
		case 38:
		case 39:
		case 40:
		case 44:
		case 45:
		case 46:
		case 111:
		case 144:
		case 163:
		case 165:
			return true;
		default:
			return false;
		}
	}

	private static Input CreateKeyboardInput(int virtualKey, bool isDown)
	{
		return new Input
		{
			Type = 1u,
			Union = new InputUnion
			{
				Keyboard = new KeyboardInput
				{
					VirtualKey = (ushort)virtualKey,
					Flags = ((uint)((!isDown) ? 2 : 0) | (IsExtendedKey(virtualKey) ? 1u : 0u))
				}
			}
		};
	}

	public static void SendMouseButton(MouseButtonKind button, bool isDown)
	{
		Input[] inputs = new Input[1] { CreateMouseButtonInput(button, isDown) };
		SendInput(1u, inputs, Marshal.SizeOf<Input>());
	}

	public static void SendMouseWheel(int delta)
	{
		Input[] inputs = new Input[1]
		{
			new Input
			{
				Type = 0u,
				Union = new InputUnion
				{
					Mouse = new MouseInput
					{
						MouseData = (uint)delta,
						Flags = 2048u
					}
				}
			}
		};
		SendInput(1u, inputs, Marshal.SizeOf<Input>());
	}

	public static void SetCursorPosition(int x, int y)
	{
		SetCursorPos(x, y);
	}

	private static Input CreateMouseButtonInput(MouseButtonKind button, bool isDown)
	{
		var (flags, mouseData) = button switch
		{
			MouseButtonKind.Left => (isDown ? 2u : 4u, 0u),
			MouseButtonKind.Right => (isDown ? 8u : 16u, 0u),
			MouseButtonKind.Middle => (isDown ? 32u : 64u, 0u),
			MouseButtonKind.X1 => (isDown ? 128u : 256u, 1u),
			MouseButtonKind.X2 => (isDown ? 128u : 256u, 2u),
			_ => (isDown ? 2u : 4u, 0u),
		};
		return new Input
		{
			Type = 0u,
			Union = new InputUnion
			{
				Mouse = new MouseInput
				{
					MouseData = mouseData,
					Flags = flags
				}
			}
		};
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetCursorPos(int x, int y);
}

#pragma warning restore CS0649
