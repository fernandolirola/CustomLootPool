using System.Reflection;
using System.Text.Json;
using CustomLootPool.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Common.Models.Logging;

namespace CustomLootPool.Services;

public sealed class ConfigConflictException() : InvalidOperationException("A configuração foi alterada em outra aba. Recarregue antes de salvar. / Configuration changed in another editor. Reload before saving.");
public sealed record ConfigSnapshot(LootPoolConfig Config, int Revision);

[Injectable(InjectionType.Singleton)]
public class ConfigService
{
    public const string LogPrefix = "[Custom Loot Pool]";
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
    };
    private readonly Lock _writeLock = new();
    private readonly ISptLogger<ConfigService> _logger;
    private ConfigSnapshot _state = new(new(), 0);

    public ConfigService(ModHelper modHelper, ISptLogger<ConfigService> logger)
        : this(modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), logger) { }
    internal ConfigService(string modPath, ISptLogger<ConfigService> logger)
    {
        ModPath = Path.GetFullPath(modPath);
        _logger = logger;
    }
    public string ModPath { get; }
    public string ConfigPath => Path.Combine(ModPath, "config.json");
    public LootPoolConfig Config => Volatile.Read(ref _state).Config;
    public int Revision => Volatile.Read(ref _state).Revision;
    public event Action? ConfigChanged;
    public bool IsPortuguese => string.Equals(Config.Language, "pt", StringComparison.OrdinalIgnoreCase);
    public string L(string pt, string en) => IsPortuguese ? pt : en;

    public ConfigSnapshot GetSnapshot()
    {
        lock (_writeLock) return new(CloneConfigOf(_state.Config), _state.Revision);
    }

    public void SetLanguage(string language)
    {
        lock (_writeLock)
        {
            var updated = CloneConfigOf(_state.Config);
            updated.Language = language;
            ConfigValidation.Normalize(updated);
            WriteAtomic(updated);
            Volatile.Write(ref _state, new(updated, _state.Revision));
        }
        NotifyChanged();
    }

    public void Load()
    {
        lock (_writeLock)
        {
            try
            {
                var loaded = File.Exists(ConfigPath)
                    ? JsonSerializer.Deserialize<LootPoolConfig>(File.ReadAllText(ConfigPath), JsonOptions) ?? throw new InvalidDataException("Config cannot be null.")
                    : new LootPoolConfig();
                ConfigValidation.Normalize(loaded);
                if (!File.Exists(ConfigPath)) WriteAtomic(loaded);
                Volatile.Write(ref _state, new(loaded, _state.Revision + 1));
                _logger.Success($"{LogPrefix} Config loaded: {loaded.Entries.Count} item(s) configured");
            }
            catch (Exception ex)
            {
                var backup = ConfigPath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json";
                try { File.Copy(ConfigPath, backup); }
                catch { backup = "(backup failed)"; }
                _logger.Error($"{LogPrefix} Invalid config; retaining the last valid config. Backup: {backup}. Error: {ex.Message}");
            }
        }
    }

    public int Save(LootPoolConfig newConfig, int expectedRevision)
    {
        int revision;
        lock (_writeLock)
        {
            if (expectedRevision != _state.Revision) throw new ConfigConflictException();
            var updated = CloneConfigOf(newConfig);
            updated.Language = _state.Config.Language; // Language is saved independently by the editor.
            WriteAtomic(updated);
            revision = _state.Revision + 1;
            Volatile.Write(ref _state, new(updated, revision));
        }
        NotifyChanged();
        return revision;
    }

    private void WriteAtomic(LootPoolConfig value)
    {
        var temporary = ConfigPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, ConfigPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void NotifyChanged()
    {
        foreach (Action handler in ConfigChanged?.GetInvocationList() ?? [])
        {
            try { handler(); }
            catch (Exception ex) { _logger.Warning($"{LogPrefix} Config notification failed: {ex.Message}"); }
        }
    }

    public LootPoolConfig CloneConfig() => GetSnapshot().Config;
    public static T Clone<T>(T source) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(source, JsonOptions), JsonOptions)!;
    public static string ExportEntries(IEnumerable<LootEntry> entries) => JsonSerializer.Serialize(new { Entries = entries }, JsonOptions);
    public static List<LootEntry> ImportEntries(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = document.RootElement;
        List<LootEntry>? entries;
        if (root.ValueKind == JsonValueKind.Array) entries = root.Deserialize<List<LootEntry>>(JsonOptions);
        else if (root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Any(property => property.Name.Equals("Entries", StringComparison.OrdinalIgnoreCase)))
            entries = root.Deserialize<LootPoolConfig>(JsonOptions)?.Entries;
        else throw new InvalidDataException("Expected an entries array or an object containing Entries.");
        if (entries is null) throw new InvalidDataException("Entries cannot be null.");
        return ConfigValidation.Normalize(new LootPoolConfig { Entries = entries }).Entries;
    }
    public LootPoolConfig CloneConfigOf(LootPoolConfig source) => ConfigValidation.Normalize(Clone(source));
}
