using HarmonyLib;
using NargTrophy.Banners;
using NargTrophy.Config;

namespace NargTrophy.Patches;

/// <summary>
/// Portable banner hooks (verified against the decompile, 2026-11-13).
///
/// Using a consumable from the hotbar (<c>Player.UseHotbarItem</c>) or the inventory goes through
/// <c>Humanoid.UseItem</c>, which calls <c>Player.ConsumeItem</c> without touching equipped weapons. We take
/// over ConsumeItem for banner items: plant, never remove the item, and return false so vanilla skips the
/// eating animation and sound.
///
/// Planted banners are protected through <c>WearNTear.RPC_Damage</c>, which runs on the banner's owner.
/// Hammer removal is blocked by <c>Piece.m_canBeRemoved</c> (set by TrophyBanner from the same setting).
/// </summary>
[HarmonyPatch]
internal static class PortablePatches
{
    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeItem))]
    [HarmonyPrefix]
    private static bool UseBannerItem(Player __instance, ItemDrop.ItemData item, ref bool __result)
    {
        if (item == null || !BannerPieces.TryGetPortable(item, out BannerDefinition def)) return true;

        __result = false;
        if (__instance == Player.m_localPlayer) PortablePlanting.TryPlant(__instance, def, item);
        return false;
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.RPC_Damage))]
    [HarmonyPrefix]
    private static bool ProtectPlanted(WearNTear __instance)
    {
        if (!ModConfig.PortableProtected.Value) return true;
        return !(__instance.TryGetComponent(out TrophyBanner banner) && banner.Kind == BannerKind.Portable);
    }
}
