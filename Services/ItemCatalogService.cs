using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Locales;

namespace CustomLootPool.Services;

public sealed record CatalogItem(string Id, string Name, string ShortName, string ParentId, string ParentName, int Width, int Height)
{
    public string IconUrl => $"https://assets.tarkov.dev/{Id}-icon.webp";
    public string ImageUrl => $"https://assets.tarkov.dev/{Id}-grid-image.webp";
}

/// <summary>Item names come from the server's locale, not the page language</summary>
[Injectable(InjectionType.Singleton)]
public sealed class ItemCatalogService(TemplateTable templateTable, LocaleService localeService)
{
    private Dictionary<string, CatalogItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _englishNames = new(StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlyDictionary<string, string> MapNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["bigmap"] = "Customs",
        ["factory4_day"] = "Factory (day)",
        ["factory4_night"] = "Factory (night)",
        ["interchange"] = "Interchange",
        ["laboratory"] = "The Lab",
        ["labyrinth"] = "Labyrinth",
        ["lighthouse"] = "Lighthouse",
        ["rezervbase"] = "Reserve",
        ["sandbox"] = "Ground Zero",
        ["sandbox_high"] = "Ground Zero (21+)",
        ["shoreline"] = "Shoreline",
        ["tarkovstreets"] = "Streets of Tarkov",
        ["woods"] = "Woods",
        ["icebreaker"] = "Icebreaker",
    };

    public static string MapName(string id, bool portuguese = false)
    {
        var name = MapNames.TryGetValue(id, out var n) ? n : id;
        return portuguese ? name.Replace("(day)", "(dia)").Replace("(night)", "(noite)") : name;
    }

    public void Build()
    {
        var locale = localeService.GetLocaleDb();
        var english = localeService.GetLocaleDb("en");
        var items = new Dictionary<string, CatalogItem>(StringComparer.OrdinalIgnoreCase);
        var englishNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach ((MongoId id, TemplateItem template) in templateTable.Items)
        {
            if (!string.Equals(template.Type, "Item", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string key = id.ToString();
            string name = locale.GetValueOrDefault($"{key} Name") ?? template.Name ?? key;
            string shortName = locale.GetValueOrDefault($"{key} ShortName") ?? string.Empty;
            string parentId = template.Parent.ToString();
            string parentName = templateTable.Items.TryGetValue(template.Parent, out var parent)
                ? locale.GetValueOrDefault($"{parentId} Name") ?? parent.Name ?? parentId
                : parentId;

            items[key] = new CatalogItem(
                key,
                name,
                shortName,
                parentId,
                parentName,
                template.Properties?.Width ?? 1,
                template.Properties?.Height ?? 1
            );

            if (english.TryGetValue($"{key} Name", out var en))
            {
                englishNames[key] = en;
            }
        }

        _items = items;
        _englishNames = englishNames;
    }

    public CatalogItem? Get(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return _items.GetValueOrDefault(id.Trim());
    }

    public string NameOf(string id) => Get(id)?.Name ?? id;

    public bool Exists(string id) => _items.ContainsKey(id);

    public IEnumerable<CatalogItem> Search(string? text, int max = 40)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        text = text.Trim();

        if (_items.TryGetValue(text, out var exact))
        {
            return [exact];
        }

        return _items.Values
            .Select(item => (item, score: Score(item, text)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.item.Name)
            .Take(max)
            .Select(x => x.item);
    }

    private int Score(CatalogItem item, string text)
    {
        var en = _englishNames.GetValueOrDefault(item.Id) ?? string.Empty;

        if (item.ShortName.Equals(text, StringComparison.OrdinalIgnoreCase)) return 100;
        if (item.Name.Equals(text, StringComparison.OrdinalIgnoreCase) || en.Equals(text, StringComparison.OrdinalIgnoreCase)) return 90;
        if (item.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase) || en.StartsWith(text, StringComparison.OrdinalIgnoreCase)) return 70;
        if (item.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || en.Contains(text, StringComparison.OrdinalIgnoreCase)) return 50;
        if (item.ShortName.Contains(text, StringComparison.OrdinalIgnoreCase)) return 40;
        if (item.Id.StartsWith(text, StringComparison.OrdinalIgnoreCase)) return 30;
        return 0;
    }
}
