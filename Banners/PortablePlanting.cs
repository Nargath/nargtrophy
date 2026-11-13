using NargTrophy.Config;
using UnityEngine;

namespace NargTrophy.Banners;

/// <summary>
/// Quick-plant (Agreed): using a portable banner item plants its banner at the player's feet, with no ghost,
/// keeping the item. Checks run in the documented order and a failure shows a message without starting the
/// cooldown: cooldown, cap, spot (ship, liquid, mid-air, slope), ward.
///
/// Spot checks use the player's own movement state, so they match what the player feels (verified against
/// the decompile, 2026-11-13):
/// - Ship: <c>GetStandingOnShip()</c> or <c>IsAttachedToShip()</c>.
/// - Liquid: <c>InLiquid()</c> (any depth; liquid level is max(water, tar), so tar is blocked too).
/// - Mid-air: <c>IsOnGround()</c>.
/// - Slope: the angle of the last ground normal above <c>GetSlideAngle()</c> (38° for players), the same test
///   vanilla uses to start sliding, or already sliding.
/// </summary>
internal static class PortablePlanting
{
    public static void TryPlant(Player player, BannerDefinition def, ItemDrop.ItemData item)
    {
        if (!def.IsActive)
        {
            player.Message(MessageHud.MessageType.Center, "$nt_msg_inactive");
            return;
        }

        float cooldown = BannerCooldowns.Remaining(player, def);
        if (cooldown > 0f)
        {
            player.Message(MessageHud.MessageType.Center,
                Localization.instance.Localize("$nt_msg_cooldown", BannerCooldowns.Format(cooldown)));
            return;
        }

        int cap = ModConfig.PortableMaxActive.Value;
        if (cap > 0 && CountPlantedBy(player) >= cap)
        {
            player.Message(MessageHud.MessageType.Center,
                Localization.instance.Localize("$nt_msg_cap", cap.ToString()));
            return;
        }

        string? problem = CheckSpot(player);
        if (problem != null)
        {
            player.Message(MessageHud.MessageType.Center, problem);
            return;
        }

        Vector3 position = player.m_lastGroundPoint;
        // Fall back to the feet if the stored contact point is stale (far from where the player stands).
        if ((position - player.transform.position).sqrMagnitude > 1f) position = player.transform.position;

        if (ModConfig.PortableRespectWards.Value && !PrivateArea.CheckAccess(position))
        {
            player.Message(MessageHud.MessageType.Center, "$nt_msg_ward");
            return;
        }

        GameObject? prefab = BannerPieces.PlantedPrefab(def);
        if (prefab == null || ZNetScene.instance == null) return;

        // Turned so the standard's front (cloth face and trophy) faces the planter: front yaw = view yaw + 180.
        float viewYaw = player.transform.rotation.eulerAngles.y;
        Quaternion rotation = Quaternion.Euler(0f, viewYaw + 180f - BannerPieces.FrontYaw(def), 0f);
        GameObject planted = Object.Instantiate(prefab, position, rotation);
        // The item's quality is the standard's level (upgraded at the crafting station like gear).
        if (planted.TryGetComponent(out TrophyBanner banner)) banner.Setup(player, def.Portable.Duration, Mathf.Clamp(item.m_quality, 1, def.Portable.TopLevel));
        if (prefab.TryGetComponent(out Piece piece))
            piece.m_placeEffect.Create(position, rotation, planted.transform, 1f, -1, player.GetZDOID());

        BannerCooldowns.Start(player, def);
        BannerAura.MarkDirty();
        player.Message(MessageHud.MessageType.TopLeft, "$nt_msg_planted");
    }

    /// <summary>Null when the player's spot is valid, otherwise the message to show.</summary>
    private static string? CheckSpot(Player player)
    {
        if (player.GetStandingOnShip() != null || player.IsAttachedToShip()) return "$nt_msg_ship";
        if (player.InLiquid() || player.IsSwimming()) return "$nt_msg_water";
        if (!player.IsOnGround()) return "$nt_msg_air";

        float angle = Mathf.Acos(Mathf.Clamp01(player.m_lastGroundNormal.y)) * Mathf.Rad2Deg;
        if (player.m_sliding || angle > player.GetSlideAngle()) return "$nt_msg_slope";
        return null;
    }

    /// <summary>Unexpired portable banners this player planted, among those loaded on this client.</summary>
    private static int CountPlantedBy(Player player)
    {
        long id = player.GetPlayerID();
        int count = 0;
        foreach (TrophyBanner banner in TrophyBanner.Active)
        {
            if (banner == null || banner.Kind != BannerKind.Portable) continue;
            if (banner.OwnerId == id && !banner.IsExpired()) count++;
        }
        return count;
    }
}
