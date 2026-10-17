using System.Reflection;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using SPTarkov.Server.Core.Helpers.InRaid;
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

public sealed record SimResult(string MapId, string MapName, int Raids, double StaticPerRaid, double LoosePerRaid, string? Error = null, HeatSample? Spatial = null);

public sealed record ItemWarning(string Pt, string En, bool Blocking);

public sealed record MultiplierInfo(string MapId, string MapName, double Static, double Loose)
{
    public bool Changed => Math.Abs(Static - 1) > 0.01 || Math.Abs(Loose - 1) > 0.01;
}

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
    private static List<LootEntry>? _simEntries;

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

    private void SafeRun(string mapId, string kind, Func<List<string>> action)
    {
        try
        {
            var summary = action();
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
        if (_simEntries is { } simulated)
        {
            return simulated.Where(entry => catalog.Exists(entry.Tpl) && entry.AllowsMap(mapId)
                && (ReferenceEquals(entry, _simEntry) || (entry.Enabled && IsActiveNow(entry.Schedule))));
        }

        var cfg = configService.Config;
        if (!cfg.Enabled)
        {
            return [];
        }

        return cfg.Entries.Where(e =>
            e.Enabled
            && catalog.Exists(e.Tpl)
            && e.AllowsMap(mapId)
            && IsActiveNow(e.Schedule)
        );
    }

    public static bool IsActiveNow(ScheduleSettings schedule) => IsActiveOn(schedule, DateTime.Now);

    public static bool IsActiveOn(ScheduleSettings schedule, DateTime date)
    {
        switch (schedule.Mode)
        {
            case ScheduleMode.Weekends:
                return date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            case ScheduleMode.Dates:
                if (!TryParseDay(schedule.From, out int from) || !TryParseDay(schedule.To, out int to))
                {
                    return true;
                }

                int today = date.Month * 100 + date.Day;
                return from <= to ? today >= from && today <= to : today >= from || today <= to;
            default:
                return true;
        }
    }

    private static bool TryParseDay(string? value, out int day)
    {
        day = 0;
        var parts = value?.Split('-', '/');
        if (parts is not { Length: 2 } || !int.TryParse(parts[0], out int month) || !int.TryParse(parts[1], out int dom)
            || month is < 1 or > 12 || dom is < 1 or > 31)
        {
            return false;
        }

        day = month * 100 + dom;
        return true;
    }

    public static double ChanceFor(double fallback, Dictionary<string, double>? perMap, string mapId)
    {
        return perMap is not null && perMap.TryGetValue(mapId, out double chance) ? chance : fallback;
    }

    private static double ClampChance(double chance) => Math.Clamp(chance, 0.001, MaxChance);

    private List<string> ApplyStatic(string mapId, Dictionary<MongoId, StaticLootDetails> data)
    {
        var entries = EntriesFor(mapId)
            .Where(e => e.Static.Enabled && ChanceFor(e.Static.ChancePercent, e.Static.MapChances, mapId) > 0)
            .Select(e => (entry: e, item: catalog.Get(e.Tpl)!, chance: ClampChance(ChanceFor(e.Static.ChancePercent, e.Static.MapChances, mapId) / (Compensate ? StaticRatio(mapId) : 1))))
            .ToList();
        if (entries.Count == 0) return [];
        var counts = new int[entries.Count];
        foreach (var (containerId, details) in data)
        {
            if (details?.ItemDistribution is null) continue;
            var pool = details.ItemDistribution.ToList();
            if (pool.Count == 0) continue;
            var requested = new List<KeyValuePair<MongoId, double>>();
            for (int i = 0; i < entries.Count; i++)
            {
                var (entry, item, chance) = entries[i];
                if (!StaticMatches(entry.Static, containerId, pool, item) || !Fits(containerId, item)) continue;
                requested.Add(new(new MongoId(item.Id), chance));
                counts[i]++;
            }
            if (requested.Count == 0) continue;
            var original = pool.GroupBy(item => item.Tpl).ToDictionary(group => group.Key, group => group.Sum(item => (double)(item.RelativeProbability ?? 0)));
            var weights = PoolWeights.Calculate(original, requested);
            details.ItemDistribution = weights.Where(pair => pair.Value > 0).Select(pair =>
            {
                var distribution = pool.FirstOrDefault(item => item.Tpl == pair.Key)
                    ?? new ItemDistribution { Tpl = pair.Key, ExtensionData = [] };
                distribution.RelativeProbability = (float)Math.Max(1, Math.Round(pair.Value * 100000));
                return distribution;
            }).ToList();
        }
        return entries.Select((entry, i) => $"{entry.item.Name} → {counts[i]} " + configService.L("tipos de contêiner", "container types")).ToList();
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

    private List<string> ApplyLoose(string mapId, LooseLoot data)
    {
        var entries = EntriesFor(mapId).Where(e => (e.Loose.Enabled && ChanceFor(e.Loose.ChancePercent, e.Loose.MapChances, mapId) > 0) || e.Guaranteed).ToList();
        var summary = new List<string>();
        if (entries.Count == 0 || data.Spawnpoints is null) return summary;
        var spawnpoints = data.Spawnpoints.ToList();
        data.Spawnpoints = spawnpoints;
        double ratio = LooseRatio(mapId, data.SpawnpointCount?.Mean ?? 0, CountRollableSpawnpoints(spawnpoints));
        var forced = new List<Spawnpoint>();
        var originals = spawnpoints.Where(sp => sp.Template?.Items is not null && sp.ItemDistribution is not null && sp.Template.IsAlwaysSpawn != true && sp.ItemDistribution.Sum(item => item.RelativeProbability ?? 0) > 0)
            .Select(sp => (sp, parents: sp.Template!.Items!.Where(item => item.ComposedKey is not null).SelectMany(item => AncestorsOf(item.Template)).ToHashSet(StringComparer.OrdinalIgnoreCase)))
            .ToList();
        foreach (var entry in entries.Where(entry => entry.Guaranteed).GroupBy(entry => entry.Tpl).Select(group => group.First()))
        {
            var item = catalog.Get(entry.Tpl)!;
            var mode = entry.Loose.Enabled ? entry.Loose.Mode : TargetMode.Similar;
            var categories = TargetCategories(mode, entry.Loose.Categories, item);
            var matching = originals.Where(o => mode == TargetMode.All || o.parents.Overlaps(categories)).ToList();
            if (matching.Count == 0) matching = originals;
            forced.AddRange(matching.Select((point, i) => BuildForcedSpawnpoint(point.sp, item, i)));
            summary.Add($"{item.Name} → 1 " + configService.L("garantido", "guaranteed"));
        }
        int count = 0;
        foreach (var (sp, parents) in originals)
        {
            var matching = entries.Where(entry => entry.Loose.Enabled && ChanceFor(entry.Loose.ChancePercent, entry.Loose.MapChances, mapId) > 0
                && (entry.Loose.Mode == TargetMode.All || parents.Overlaps(TargetCategories(entry.Loose.Mode, entry.Loose.Categories, catalog.Get(entry.Tpl)!))))
                .GroupBy(entry => entry.Tpl).ToDictionary(group => group.Key, group => ClampChance(group.Max(entry => ChanceFor(entry.Loose.ChancePercent, entry.Loose.MapChances, mapId)) / (Compensate ? ratio : 1)));
            if (matching.Count == 0) continue;
            var roots = sp.Template!.Items!.Where(item => item.ComposedKey is not null).GroupBy(item => item.ComposedKey!).ToDictionary(group => group.Key, group => group.First());
            var distributions = sp.ItemDistribution!.Where(item => item.ComposedKey?.Key is not null && roots.ContainsKey(item.ComposedKey.Key)).ToList();
            var original = distributions.GroupBy(item => roots[item.ComposedKey!.Key!].Template.ToString())
                .ToDictionary(group => group.Key, group => group.Sum(item => item.RelativeProbability ?? 0));
            if (original.Values.Sum() <= 0) continue;
            var weights = PoolWeights.Calculate(original, matching);
            foreach (var (tpl, chance) in matching)
            {
                if (original.ContainsKey(tpl)) continue;
                string key = $"customlootpool_{tpl}";
                var items = BuildLooseItems(catalog.Get(tpl)!, key);
                sp.Template.Items = sp.Template.Items!.Concat(items).ToList();
                roots[key] = items.First(item => item.ComposedKey == key);
                distributions.Add(new LooseLootItemDistribution { ComposedKey = new ComposedKey { Key = key }, RelativeProbability = 1 });
            }
            foreach (var group in distributions.GroupBy(item => roots[item.ComposedKey!.Key!].Template.ToString()))
            {
                double total = group.Sum(item => item.RelativeProbability ?? 0);
                int alternatives = group.Count();
                foreach (var item in group)
                    item.RelativeProbability = weights[group.Key] * (total > 0 ? (item.RelativeProbability ?? 0) / total : 1.0 / alternatives);
            }
            sp.ItemDistribution = distributions.Where(item => item.RelativeProbability > 0).ToList();
            count++;
        }
        if (forced.Count > 0) data.SpawnpointsForced = (data.SpawnpointsForced ?? []).Concat(forced).ToList();
        if (count > 0) summary.Add($"{count} " + configService.L("pontos de spawn ajustados", "spawn points adjusted"));
        return summary;
    }
    /// <summary>
    /// SPT spawns one forced spawnpoint per template id, drawn among entries sharing that id,
    /// so every candidate spot gets the same id and the item lands on one of them each raid
    /// </summary>
    private Spawnpoint BuildForcedSpawnpoint(Spawnpoint source, CatalogItem item, int index)
    {
        string id = $"customlootpool_guaranteed_{item.Id}";
        var template = cloner.Clone(source.Template)!;
        var items = BuildLooseItems(item, id);
        var root = items.First(i => i.ComposedKey == id);
        items.Remove(root);
        items.Insert(0, root);

        template.Id = id;
        template.IsAlwaysSpawn = true;
        template.Root = root.Id.ToString();
        template.Items = items;

        return new Spawnpoint
        {
            LocationId = $"{source.LocationId}_clp{index}",
            Probability = 1,
            Template = template,
            ItemDistribution = [],
        };
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

    public async Task<List<SimResult>> Simulate(LootEntry entry, int raids, bool compensate, Action<string>? progress = null, CancellationToken ct = default, LootPoolConfig? context = null, string? spatialMap = null)
    {
        if (raids is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(raids));
        var results = new List<SimResult>();
        var item = catalog.Get(entry.Tpl);
        if (item is null)
        {
            return results;
        }

        await SimLock.WaitAsync(ct);
        try
        {
            using var scope = services.CreateScope();
            // Explicitly pair a fresh generator with its own counter; never change the live raid's counter.
            var counter = new CounterTrackerHelper();
            var resolved = scope.ServiceProvider.GetRequiredService<LocationLootGenerator>();
            var generator = (LocationLootGenerator)ActivatorUtilities.CreateInstance(scope.ServiceProvider, resolved.GetType(), counter);
            var simulationConfig = configService.CloneConfigOf(context ?? new LootPoolConfig { Entries = [entry] });
            int index = context?.Entries.IndexOf(entry) ?? 0;
            if (index < 0) { simulationConfig.Entries.Add(ConfigService.Clone(entry)); index = simulationConfig.Entries.Count - 1; }
            var sim = simulationConfig.Entries[index];
            sim.Enabled = true;
            var tpl = new MongoId(item.Id);

            var maps = _maps
                .Where(m => entry.AllowsMap(m.Key))
                .Where(m => spatialMap is null || m.Key.Equals(spatialMap, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => MapName(m.Key))
                .ToList();

            foreach ((string mapId, Location location) in maps)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Invoke(MapName(mapId));

                var spatial = new Dictionary<(double X, double Y, double Z, string Source), HeatPoint>();
                int missing = 0;
                int missingContainers = 0;
                int CountLoot(SpawnpointTemplate spawn, string source)
                {
                    int count = spawn.Items?.Count(it => it.Template == tpl) ?? 0;
                    if (spatialMap is not null && count > 0)
                    {
                        if (ToHeatPoint(spawn.Position, source, source == "container" ? "Container" : "Loose loot") is { } point)
                        {
                            var key = (Math.Round(point.X, 1), Math.Round(point.Y, 1), Math.Round(point.Z, 1), source);
                            spatial[key] = point with { Weight = (spatial.GetValueOrDefault(key)?.Weight ?? 0) + count };
                        }
                        else { missing += count; if (source == "container") missingContainers += count; }
                    }
                    return count;
                }

                var (st, lo, error) = await Task.Run(() =>
                {
                    int staticCount = 0, looseCount = 0;
                    _simEntry = sim;
                    _simEntries = simulationConfig.Entries;
                    _simCompensate = compensate;
                    try
                    {
                        for (int i = 0; i < raids; i++)
                        {
                            ct.ThrowIfCancellationRequested();
                            var ammo = cloner.Clone(location.StaticAmmo) ?? [];
                            var counts = SimulationRaid.Run(counter, locationConfig.LootMaxSpawnLimits.GetValueOrDefault(mapId),
                                () => generator.GenerateStaticContainers(mapId, ammo).Sum(sp => CountLoot(sp, "container")),
                                () => location.LooseLoot?.Value is { } loose
                                    ? generator.GenerateDynamicLoot(cloner.Clone(loose)!, ammo, mapId).Sum(sp => CountLoot(sp, "loose"))
                                    : 0);
                            staticCount += counts.Static;
                            looseCount += counts.Loose;
                        }

                        return (staticCount, looseCount, (string?)null);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        // Other loot generators can throw on maps they don't know (e.g. Lots of Loot on Icebreaker)
                        logger.Warning($"{ConfigService.LogPrefix} Simulation skipped {mapId}: {ex.GetType().Name}: {ex.Message}");
                        return (0, 0, (string?)ex.Message);
                    }
                    finally
                    {
                        _simEntry = null;
                        _simEntries = null;
                        _simCompensate = null;
                    }
                }, ct);

                results.Add(new SimResult(mapId, MapName(mapId), raids, (double)st / raids, (double)lo / raids, error,
                    spatialMap is null || error is not null ? null : new HeatSample(spatial.Values.ToList(), missing, missingContainers)));
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

        var looseTargets = TargetCategories(entry.Loose.Mode, entry.Loose.Categories, item);

        foreach (string mapId in _maps.Keys.OrderBy(MapName))
        {
            if (!entry.AllowsMap(mapId))
            {
                continue;
            }

            var containers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (entry.Static.Enabled)
            {
                containers = EligibleContainers(index, entry.Static, item, mapId);
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
                    points = SpotsForCategories(index, mapId, looseTargets).Count;
                }
            }

            var names = containers.Select(catalog.NameOf).Distinct().OrderBy(n => n).ToList();
            result.Add(new MapReach(mapId, MapName(mapId), containers.Count, names, points));
        }

        return result;
    }

    private HashSet<string> EligibleContainers(PoolIndex index, StaticLootSettings settings, CatalogItem item, string mapId)
    {
        var onMap = index.MapContainers.GetValueOrDefault(mapId) ?? [];
        IEnumerable<string> candidates = settings.Mode switch
        {
            TargetMode.All => onMap,
            TargetMode.Selected => settings.Containers.Where(onMap.Contains),
            _ => TargetCategories(settings.Mode, settings.Categories, item)
                .SelectMany(category => (index.StaticContainers.GetValueOrDefault(category) ?? [])
                    .Where(pair => pair.Value.Contains(mapId)).Select(pair => pair.Key)),
        };
        return candidates.Where(id => Fits(new MongoId(id), item)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<int> SpotsForCategories(PoolIndex index, string mapId, IEnumerable<string> categories) =>
        categories.SelectMany(category => index.LooseSpots.GetValueOrDefault(category)?.GetValueOrDefault(mapId) ?? []).ToHashSet();

    private static HeatPoint? ToHeatPoint(Vector3? position, string source, string name) =>
        position is { } p && float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)
            // Zero vectors in stock container records are placeholders, not world positions.
            && !(source == "container" && p.X == 0 && p.Y == 0 && p.Z == 0)
            ? new(p.X, p.Y, p.Z, source, name) : null;

    public async Task<HeatSample> HeatLocations(LootEntry entry, string mapId)
    {
        await EnsurePoolIndex();
        if (!_maps.ContainsKey(mapId) || !entry.AllowsMap(mapId) || catalog.Get(entry.Tpl) is not { } item)
            return new([]);
        var index = _poolIndex!.Result;
        var points = new List<HeatPoint>();
        int missing = 0, missingContainers = 0;
        void Add(HeatPoint? point) { if (point is null) missing++; else points.Add(point); }
        if (entry.Static.Enabled && ChanceFor(entry.Static.ChancePercent, entry.Static.MapChances, mapId) > 0)
        {
            var allowed = EligibleContainers(index, entry.Static, item, mapId);
            foreach (var (container, point) in index.ContainerPositions.GetValueOrDefault(mapId) ?? [])
                if (allowed.Contains(container)) { Add(point); if (point is null) missingContainers++; }
        }
        if ((entry.Loose.Enabled && ChanceFor(entry.Loose.ChancePercent, entry.Loose.MapChances, mapId) > 0) || entry.Guaranteed)
        {
            var positions = index.LoosePositions.GetValueOrDefault(mapId) ?? [];
            var targets = TargetCategories(entry.Loose.Enabled ? entry.Loose.Mode : TargetMode.Similar, entry.Loose.Categories, item);
            var matching = entry.Loose.Enabled && entry.Loose.Mode == TargetMode.All
                ? positions.Keys.ToHashSet()
                : SpotsForCategories(index, mapId, targets);
            if (entry.Guaranteed && !matching.Any(positions.ContainsKey)) matching = positions.Keys.ToHashSet();
            foreach (int id in matching) if (positions.TryGetValue(id, out var point)) Add(point);
        }
        return new(points, missing, missingContainers);
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
        public Dictionary<string, (string Map, string Place, double Share)> Contributions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public double Share => Contributions.Values.Sum(value => value.Share);
        public int Pools => Contributions.Count;
        public HashSet<string> Places => Contributions.Values.Select(value => value.Place).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static void AddStat(Dictionary<string, ItemStat> bucket, string tpl, double share, string place, string map)
    {
        if (!bucket.TryGetValue(tpl, out var stat))
        {
            stat = new ItemStat();
            bucket[tpl] = stat;
        }

        string key = map + ":" + place;
        stat.Contributions[key] = (map, place, stat.Contributions.GetValueOrDefault(key).Share + share);
    }

    private sealed class PoolIndex
    {
        public Dictionary<string, List<(string Container, HeatPoint? Point)>> ContainerPositions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Dictionary<int, HeatPoint?>> LoosePositions { get; } = new(StringComparer.OrdinalIgnoreCase);
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

    public async Task WarmPoolIndexAsync()
    {
        try { await EnsurePoolIndex(); }
        catch (Exception ex) { logger.Warning($"{ConfigService.LogPrefix} Pool preview unavailable; retry from the page: {ex.Message}"); }
    }

    public Task EnsurePoolIndex()
    {
        lock (_poolIndexLock)
        {
            if (_poolIndex is null || _poolIndex.IsFaulted || _poolIndex.IsCanceled) _poolIndex = Task.Run(BuildPoolIndex);
            return _poolIndex;
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
                index.ContainerPositions[mapId] = (RawValue(location.StaticContainers)?.StaticContainers ?? [])
                    .Where(c => c.Template?.Items is not null && c.Probability.GetValueOrDefault(1) > 0)
                    .Select(c =>
                    {
                        var root = c.Template!.Items!.FirstOrDefault(i => i.Id.ToString() == c.Template.Root) ?? c.Template.Items!.FirstOrDefault();
                        string tpl = root?.Template.ToString() ?? "";
                        return (tpl, ToHeatPoint(c.Template.Position, "container", catalog.NameOf(tpl)));
                    }).ToList();
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
                            AddStat(Bucket(index.StaticItems, category), dist.Tpl.ToString(), (dist.RelativeProbability ?? 0) / total, containerId.ToString(), mapId);
                        }
                    }
                }

                var looseLoot = RawValue(location.LooseLoot);
                var allSpawnpoints = looseLoot?.Spawnpoints?.ToList() ?? [];
                index.LoosePositions[mapId] = [];
                index.LooseStats[mapId] = (looseLoot?.SpawnpointCount?.Mean ?? 0, CountRollableSpawnpoints(allSpawnpoints));

                for (int spIndex = 0; spIndex < allSpawnpoints.Count; spIndex++)
                {
                    var sp = allSpawnpoints[spIndex];
                    if (sp.Template?.Items is null || sp.ItemDistribution is null || sp.Template.IsAlwaysSpawn == true)
                    {
                        continue;
                    }

                    index.LooseSpotCount[mapId] = index.LooseSpotCount.GetValueOrDefault(mapId) + 1;
                    if (sp.ItemDistribution.Sum(d => d.RelativeProbability ?? 0) > 0)
                        index.LoosePositions[mapId][spIndex] = ToHeatPoint(sp.Template.Position, "loose", "Loose loot");

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
                                AddStat(Bucket(index.LooseItems, category), root.Template.ToString(), weights.GetValueOrDefault(root.ComposedKey!) / total, $"{mapId}:{spIndex}", mapId);
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
        bool MapAllowed(string map) => entry.AllowsMap(map);
        var targets = loose
            ? TargetCategories(entry.Loose.Mode, entry.Loose.Categories, item)
            : TargetCategories(entry.Static.Mode, entry.Static.Categories, item);

        // A parent and a child category can both be selected, the same item's stats are only counted once
        var stats = new Dictionary<string, ItemStat>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in targets)
        {
            foreach (var (tpl, stat) in (loose ? index.LooseItems : index.StaticItems).GetValueOrDefault(category) ?? [])
            {
                if (!stats.TryGetValue(tpl, out var filtered)) { filtered = new ItemStat(); stats[tpl] = filtered; }
                foreach (var pair in stat.Contributions.Where(pair => MapAllowed(pair.Value.Map)))
                    filtered.Contributions.TryAdd(pair.Key, pair.Value);
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
                spots += SpotsForCategories(index, map, targets).Count;
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
