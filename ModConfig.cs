using BepInEx.Configuration;

namespace NargTrophy;

/// <summary>
/// General BepInEx settings. Banner values live in banners.yml (server-synced separately, see Sync/BannerSync).
/// Gameplay settings are admin-only and server-synced by Jötunn (IsAdminOnly); debug settings are client-side.
/// </summary>
internal static class ModConfig
{
    public static ConfigEntry<bool> LogDamage = null!;
    public static ConfigEntry<bool> ShowRadius = null!;

    public static ConfigEntry<int> PortableMaxActive = null!;
    public static ConfigEntry<bool> PortableRespectWards = null!;
    public static ConfigEntry<bool> PortableProtected = null!;

    public static void Bind(ConfigFile config)
    {
        LogDamage = config.Bind("Debug", "LogDamage", false,
            "Log every hit a banner changes, with damage before and after. Client-side; for testing balance.");

        ShowRadius = config.Bind("Visuals", "ShowRadius", true,
            "Show a banner's area as a ward-style circle while you hover it, and while placing a trophy banner. Client-side.");

        // Agreed default (docs/07): no cap; the per-creature cooldown is the limit.
        PortableMaxActive = config.Bind("Portable", "MaxActivePerPlayer", 0,
            Synced("How many portable banners one player can have planted at once (different creatures). 0 = no cap. " +
                   "Only banners in loaded areas are counted. Server-synced."));

        // Agreed default (docs/07): war standards respect wards.
        PortableRespectWards = config.Bind("Portable", "RespectWards", true,
            Synced("If true, a portable banner can't be planted inside a ward you don't have access to. Server-synced."));

        // Proposed (docs/01): planted banners can't be damaged or removed by default.
        PortableProtected = config.Bind("Portable", "PlantedBannerProtected", true,
            Synced("If true, a planted portable banner can't be damaged or removed with the hammer. Server-synced."));
    }

    private static ConfigDescription Synced(string description) =>
        new(description, null, new ConfigurationManagerAttributes { IsAdminOnly = true });
}
