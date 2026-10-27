using CustomLootPool.Models;

namespace CustomLootPool.Services;

/// <summary>Scales pool weights per item and keeps the pool's total, so the amount of loot is unchanged</summary>
internal static class QualityWeights
{
    internal static int ValueTierOf(IReadOnlyList<ValueTier> tiers, double price)
    {
        int tier = 0;
        for (int i = 0; i < tiers.Count; i++)
        {
            if (tiers[i].From <= price) tier = i;
        }
        return tier;
    }

    /// <summary>Items with no known price are left alone instead of counting as the cheapest tier</summary>
    internal static double ForValue(IReadOnlyList<ValueTier> tiers, double price) => price > 0 ? tiers[ValueTierOf(tiers, price)].Multiplier : 1;

    internal static int RarityTierOf(string? rarity) => rarity switch
    {
        "Common" => 0,
        "Rare" => 1,
        "Superrare" => 2,
        _ => 3,
    };

    internal static double ForRarity(QualitySettings quality, string? rarity) => RarityTierOf(rarity) switch
    {
        0 => quality.CommonMultiplier,
        1 => quality.RareMultiplier,
        2 => quality.SuperrareMultiplier,
        _ => 1,
    };

    /// <summary>A null multiplier pins the item: it keeps its weight and only the other items share the rest</summary>
    internal static Dictionary<T, double> Apply<T>(IReadOnlyDictionary<T, double> weights, Func<T, double?> multiplier) where T : notnull
    {
        var factors = weights.Keys.ToDictionary(key => key, multiplier);
        double before = weights.Where(pair => factors[pair.Key] is not null).Sum(pair => Math.Max(0, pair.Value));
        var scaled = weights.ToDictionary(pair => pair.Key, pair => Math.Max(0, pair.Value) * (factors[pair.Key] ?? 1));
        double after = scaled.Where(pair => factors[pair.Key] is not null).Sum(pair => pair.Value);

        // Every item at x0 would leave an empty pool, so the pool stays as it was
        if (before <= 0 || after <= 0) return new(weights);
        return scaled.ToDictionary(pair => pair.Key, pair => factors[pair.Key] is null ? pair.Value : pair.Value / after * before);
    }
}
