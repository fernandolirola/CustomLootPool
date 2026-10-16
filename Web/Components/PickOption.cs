namespace CustomLootPool.Web.Components;

public sealed record PickOption(string Id, string Name, string? Sub = null, int? Count = null);

public sealed record SegmentOption<T>(T Value, string Label, string? Icon = null);

public sealed record RarityOption(string Key, string Label, double Value, string Hint);
