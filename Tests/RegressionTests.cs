using System.Reflection;
using System.Text.Json;
using CustomLootPool.Models;
using CustomLootPool.Services;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.Helpers.InRaid;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Profile;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Core.Utils.Json;
using Xunit;
using Path = System.IO.Path;

namespace CustomLootPool.Tests;

public class RegressionTests
{
    private const string A = "000000000000000000000001", B = "000000000000000000000002", Vanilla = "000000000000000000000003", Container = "000000000000000000000004";

    [Theory]
    [InlineData(20, 30, 20, 30, 50)]
    [InlineData(50, 50, 50, 50, 0)]
    [InlineData(90, 90, 50, 50, 0)]
    public void CombinedChancesMatchRequestedSharesOrNormalize(double a, double b, double expectedA, double expectedB, double expectedVanilla)
    {
        var weights = PoolWeights.Calculate(new Dictionary<string, double> { [Vanilla] = 100 }, new Dictionary<string, double> { [A] = a, [B] = b });
        Assert.Equal(expectedA, weights[A], 8);
        Assert.Equal(expectedB, weights[B], 8);
        Assert.Equal(expectedVanilla, weights[Vanilla], 8);
        Assert.Equal(100, weights.Values.Sum(), 8);
    }

    [Fact]
    public void ExistingItemCanBeReducedAndOtherRatiosArePreserved()
    {
        var weights = PoolWeights.Calculate(new Dictionary<string, double> { [A] = 800, [B] = 50, [Vanilla] = 150 }, new Dictionary<string, double> { [A] = 10 });
        Assert.Equal(10, weights[A]); Assert.Equal(22.5, weights[B]); Assert.Equal(67.5, weights[Vanilla]);
    }

    [Fact]
    public void DuplicateRequestsUseHighestApplicableChance()
    {
        var weights = PoolWeights.Calculate(new Dictionary<string, double> { [Vanilla] = 100 }, new[] { new KeyValuePair<string, double>(A, 10), new(A, 30) });
        Assert.Equal(30, weights[A]); Assert.Equal(70, weights[Vanilla]);
    }

    [Fact]
    public void StaticInjectionAndBotPoolsBothUseCombinedChances()
    {
        using var fixture = new Fixture(new() { Entries = [Entry(A, 20), Entry(B, 30)] });
        var details = new StaticLootDetails { ItemDistribution = [new ItemDistribution { Tpl = new(Vanilla), RelativeProbability = 100, ExtensionData = [] }] };
        Call(fixture.Injection, "ApplyStatic", "woods", new Dictionary<MongoId, StaticLootDetails> { [new(Container)] = details });
        var pool = details.ItemDistribution!.ToDictionary(item => item.Tpl.ToString(), item => (double)item.RelativeProbability!.Value);
        Assert.Equal(20, pool[A] / pool.Values.Sum() * 100, 5);
        Assert.Equal(30, pool[B] / pool.Values.Sum() * 100, 5);
        foreach (var entry in fixture.Config.Config.Entries) entry.Bots = new() { Enabled = true, ChancePercent = entry.Static.ChancePercent };
        var extra = new ExtraLootService(fixture.Config, fixture.Catalog, fixture.Presets, new CopyCloner(), Logger<ExtraLootService>(), null!);
        var bots = extra.AddBotLoot("assault", false, "Backpack", new() { [new(Vanilla)] = 100 });
        Assert.Equal(20, bots[new(A)]); Assert.Equal(30, bots[new(B)]); Assert.Equal(50, bots[new(Vanilla)]);
    }

