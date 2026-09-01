using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Dabudi.Core;

namespace Dabudi.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class WindowsInputSender : IInputSender
{
    public void Send(InputTarget target)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == 0) return;
        NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        // Do not click the Start/Stop button again or type into our own settings.
        if (processId == Environment.ProcessId) return;
        var inputs = new[] { CreateInput(target, false), CreateInput(target, true) };
        var count = NativeMethods.SendInput(2, inputs, Marshal.SizeOf<NativeMethods.Input>());
        if (count == 2) return;
        var error = Marshal.GetLastWin32Error();
        if (count == 1)
            NativeMethods.SendInput(1, [inputs[1]], Marshal.SizeOf<NativeMethods.Input>());
        throw new Win32Exception(error, "Windows не приняла нажатие. Проверьте права целевого приложения.");
    }

    private static NativeMethods.Input CreateInput(InputTarget target, bool released)
    {
        if (target.Kind == InputKind.Keyboard)
            return new()
            {
                Type = 1,
                Data = new() { Keyboard = new()
                {
                    VirtualKey = (ushort)target.VirtualKey,
                    Flags = (released ? 2u : 0u) | (IsExtended(target.VirtualKey) ? 1u : 0u),
                    ExtraInfo = 0x44414255
                } }
            };
        var (down, up, data) = target.MouseButton switch
        {
            MouseButton.Right => (8u, 16u, 0u),
            MouseButton.Middle => (32u, 64u, 0u),
            MouseButton.X1 => (128u, 256u, 1u),
            MouseButton.X2 => (128u, 256u, 2u),
            _ => (2u, 4u, 0u)
        };
        return new() { Type = 0, Data = new() { Mouse = new()
        {
            Flags = released ? up : down, MouseData = data, ExtraInfo = 0x44414255
        } } };
    }

    private static bool IsExtended(int key) => key is 3 or >= 33 and <= 40 or 44 or 45 or 46
        or 91 or 92 or 93 or 111 or 144 or 163 or 165;
}
