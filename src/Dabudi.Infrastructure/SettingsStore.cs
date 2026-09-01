using System.Text.Json;
using System.Text.Json.Serialization;
using Dabudi.Core;

namespace Dabudi.Infrastructure;

public sealed record SettingsLoadResult(AppSettings Settings, string? Notice = null, bool CanSave = true);

public sealed class SettingsStore(string directory, AppLog log, string? legacyDirectory = null)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public string DirectoryPath { get; } = directory;
    public string FilePath => Path.Combine(DirectoryPath, "settings.json");

    public SettingsLoadResult Load(Func<string, int>? legacyKeyResolver = null)
    {
        if (File.Exists(FilePath))
        {
            try
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options)
                    ?? throw new JsonException("Пустой файл настроек.");
                if (settings.SchemaVersion > AppSettings.CurrentSchema)
                    return new(new AppSettings(), "Настройки созданы более новой версией dabudi. Файл сохранён; запись отключена.", false);
                var normalized = AppSettings.Normalize(settings);
                return new(normalized, (settings with { SchemaVersion = AppSettings.CurrentSchema }).Validate().Count > 0
                    ? "Некорректные настройки исправлены. Проверьте клавиши и сохраните изменения." : null);
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                log.Write("Could not load settings", exception);
                var backup = FilePath + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
                try
                {
                    File.Copy(FilePath, backup, overwrite: false);
                    return new(new AppSettings(), "Не удалось прочитать настройки. Исходный файл скопирован; загружены стандартные значения.");
                }
                catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
                {
                    log.Write("Could not preserve unreadable settings", backupError);
                    return new(new AppSettings(), "Настройки недоступны. Запись отключена, чтобы сохранить исходный файл.", false);
                }
            }
        }

        var legacyPath = legacyDirectory == null ? null : Path.Combine(legacyDirectory, "settings.json");
        if (legacyPath != null && File.Exists(legacyPath) && legacyKeyResolver != null)
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(legacyPath));
                var migrated = MigrateLegacy(json.RootElement, legacyKeyResolver);
                Save(migrated);
                return new(migrated, "Настройки версии 2.5.8 перенесены. Проверьте горячие клавиши; старый файл сохранён.");
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                log.Write("Could not migrate legacy settings", exception);
                return new(new AppSettings(), "Не удалось перенести старые настройки. Старый файл сохранён; подробности в журнале.");
            }
        }
        return new(new AppSettings());
    }

    public void Save(AppSettings settings)
    {
        var errors = settings.Validate();
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));
        Directory.CreateDirectory(DirectoryPath);
        var temporary = Path.Combine(DirectoryPath, $"settings-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, Options);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak", ignoreMetadataErrors: true);
            else File.Move(temporary, FilePath);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); }
                catch (IOException exception) { log.Write("Could not remove temporary settings file", exception); }
            }
        }
    }

    public static AppSettings MigrateLegacy(JsonElement root, Func<string, int> resolveKey)
    {
        JsonElement? Get(string name)
        {
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Expected a settings object.");
            foreach (var property in root.EnumerateObject())
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return property.Value;
            return null;
        }
        int Number(string name, int fallback) => Get(name) is { ValueKind: JsonValueKind.Number } value
            && value.TryGetInt32(out var number) ? number : fallback;
        string Text(string name, string fallback) => Get(name) is { ValueKind: JsonValueKind.String } value
            ? value.GetString() ?? fallback : fallback;
        var settings = new AppSettings();
        var shortcuts = Shortcut.Defaults();
        foreach (var (action, prefix) in new (AppAction, string)[]
        {
            (AppAction.RestartEffects, ""), (AppAction.CloseEffects, "Close"),
            (AppAction.Exit, "Exit"), (AppAction.ToggleCrosshair, "Crosshair"),
            (AppAction.ToggleStopwatch, "Timer"), (AppAction.TogglePerformance, "Performance"),
            (AppAction.ToggleClicker, "Clicker")
        })
        {
            if (Get(prefix + "Key") is { ValueKind: JsonValueKind.String } key)
            {
                var virtualKey = resolveKey(key.GetString() ?? "");
                shortcuts[action] = virtualKey == 0 ? new() : new(virtualKey,
                    (ShortcutModifiers)(Number(prefix + "Modifiers", 0) & 15));
            }
        }
        return AppSettings.Normalize(settings with
        {
            DecisiveStrikeSeconds = Number("DsDuration", settings.DecisiveStrikeSeconds),
            EnduranceSeconds = Number("EndDuration", settings.EnduranceSeconds),
            ClicksPerSecond = Number("ClickerCps", settings.ClicksPerSecond),
            ClickTarget = new((InputKind)Number("ClickerTargetKind", 0),
                (MouseButton)Number("ClickerButton", 0), Number("ClickerVirtualKey", 0)),
            RunAtStartup = Get("RunAtStartup") is { ValueKind: JsonValueKind.True },
            BackgroundColor = Text("GridColor", settings.BackgroundColor),
            PanelColor = Text("BorderColor", settings.PanelColor),
            AccentColor = Text("AccentColor", settings.AccentColor),
            TextColor = Text("TextColor", settings.TextColor),
            Shortcuts = shortcuts
        });
    }
}
