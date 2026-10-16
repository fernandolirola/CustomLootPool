namespace CustomLootPool.Services;

/// <summary>Solves all requested shares together; oversubscribed pools normalize the requested shares to 100%.</summary>
internal static class PoolWeights
{
    internal static Dictionary<T, double> Calculate<T>(IReadOnlyDictionary<T, double> original, IEnumerable<KeyValuePair<T, double>> requested) where T : notnull
    {
        var targets = requested.GroupBy(pair => pair.Key)
            .ToDictionary(group => group.Key, group => Math.Clamp(group.Max(pair => pair.Value), 0, 90));
        if (targets.Count == 0) return new(original);
        double requestedTotal = targets.Values.Sum();
        double otherTotal = original.Where(pair => !targets.ContainsKey(pair.Key)).Sum(pair => Math.Max(0, pair.Value));
        bool normalize = requestedTotal >= 100 || otherTotal <= 0;
        var result = new Dictionary<T, double>();
        foreach (var pair in original.Where(pair => !targets.ContainsKey(pair.Key)))
            result[pair.Key] = normalize ? 0 : Math.Max(0, pair.Value) / otherTotal * (100 - requestedTotal);
        foreach (var pair in targets)
            result[pair.Key] = normalize ? pair.Value / requestedTotal * 100 : pair.Value;
        return result;
    }
}
