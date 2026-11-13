using System.Text;
using Jotunn.Entities;
using Jotunn.Managers;
using NargTrophy.Banners;
using NargTrophy.Config;
using UnityEngine;

namespace NargTrophy.Commands;

/// <summary>
/// <c>nt_remount</c>: rebuilds the trophy mount on every banner prefab and every loaded banner from the current
/// banners.yml values (trophyMount size / offset / rotation), so the mount can be tuned live: edit, save, run.
/// Writes one diagnostic line per loaded banner to the BepInEx log. Visual only; nothing is saved.
/// </summary>
internal sealed class RemountCommand : ConsoleCommand
{
    public override string Name => "nt_remount";

    public override string Help => "NargTrophy: rebuild trophy mounts from banners.yml (trophyMount) on all loaded banners, and log their placement";

    public override void Run(string[] args)
    {
        int prefabs = 0;
        foreach (BannerDefinition def in BannerRegistry.All)
        {
            if (!def.Registered || !TryGetTrophy(def, out ItemDrop trophy)) continue;
            if (PrefabManager.Instance.GetPrefab(BannerPieces.PermanentPrefabName(def)) is { } permanent)
            {
                BannerVisuals.MountTrophy(permanent, trophy, def.Permanent, def.Id);
                prefabs++;
            }
            if (BannerPieces.PlantedPrefab(def) is { } planted)
            {
                BannerVisuals.MountTrophy(planted, trophy, def.Portable, def.Id);
                prefabs++;
            }
        }

        var log = new StringBuilder();
        int placed = 0;
        foreach (TrophyBanner banner in TrophyBanner.Active)
        {
            if (banner == null || !BannerRegistry.TryGet(banner.m_bannerId, out BannerDefinition def)) continue;
            if (!TryGetTrophy(def, out ItemDrop trophy)) continue;
            string? line = BannerVisuals.MountTrophy(banner.gameObject, trophy, def.Get(banner.Kind), def.Id);
            if (line != null) log.AppendLine($"[{banner.Kind}] {line}");
            placed++;
        }

        Plugin.Log.LogInfo($"nt_remount: rebuilt {prefabs} prefab mount(s) and {placed} placed banner(s).\n{log}");
        Console.instance.Print($"nt_remount: rebuilt {placed} placed banner(s); details in the BepInEx log.");
    }

    private static bool TryGetTrophy(BannerDefinition def, out ItemDrop trophy)
    {
        trophy = null!;
        GameObject? prefab = PrefabManager.Instance.GetPrefab(def.Trophy);
        return prefab != null && prefab.TryGetComponent(out trophy);
    }
}
