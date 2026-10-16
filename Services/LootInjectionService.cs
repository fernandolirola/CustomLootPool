using System.Reflection;
using System.Collections.Concurrent;
using CustomLootPool.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Generators.Loot;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services.Server;
using SPTarkov.Server.Core.Services.Items;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Core.Utils.Json;

namespace CustomLootPool.Services;

public sealed record SimResult(string MapId, string MapName, int Raids, double StaticPerRaid, double LoosePerRaid, string? Error = null);

public sealed record ItemWarning(string Pt, string En, bool Blocking);

public sealed record MultiplierInfo(string MapId, string MapName, double Static, double Loose);

public sealed record PoolCategory(string Id, string Name, string Path, int ItemCount);

public sealed record PoolItem(CatalogItem Item, double AveragePercent, int Places);

public sealed record SimilarPool(string CategoryName, List<PoolItem> Items, List<string> Containers, int Spots);

public sealed record MapReach(string MapId, string MapName, int ContainerTypes, List<string> ContainerNames, int Spawnpoints);

/// <summary>
/// Map loot is lazy-loaded by SPT, so items are injected through LazyLoad transformers
/// that run whenever SPT builds the loot data, instead of editing the database once
/// </summary>
[Injectable(InjectionType.Singleton)]
public class LootInjectionService(
    LocationTable locationTable,
    TemplateTable templateTable,
    PresetHelper presetHelper,
    ICloner cloner,
    ConfigService configService,
    ItemCatalogService catalog,
    IServiceProvider services,
    LocationConfig locationConfig,
    ItemFilterService itemFilterService,
    SeasonalEventService seasonalEventService,
    ISptLogger<LootInjectionService> logger
)
{
    private const double MaxChance = 90;

    [ThreadStatic]
    private static bool _bypass;

    /// <summary>Lets the simulation inject an unsaved entry without touching the live config</summary>
    [ThreadStatic]
    private static LootEntry? _simEntry;

    [ThreadStatic]
    private static bool? _simCompensate;

    private static readonly SemaphoreSlim SimLock = new(1, 1);

    private Dictionary<string, double> _baseLoose = [];
    private Dictionary<string, double> _baseStatic = [];

    private readonly ConcurrentDictionary<string, int> _loggedRevision = new();
    private readonly Dictionary<string, Location> _maps = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, Location> Maps => _maps;

    /// <summary>Called before Lots of Loot applies its preset (PostLoad + 99999), so these are the multipliers it replaces</summary>
    public void SnapshotBaseMultipliers()
    {
        _baseLoose = new Dictionary<string, double>(locationConfig.LooseLootMultiplier, StringComparer.OrdinalIgnoreCase);
        _baseStatic = new Dictionary<string, double>(locationConfig.StaticLootMultiplier, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Runs after other mods so their items are known and their weight rounding (Lots of Loot) happens before ours</summary>
    public void Register()
    {

        foreach ((string key, Location location) in locationTable.GetDictionary())
        {
            if (location is null || (location.StaticLoot is null && location.LooseLoot is null))
            {
                continue;
            }

            string mapId = (location.Base?.Id ?? key).ToLowerInvariant();
            if (mapId is "hideout" or "privatearea" or "develop" or "suburbs" or "terminal" or "town")
            {
                continue;
            }

            _maps[mapId] = location;

            location.StaticLoot?.AddTransformer(data =>
            {
                if (!_bypass && data is not null)
                {
                    SafeRun(mapId, "static", () => ApplyStatic(mapId, data));
                }

                return data;
            });

            location.LooseLoot?.AddTransformer(data =>
            {
                if (!_bypass && data is not null)
                {
                    SafeRun(mapId, "loose", () => ApplyLoose(mapId, data));
                }

                return data;
            });
        }

        logger.Info($"{ConfigService.LogPrefix} " + configService.L($"Ganchos de loot registrados em {_maps.Count} mapas", $"Loot hooks registered on {_maps.Count} maps"));
    }

    public void InvalidateCaches()
    {
        foreach (var location in _maps.Values)
        {
            InvokeClear(location.StaticLoot);
            InvokeClear(location.LooseLoot);
        }
    }

    private static void InvokeClear(object? lazyLoad)
    {
        lazyLoad?.GetType().GetMethod("Clear", Type.EmptyTypes)?.Invoke(lazyLoad, null);
    }

    private void SafeRun(string mapId, string kind, Func<(int containers, int points, List<string> summary)> action)
    {
        try
        {
            var (containers, points, summary) = action();
            if (_simEntry is not null)
            {
                return;
            }

            var cfg = configService.Config;
            var logKey = $"{mapId}:{kind}";

            // Other mods (e.g. quest or loot browsers) read every map, so the per-map summary is debug only
            if (summary.Count > 0 && cfg.Debug && _loggedRevision.GetValueOrDefault(logKey) != configService.Revision)
            {
                _loggedRevision[logKey] = configService.Revision;
                logger.Info($"{ConfigService.LogPrefix} {MapName(mapId)} ({kind}): {string.Join(", ", summary)}");
            }
        }
        catch (Exception ex)
        {
            logger.Error($"{ConfigService.LogPrefix} " + configService.L($"Erro ao injetar loot {kind} em {mapId}", $"Failed to inject {kind} loot on {mapId}") + $": {ex.Message}");
        }
    }

    private string MapName(string id) => ItemCatalogService.MapName(id, configService.IsPortuguese);

    private IEnumerable<LootEntry> EntriesFor(string mapId)
    {
        if (_simEntry is { } sim)
        {
            return catalog.Exists(sim.Tpl) && (sim.Maps.Count == 0 || sim.Maps.Contains(mapId, StringComparer.OrdinalIgnoreCase))
                ? [sim]
                : [];
        }

        var cfg = configService.Config;
        if (!cfg.Enabled)
        {
            return [];
        }

        return cfg.Entries.Where(e =>
            e.Enabled
            && catalog.Exists(e.Tpl)
            && (e.Maps.Count == 0 || e.Maps.Any(m => m.Equals(mapId, StringComparison.OrdinalIgnoreCase)))
        );
    }

    private static double ClampChance(double chance) => Math.Clamp(chance, 0.001, MaxChance);

    private (int, int, List<string>) ApplyStatic(string mapId, Dictionary<MongoId, StaticLootDetails> data)
    {
        var entries = EntriesFor(mapId)
            .Where(e => e.Static.Enabled && e.Static.ChancePercent > 0)
            .Select(e => (entry: e, item: catalog.Get(e.Tpl)!, chance: ClampChance(e.Static.ChancePercent / (Compensate ? StaticRatio(mapId) : 1))))
            .ToList();
        var summary = new List<string>();
        if (entries.Count == 0)
        {
            return (0, 0, summary);
        }

        var addedPerEntry = new int[entries.Count];

        foreach ((MongoId containerId, StaticLootDetails details) in data)
        {
            var pool = details?.ItemDistribution?.ToList();
            if (pool is null || pool.Count == 0)
            {
                continue;
            }

            // Weights are computed against the untouched pool so entries don't influence each other
            var additions = new Dictionary<MongoId, double>();
            for (int i = 0; i < entries.Count; i++)
            {
                var (entry, item, chance) = entries[i];
                var tpl = new MongoId(item.Id);
                if (!StaticMatches(entry.Static, containerId, pool, item) || !Fits(containerId, item))
                {
                    continue;
                }

                double others = pool.Where(d => d.Tpl != tpl).Sum(d => (double)(d.RelativeProbability ?? 0));
                if (others <= 0)
                {
                    continue;
                }

                additions[tpl] = Math.Max(additions.GetValueOrDefault(tpl), others * chance / (100 - chance));
                addedPerEntry[i]++;
            }

            if (additions.Count == 0)
            {
                continue;
            }

            // Pools use whole-number weights and some mods round them again, so a small pool is scaled up
            // as a whole instead of rounding a tiny weight to 0 or up to 1
            double total = pool.Sum(d => (double)(d.RelativeProbability ?? 0));
            double scale = Math.Ceiling(10 / additions.Values.Min());
            scale = Math.Clamp(scale, 1, Math.Max(1, Math.Floor(1e7 / Math.Max(1, total + additions.Values.Sum()))));

            if (scale > 1)
            {
                foreach (var dist in pool)
                {
                    dist.RelativeProbability = (float)Math.Round((dist.RelativeProbability ?? 0) * scale);
                }
            }

            foreach ((MongoId tpl, double weight) in additions)
            {
                float scaled = (float)Math.Max(1, Math.Round(weight * scale));
                var existing = pool.FirstOrDefault(d => d.Tpl == tpl);
                if (existing is not null)
                {
                    existing.RelativeProbability = Math.Max(existing.RelativeProbability ?? 0, scaled);
                }
                else
                {
                    pool.Add(new ItemDistribution { Tpl = tpl, RelativeProbability = scaled, ExtensionData = [] });
                }
            }

            details.ItemDistribution = pool;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            summary.Add($"{entries[i].item.Name} → {addedPerEntry[i]} " + configService.L("tipos de contêiner", "container types"));
        }

        return (addedPerEntry.Sum(), 0, summary);
    }

    private bool StaticMatches(StaticLootSettings settings, MongoId containerId, List<ItemDistribution> pool, CatalogItem item)
    {
        return settings.Mode switch
        {
            TargetMode.All => true,
            TargetMode.Selected => settings.Containers.Any(c => c.Equals(containerId.ToString(), StringComparison.OrdinalIgnoreCase)),
            _ => PoolHasCategory(pool, TargetCategories(settings.Mode, settings.Categories, item)),
        };
    }

    private bool PoolHasCategory(List<ItemDistribution> pool, HashSet<string> categories)
    {
        return categories.Count > 0 && pool.Any(d => AncestorsOf(d.Tpl).Overlaps(categories));
    }

    private bool Fits(MongoId containerId, CatalogItem item)
    {
        if (!templateTable.Items.TryGetValue(containerId, out var container) || container.Properties?.Grids is null)
        {
            return true;
        }

        foreach (var grid in container.Properties.Grids)
        {
            int h = grid.Properties?.CellsH ?? 0;
            int v = grid.Properties?.CellsV ?? 0;
            if ((h >= item.Width && v >= item.Height) || (h >= item.Height && v >= item.Width))
            {
                return true;
            }
        }

        return false;
    }

    private string ParentOf(MongoId tpl)
    {
        return templateTable.Items.TryGetValue(tpl, out var t) ? t.Parent.ToString() : string.Empty;
    }

    private (int, int, List<string>) ApplyLoose(string mapId, LooseLoot data)
    {
        var entries = EntriesFor(mapId).Where(e => e.Loose.Enabled && e.Loose.ChancePercent > 0).ToList();
        var summary = new List<string>();
        if (entries.Count == 0 || data.Spawnpoints is null)
        {
            return (0, 0, summary);
        }

        var spawnpoints = data.Spawnpoints as List<Spawnpoint> ?? data.Spawnpoints.ToList();
        data.Spawnpoints = spawnpoints;
        double looseRatio = LooseRatio(mapId, data.SpawnpointCount?.Mean ?? 0, CountRollableSpawnpoints(spawnpoints));

        var originals = spawnpoints
            .Where(sp => sp.Template?.Items is not null && sp.ItemDistribution is not null && sp.Template.IsAlwaysSpawn != true)
            .Select(sp => (
                sp,
                parents: sp.Template!.Items!.Where(i => i.ComposedKey is not null).SelectMany(i => AncestorsOf(i.Template)).ToHashSet(StringComparer.OrdinalIgnoreCase),
                tpls: sp.Template.Items!.Select(i => i.Template.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase),
                weight: sp.ItemDistribution!.Sum(d => d.RelativeProbability ?? 0)
            ))
            .ToList();

        int total = 0;
        foreach (var entry in entries)
        {
            var item = catalog.Get(entry.Tpl)!;
            double chance = ClampChance(entry.Loose.ChancePercent / (Compensate ? looseRatio : 1));
            string composedKey = $"customlootpool_{item.Id}";
            var targets = TargetCategories(entry.Loose.Mode, entry.Loose.Categories, item);
            int added = 0;

            foreach (var (sp, parents, tpls, weight) in originals)
            {
                if (weight <= 0 || tpls.Contains(item.Id))
                {
                    continue;
                }

                if (entry.Loose.Mode != TargetMode.All && !parents.Overlaps(targets))
                {
                    continue;
                }

                var newItems = BuildLooseItems(item, composedKey);
                sp.Template!.Items = sp.Template.Items!.Concat(newItems).ToList();
                sp.ItemDistribution = sp.ItemDistribution!
                    .Append(new LooseLootItemDistribution
                    {
                        ComposedKey = new ComposedKey { Key = composedKey },
                        RelativeProbability = weight * chance / (100 - chance),
                    })
                    .ToList();
                added++;
            }

            total += added;
            summary.Add($"{item.Name} → {added} " + configService.L("pontos de spawn", "spawn points"));
        }

        return (0, total, summary);
    }

    private List<SptLootItem> BuildLooseItems(CatalogItem item, string composedKey)
    {
        var tpl = new MongoId(item.Id);

        // Weapons / armor need their parts, use the default preset when there is one
        if (presetHelper.HasPreset(tpl) && presetHelper.GetDefaultPreset(tpl) is { Items.Count: > 0 } preset)
        {
            var cloned = cloner.Clone(preset.Items)!.ReplaceIDs().ToList();
            var ids = cloned.Select(i => i.Id.ToString()).ToHashSet();
            var lootItems = cloned.Select(i => i.ToLootItem()).ToList();
            var root = lootItems.FirstOrDefault(i => i.ParentId is null || !ids.Contains(i.ParentId)) ?? lootItems[0];
            root.ParentId = null;
            root.SlotId = null;
            root.ComposedKey = composedKey;
            return lootItems;
        }

        return
        [
            new SptLootItem
            {
                Id = new MongoId(),
                Template = tpl,
                ComposedKey = composedKey,
                Upd = new Upd { StackObjectsCount = 1 },
            },
        ];
    }

    public async Task<List<SimResult>> Simulate(LootEntry entry, int raids, bool compensate, Action<string>? progress = null, CancellationToken ct = default)
    {
        var results = new List<SimResult>();
        var item = catalog.Get(entry.Tpl);
        if (item is null)
        {
            return results;
        }

        await SimLock.WaitAsync(ct);
        try
        {
            var generator = (LocationLootGenerator)services.GetService(typeof(LocationLootGenerator))!;
            var sim = configService.CloneConfigOf(new LootPoolConfig { Entries = [entry] }).Entries[0];
            sim.Enabled = true;
            var tpl = new MongoId(item.Id);

            var maps = _maps
                .Where(m => entry.Maps.Count == 0 || entry.Maps.Contains(m.Key, StringComparer.OrdinalIgnoreCase))
                .OrderBy(m => MapName(m.Key))
                .ToList();

            foreach ((string mapId, Location location) in maps)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Invoke(MapName(mapId));

                var (st, lo, error) = await Task.Run(() =>
                {
                    int staticCount = 0, looseCount = 0;
                    _simEntry = sim;
                    _simCompensate = compensate;
                    try
                    {
                        for (int i = 0; i < raids; i++)
                        {
                            var ammo = cloner.Clone(location.StaticAmmo) ?? [];
                            if (sim.Static.Enabled)
                            {
                                staticCount += generator.GenerateStaticContainers(mapId, ammo)
                                    .Sum(sp => sp.Items?.Count(it => it.Template == tpl) ?? 0);
                            }

                            if (sim.Loose.Enabled && location.LooseLoot?.Value is { } loose)
                            {
                                looseCount += generator.GenerateDynamicLoot(loose, ammo, mapId)
                                    .Sum(sp => sp.Items?.Count(it => it.Template == tpl) ?? 0);
                            }
                        }

                        return (staticCount, looseCount, (string?)null);
                    }
                    catch (Exception ex)
                    {
                        // Other loot generators can throw on maps they don't know (e.g. Lots of Loot on Icebreaker)
                        logger.Warning($"{ConfigService.LogPrefix} Simulation skipped {mapId}: {ex.GetType().Name}: {ex.Message}");
                        return (0, 0, (string?)ex.Message);
                    }
                    finally
                    {
                        _simEntry = null;
                        _simCompensate = null;
                    }
                }, ct);

                results.Add(new SimResult(mapId, MapName(mapId), raids, (double)st / raids, (double)lo / raids, error));
            }
        }
        finally
        {
            SimLock.Release();
        }

        return results;
    }

    /// <summary>Computed on the data before injection, otherwise the item itself would make every pool look "similar"</summary>
    public async Task<List<MapReach>> Preview(LootEntry entry)
    {
        await EnsurePoolIndex();
        var index = _poolIndex!.Result;
        var result = new List<MapReach>();
        if (catalog.Get(entry.Tpl) is not { } item)
        {
            return result;
        }

        var staticTargets = TargetCategories(entry.Static.Mode, entry.Static.Categories, item);
        var looseTargets = TargetCategories(entry.Loose.Mode, entry.Loose.Categories, item);

        foreach (string mapId in _maps.Keys.OrderBy(MapName))
        {
            if (entry.Maps.Count > 0 && !entry.Maps.Contains(mapId, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var containers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (entry.Static.Enabled)
            {
                var onMap = index.MapContainers.GetValueOrDefault(mapId) ?? [];
                IEnumerable<string> candidates = entry.Static.Mode switch
                {
                    TargetMode.All => onMap,
                    TargetMode.Selected => entry.Static.Containers.Where(onMap.Contains),
                    _ => staticTargets.SelectMany(c => (index.StaticContainers.GetValueOrDefault(c) ?? [])
                        .Where(kv => kv.Value.Contains(mapId))
                        .Select(kv => kv.Key)),
                };
                containers.UnionWith(candidates.Where(id => Fits(new MongoId(id), item)));
            }

            int points = 0;
            if (entry.Loose.Enabled)
            {
                if (entry.Loose.Mode == TargetMode.All)
                {
                    points = index.LooseSpotCount.GetValueOrDefault(mapId);
                }
                else
                {
                    var union = new HashSet<int>();
                    foreach (var category in looseTargets)
                    {
                        if (index.LooseSpots.GetValueOrDefault(category)?.GetValueOrDefault(mapId) is { } set)
                        {
                            union.UnionWith(set);
                        }
                    }

                    points = union.Count;
                }
            }

            var names = containers.Select(catalog.NameOf).Distinct().OrderBy(n => n).ToList();
            result.Add(new MapReach(mapId, MapName(mapId), containers.Count, names, points));
        }

        return result;
    }

    private List<(string Id, string Name)>? _containerCache;

    public async Task<List<(string Id, string Name)>> AllContainers()
    {
        await EnsurePoolIndex();
        return _containerCache ??= _poolIndex!.Result.MapContainers.Values
            .SelectMany(ids => ids)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => (id, catalog.NameOf(id)))
            .OrderBy(c => c.Item2)
            .ToList();
    }

    private bool Compensate => _simCompensate ?? configService.Config.CompensateLootMultipliers;

    public static bool LotsOfLootInstalled { get; } =
        AppDomain.CurrentDomain.GetAssemblies().Any(a => string.Equals(a.GetName().Name, "LotsofLoot", StringComparison.OrdinalIgnoreCase));

    private static double Multiplier(Dictionary<string, double> source, string mapId)
    {
        if (source.TryGetValue(mapId, out var value) || source.TryGetValue("default", out value))
        {
            return value;
        }

        return 1;
    }

    private static int CountRollableSpawnpoints(IEnumerable<Spawnpoint> spawnpoints)
    {
        return spawnpoints.Count(sp => sp.Template?.IsAlwaysSpawn != true && (sp.Probability ?? 0) is > 0 and < 1);
    }

    public double StaticRatio(string mapId)
    {
        double baseValue = Multiplier(_baseStatic, mapId);
        return baseValue > 0 ? Multiplier(locationConfig.StaticLootMultiplier, mapId) / baseValue : 1;
    }

    /// <summary>
    /// Loose loot can't pick more spots than the map has, so a x50 multiplier usually means "every spot" rather than 50x the items
    /// </summary>
    public double LooseRatio(string mapId, double mean, int points)
    {
        if (mean <= 0 || points <= 0)
        {
            return 1;
        }

        double current = Math.Min(Multiplier(locationConfig.LooseLootMultiplier, mapId) * mean, points);
        double baseline = Math.Min(Multiplier(_baseLoose, mapId) * mean, points);
        return baseline > 0 ? current / baseline : 1;
    }

    public List<MultiplierInfo>? GetMultipliers()
    {
        if (!PoolIndexReady)
        {
            return null;
        }

        var stats = _poolIndex!.Result.LooseStats;
        return _maps.Keys
            .OrderBy(MapName)
            .Select(map =>
            {
                var (mean, points) = stats.GetValueOrDefault(map);
                return new MultiplierInfo(map, MapName(map), StaticRatio(map), LooseRatio(map, mean, points));
            })
            .ToList();
    }

    public bool MultipliersChanged() => GetMultipliers()?.Any(m => Math.Abs(m.Static - 1) > 0.01 || Math.Abs(m.Loose - 1) > 0.01) ?? false;

    public List<ItemWarning> GetItemWarnings(string tplId)
    {
        var warnings = new List<ItemWarning>();
        if (catalog.Get(tplId) is null)
        {
            return warnings;
        }

        var tpl = new MongoId(tplId);

        if (itemFilterService.IsLootableItemBlacklisted(tpl))
        {
            warnings.Add(new(
                "Bloqueado pela blacklist de loot do servidor (configs/item.json → lootableItemBlacklist): nunca vai spawnar.",
                "Blocked by the server loot blacklist (configs/item.json → lootableItemBlacklist): it will never spawn.",
                true));
        }

        if (!seasonalEventService.SeasonalEventEnabled() && seasonalEventService.GetInactiveSeasonalEventItems().Contains(tpl))
        {
            warnings.Add(new(
                "Item de evento sazonal: o servidor só deixa spawnar enquanto o evento estiver ativo.",
                "Seasonal event item: the server only lets it spawn while its event is active.",
                true));
        }

        if (itemFilterService.IsItemBlacklisted(tpl))
        {
            warnings.Add(new(
                "Está na blacklist geral de itens do servidor (itens sem uso ou quebrados). Pode não funcionar no jogo.",
                "On the server's general item blacklist (unused or broken items). It may not work in game.",
                false));
        }

        if (templateTable.Items.TryGetValue(tpl, out var template) && template.IsQuestItem())
        {
            warnings.Add(new(
                "Item de quest: fora da quest dele pode se comportar de forma estranha.",
                "Quest item: outside its quest it may behave oddly.",
                false));
        }

        var limited = locationConfig.LootMaxSpawnLimits
            .Where(kv => kv.Value is not null && kv.Value.TryGetValue(tpl, out _))
            .Select(kv => $"{MapName(kv.Key)} ({kv.Value[tpl]})")
            .ToList();
        if (limited.Count > 0)
        {
            warnings.Add(new(
                $"O servidor limita quantos spawnam por raid em: {string.Join(", ", limited)}.",
                $"The server caps how many spawn per raid on: {string.Join(", ", limited)}.",
                false));
        }

        return warnings;
    }

    private const string RootItemId = "54009119af1c881c07000029";

    private readonly ConcurrentDictionary<MongoId, HashSet<string>> _ancestors = new();

    /// <summary>Every category above the item (direct parent first), without the generic root "Item"</summary>
    public HashSet<string> AncestorsOf(MongoId tpl)
    {
        return _ancestors.GetOrAdd(tpl, t =>
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = t;
            for (int depth = 0; depth < 32 && templateTable.Items.TryGetValue(current, out var template); depth++)
            {
                var parent = template.Parent.ToString();
                if (string.IsNullOrEmpty(parent) || parent == RootItemId || !result.Add(parent))
                {
                    break;
                }

                current = template.Parent;
            }

            return result;
        });
    }

    private HashSet<string> TargetCategories(TargetMode mode, List<string> categories, CatalogItem item)
    {
        return mode == TargetMode.Categories
            ? categories.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { item.ParentId };
    }

    private sealed class ItemStat
    {
        public double Share { get; set; }
        public int Pools { get; set; }
        public HashSet<string> Places { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static void AddStat(Dictionary<string, ItemStat> bucket, string tpl, double share, string place)
    {
        if (!bucket.TryGetValue(tpl, out var stat))
        {
            stat = new ItemStat();
            bucket[tpl] = stat;
        }

        stat.Share += share;
        stat.Pools++;
        stat.Places.Add(place);
    }

    private sealed class PoolIndex
    {
        public Dictionary<string, Dictionary<string, HashSet<string>>> StaticContainers { get; } = [];
        // Summed share of the pools each item appears in, used to rank the examples
        public Dictionary<string, Dictionary<string, ItemStat>> StaticItems { get; } = [];
        public Dictionary<string, Dictionary<string, HashSet<int>>> LooseSpots { get; } = [];
        public Dictionary<string, Dictionary<string, ItemStat>> LooseItems { get; } = [];
        public Dictionary<string, (double Mean, int Points)> LooseStats { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, HashSet<string>> MapContainers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> LooseSpotCount { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private Task<PoolIndex>? _poolIndex;
    private readonly Lock _poolIndexLock = new();

    public bool PoolIndexReady => _poolIndex is { IsCompletedSuccessfully: true };

    public Task EnsurePoolIndex()
    {
        lock (_poolIndexLock)
        {
            return _poolIndex ??= Task.Run(BuildPoolIndex);
        }
    }

    private PoolIndex BuildPoolIndex()
    {
        var index = new PoolIndex();
        _bypass = true;
        try
        {
            foreach ((string mapId, Location location) in _maps)
            {
                foreach ((MongoId containerId, StaticLootDetails details) in RawValue(location.StaticLoot) ?? [])
                {
                    var pool = details?.ItemDistribution?.ToList() ?? [];
                    double total = pool.Sum(d => (double)(d.RelativeProbability ?? 0));
                    if (total <= 0)
                    {
                        continue;
                    }

                    if (!index.MapContainers.TryGetValue(mapId, out var mapContainers))
                    {
                        mapContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        index.MapContainers[mapId] = mapContainers;
                    }

                    mapContainers.Add(containerId.ToString());

                    foreach (var dist in pool)
                    {
                        foreach (string category in AncestorsOf(dist.Tpl))
                        {
                            var containers = Bucket(index.StaticContainers, category);
                            if (!containers.TryGetValue(containerId.ToString(), out var maps))
                            {
                                maps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                containers[containerId.ToString()] = maps;
                            }

                            maps.Add(mapId);
                            AddStat(Bucket(index.StaticItems, category), dist.Tpl.ToString(), (dist.RelativeProbability ?? 0) / total, containerId.ToString());
                        }
                    }
                }

                var looseLoot = RawValue(location.LooseLoot);
                var allSpawnpoints = looseLoot?.Spawnpoints?.ToList() ?? [];
                index.LooseStats[mapId] = (looseLoot?.SpawnpointCount?.Mean ?? 0, CountRollableSpawnpoints(allSpawnpoints));

                for (int spIndex = 0; spIndex < allSpawnpoints.Count; spIndex++)
                {
                    var sp = allSpawnpoints[spIndex];
                    if (sp.Template?.Items is null || sp.ItemDistribution is null || sp.Template.IsAlwaysSpawn == true)
                    {
                        continue;
                    }

                    index.LooseSpotCount[mapId] = index.LooseSpotCount.GetValueOrDefault(mapId) + 1;

                    var weights = sp.ItemDistribution
                        .Where(d => d.ComposedKey?.Key is not null)
                        .GroupBy(d => d.ComposedKey!.Key!)
                        .ToDictionary(g => g.Key, g => g.Sum(d => d.RelativeProbability ?? 0));
                    double total = weights.Values.Sum();
                    var roots = sp.Template.Items.Where(i => i.ComposedKey is not null).ToList();

                    foreach (var root in roots)
                    {
                        foreach (string category in AncestorsOf(root.Template))
                        {
                            var spots = Bucket(index.LooseSpots, category);
                            if (!spots.TryGetValue(mapId, out var set))
                            {
                                set = [];
                                spots[mapId] = set;
                            }

                            set.Add(spIndex);

                            if (total > 0)
                            {
                                AddStat(Bucket(index.LooseItems, category), root.Template.ToString(), weights.GetValueOrDefault(root.ComposedKey!) / total, $"{mapId}:{spIndex}");
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            _bypass = false;
        }

        return index;
    }

    /// <summary>
    /// Builds the value with only SPT's own transformers, so mods that log on every read (Lots of Loot) stay quiet
    /// </summary>
    private static T? RawValue<T>(LazyLoad<T>? lazy) where T : class
    {
        if (lazy is null)
        {
            return null;
        }

        try
        {
            var fields = typeof(LazyLoad<T>).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
            var deserialize = fields.FirstOrDefault(f => f.FieldType == typeof(Func<T>))?.GetValue(lazy) as Func<T>;
            var transformers = fields.FirstOrDefault(f => f.FieldType == typeof(List<Func<T?, T?>>))?.GetValue(lazy) as List<Func<T?, T?>>;
            if (deserialize is not null && transformers is not null)
            {
                var spt = typeof(LazyLoad<T>).Assembly;
                T? value = deserialize();
                foreach (var transformer in transformers.ToList())
                {
                    if (transformer.Method.DeclaringType?.Assembly == spt)
                    {
                        value = transformer(value);
                    }
                }

                return value;
            }
        }
        catch
        {
        }

        return lazy.Value;
    }

    private static Dictionary<string, T> Bucket<T>(Dictionary<string, Dictionary<string, T>> source, string key)
    {
        if (!source.TryGetValue(key, out var bucket))
        {
            bucket = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            source[key] = bucket;
        }

        return bucket;
    }

    private static void Add(Dictionary<string, double> bucket, string key, double value) => bucket[key] = bucket.GetValueOrDefault(key) + value;

    public List<PoolCategory>? GetPoolCategories()
    {
        if (!PoolIndexReady)
        {
            return null;
        }

        var index = _poolIndex!.Result;
        return index.StaticItems.Keys
            .Union(index.LooseItems.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(id => new PoolCategory(
                id,
                catalog.CategoryName(id),
                CategoryPath(id),
                (index.StaticItems.GetValueOrDefault(id)?.Keys ?? Enumerable.Empty<string>())
                    .Union(index.LooseItems.GetValueOrDefault(id)?.Keys ?? Enumerable.Empty<string>())
                    .Count()))
            .OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string CategoryPath(string id)
    {
        var chain = AncestorsOf(new MongoId(id)).Reverse().Select(catalog.CategoryName).Append(catalog.CategoryName(id));
        return string.Join(" > ", chain);
    }

    public SimilarPool? GetSimilarPool(LootEntry entry, bool loose)
    {
        if (!PoolIndexReady || catalog.Get(entry.Tpl) is not { } item)
        {
            return null;
        }

        var index = _poolIndex!.Result;
        bool MapAllowed(string map) => entry.Maps.Count == 0 || entry.Maps.Contains(map, StringComparer.OrdinalIgnoreCase);
        var targets = loose
            ? TargetCategories(entry.Loose.Mode, entry.Loose.Categories, item)
            : TargetCategories(entry.Static.Mode, entry.Static.Categories, item);

        // A parent and a child category can both be selected, the same item's stats are only counted once
        var stats = new Dictionary<string, ItemStat>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in targets)
        {
            foreach (var (tpl, stat) in (loose ? index.LooseItems : index.StaticItems).GetValueOrDefault(category) ?? [])
            {
                stats.TryAdd(tpl, stat);
            }
        }

        var others = stats
            .Where(kv => !kv.Key.Equals(item.Id, StringComparison.OrdinalIgnoreCase) && kv.Value.Pools > 0)
            .OrderByDescending(kv => kv.Value.Share)
            .Select(kv => catalog.Get(kv.Key) is { } other
                ? new PoolItem(other, kv.Value.Share / kv.Value.Pools * 100, kv.Value.Places.Count)
                : null)
            .OfType<PoolItem>()
            .ToList();

        var containers = new List<string>();
        int spots = 0;
        if (loose)
        {
            foreach (var map in _maps.Keys.Where(MapAllowed))
            {
                var union = new HashSet<int>();
                foreach (var category in targets)
                {
                    if (index.LooseSpots.GetValueOrDefault(category)?.GetValueOrDefault(map) is { } set)
                    {
                        union.UnionWith(set);
                    }
                }

                spots += union.Count;
            }
        }
        else
        {
            containers = targets
                .SelectMany(category => index.StaticContainers.GetValueOrDefault(category) ?? [])
                .Where(kv => kv.Value.Any(MapAllowed) && Fits(new MongoId(kv.Key), item))
                .Select(kv => catalog.NameOf(kv.Key))
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        string categoryName = string.Join(", ", targets.Select(catalog.CategoryName).OrderBy(n => n));
        return new SimilarPool(categoryName, others, containers, spots);
    }
}
