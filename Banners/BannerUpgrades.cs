using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NargTrophy.Config;
using UnityEngine;

namespace NargTrophy.Banners;

/// <summary>
/// Upgrades (phase 5, agreed 2026-11-13): 3 levels on top of the base; damage bonus, damage reduction and
/// radius only; each level costs trophies (by rarity) plus the biome material, with vanilla's quality cost
/// multiplier (x1, x2, x4).
///
/// - War standard: vanilla item quality, upgraded at its crafting station (the recipe carries the per-level
///   amounts). The planted standard stores the item's quality as its level.
/// - Trophy banner: hover it and press Use (E) to pay the next level's cost from the inventory. The level and
///   everything spent on upgrades are stored in the piece's ZDO; deconstructing it refunds all of it
///   (Patches/UpgradePatches).
/// </summary>
internal static class BannerUpgrades
{
    internal const string ZdoLevel = "nt_level";
    /// <summary>Upgrade items spent on a trophy banner, "Prefab:amount;Prefab:amount", for the refund.</summary>
    internal const string ZdoSpent = "nt_spent";

    /// <summary>The item's display name token for a prefab name, or the prefab name if unknown.</summary>
    public static string ItemName(string prefab)
    {
        GameObject? item = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
        return item != null && item.TryGetComponent(out ItemDrop drop) ? drop.m_itemData.m_shared.m_name : prefab;
    }

    /// <summary>"8 $item_trophy_greydwarf, 20 $item_wood" for hover text (localised by the caller).</summary>
    public static string Describe(List<ExtraCost> cost) =>
        string.Join(", ", cost.Select(c => $"{c.Amount} {ItemName(c.Item)}"));

    /// <summary>
    /// Pays and applies the next level on a trophy banner. Returns the message to show the player.
    /// Ward permission is required, like building. The no-cost cheat skips payment.
    /// </summary>
    public static string TryUpgrade(Player player, ZNetView nview, BannerDefinition def)
    {
        BannerValues values = def.Permanent;
        int level = GetLevel(nview, def);
        if (level >= values.TopLevel) return "$nt_msg_max_level";
        if (!PrivateArea.CheckAccess(nview.transform.position)) return "$msg_privatezone";

        List<ExtraCost> cost = values.UpgradeCost(def.Trophy, level + 1);
        bool free = player.NoCostCheat();
        if (!free)
        {
            Inventory inventory = player.GetInventory();
            List<ExtraCost> missing = cost.Where(c => inventory.CountItems(ItemName(c.Item)) < c.Amount).ToList();
            if (missing.Count > 0)
                return Localization.instance.Localize("$nt_msg_upgrade_missing", Localization.instance.Localize(Describe(missing)));
            foreach (ExtraCost c in cost) inventory.RemoveItem(ItemName(c.Item), c.Amount);
        }

        // Only the owner writes a ZDO; claim it (as vanilla does for interactions).
        nview.ClaimOwnership();
        ZDO zdo = nview.GetZDO();
        zdo.Set(ZdoLevel, level + 1);
        if (!free) zdo.Set(ZdoSpent, AddSpent(zdo.GetString(ZdoSpent), cost));
        return Localization.instance.Localize("$nt_msg_upgraded", (level + 1).ToString());
    }

    /// <summary>The banner's level, clamped to what the current config allows (a lowered cap lowers it too).</summary>
    public static int GetLevel(ZNetView? nview, BannerDefinition def)
    {
        int stored = nview != null && nview.GetZDO() is { } zdo ? zdo.GetInt(ZdoLevel, 1) : 1;
        return Mathf.Clamp(stored, 1, def.Permanent.TopLevel);
    }

    public static Dictionary<string, int> ParseSpent(string text)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string part in text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = part.LastIndexOf(':');
            if (colon <= 0) continue;
            if (!int.TryParse(part.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) || amount <= 0) continue;
            string item = part.Substring(0, colon);
            result[item] = result.TryGetValue(item, out int existing) ? existing + amount : amount;
        }
        return result;
    }

    private static string AddSpent(string existing, List<ExtraCost> cost)
    {
        Dictionary<string, int> spent = ParseSpent(existing);
        foreach (ExtraCost c in cost) spent[c.Item] = spent.TryGetValue(c.Item, out int n) ? n + c.Amount : c.Amount;
        var text = new StringBuilder();
        foreach (KeyValuePair<string, int> pair in spent)
            text.Append(pair.Key).Append(':').Append(pair.Value.ToString(CultureInfo.InvariantCulture)).Append(';');
        return text.ToString();
    }
}
