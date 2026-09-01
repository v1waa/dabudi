using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Dabudi.Core;

namespace Dabudi.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class HotkeyRegistry(nint window) : IDisposable
{
    private Dictionary<AppAction, Shortcut> _desired = new();
    private readonly Dictionary<int, (AppAction Action, Shortcut Shortcut)> _registered = new();
    private bool _effectsActive;
    private bool _suspended;

    public IReadOnlyList<string> Initialize(IReadOnlyDictionary<AppAction, Shortcut> shortcuts)
    {
        _desired = new(shortcuts);
        return Rebind();
    }

    public bool TryApply(IReadOnlyDictionary<AppAction, Shortcut> shortcuts, out string? error)
    {
        var previous = _desired;
        _desired = new(shortcuts);
        var failures = Rebind();
        if (failures.Count == 0) { error = null; return true; }
        _desired = previous;
        var rollbackFailures = Rebind();
        error = string.Join(" ", failures.Concat(rollbackFailures));
        return false;
    }

    public IReadOnlyList<string> SetEffectsActive(bool active)
    {
        if (_effectsActive == active) return [];
        _effectsActive = active;
        return Rebind();
    }

    public void Suspend() { _suspended = true; UnregisterAll(); }
    public IReadOnlyList<string> Resume() { _suspended = false; return Rebind(); }

    public AppAction? Resolve(nint id, nint detail)
    {
        if (!_registered.TryGetValue((int)id, out var binding)) return null;
        var data = detail.ToInt64();
        return (int)((data >> 16) & 0xFFFF) == binding.Shortcut.VirtualKey
            && (ShortcutModifiers)(data & 15) == binding.Shortcut.Modifiers ? binding.Action : null;
    }

    private IReadOnlyList<string> Rebind()
    {
        UnregisterAll();
        var failures = new List<string>();
        if (_suspended) return failures;
        foreach (var (action, shortcut) in _desired)
        {
            if (!shortcut.IsEnabled || (action == AppAction.CloseEffects && !_effectsActive)) continue;
            var id = 0x3000 + (int)action;
            if (NativeMethods.RegisterHotKey(window, id, (uint)shortcut.Modifiers | 0x4000, (uint)shortcut.VirtualKey))
                _registered.Add(id, (action, shortcut));
            else
                failures.Add($"«{Shortcut.ActionName(action)}»: сочетание занято другой программой или недоступно (Windows {Marshal.GetLastWin32Error()}).");
        }
        return failures;
    }

    private void UnregisterAll()
    {
        foreach (var id in _registered.Keys) NativeMethods.UnregisterHotKey(window, id);
        _registered.Clear();
    }

    public void Dispose() => UnregisterAll();
}
