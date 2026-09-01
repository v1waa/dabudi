namespace Dabudi.Presentation;

public sealed class OverlayManager
{
    private readonly Dictionary<OverlayKind, OverlayWindow> _windows = new();
    public int Count => _windows.Count;
    public bool IsVisible(OverlayKind kind) => _windows.ContainsKey(kind);
    public OverlayWindow? Get(OverlayKind kind) => _windows.GetValueOrDefault(kind);

    public OverlayWindow Show(OverlayKind kind, AppSettings settings)
    {
        if (_windows.TryGetValue(kind, out var current)) return current;
        var window = new OverlayWindow(kind, settings);
        _windows.Add(kind, window);
        window.Closed += (_, _) => { _windows.Remove(kind); Arrange(); };
        try { window.Show(); }
        catch { _windows.Remove(kind); window.Close(); throw; }
        Arrange();
        return window;
    }

    public void Close(OverlayKind kind) => Get(kind)?.Close();
    public void CloseAll()
    {
        foreach (var window in _windows.Values.ToArray()) window.Close();
    }
    public void Configure(AppSettings settings)
    {
        foreach (var window in _windows.Values) window.Configure(settings);
        Arrange();
    }
    private void Arrange() => Get(OverlayKind.Effects)?.AvoidPerformanceOverlap(Get(OverlayKind.Performance));
}
