using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace CustomLootPool.Services;

/// <summary>
/// Stock SPT stores (0, 0, 0) for every static container, because the client takes the position from the scene.
/// The real positions were extracted once from the game scenes (tools/container-positions) and are matched by instance Id.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class ContainerPositions(ConfigService config)
{
    private sealed record PositionFile(Dictionary<string, Dictionary<string, double[]>> Maps);

    private readonly Lazy<Dictionary<string, Dictionary<string, double[]>>> positions = new(() =>
    {
        try
        {
            string path = Path.Combine(config.ModPath, "wwwroot", "maps", "container-positions.json");
            var file = JsonSerializer.Deserialize<PositionFile>(File.ReadAllText(path), ConfigService.JsonOptions);
            return (file?.Maps ?? []).ToDictionary(
                kv => kv.Key,
                kv => new Dictionary<string, double[]>(kv.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, Dictionary<string, double[]>>(StringComparer.OrdinalIgnoreCase);
        }
    });

    /// <summary>The stored position when it is real, otherwise the extracted one for this container instance.</summary>
    public Vector3? Resolve(string mapId, string? containerId, Vector3? stored)
    {
        if (stored is { } p && (p.X != 0 || p.Y != 0 || p.Z != 0))
        {
            return stored;
        }

        if (containerId is not null
            && positions.Value.TryGetValue(mapId, out var map)
            && map.TryGetValue(containerId, out var xyz)
            && xyz.Length == 3)
        {
            return new Vector3((float)xyz[0], (float)xyz[1], (float)xyz[2]);
        }

        return stored;
    }
}
