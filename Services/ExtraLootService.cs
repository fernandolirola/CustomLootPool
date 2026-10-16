using System.Reflection;
using CustomLootPool.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Services.Bot;
using SPTarkov.Server.Core.Services.InRaid;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Services.Profile;
using SPTarkov.Server.Core.Callbacks;

namespace CustomLootPool.Services;

/// <summary>Bot inventories and airdrops are generated on demand, so they are reached through small Harmony patches</summary>
[Injectable(InjectionType.Singleton)]
public class ExtraLootService(
    ConfigService configService,
    ItemCatalogService catalog,
    PresetHelper presetHelper,
    ICloner cloner,
    ISptLogger<ExtraLootService> logger,
    ProfileActivityService profileActivityService
)
{
    private static readonly HashSet<string> ScavRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "assault", "marksman", "cursedassault", "crazyassaultevent", "assaultgroup",
    };

    internal static ExtraLootService? Instance { get; private set; }

    /// <summary>Map of the raid being generated, used by the map filter for bots and airdrops</summary>
    private static readonly AsyncLocal<string?> RequestMap = new();
    public string? CurrentMap => RequestMap.Value;

    internal static IDisposable EnterMap(string? map)
    {
        var previous = RequestMap.Value;
        RequestMap.Value = map?.ToLowerInvariant();
        return new MapScope(() => RequestMap.Value = previous);
    }
    private sealed class MapScope(Action restore) : IDisposable { public void Dispose() => restore(); }
    private IDisposable EnterSession(MongoId session) => EnterMap(profileActivityService.GetProfileActivityRaidData(session)?.RaidConfiguration?.Location);

    public void EnablePatches()
    {
        Instance = this;
        foreach (AbstractPatch patch in new AbstractPatch[] { new BotRequestPatch(), new AirdropRequestPatch(), new BotLootPatch(), new AirdropPatch() })
        {
            try
            {
                patch.Enable();
            }
            catch (Exception ex)
            {
                logger.Warning($"{ConfigService.LogPrefix} {patch.GetType().Name}: {ex.Message}");
            }
        }
    }

    private IEnumerable<LootEntry> Active(Func<LootEntry, bool> filter)
    {
        var cfg = configService.Config;
        if (!cfg.Enabled)
        {
            return [];
        }

        return cfg.Entries.Where(e =>
            e.Enabled
            && catalog.Exists(e.Tpl)
            && (e.Maps.Count == 0 || (CurrentMap is not null && e.Maps.Contains(CurrentMap, StringComparer.OrdinalIgnoreCase)))
            && LootInjectionService.IsActiveNow(e.Schedule)
            && filter(e));
    }

    internal Dictionary<MongoId, double> AddBotLoot(string role, bool isPmc, string lootType, Dictionary<MongoId, double> pool)
    {
        Func<BotLootSettings, bool>? slot = lootType switch
        {
            "Backpack" => b => b.Backpack,
            "Pocket" => b => b.Pockets,
            "Vest" => b => b.Vest,
            _ => null,
        };
        if (slot is null || pool.Count == 0)
        {
            return pool;
        }

        bool boss = role.StartsWith("boss", StringComparison.OrdinalIgnoreCase) || role.StartsWith("follower", StringComparison.OrdinalIgnoreCase);
        bool scav = !isPmc && ScavRoles.Contains(role);

        var entries = Active(e => e.Bots.Enabled
                                  && e.Bots.ChancePercent > 0
                                  && slot(e.Bots)
                                  && ((isPmc && e.Bots.Pmcs) || (boss && e.Bots.Bosses) || (scav && e.Bots.Scavs)))
            .ToList();
        if (entries.Count == 0)
        {
            return pool;
        }

        return PoolWeights.Calculate(pool, entries.Select(entry => new KeyValuePair<MongoId, double>(new(entry.Tpl), entry.Bots.ChancePercent)))
            .Where(pair => pair.Value > 0).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    internal IEnumerable<List<Item>> AddAirdropLoot(IEnumerable<List<Item>> pool)
    {
        var extra = Active(e => e.Airdrops.Enabled && e.Airdrops.ChancePercent > 0)
            .Where(e => Random.Shared.NextDouble() * 100 < e.Airdrops.ChancePercent)
            .Select(e => CreateItem(new MongoId(e.Tpl)))
            .ToList();

        // Placed first so the crate always has room for them
        return extra.Count == 0 ? pool : extra.Concat(pool).ToList();
    }

    private List<Item> CreateItem(MongoId tpl)
    {
        if (presetHelper.HasPreset(tpl) && presetHelper.GetDefaultPreset(tpl) is { Items.Count: > 0 } preset)
        {
            var items = cloner.Clone(preset.Items)!.ReplaceIDs().ToList();
            var ids = items.Select(i => i.Id.ToString()).ToHashSet();
            var root = items.First(i => i.ParentId is null || !ids.Contains(i.ParentId));
            root.ParentId = null;
            root.SlotId = null;
            items.Remove(root);
            items.Insert(0, root);
            return items;
        }

        return [new Item { Id = new MongoId(), Template = tpl, Upd = new Upd { StackObjectsCount = 1, SpawnedInSession = true } }];
    }

    private sealed class BotRequestPatch : AbstractPatch
    {
        protected override MethodBase? GetTargetMethod() =>
            typeof(BotCallbacks).GetMethod(nameof(BotCallbacks.GenerateBots));

        [PatchPrefix]
        public static void Prefix(MongoId sessionID, out IDisposable? __state) => __state = Instance?.EnterSession(sessionID);
        [PatchFinalizer]
        public static Exception? Finalizer(Exception? __exception, IDisposable? __state) { __state?.Dispose(); return __exception; }
    }

    private sealed class AirdropRequestPatch : AbstractPatch
    {
        protected override MethodBase? GetTargetMethod() => typeof(LocationCallbacks).GetMethod(nameof(LocationCallbacks.GetAirdropLoot));
        [PatchPrefix]
        public static void Prefix(MongoId sessionID, out IDisposable? __state) => __state = Instance?.EnterSession(sessionID);
        [PatchFinalizer]
        public static Exception? Finalizer(Exception? __exception, IDisposable? __state) { __state?.Dispose(); return __exception; }
    }

    private sealed class BotLootPatch : AbstractPatch
    {
        protected override MethodBase? GetTargetMethod() =>
            typeof(BotLootCacheService).GetMethod(nameof(BotLootCacheService.GetLootFromCache));

        [PatchPostfix]
        public static void Postfix(string botRole, bool isPmc, string lootType, ref Dictionary<MongoId, double> __result)
        {
            if (Instance is { } service && __result is not null)
            {
                __result = service.AddBotLoot(botRole, isPmc, lootType, __result);
            }
        }
    }

    private sealed class AirdropPatch : AbstractPatch
    {
        protected override MethodBase? GetTargetMethod() =>
            typeof(AirdropService).GetMethod("GetLootThatFitsContainer", BindingFlags.Instance | BindingFlags.NonPublic);

        [PatchPrefix]
        public static void Prefix(ref IEnumerable<List<Item>> crateLootPool)
        {
            if (Instance is { } service)
            {
                crateLootPool = service.AddAirdropLoot(crateLootPool);
            }
        }
    }
}
