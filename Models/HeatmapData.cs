using System.Globalization;

namespace CustomLootPool.Models;

public sealed record HeatPoint(double X, double Y, double Z, string Source, string Name, double Weight = 1, string? Tpl = null);
public sealed record HeatSample(List<HeatPoint> Points, int MissingCoordinates = 0, int MissingContainers = 0);
public sealed record HeatCell(double X, double Z, double Weight);

public static class HeatmapGrid
{
    public static List<HeatCell> Aggregate(IEnumerable<HeatPoint> points, double size)
    {
        if (!double.IsFinite(size) || size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        return points.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z) && double.IsFinite(p.Weight) && p.Weight > 0)
            .GroupBy(p => (X: Math.Floor(p.X / size), Z: Math.Floor(p.Z / size)))
            .Select(g => new HeatCell((g.Key.X + .5) * size, (g.Key.Z + .5) * size, g.Sum(p => p.Weight)))
            .OrderByDescending(c => c.Weight).ToList();
    }

    public static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

public sealed class HeatProjection
{
    private readonly double cosine, sine, minX, minZ, width, height;
    public double Height { get; }
    public HeatProjection(double lowX, double lowZ, double highX, double highZ, double rotation)
    {
        if (!new[] { lowX, lowZ, highX, highZ, rotation }.All(double.IsFinite) || highX <= lowX || highZ <= lowZ)
            throw new ArgumentException("Invalid map bounds.");
        cosine = Math.Cos(rotation * Math.PI / 180); sine = Math.Sin(rotation * Math.PI / 180);
        var corners = new[] { Rotate(lowX, lowZ), Rotate(lowX, highZ), Rotate(highX, lowZ), Rotate(highX, highZ) };
        minX = corners.Min(p => p.X); minZ = corners.Min(p => p.Z);
        width = corners.Max(p => p.X) - minX; height = corners.Max(p => p.Z) - minZ;
        Height = 1000 * height / width;
    }
    private (double X, double Z) Rotate(double x, double z) => (x * cosine - z * sine, x * sine + z * cosine);
    public (double X, double Y) Project(double x, double z)
    {
        var r = Rotate(x, z);
        return ((r.X - minX) / width * 1000, (1 - (r.Z - minZ) / height) * Height);
    }
    public double Radius(double meters) => 1000 * meters / width;
}