    [Fact]
    public void LooseInjectionUpdatesExistingKeysAndAddsEachNewRootOnce()
    {
        using var fixture = new Fixture(new() { Entries = [Entry(A, 10, true), Entry(B, 20, true), Entry(B, 15, true)] });
        var spawn = Spawn();
        Call(fixture.Injection, "ApplyLoose", "woods", new LooseLoot { Spawnpoints = [spawn] });
        var distributions = spawn.ItemDistribution!.ToList();
        Assert.Equal(3, distributions.Count);
        Assert.Equal(10, distributions.Single(item => item.ComposedKey!.Key == "existing-a").RelativeProbability);
        Assert.Equal(20, distributions.Single(item => item.ComposedKey!.Key == $"customlootpool_{B}").RelativeProbability);
        Assert.Equal(70, distributions.Single(item => item.ComposedKey!.Key == "existing-v").RelativeProbability);
        Assert.Single(spawn.Template!.Items!, item => item.ComposedKey == $"customlootpool_{B}");
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("{\"Entries\":null}")]
    [InlineData("[ {\"Tpl\":\"000000000000000000000001\", \"Static\":null} ]")]
    [InlineData("[ {\"Tpl\":\"000000000000000000000001\", \"Maps\":null} ]")]
    [InlineData("[ {\"Tpl\":\"invalid\"} ]")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[ {\"Tpl\":\"000000000000000000000001\", \"Schedule\":{\"Mode\":\"Dates\",\"From\":\"02-31\"}} ]")]
    public void MalformedImportsAreRejectedBeforeReachingReaders(string json) => Assert.ThrowsAny<Exception>(() => ConfigService.ImportEntries(json));

    [Fact]
    public void MissingOptionalFieldsAndAllSupportedImportShapesWork()
    {
        foreach (var json in new[] { $"[{{\"Tpl\":\"{A}\"}}]", $"{{\"Entries\":[{{\"Tpl\":\"{A}\"}}]}}", $"{{\"Enabled\":true,\"entries\":[{{\"Tpl\":\"{A}\"}}]}}" })
        {
            var entry = Assert.Single(ConfigService.ImportEntries(json));
            Assert.NotNull(entry.Static); Assert.NotNull(entry.Loose); Assert.NotNull(entry.Schedule);
        }
    }

    [Fact]
    public void OverridesRemainCaseInsensitiveAcrossImportCloneSaveAndLoad()
    {
        using var fixture = new Fixture(new() { Entries = ConfigService.ImportEntries($"[{{\"Tpl\":\"{A}\",\"Static\":{{\"MapChances\":{{\"WOODS\":7}}}}}}]") });
        fixture.Config.Load();
        var clone = fixture.Config.CloneConfig();
        Assert.Equal(7, LootInjectionService.ChanceFor(1, clone.Entries[0].Static.MapChances, "woods"));
        Assert.Equal(7, LootInjectionService.ChanceFor(1, clone.Entries[0].Static.MapChances, "WOODS"));
        var genericClone = ConfigService.Clone(clone.Entries[0].Static);
        Assert.Equal(7, genericClone.MapChances["woods"]);
    }

    [Fact]
    public void StaleEditorCannotOverwriteNewerSavedEntries()
    {
        using var fixture = new Fixture(new());
        var first = fixture.Config.GetSnapshot(); var second = fixture.Config.GetSnapshot();
        first.Config.Entries.Add(Entry(A, 10));
        fixture.Config.Save(first.Config, first.Revision);
        string saved = File.ReadAllText(fixture.Config.ConfigPath);
        second.Config.CompensateLootMultipliers = true;
        Assert.Throws<ConfigConflictException>(() => fixture.Config.Save(second.Config, second.Revision));
        Assert.Single(fixture.Config.Config.Entries);
        Assert.Equal(saved, File.ReadAllText(fixture.Config.ConfigPath));
    }

    [Fact]
    public async Task ConcurrentEditorsHaveExactlyOneWinner()
    {
        using var fixture = new Fixture(new());
        var snapshots = Enumerable.Range(0, 8).Select(_ => fixture.Config.GetSnapshot()).ToList();
        var results = await Task.WhenAll(snapshots.Select(snapshot => Task.Run(() =>
        {
            snapshot.Config.Entries.Add(Entry(A, 10));
            try { fixture.Config.Save(snapshot.Config, snapshot.Revision); return true; }
            catch (ConfigConflictException) { return false; }
        })));
        Assert.Single(results, value => value);
        Assert.Single(JsonSerializer.Deserialize<LootPoolConfig>(File.ReadAllText(fixture.Config.ConfigPath), ConfigService.JsonOptions)!.Entries);
        Assert.Empty(Directory.GetFiles(fixture.Directory, "*.tmp"));
    }

    [Fact]
    public void InvalidSavePreservesFileAndRevisionAndLanguageChangesPreserveEntries()
    {
        using var fixture = new Fixture(new() { Entries = [Entry(A, 10)] });
        var snapshot = fixture.Config.GetSnapshot();
        string original = File.ReadAllText(fixture.Config.ConfigPath);
        snapshot.Config.Entries[0].Static.ChancePercent = double.NaN;
        Assert.ThrowsAny<Exception>(() => fixture.Config.Save(snapshot.Config, snapshot.Revision));
        Assert.Equal(original, File.ReadAllText(fixture.Config.ConfigPath));
        Assert.Equal(snapshot.Revision, fixture.Config.Revision);
        fixture.Config.SetLanguage("pt");
        var editor = fixture.Config.GetSnapshot(); editor.Config.Language = "en";
        fixture.Config.Save(editor.Config, editor.Revision);
        Assert.Equal("pt", fixture.Config.Config.Language); Assert.Single(fixture.Config.Config.Entries);
        File.WriteAllText(fixture.Config.ConfigPath, "{invalid");
        fixture.Config.Load();
        Assert.Single(fixture.Config.Config.Entries);
        Assert.Single(Directory.GetFiles(fixture.Directory, "*.broken-*.json"));
    }

    [Fact]
    public void SimulationSharesCapAcrossSourcesAndResetsPerRaidEvenOnFailure()
    {
        var tracker = new CounterTrackerHelper(); var id = new MongoId(A);
        int Generate() => Enumerable.Range(0, 5).Count(_ => !tracker.IncrementCount(id));
        for (int raid = 0; raid < 2; raid++)
        {
            var result = SimulationRaid.Run(tracker, new() { [id] = 2 }, Generate, Generate);
            Assert.Equal(2, result.Static); Assert.Equal(0, result.Loose);
            Assert.False(tracker.IncrementCount(id)); // No cap is retained outside the simulated raid.
        }
        Assert.Throws<IOException>(() => SimulationRaid.Run(tracker, new() { [id] = 0 }, () => throw new IOException(), Generate));
        Assert.False(tracker.IncrementCount(id));
    }

    [Fact]
    public async Task MapContextsAreIsolatedAcrossConcurrentAsyncRequestsAndRestoreNestedContext()
    {
        using var fixture = new Fixture(new());
        var extra = new ExtraLootService(fixture.Config, fixture.Catalog, fixture.Presets, new CopyCloner(), Logger<ExtraLootService>(), null!);
        var results = await Task.WhenAll(new[] { "woods", "shoreline" }.Select(async map =>
        {
            using (ExtraLootService.EnterMap(map))
            {
                await Task.Delay(10);
                Assert.Equal(map, extra.CurrentMap);
                using (ExtraLootService.EnterMap("factory4_day")) Assert.Equal("factory4_day", extra.CurrentMap);
                return extra.CurrentMap;
            }
        }));
        Assert.Equal(new[] { "woods", "shoreline" }, results); Assert.Null(extra.CurrentMap);
    }

    [Fact]
    public void RequestAndLootHarmonyPatchesAttachToInstalledServer()
    {
        using var fixture = new Fixture(new());
        var extra = new ExtraLootService(fixture.Config, fixture.Catalog, fixture.Presets, new CopyCloner(), Logger<ExtraLootService>(), new ProfileActivityService(null!));
        extra.EnablePatches();
        foreach (var method in new[] { AccessTools.Method(typeof(BotCallbacks), nameof(BotCallbacks.GenerateBots)), AccessTools.Method(typeof(LocationCallbacks), nameof(LocationCallbacks.GetAirdropLoot)) })
        {
            var patches = Harmony.GetPatchInfo(method);
            Assert.Contains(patches.Prefixes, patch => patch.PatchMethod.DeclaringType!.Assembly == typeof(ExtraLootService).Assembly);
            Assert.Contains(patches.Finalizers, patch => patch.PatchMethod.DeclaringType!.Assembly == typeof(ExtraLootService).Assembly);
        }
    }

    [Fact]
    public async Task FailedPreviewIndexCanBeRetried()
    {
        using var fixture = new Fixture(new());
        int attempts = 0;
        var lazy = new LazyLoad<Dictionary<MongoId, StaticLootDetails>>(() => { if (++attempts <= 2) throw new IOException("synthetic"); return []; }, false);
        var maps = (Dictionary<string, Location>)typeof(LootInjectionService).GetField("_maps", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Injection)!;
        maps["woods"] = new() { StaticLoot = lazy };
        await Assert.ThrowsAsync<IOException>(() => fixture.Injection.EnsurePoolIndex());
        await fixture.Injection.EnsurePoolIndex(); Assert.True(fixture.Injection.PoolIndexReady);
    }

    [Fact]
    public async Task SimilarItemStatisticsUseOnlyTheSelectedMaps()
    {
        using var fixture = new Fixture(new());
        foreach (var id in new[] { B, Vanilla }) fixture.Templates.Items[new(id)] = new TemplateItem { Parent = new(Container) };
        var items = (Dictionary<string, CatalogItem>)typeof(ItemCatalogService).GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fixture.Catalog)!;
        items[A] = items[A] with { ParentId = Container };
        var maps = (Dictionary<string, Location>)typeof(LootInjectionService).GetField("_maps", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Injection)!;
        foreach (var (map, chance) in new[] { ("woods", 10), ("shoreline", 90) })
        {
            maps[map] = new() { StaticLoot = new(() => new() { [new(Container)] = new() { ItemDistribution = [new() { Tpl = new(B), RelativeProbability = chance, ExtensionData = [] }, new() { Tpl = new(Vanilla), RelativeProbability = 100 - chance, ExtensionData = [] }] } }, false) };
        }
        await fixture.Injection.EnsurePoolIndex();
        var entry = Entry(A, 1); entry.Maps = ["woods"]; entry.Static.Mode = TargetMode.Similar;
        var pool = fixture.Injection.GetSimilarPool(entry, false)!;
        Assert.Equal(10, Assert.Single(pool.Items, item => item.Item.Id == B).AveragePercent);
        entry.Maps = ["shoreline"];
        Assert.Equal(90, Assert.Single(fixture.Injection.GetSimilarPool(entry, false)!.Items, item => item.Item.Id == B).AveragePercent);
    }

