using BepInEx.Configuration;

namespace NargTrophy;

/// <summary>
/// General BepInEx settings. Banner values live in banners.yml (server-synced separately, see Sync/BannerSync).
/// Phase 1 has only client-side debug settings; synced gameplay toggles arrive with later phases.
/// </summary>
internal static class ModConfig
{
    public static ConfigEntry<bool> LogDamage = null!;

    public static void Bind(ConfigFile config)
    {
        LogDamage = config.Bind("Debug", "LogDamage", false,
            "Log every hit a banner changes, with damage before and after. Client-side; for testing balance.");
    }
}
