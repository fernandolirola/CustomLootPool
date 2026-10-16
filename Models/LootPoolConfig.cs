using System.Text.Json.Serialization;

namespace CustomLootPool.Models;

public enum TargetMode
{
    /// <summary>Only containers / spawn points that already hold items of the same category</summary>
    Similar,
    All,
    Selected,
    Categories,
}

public class LootPoolConfig
{
    public bool Enabled { get; set; } = true;
    public bool Debug { get; set; } = false;

    public string Language { get; set; } = "en";

    /// <summary>Divide each chance by the map's loot multiplier ratio, so the amount per raid stays close to vanilla</summary>
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

    public List<string> Categories { get; set; } = [];

    /// <summary>Chance (0-90%) that each item rolled inside a matching container is this item</summary>
    public double ChancePercent { get; set; } = 1.0;
}

public class LooseLootSettings
{
    public bool Enabled { get; set; } = false;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TargetMode Mode { get; set; } = TargetMode.Similar;

    public List<string> Categories { get; set; } = [];

    /// <summary>Chance (0-90%) that a matching spawn point, once it spawns, spawns this item</summary>
    public double ChancePercent { get; set; } = 1.0;
}
