using System.Collections.Generic;
using HarmonyLib;
using NargTrophy.Banners;
using UnityEngine;

namespace NargTrophy.Patches;

/// <summary>
/// Refund of upgrade costs (agreed 2026-11-13: refund all). When a trophy banner drops its resources
/// (deconstructed with the hammer, or destroyed), it also drops everything recorded as spent on its upgrades in
/// the ZDO. Same spawn as vanilla's Piece.DropResources (verified against the decompile, 2026-11-13), and skipped
/// in the same free-build case. Recorded amounts are used, not the current config, so a changed cost never
/// refunds more or less than was paid.
/// </summary>
[HarmonyPatch(typeof(Piece), nameof(Piece.DropResources))]
internal static class UpgradePatches
{
    private static void Postfix(Piece __instance)
    {
        if (!__instance.TryGetComponent(out TrophyBanner banner)) return;
        ZNetView? nview = __instance.GetComponent<ZNetView>();
        if (nview == null || nview.GetZDO() is not { } zdo) return;
        if (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(__instance.FreeBuildKey())) return;

        Dictionary<string, int> spent = BannerUpgrades.ParseSpent(zdo.GetString(BannerUpgrades.ZdoSpent));
        if (spent.Count == 0) return;
        zdo.Set(BannerUpgrades.ZdoSpent, ""); // never twice

        Vector3 position = __instance.transform.position + Vector3.up * __instance.m_returnResourceHeightOffset;
        foreach (KeyValuePair<string, int> pair in spent)
        {
            GameObject? prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(pair.Key) : null;
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"Banner '{banner.m_bannerId}': can't refund unknown upgrade item '{pair.Key}'.");
                continue;
            }
            int left = pair.Value;
            while (left > 0)
            {
                ItemDrop drop = Object.Instantiate(prefab, position, Quaternion.identity).GetComponent<ItemDrop>();
                drop.SetStack(Mathf.Min(left, drop.m_itemData.m_shared.m_maxStackSize));
                ItemDrop.OnCreateNew(drop);
                left -= drop.m_itemData.m_stack;
            }
        }
    }
}