    [Fact]
    public void MapRestrictedBotsDoNotLeakIntoUnknownOrOtherSessions()
    {
        var entry = Entry(A, 10); entry.Maps = ["woods"]; entry.Bots = new() { Enabled = true, ChancePercent = 10 };
        using var fixture = new Fixture(new() { Entries = [entry] });
        var extra = new ExtraLootService(fixture.Config, fixture.Catalog, fixture.Presets, new CopyCloner(), Logger<ExtraLootService>(), null!);
        var original = new Dictionary<MongoId, double> { [new(Vanilla)] = 100 };
        Assert.DoesNotContain(new MongoId(A), extra.AddBotLoot("assault", false, "Backpack", original).Keys);
        using (ExtraLootService.EnterMap("shoreline")) Assert.DoesNotContain(new MongoId(A), extra.AddBotLoot("assault", false, "Backpack", original).Keys);
        using (ExtraLootService.EnterMap("woods")) Assert.Equal(10, extra.AddBotLoot("assault", false, "Backpack", original)[new(A)]);
        Assert.Null(extra.CurrentMap);
    }

    private static LootEntry Entry(string id, double chance, bool loose = false) => new()
    {
        Tpl = id, Static = new() { Mode = TargetMode.All, ChancePercent = chance }, Loose = new() { Enabled = loose, Mode = TargetMode.All, ChancePercent = chance },
    };
    private static Spawnpoint Spawn() => new()
    {
        Probability = 0.5, LocationId = "example", Template = new() { Id = "example", Items = [new() { Id = new MongoId(), Template = new(A), ComposedKey = "existing-a" }, new() { Id = new MongoId(), Template = new(Vanilla), ComposedKey = "existing-v" }] },
        ItemDistribution = [new() { ComposedKey = new() { Key = "existing-a" }, RelativeProbability = 50 }, new() { ComposedKey = new() { Key = "existing-v" }, RelativeProbability = 50 }],
    };
    private static object? Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static ISptLogger<T> Logger<T>() => DispatchProxy.Create<ISptLogger<T>, SilentLogger>();
    private sealed class CopyCloner : ICloner { public T? Clone<T>(T? source) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(source)); }

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "CustomLootPool-tests-" + Guid.NewGuid().ToString("N"));
        public ConfigService Config { get; }
        public ItemCatalogService Catalog { get; }
        public LootInjectionService Injection { get; }
        public PresetHelper Presets { get; }
        public TemplateTable Templates { get; }
        public Fixture(LootPoolConfig config)
        {
            System.IO.Directory.CreateDirectory(Directory);
            Config = new(Directory, Logger<ConfigService>());
            Config.Save(config, 0);
            var templates = Activator.CreateInstance<TemplateTable>();
            Templates = templates;
            typeof(TemplateTable).GetProperty("Items")!.SetValue(templates, new Dictionary<MongoId, TemplateItem>());
            Catalog = new(templates, null!);
            var items = new[] { A, B, Vanilla }.ToDictionary(id => id, id => new CatalogItem(id, id, id, "", "", 1, 1));
            typeof(ItemCatalogService).GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(Catalog, items);
            Presets = new(Activator.CreateInstance<GlobalTable>(), null!, new CopyCloner());
            Presets.HydratePresetStore([]);
            Injection = new(null!, templates, Presets, new CopyCloner(), Config, Catalog, null!, null!, null!, null!, Logger<LootInjectionService>());
        }
        public void Dispose()
        {
            var resolved = Path.GetFullPath(Directory);
            if (!resolved.StartsWith(Path.Combine(Path.GetTempPath(), "CustomLootPool-tests-"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid test cleanup path.");
            System.IO.Directory.Delete(resolved, recursive: true);
        }
    }
}

public class SilentLogger : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.ReturnType == typeof(bool) ? false : null;
}
