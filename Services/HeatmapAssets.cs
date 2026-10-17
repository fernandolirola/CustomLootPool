using System.Text.Json;
using CustomLootPool.Models;
using SPTarkov.DI.Annotations;

namespace CustomLootPool.Services;

public sealed record MapExtent(double MinX, double MinZ, double MaxX, double MaxZ)
{
    public bool Valid => new[] { MinX, MinZ, MaxX, MaxZ }.All(double.IsFinite) && MaxX > MinX && MaxZ > MinZ;
}
public sealed record FloorVolume(double MinX, double MinZ, double MinHeight, double MaxX, double MaxZ, double MaxHeight)
{
    public bool Contains(HeatPoint p) => p.X >= MinX && p.X <= MaxX && p.Z >= MinZ && p.Z <= MaxZ && p.Y >= MinHeight && p.Y <= MaxHeight;
}
public sealed record MapFloor(string Name, int Level, MapExtent Bounds, string ImagePath, List<FloorVolume> Volumes);
public sealed record MapLandmark(string Name, double X, double Z);
public sealed record MapArtwork(string Id, string Name, string Author, string[] Maps, double Rotation, int DefaultLevel, List<MapFloor> Floors, List<MapLandmark> Landmarks)
{
    public MapFloor? DefaultFloor => Floors.Find(floor => floor.Level == DefaultLevel);

    public string? FloorFor(HeatPoint point)
    {
        MapFloor? match = null;
        foreach (var floor in Floors)
            if (floor.Level != DefaultLevel && (match is null || floor.Level > match.Level) && floor.Volumes.Any(volume => volume.Contains(point)))
                match = floor;
        return match?.Name ?? (DefaultFloor is { } ground && ground.Volumes.Any(volume => volume.Contains(point)) ? ground.Name : null);
    }
}

/// <summary>Uses only the bundled, attributed Tarkov.dev SVG artwork and metadata.</summary>
[Injectable(InjectionType.Singleton)]
public sealed class HeatmapAssets(ConfigService config)
{
    private readonly Lazy<List<MapArtwork>> maps = new(() =>
    {
        string path = Path.Combine(config.ModPath, "wwwroot", "maps", "index.json");
        return JsonSerializer.Deserialize<List<MapArtwork>>(File.ReadAllText(path), ConfigService.JsonOptions) ?? [];
    });

    public MapArtwork? FindMap(string map) => maps.Value.Find(artwork => artwork.Maps.Contains(map, StringComparer.OrdinalIgnoreCase));

    public string? ImageUrl(MapFloor floor)
    {
        string root = Path.GetFullPath(Path.Combine(config.ModPath, "wwwroot", "maps")) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, floor.ImagePath));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
        return "/CustomLootPool/maps/" + string.Join('/', floor.ImagePath.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));
    }
}
