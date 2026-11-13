using HarmonyLib;
using NargTrophy.Banners;

namespace NargTrophy.Patches;

/// <summary>
/// Applies banner values to hits. Both patches only read <see cref="BannerAura"/>'s cache.
///
/// Outgoing: the attacker's client builds the hit and calls Character.Damage, which forwards it to the
/// target's owner by RPC. We scale it here, before it leaves, when the local player is the attacker.
///
/// Incoming: a creature's hit arrives on the player's own client through Character.RPC_Damage. We scale it
/// there, before armour, blocking and status effects (so poison/fire carried by the hit shrink too).
///
/// The two never apply to the same hit: outgoing needs a non-player target, incoming a player target.
/// Only damage is scaled (HitData.m_damage); push force and the stagger multiplier are left alone.
/// Low priority so other mods' prefixes run first; pure multipliers combine in any order.
/// </summary>
[HarmonyPatch]
internal static class DamagePatches
{
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Low)]
    private static void OutgoingBonus(Character __instance, HitData hit)
    {
        Player local = Player.m_localPlayer;
        if (local == null || hit == null) return;
        if (!hit.m_attacker.Equals(local.GetZDOID())) return;
        if (__instance.IsPlayer()) return; // never PvP

        if (!TryGetPrefabHash(__instance, out int hash)) return;
        if (!BannerAura.TryGet(hash, out BannerAura.Effect effect) || effect.Bonus <= 0f) return;

        float before = hit.GetTotalDamage();
        hit.m_damage.Modify(1f + effect.Bonus);
        if (ModConfig.LogDamage.Value)
            Plugin.Log.LogInfo($"[banner] dealt to {__instance.name}: {before:0.##} -> {hit.GetTotalDamage():0.##} (+{TrophyBanner.Percent(effect.Bonus)}%)");
    }

    [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Low)]
    private static void IncomingReduction(Character __instance, HitData hit)
    {
        if (hit == null || __instance != Player.m_localPlayer) return;
        if (__instance.m_nview == null || !__instance.m_nview.IsOwner()) return;
        if (!hit.HaveAttacker()) return; // no recorded attacker (AoE clouds, environment): out of scope (docs/04)

        Character attacker = hit.GetAttacker();
        if (attacker == null || attacker.IsPlayer()) return; // never PvP

        if (!TryGetPrefabHash(attacker, out int hash)) return;
        if (!BannerAura.TryGet(hash, out BannerAura.Effect effect) || effect.Reduction <= 0f) return;

        float before = hit.GetTotalDamage();
        hit.m_damage.Modify(1f - effect.Reduction);
        if (ModConfig.LogDamage.Value)
            Plugin.Log.LogInfo($"[banner] taken from {attacker.name}: {before:0.##} -> {hit.GetTotalDamage():0.##} (-{TrophyBanner.Percent(effect.Reduction)}%)");
    }

    /// <summary>The creature's prefab hash from its ZDO. Starred creatures share their prefab, so they match.</summary>
    private static bool TryGetPrefabHash(Character character, out int hash)
    {
        hash = 0;
        ZDO? zdo = character.m_nview != null ? character.m_nview.GetZDO() : null;
        if (zdo == null) return false;
        hash = zdo.GetPrefab();
        return true;
    }
}
