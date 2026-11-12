using System;
using System.Globalization;
using NargTrophy.Config;

namespace NargTrophy.Banners;

/// <summary>
/// Per-player, per-creature portable cooldowns. Stored in <c>Player.m_customData</c> (saved with the character)
/// as the expiry in server time ticks, keyed <c>NargTrophy.cd.&lt;id&gt;</c>. Measured from planting.
///
/// World-switch clamp: server time is per world. A stored expiry further away than the full cooldown length
/// can't be real in this world, so it counts as expired. The entry isn't deleted, so going back to the world
/// where it was set still finds it.
/// </summary>
internal static class BannerCooldowns
{
    private const string KeyPrefix = "NargTrophy.cd.";

    /// <summary>Server network time. Same value on the server and every client.</summary>
    public static bool TryGetNow(out long ticks)
    {
        ticks = 0;
        if (ZNet.instance == null) return false;
        ticks = ZNet.instance.GetTime().Ticks;
        return true;
    }

    /// <summary>Seconds left on this banner's cooldown for this player (0 if ready).</summary>
    public static float Remaining(Player player, BannerDefinition def)
    {
        if (player == null || !TryGetNow(out long now)) return 0f;
        if (!player.m_customData.TryGetValue(KeyPrefix + def.Id, out string raw)) return 0f;
        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long expiry)) return 0f;

        double remaining = TimeSpan.FromTicks(expiry - now).TotalSeconds;
        if (remaining <= 0d) return 0f;
        if (remaining > def.Portable.Cooldown + 1d) return 0f; // clamp: from another world, or the cooldown was shortened
        return (float)remaining;
    }

    public static void Start(Player player, BannerDefinition def)
    {
        if (def.Portable.Cooldown <= 0f || !TryGetNow(out long now)) return;
        long expiry = now + TimeSpan.FromSeconds(def.Portable.Cooldown).Ticks;
        player.m_customData[KeyPrefix + def.Id] = expiry.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Keeps one cooldown status effect per cooling banner, with the right time left. Called from the aura tick.</summary>
    public static void UpdateStatusEffects(Player player)
    {
        SEMan seman = player.GetSEMan();
        if (seman == null) return;

        foreach (BannerDefinition def in BannerRegistry.All)
        {
            if (!def.Registered) continue;
            int hash = BannerPieces.CooldownEffectHash(def);
            float remaining = player.IsDead() ? 0f : Remaining(player, def);
            StatusEffect? current = seman.GetStatusEffect(hash);

            if (remaining <= 0f)
            {
                if (current != null) seman.RemoveStatusEffect(hash, quiet: true);
                continue;
            }

            current ??= seman.AddStatusEffect(hash);
            if (current == null) continue;
            // Keep the HUD timer on the stored expiry (also fixes it after relog or a shortened cooldown).
            float shown = current.m_ttl - current.m_time;
            if (Math.Abs(shown - remaining) > 1.5f)
            {
                current.m_ttl = remaining;
                current.m_time = 0f;
            }
        }
    }

    /// <summary>"8:42" style.</summary>
    public static string Format(float seconds)
    {
        int total = Math.Max(0, (int)Math.Ceiling(seconds));
        return total >= 3600
            ? $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}"
            : $"{total / 60}:{total % 60:00}";
    }
}
