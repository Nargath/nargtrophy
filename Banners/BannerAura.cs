using System.Collections.Generic;
using NargTrophy.Config;
using UnityEngine;

namespace NargTrophy.Banners;

/// <summary>
/// Local player only. About once a second, finds the banners whose area the player stands in and builds a cache
/// of creature prefab hash → strongest bonus and strongest reduction. Overlapping banners for the same creature
/// never add up. Damage patches read the cache (one lookup per hit); the same pass updates the HUD effects
/// (aura and portable cooldown).
/// </summary>
internal static class BannerAura
{
    public struct Effect
    {
        public float Bonus;
        public float Reduction;
        /// <summary>Which kinds of banner cover the player (HUD label and Active Effects only).</summary>
        public bool FromPermanent;
        public bool FromPortable;
    }

    private const float TickSeconds = 1f;

    private static readonly Dictionary<int, Effect> ByCreature = new();
    private static readonly Dictionary<string, Effect> ByBanner = new();
    private static float _nextTick;

    public static void MarkDirty() => _nextTick = 0f;

    public static bool TryGet(int creatureHash, out Effect effect) => ByCreature.TryGetValue(creatureHash, out effect);

    /// <summary>The strongest values the player gets from this banner type right now (zero if none).</summary>
    public static Effect GetEffectFor(BannerDefinition def) => ByBanner.TryGetValue(def.Id, out Effect effect) ? effect : default;

    public static void Update()
    {
        if (Time.time < _nextTick) return;
        _nextTick = Time.time + TickSeconds;
        Rebuild();
    }

    private static void Rebuild()
    {
        ByCreature.Clear();
        ByBanner.Clear();

        Player player = Player.m_localPlayer;
        if (player == null) return;

        if (!player.IsDead())
        {
            Vector3 position = player.transform.position;
            foreach (TrophyBanner banner in TrophyBanner.Active)
            {
                if (banner == null || !banner.TryGetValues(out BannerDefinition def, out BannerValues values)) continue;

                // Flat distance, like a ward: height doesn't matter inside the circle.
                Vector3 offset = banner.transform.position - position;
                offset.y = 0f;
                if (offset.sqrMagnitude > values.Radius * values.Radius) continue;

                ByBanner[def.Id] = Strongest(ByBanner, def.Id, values, banner.Kind);
                foreach (int hash in def.CreatureHashes) ByCreature[hash] = Strongest(ByCreature, hash, values, banner.Kind);
            }
        }

        UpdateStatusEffects(player);
        BannerCooldowns.UpdateStatusEffects(player);
    }

    private static Effect Strongest<TKey>(Dictionary<TKey, Effect> map, TKey key, BannerValues values, BannerKind kind)
    {
        map.TryGetValue(key, out Effect current);
        return new Effect
        {
            Bonus = Mathf.Max(current.Bonus, values.DamageBonus),
            Reduction = Mathf.Max(current.Reduction, values.DamageReduction),
            FromPermanent = current.FromPermanent || kind == BannerKind.Permanent,
            FromPortable = current.FromPortable || kind == BannerKind.Portable,
        };
    }

    private static void UpdateStatusEffects(Player player)
    {
        SEMan seman = player.GetSEMan();
        if (seman == null) return;

        foreach (BannerDefinition def in BannerRegistry.All)
        {
            if (!def.Registered) continue;
            int hash = BannerPieces.AuraEffectHash(def);
            bool want = ByBanner.TryGetValue(def.Id, out Effect effect);
            StatusEffect? current = seman.GetStatusEffect(hash);
            if (!want)
            {
                if (current != null) seman.RemoveStatusEffect(hash, quiet: true);
                continue;
            }
            current ??= seman.AddStatusEffect(hash);
            // One icon per creature (values never stack). The HUD label names the kind: Guarded (trophy banner)
            // or Rallied (war standard, which wins because it's the one that runs out). The trophy icon shows the
            // creature; Active Effects gets the full name.
            if (current == null) continue;
            current.m_name = effect.FromPortable ? "$nt_hud_rallied" : "$nt_hud_guarded";
            // The icon's frame matches the label: cloth on a crossbar (Guarded) or on a pole (Rallied).
            Sprite? icon = effect.FromPortable ? BannerPieces.StandardIcon(def) : BannerPieces.BannerIcon(def);
            if (icon != null) current.m_icon = icon;
            if (current is SE_TrophyBanner aura)
                aura.m_longName = $"{current.m_name}: {def.CreatureName} {(effect.FromPortable ? "$nt_portable" : "$nt_banner")}";
        }
    }
}
