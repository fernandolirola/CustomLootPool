using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Web;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace CustomLootPool;

public record CustomLootPoolMetadata : IModMetadata, IModBlazorMetadata
{
    public string ModGuid { get; init; } = "com.zezinho.customlootpool";
    public string Name { get; init; } = "Custom Loot Pool";
    public string Author { get; init; } = "zezinho";
    public List<string>? Contributors { get; init; } = [];
    public Version Version { get; init; } = new("1.2.0");
    public Range SptVersion { get; init; } = new("~4.1");
    public List<string>? Incompatibilities { get; init; } = [];
    public Dictionary<string, Range>? ModDependencies { get; init; } = [];
    public string? Url { get; init; } = "https://github.com/fernandolirola/CustomLootPool";
    public string License { get; init; } = "MIT";
    public bool HasPrepatcher { get; init; } = false;

    public string? WWWRootUrl { get; init; }
    public string? HomePage { get; init; } = "/customlootpool";
    public string? HomePageDescription { get; init; } = "Add any item to the map loot pools (containers and loose loot).";
}
