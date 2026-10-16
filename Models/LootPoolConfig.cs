using System.Text.Json.Serialization;

namespace CustomLootPool.Models;

public enum TargetMode
{
    /// <summary>Only containers / spawn points that already hold items of the same category</summary>
    Similar,
    All,
    Selected,
}

public class LootPoolConfig
{
    public bool Enabled { get; set; } = true;
    public bool Debug { get; set; } = false;

    public string Language { get; set; } = "en";

    /// <summary>
    /// Divide each chance by how much other mods (e.g. Lots of Loot) multiply the loot on that map,
    /// so the amount per raid stays close to vanilla SPT
    /// </summary>
    public bool CompensateLootMultipliers { get; set; } = false;

    public List<LootEntry> Entries { get; set; } = [];
}

public class LootEntry
{
    public string Tpl { get; set; } = string.Empty;

    /// <summary>Informational only, the item is identified by Tpl</summary>
    public string? Name { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Empty = every map</summary>
    public List<string> Maps { get; set; } = [];

    public StaticLootSettings Static { get; set; } = new();
    public LooseLootSettings Loose { get; set; } = new();
}

public class StaticLootSettings
{
    public bool Enabled { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TargetMode Mode { get; set; } = TargetMode.Similar;

    public List<string> Containers { get; set; } = [];

    /// <summary>Chance (0-90%) that each item rolled inside a matching container is this item</summary>
    public double ChancePercent { get; set; } = 1.0;
}

public class LooseLootSettings
{
    public bool Enabled { get; set; } = false;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TargetMode Mode { get; set; } = TargetMode.Similar;

    /// <summary>Chance (0-90%) that a matching spawn point, once it spawns, spawns this item</summary>
    public double ChancePercent { get; set; } = 1.0;
}
