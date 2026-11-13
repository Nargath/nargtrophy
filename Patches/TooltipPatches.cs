using System.Text;
using HarmonyLib;
using NargTrophy.Banners;
using NargTrophy.Config;

namespace NargTrophy.Patches;

/// <summary>
/// War standard tooltip: what it will do once planted, at the quality being shown. The inventory passes the
/// item's own quality; the crafting screen passes the level it previews, so an upgrade shows the next level's
/// values. Outside crafting it also says whether this player can plant it now (cooldown).
/// Verified against the decompile (2026-11-13): both go through the static ItemDrop.ItemData.GetTooltip.
/// </summary>
[HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
    typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool))]
internal static class TooltipPatches
{
    private static void Postfix(ItemDrop.ItemData item, int qualityLevel, bool crafting, ref string __result)
    {
        if (item == null || !BannerPieces.TryGetPortable(item, out BannerDefinition def)) return;

        BannerValues kind = def.Portable;
        LevelValues values = kind.At(qualityLevel);
        string creature = def.CreatureName;
        var text = new StringBuilder(__result);
        text.Append("\n\n<color=orange>$nt_tip_when_planted</color>");
        if (kind.TopLevel > 1) text.Append($"\n$nt_hover_level <color=orange>{values.Level}/{kind.TopLevel}</color>");
        text.Append($"\n<color=orange>+{TrophyBanner.Percent(values.DamageBonus)}%</color> $nt_hover_damage_dealt ({creature})");
        text.Append($"\n<color=orange>-{TrophyBanner.Percent(values.DamageReduction)}%</color> $nt_hover_damage_taken ({creature})");
        text.Append($"\n$nt_hover_radius: <color=orange>{values.Radius:0.#} m</color>");
        text.Append($"\n$nt_tip_duration: <color=orange>{BannerCooldowns.Format(kind.Duration)}</color>");
        text.Append($"\n$nt_tip_cooldown: <color=orange>{BannerCooldowns.Format(kind.Cooldown)}</color>");

        Player player = Player.m_localPlayer;
        if (!crafting && player != null)
        {
            float left = BannerCooldowns.Remaining(player, def);
            text.Append(left > 0f
                ? $"\n$nt_tip_status: <color=orange>$nt_tip_replant {BannerCooldowns.Format(left)}</color>"
                : "\n$nt_tip_status: <color=#7fd17f>$nt_tip_ready</color>");
        }
        if (!def.IsActive) text.Append("\n$nt_hover_inactive");
        __result = text.ToString();
    }
}
