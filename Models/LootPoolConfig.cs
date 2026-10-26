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

public enum ScheduleMode
{
    Always,
    Dates,
    Weekends,
}

public class LootPoolConfig
{
    public bool Enabled { get; set; } = true;
    public bool Debug { get; set; } = false;

    public string Language { get; set; } = "en";

    /// <summary>Divide each chance by the map's loot multiplier ratio, so the amount per raid stays close to vanilla</summary>
    public bool CompensateLootMultipliers { get; set; } = false;

    public List<LootEntry> Entries { get; set; } = [];

    public QualitySettings Quality { get; set; } = new();
}

public enum QualityBasis
{
    Value,
    Rarity,
}

/// <summary>Multiplies the weight of map loot by value tier or rarity; the amount of items that spawn is unchanged</summary>
public class QualitySettings
{
    public bool Enabled { get; set; } = false;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public QualityBasis Basis { get; set; } = QualityBasis.Value;

    /// <summary>Empty = every map</summary>
    public List<string> Maps { get; set; } = [];

    public bool Containers { get; set; } = true;
    public bool LooseLoot { get; set; } = true;

    /// <summary>Expensive keys would otherwise flood drawers and jackets</summary>
    public bool ExcludeKeys { get; set; } = true;

    /// <summary>Sorted by From; the first tier starts at 0 roubles</summary>
    public List<ValueTier> ValueTiers { get; set; } =
    [
        new() { From = 0, Multiplier = 1 },
        new() { From = 10000, Multiplier = 1 },
        new() { From = 50000, Multiplier = 2 },
        new() { From = 150000, Multiplier = 3 },
    ];

    public double CommonMultiplier { get; set; } = 1;
    public double RareMultiplier { get; set; } = 1;
    public double SuperrareMultiplier { get; set; } = 2;

    internal bool AllowsMap(string? mapId) => Maps.Count == 0 || (mapId is not null && Maps.Contains(mapId, StringComparer.OrdinalIgnoreCase));
}

public class ValueTier
{
    /// <summary>Lowest price in roubles (flea, or handbook when the flea has none)</summary>
    public int From { get; set; }

    public double Multiplier { get; set; } = 1;
}

public class LootEntry
{
    public string Tpl { get; set; } = string.Empty;

    /// <summary>Informational only, the item is identified by Tpl</summary>
    public string? Name { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Empty = every map</summary>
    public List<string> Maps { get; set; } = [];

    internal bool AllowsMap(string? mapId) => Maps.Count == 0 || (mapId is not null && Maps.Contains(mapId, StringComparer.OrdinalIgnoreCase));

    public ScheduleSettings Schedule { get; set; } = new();

    /// <summary>One extra copy at a random loose loot spot every raid</summary>
    public bool Guaranteed { get; set; } = false;

    public StaticLootSettings Static { get; set; } = new();
    public LooseLootSettings Loose { get; set; } = new();
    public BotLootSettings Bots { get; set; } = new();
    public AirdropSettings Airdrops { get; set; } = new();
}

public class ScheduleSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ScheduleMode Mode { get; set; } = ScheduleMode.Always;

    /// <summary>Month and day, "MM-dd". The window can wrap around the new year</summary>
    public string From { get; set; } = "12-01";
    public string To { get; set; } = "12-31";
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

    /// <summary>Overrides ChancePercent on specific maps</summary>
    [JsonConverter(typeof(MapChancesConverter))]
    public Dictionary<string, double> MapChances { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class LooseLootSettings
{
    public bool Enabled { get; set; } = false;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TargetMode Mode { get; set; } = TargetMode.Similar;

    public List<string> Categories { get; set; } = [];

    /// <summary>Chance (0-90%) that a matching spawn point, once it spawns, spawns this item</summary>
    public double ChancePercent { get; set; } = 1.0;

    [JsonConverter(typeof(MapChancesConverter))]
    public Dictionary<string, double> MapChances { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class BotLootSettings
{
    public bool Enabled { get; set; } = false;

    public bool Scavs { get; set; } = true;
    public bool Pmcs { get; set; } = true;
    public bool Bosses { get; set; } = false;

    public bool Backpack { get; set; } = true;
    public bool Pockets { get; set; } = true;
    public bool Vest { get; set; } = true;

    /// <summary>Chance (0-90%) that each item rolled into the chosen slots is this item</summary>
    public double ChancePercent { get; set; } = 0.5;
}

public class AirdropSettings
{
    public bool Enabled { get; set; } = false;

    /// <summary>Empty means all crate types, including crates added by other mods.</summary>
    public List<string> Types { get; set; } = [];

    /// <summary>Chance (0-100%) that an airdrop crate contains one of this item</summary>
    public double ChancePercent { get; set; } = 10;
}
