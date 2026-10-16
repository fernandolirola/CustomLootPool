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
}

public class LootEntry
{
    public string Tpl { get; set; } = string.Empty;

    /// <summary>Informational only, the item is identified by Tpl</summary>
    public string? Name { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Empty = every map</summary>
    public List<string> Maps { get; set; } = [];

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

    /// <summary>Chance (0-100%) that an airdrop crate contains one of this item</summary>
    public double ChancePercent { get; set; } = 10;
}
