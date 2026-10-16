using System.Reflection;
using System.Text.Json;
using CustomLootPool.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;

namespace CustomLootPool.Services;

[Injectable(InjectionType.Singleton)]
public class ConfigService(ModHelper modHelper, ISptLogger<ConfigService> logger)
{
    public const string LogPrefix = "[Custom Loot Pool]";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string ModPath { get; } = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

    public string ConfigPath => Path.Combine(ModPath, "config.json");

    /// <summary>Live config, replaced as a whole on save so readers never see a half-edited state</summary>
    public LootPoolConfig Config { get; private set; } = new();

    /// <summary>Used to log the injection summary once per saved config instead of on every loot read</summary>
    public int Revision { get; private set; }

    public event Action? ConfigChanged;

    public bool IsPortuguese => string.Equals(Config.Language, "pt", StringComparison.OrdinalIgnoreCase);

    public string L(string pt, string en) => IsPortuguese ? pt : en;

    public void SetLanguage(string language)
    {
        var updated = CloneConfig();
        updated.Language = language;
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(updated, JsonOptions));
        Config = updated;
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                logger.Warning($"{LogPrefix} config.json not found, creating a new one");
                Save(new LootPoolConfig());
                return;
            }

            var loaded = JsonSerializer.Deserialize<LootPoolConfig>(File.ReadAllText(ConfigPath), JsonOptions);
            Config = loaded ?? new LootPoolConfig();
            Revision++;
            logger.Success($"{LogPrefix} " + L($"Config carregada: {Config.Entries.Count} item(ns) configurado(s)", $"Config loaded: {Config.Entries.Count} item(s) configured"));
        }
        catch (Exception ex)
        {
            // Keep the broken file, otherwise the next save from the page would overwrite the user's entries
            var backup = ConfigPath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json";
            try
            {
                File.Copy(ConfigPath, backup, overwrite: false);
            }
            catch
            {
                backup = "(backup failed)";
            }

            logger.Error($"{LogPrefix} Failed to read config.json, using an empty config. Your file was copied to {backup}. Error: {ex.Message}");
            Config = new LootPoolConfig();
        }
    }

    public void Save(LootPoolConfig newConfig)
    {
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(newConfig, JsonOptions));
        Config = newConfig;
        Revision++;
        ConfigChanged?.Invoke();
    }

    /// <summary>Deep copy so the UI can edit freely without touching the live config</summary>
    public LootPoolConfig CloneConfig() => CloneConfigOf(Config);

    public LootPoolConfig CloneConfigOf(LootPoolConfig source)
    {
        return JsonSerializer.Deserialize<LootPoolConfig>(JsonSerializer.Serialize(source, JsonOptions), JsonOptions)!;
    }
}
