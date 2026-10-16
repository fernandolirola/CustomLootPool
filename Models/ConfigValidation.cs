using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CustomLootPool.Models;

public sealed class MapChancesConverter : JsonConverter<Dictionary<string, double>>
{
    public override Dictionary<string, double> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("MapChances must be an object.");
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Invalid map override.");
            var key = reader.GetString()!.Trim().ToLowerInvariant();
            if (!reader.Read() || reader.TokenType != JsonTokenType.Number || !reader.TryGetDouble(out var value) || !double.IsFinite(value))
                throw new JsonException("Map chance must be a finite number.");
            if (!values.TryAdd(key, value)) throw new JsonException($"Duplicate map override: {key}.");
        }
        return values;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, double> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var pair in value) writer.WriteNumber(pair.Key, pair.Value);
        writer.WriteEndObject();
    }
}

internal static class ConfigValidation
{
    internal static LootPoolConfig Normalize(LootPoolConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Require(config.Entries is not null, "Entries must be an array.");
        config.Language = config.Language?.Trim().ToLowerInvariant() ?? "en";
        Require(config.Language is "en" or "pt", "Language must be en or pt.");
        for (int i = 0; i < config.Entries.Count; i++)
        {
            var entry = config.Entries[i];
            string path = $"Entries[{i}]";
            Require(entry is not null, $"{path} must be an object.");
            entry.Tpl = Id(entry.Tpl, path + ".Tpl");
            entry.Maps = Strings(entry.Maps, path + ".Maps");
            Require(entry.Static is not null && entry.Loose is not null && entry.Bots is not null && entry.Airdrops is not null && entry.Schedule is not null,
                $"{path}: Static, Loose, Bots, Airdrops and Schedule cannot be null.");
            Require(Enum.IsDefined(entry.Static.Mode) && Enum.IsDefined(entry.Loose.Mode) && entry.Loose.Mode != TargetMode.Selected,
                $"{path}: invalid target mode.");
            entry.Static.Containers = Ids(entry.Static.Containers, path + ".Static.Containers");
            entry.Static.Categories = Ids(entry.Static.Categories, path + ".Static.Categories");
            entry.Loose.Categories = Ids(entry.Loose.Categories, path + ".Loose.Categories");
            Chance(entry.Static.ChancePercent, 90, path + ".Static.ChancePercent");
            Chance(entry.Loose.ChancePercent, 90, path + ".Loose.ChancePercent");
            Chance(entry.Bots.ChancePercent, 90, path + ".Bots.ChancePercent");
            Chance(entry.Airdrops.ChancePercent, 100, path + ".Airdrops.ChancePercent");
            entry.Static.MapChances = Chances(entry.Static.MapChances, path + ".Static.MapChances");
            entry.Loose.MapChances = Chances(entry.Loose.MapChances, path + ".Loose.MapChances");
            Require(Enum.IsDefined(entry.Schedule.Mode), $"{path}: invalid schedule mode.");
            if (entry.Schedule.Mode == ScheduleMode.Dates)
            {
                entry.Schedule.From = Day(entry.Schedule.From, path + ".Schedule.From");
                entry.Schedule.To = Day(entry.Schedule.To, path + ".Schedule.To");
            }
        }
        return config;
    }

    private static Dictionary<string, double> Chances(Dictionary<string, double> source, string path)
    {
        Require(source is not null, $"{path} cannot be null.");
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            Require(!string.IsNullOrWhiteSpace(pair.Key), $"{path}: empty map name.");
            Chance(pair.Value, 90, path);
            Require(result.TryAdd(pair.Key.Trim().ToLowerInvariant(), pair.Value), $"{path}: duplicate map name.");
        }
        return result;
    }

    private static List<string> Strings(List<string> source, string path)
    {
        Require(source is not null && source.All(value => !string.IsNullOrWhiteSpace(value)), $"{path} must contain non-empty strings.");
        return source.Select(value => value.Trim().ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> Ids(List<string> source, string path) => Strings(source, path).Select(id => Id(id, path)).ToList();
    private static string Id(string value, string path)
    {
        Require(value is not null && value.Trim().Length == 24 && value.Trim().All(Uri.IsHexDigit), $"{path} must be a 24-character hexadecimal item ID.");
        return value.Trim().ToLowerInvariant();
    }
    private static string Day(string value, string path)
    {
        Require(DateTime.TryParseExact("2000-" + value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _), $"{path} must be a valid MM-dd date.");
        return value;
    }
    private static void Chance(double value, double max, string path) => Require(double.IsFinite(value) && value >= 0 && value <= max, $"{path} must be between 0 and {max}.");
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
