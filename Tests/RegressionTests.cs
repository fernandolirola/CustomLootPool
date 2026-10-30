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
    public void ZeroRequestedSharesNeverProduceNonFiniteWeights()
    {
        var weights = PoolWeights.Calculate(new Dictionary<string, double> { [A] = 100 }, new Dictionary<string, double> { [A] = 0 });
        Assert.Equal(0, weights[A]); Assert.All(weights.Values, value => Assert.True(double.IsFinite(value)));
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
        var clone = fixture.Config.GetSnapshot().Config;
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
    public void AirdropTypeFilterPreservesDefaultsAndOnlyAddsLootToSelectedCrates()
    {
        var entry = Entry(A, 20);
        entry.Airdrops = new() { Enabled = true, ChancePercent = 100 };
        using var fixture = new Fixture(new() { Entries = [entry] });
        var extra = new ExtraLootService(fixture.Config, fixture.Catalog, fixture.Presets, new CopyCloner(), Logger<ExtraLootService>(), null!);
        var medical = SPTarkov.Server.Core.Models.Enums.ItemTpl.LOOTCONTAINER_AIRDROP_MEDICAL_CRATE;
        var weapons = SPTarkov.Server.Core.Models.Enums.ItemTpl.LOOTCONTAINER_AIRDROP_WEAPON_CRATE;
        Assert.Single(extra.AddAirdropLoot([], medical));
        Assert.Single(extra.AddAirdropLoot([], Vanilla));
        fixture.Config.Config.Entries[0].Airdrops.Types = ["foodMedical"];
        Assert.Single(extra.AddAirdropLoot([], medical));
        Assert.Empty(extra.AddAirdropLoot([], weapons));
        Assert.Empty(extra.AddAirdropLoot([], Vanilla));
    }

    [Fact]
    public void AirdropTypesRoundTripAndRejectInvalidSelections()
    {
        var legacy = JsonSerializer.Deserialize<AirdropSettings>("{\"Enabled\":true}")!;
        Assert.Empty(legacy.Types);
        var entry = Entry(A, 20);
        entry.Airdrops.Types = ["FOODMEDICAL", "foodMedical", "barter"];
        var config = ConfigValidation.Normalize(new() { Entries = [entry] });
        Assert.Equal(new[] { "foodMedical", "barter" }, config.Entries[0].Airdrops.Types);
        var copy = JsonSerializer.Deserialize<LootPoolConfig>(JsonSerializer.Serialize(config))!;
        Assert.Equal(entry.Airdrops.Types, copy.Entries[0].Airdrops.Types);
        entry.Airdrops.Types = ["unknown"];
        Assert.Throws<InvalidDataException>(() => ConfigValidation.Normalize(config));
        entry.Airdrops.Types = null!;
        Assert.Throws<InvalidDataException>(() => ConfigValidation.Normalize(config));
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

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void HeatProjectionKeepsRotatedCornersInsideTheMap(double rotation)
    {
        var projection = new HeatProjection(-100, -50, 300, 150, rotation);
        var projected = new[] { projection.Project(-100, -50), projection.Project(-100, 150), projection.Project(300, -50), projection.Project(300, 150) };
        Assert.All(projected, p => { Assert.InRange(p.X, -.0001, 1000.0001); Assert.InRange(p.Y, -.0001, projection.Height + .0001); });
        var center = projection.Project(100, 50);
        Assert.Equal(500, center.X, 6); Assert.Equal(projection.Height / 2, center.Y, 6);
    }

    [Fact]
    public void HeatCellsKeepNegativeCoordinatesAndWeightsWithoutCountingInvalidPoints()
    {
        var cells = HeatmapGrid.Aggregate([new(-1, 0, -1, "container", "a", 5), new(-2, 0, -2, "loose", "b", 3), new(1, 0, 1, "loose", "c"), new(double.NaN, 0, 0, "loose", "invalid")], 40);
        Assert.Equal(2, cells.Count); Assert.Equal(-20, cells[0].X); Assert.Equal(8, cells[0].Weight);
        Assert.Equal(9, cells.Sum(c => c.Weight));
        Assert.Throws<ArgumentOutOfRangeException>(() => HeatmapGrid.Aggregate([], 0));
    }

    [Fact]
    public async Task HeatLocationsUsePhysicalContainersAndRespectSourcesMapsAndZeroChances()
    {
        using var fixture = new Fixture(new());
        var maps = (Dictionary<string, Location>)typeof(LootInjectionService).GetField("_maps", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Injection)!;
        var spawn = Spawn(); spawn.Template!.Position = new(50, 2, 100);
        maps["woods"] = new()
        {
            StaticLoot = new(() => new() { [new(Container)] = new() { ItemDistribution = [new() { Tpl = new(Vanilla), RelativeProbability = 100 }] } }, false),
            StaticContainers = new(() => new() { StaticContainers = [new() { Probability = 1, Template = new() { Root = A, Position = new(10, 2, 20), Items = [new() { Id = new(A), Template = new(Container) }] } }] }, false),
            LooseLoot = new(() => new() { Spawnpoints = [spawn] }, false),
        };
        var entry = Entry(A, 10);
        var result = await fixture.Injection.HeatLocations(entry, "woods");
        Assert.Equal("container", Assert.Single(result.Points).Source); Assert.Equal(10, result.Points[0].X);
        entry.Loose.Enabled = true;
        Assert.Equal(2, (await fixture.Injection.HeatLocations(entry, "woods")).Points.Count);
        entry.Static.ChancePercent = 0; entry.Loose.ChancePercent = 0;
        Assert.Empty((await fixture.Injection.HeatLocations(entry, "woods")).Points);
        entry.Maps = ["shoreline"]; entry.Static.ChancePercent = 10;
        Assert.Empty((await fixture.Injection.HeatLocations(entry, "woods")).Points);
    }

    [Fact]
    public async Task PlaceholderContainerCoordinatesAreReportedInsteadOfCreatingAnOriginHotspot()
    {
        using var fixture = new Fixture(new());
        var maps = (Dictionary<string, Location>)fixture.Injection.Maps;
        maps["woods"] = new()
        {
            StaticLoot = new(() => new() { [new(Container)] = new() { ItemDistribution = [new() { Tpl = new(Vanilla), RelativeProbability = 100 }] } }, false),
            StaticContainers = new(() => new() { StaticContainers = [new() { Probability = 1, Template = new() { Position = new(0, 0, 0), Items = [new() { Id = new(A), Template = new(Container) }] } }] }, false),
        };
        var result = await fixture.Injection.HeatLocations(Entry(A, 10), "woods");
        Assert.Empty(result.Points); Assert.Equal(1, result.MissingContainers); Assert.Equal(1, result.MissingCoordinates);
    }

    [Fact]
    public void BundledTarkovMapManifestLoadsAndEveryFloorHasArtworkAndValidBounds()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "wwwroot", "maps"));
        var maps = JsonSerializer.Deserialize<List<MapArtwork>>(File.ReadAllText(Path.Combine(root, "index.json")), ConfigService.JsonOptions)!;
        Assert.NotEmpty(maps);
        foreach (var map in maps)
        {
            Assert.NotEmpty(map.Maps); Assert.NotEmpty(map.Floors);
            Assert.Contains(map.Floors, floor => floor.Level == map.DefaultLevel);
            foreach (var floor in map.Floors)
            {
                Assert.True(floor.Bounds.Valid); Assert.NotEmpty(floor.Volumes);
                Assert.True(File.Exists(Path.Combine(root, floor.ImagePath)));
            }
        }
        Assert.True(File.Exists(Path.Combine(root, "LICENSE-CC-BY-NC-SA-4.0.md")));
    }

    [Fact]
    public void FloorSelectionPreservesUpperFloorPriorityOverBroadGroundBounds()
    {
        var bounds = new MapExtent(-100, -100, 100, 100);
        var map = new MapArtwork("test", "test", "test", ["woods"], 0, 0,
        [
            new("Ground", 0, bounds, "", [new(-100, -100, -100, 100, 100, 100)]),
            new("Upper", 1, bounds, "", [new(0, 0, 2, 10, 10, 8)]),
            new("Top", 2, bounds, "", [new(0, 0, 5, 10, 10, 10)]),
        ], []);
        Assert.Equal("Upper", map.FloorFor(new(5, 3, 5, "loose", "")));
        Assert.Equal("Top", map.FloorFor(new(5, 6, 5, "loose", "")));
        Assert.Equal("Ground", map.FloorFor(new(-10, 0, -10, "loose", "")));
        Assert.Null(map.FloorFor(new(500, 0, 500, "loose", "")));
    }

    [Fact]
    public void QualityScalesTiersButKeepsThePoolTotal()
    {
        var weights = QualityWeights.Apply(new Dictionary<string, double> { [A] = 50, [B] = 50 }, tpl => tpl == A ? 2 : 1);
        Assert.Equal(100, weights.Values.Sum(), 8);
        Assert.Equal(2, weights[A] / weights[B], 8);

        var untouched = QualityWeights.Apply(new Dictionary<string, double> { [A] = 30, [B] = 70 }, _ => 0);
        Assert.Equal(30, untouched[A]); Assert.Equal(70, untouched[B]);
    }

    [Fact]
    public void PinnedItemsKeepTheirWeightWhileTheRestIsScaled()
    {
        var weights = QualityWeights.Apply(new Dictionary<string, double> { [A] = 50, [B] = 25, [Vanilla] = 25 }, tpl => tpl == A ? null : tpl == B ? 3 : 1);
        Assert.Equal(50, weights[A], 8);
        Assert.Equal(37.5, weights[B], 8);
        Assert.Equal(12.5, weights[Vanilla], 8);

        var allPinned = QualityWeights.Apply(new Dictionary<string, double> { [A] = 30, [B] = 70 }, _ => null);
        Assert.Equal(30, allPinned[A]); Assert.Equal(70, allPinned[B]);
    }

    [Theory]
    [InlineData(true, false, 50)]
    [InlineData(false, true, 50)]
    [InlineData(false, false, 25)]
    public void KeysAndQuestItemsDoNotGetRarerInSharedLooseSpots(bool key, bool questItem, double expected)
    {
        using var fixture = new Fixture(new() { Quality = new() { Enabled = true, Basis = QualityBasis.Value } });
        fixture.Templates.Items[new(A)] = new TemplateItem
        {
            Parent = new(key ? "543be5e94bdc2df1348b4568" : Container),
            Properties = new() { QuestItem = questItem },
        };
        typeof(TemplateTable).GetProperty("Prices")!.SetValue(fixture.Templates, new Dictionary<MongoId, double> { [new(A)] = 1_000, [new(Vanilla)] = 200_000 });

        var spawn = Spawn();
        Call(fixture.Injection, "ApplyLoose", "woods", new LooseLoot { Spawnpoints = [spawn] });
        Assert.Equal(expected, spawn.ItemDistribution!.Single(item => item.ComposedKey!.Key == "existing-a").RelativeProbability!.Value, 6);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(9_999, 1)]
    [InlineData(50_000, 2)]
    [InlineData(149_999, 2)]
    [InlineData(1_000_000, 3)]
    public void ValueTiersUseTheHighestReachedPriceAndIgnoreUnpricedItems(double price, double expected) =>
        Assert.Equal(expected, QualityWeights.ForValue(new QualitySettings().ValueTiers, price));

    [Fact]
    public void QualityRunsBeforeConfiguredChancesSoTheyStayExact()
    {
        using var fixture = new Fixture(new() { Entries = [Entry(A, 20)], Quality = new() { Enabled = true } });
        typeof(TemplateTable).GetProperty("Prices")!.SetValue(fixture.Templates, new Dictionary<MongoId, double> { [new(B)] = 100_000, [new(Vanilla)] = 1_000 });
        var details = new StaticLootDetails
        {
            ItemDistribution = [new ItemDistribution { Tpl = new(B), RelativeProbability = 50, ExtensionData = [] }, new ItemDistribution { Tpl = new(Vanilla), RelativeProbability = 50, ExtensionData = [] }],
        };
        Call(fixture.Injection, "ApplyStatic", "woods", new Dictionary<MongoId, StaticLootDetails> { [new(Container)] = details });
        var pool = details.ItemDistribution!.ToDictionary(item => item.Tpl.ToString(), item => (double)item.RelativeProbability!.Value / details.ItemDistribution!.Sum(d => (double)d.RelativeProbability!.Value) * 100);
        Assert.Equal(20, pool[A], 4);
        Assert.Equal(80 * 2 / 3.0, pool[B], 4);
        Assert.Equal(80 / 3.0, pool[Vanilla], 4);
    }

    [Fact]
    public void ConfiguredItemsStillFillEmptyPoolsWhenQualityIsOn()
    {
        using var fixture = new Fixture(new() { Entries = [Entry(A, 20)], Quality = new() { Enabled = true } });
        var details = new StaticLootDetails { ItemDistribution = [new ItemDistribution { Tpl = new(Vanilla), RelativeProbability = 0, ExtensionData = [] }] };
        Call(fixture.Injection, "ApplyStatic", "woods", new Dictionary<MongoId, StaticLootDetails> { [new(Container)] = details });
        Assert.Equal(A, Assert.Single(details.ItemDistribution!).Tpl.ToString());
    }

    [Fact]
    public void QualityAloneReweightsLooseSpotsAndRespectsItsMapsAndSources()
    {
        var quality = new QualitySettings { Enabled = true, Basis = QualityBasis.Value, Maps = ["woods"] };
        using var fixture = new Fixture(new() { Quality = quality });
        typeof(TemplateTable).GetProperty("Prices")!.SetValue(fixture.Templates, new Dictionary<MongoId, double> { [new(A)] = 200_000, [new(Vanilla)] = 1_000 });

        var spawn = Spawn();
        Call(fixture.Injection, "ApplyLoose", "woods", new LooseLoot { Spawnpoints = [spawn] });
        Assert.Equal(75, spawn.ItemDistribution!.Single(item => item.ComposedKey!.Key == "existing-a").RelativeProbability!.Value, 6);
        Assert.Equal(25, spawn.ItemDistribution!.Single(item => item.ComposedKey!.Key == "existing-v").RelativeProbability!.Value, 6);

        var otherMap = Spawn();
        Call(fixture.Injection, "ApplyLoose", "bigmap", new LooseLoot { Spawnpoints = [otherMap] });
        Assert.All(otherMap.ItemDistribution!, item => Assert.Equal(50, item.RelativeProbability));
    }

    [Fact]
    public void DefaultValueTiersRoughlyDoubleTheExpensiveShareOfCustomsContainers()
    {
        string database = Path.Combine(Environment.GetEnvironmentVariable("SPT_PATH") ?? @"C:\SPT", "SPT_Runtime", "SPT_Data", "database");
        var loot = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(Path.Combine(database, "locations", "bigmap", "staticLoot.json")))!;
        var prices = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(Path.Combine(database, "templates", "prices.json")))!;
        var tiers = new QualitySettings().ValueTiers;
        double before = 0, after = 0;
        int pools = 0;
        foreach (var container in loot.Values)
        {
            var pool = container.GetProperty("itemDistribution").EnumerateArray()
                .GroupBy(item => item.GetProperty("tpl").GetString()!)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.GetProperty("relativeProbability").GetDouble()));
            if (pool.Values.Sum() <= 0) continue;
            var scaled = QualityWeights.Apply(pool, tpl => QualityWeights.ForValue(tiers, prices.GetValueOrDefault(tpl)));
            before += pool.Where(pair => prices.GetValueOrDefault(pair.Key) >= 50_000).Sum(pair => pair.Value) / pool.Values.Sum();
            after += scaled.Where(pair => prices.GetValueOrDefault(pair.Key) >= 50_000).Sum(pair => pair.Value) / scaled.Values.Sum();
            pools++;
        }

        Assert.True(pools > 20);
        Assert.InRange(after / before, 1.8, 2.6);
    }

    [Theory]
    [InlineData("""{"Quality":{"ValueTiers":[{"From":5000,"Multiplier":1}]}}""")]
    [InlineData("""{"Quality":{"ValueTiers":[{"From":0,"Multiplier":1},{"From":0,"Multiplier":2}]}}""")]
    [InlineData("""{"Quality":{"RareMultiplier":11}}""")]
    [InlineData("""{"Quality":{"ValueTiers":[]}}""")]
    public void InvalidQualitySettingsAreRejected(string json) =>
        Assert.ThrowsAny<Exception>(() => ConfigValidation.Normalize(JsonSerializer.Deserialize<LootPoolConfig>(json, ConfigService.JsonOptions)!));

    [Fact]
    public void QualitySettingsDefaultOffAndSurviveSaveAndReload()
    {
        Assert.False(ConfigValidation.Normalize(JsonSerializer.Deserialize<LootPoolConfig>("{}", ConfigService.JsonOptions)!).Quality.Enabled);
        var quality = new QualitySettings { Enabled = true, Basis = QualityBasis.Rarity, SuperrareMultiplier = 3, Maps = ["Woods"], ValueTiers = [new() { From = 20_000, Multiplier = 2 }, new() { From = 0, Multiplier = 1 }] };
        using var fixture = new Fixture(new() { Quality = quality });
        fixture.Config.Load();
        var loaded = fixture.Config.Config.Quality;
        Assert.True(loaded.Enabled);
        Assert.Equal(QualityBasis.Rarity, loaded.Basis);
        Assert.Equal(3, loaded.SuperrareMultiplier);
        Assert.Equal(["woods"], loaded.Maps);
        Assert.Equal([0, 20_000], loaded.ValueTiers.Select(tier => tier.From));
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
            Injection = new(null!, templates, Presets, new CopyCloner(), Config, Catalog, null!, null!, null!, null!, new ContainerPositions(Config), Logger<LootInjectionService>());
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
