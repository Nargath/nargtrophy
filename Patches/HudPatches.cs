using System.Collections.Generic;
using HarmonyLib;
using NargTrophy.Banners;

namespace NargTrophy.Patches;

/// <summary>
/// The HUD effect row and the Active Effects list (inventory → Texts) both read <c>StatusEffect.m_name</c>
/// (verified against the decompile, 2026-11-13: <c>Hud.UpdateStatusEffects</c>, <c>TextsDialog.AddActiveEffects</c>).
/// The row needs short names to stay readable; the list needs full ones because it has no icons. While the list
/// is being built, our effects get their long name, then the short one is put back.
/// </summary>
[HarmonyPatch(typeof(TextsDialog), nameof(TextsDialog.AddActiveEffects))]
internal static class HudPatches
{
    private static readonly List<(StatusEffect effect, string shortName)> Swapped = new();
    private static readonly List<StatusEffect> Effects = new();

    private static void Prefix()
    {
        Swapped.Clear();
        Player player = Player.m_localPlayer;
        if (player == null || player.GetSEMan() == null) return;

        Effects.Clear();
        player.GetSEMan().GetHUDStatusEffects(Effects);
        foreach (StatusEffect effect in Effects)
        {
            if (effect is not IBannerEffect banner || string.IsNullOrEmpty(banner.LongName)) continue;
            Swapped.Add((effect, effect.m_name));
            effect.m_name = banner.LongName;
        }
    }

    // A finalizer runs even if the method throws, so the HUD never keeps the long names.
    private static void Finalizer()
    {
        foreach ((StatusEffect effect, string shortName) in Swapped)
            if (effect != null) effect.m_name = shortName;
        Swapped.Clear();
    }
}
