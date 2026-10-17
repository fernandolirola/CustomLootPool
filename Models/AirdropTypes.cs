using SPTarkov.Server.Core.Models.Enums;

namespace CustomLootPool.Models;

public static class AirdropTypes
{
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { "mixed", "weaponArmor", "foodMedical", "barter", "radar" });

    private static readonly Dictionary<string, string> Crates = new(StringComparer.OrdinalIgnoreCase)
    {
        [ItemTpl.LOOTCONTAINER_AIRDROP_COMMON_SUPPLY_CRATE.ToString()] = "mixed",
        [ItemTpl.LOOTCONTAINER_AIRDROP_WEAPON_CRATE.ToString()] = "weaponArmor",
        [ItemTpl.LOOTCONTAINER_AIRDROP_MEDICAL_CRATE.ToString()] = "foodMedical",
        [ItemTpl.LOOTCONTAINER_AIRDROP_SUPPLY_CRATE.ToString()] = "barter",
        [ItemTpl.LOOTCONTAINER_AIRDROP_TECHNICAL_SUPPLY_CRATE_EVENT_1.ToString()] = "radar",
    };

    internal static string? FromCrate(string template) => Crates.GetValueOrDefault(template);

    internal static bool Matches(AirdropSettings settings, string template) =>
        settings.Types.Count == 0 || settings.Types.Contains(FromCrate(template), StringComparer.OrdinalIgnoreCase);
}
