using System.Text;
using Jotunn.Entities;
using NargTrophy.Banners;
using NargTrophy.Config;
using UnityEngine;

namespace NargTrophy.Commands;

/// <summary>
/// <c>nt_standards</c>: lists every loaded war standard with its time left, planter, position and distance, to the
/// console and the BepInEx log. Read-only; for testing.
/// </summary>
internal sealed class StandardsCommand : ConsoleCommand
{
    public override string Name => "nt_standards";

    public override string Help => "NargTrophy: list loaded war standards with time left";

    public override void Run(string[] args)
    {
        var text = new StringBuilder();
        int count = 0;
        foreach (TrophyBanner banner in TrophyBanner.Active)
        {
            if (banner == null || banner.Kind != BannerKind.Portable) continue;
            count++;
            float? left = banner.SecondsLeft();
            float distance = Player.m_localPlayer != null
                ? Vector3.Distance(Player.m_localPlayer.transform.position, banner.transform.position)
                : -1f;
            text.AppendLine($"{banner.m_bannerId}: {(left is float s ? (s > 0f ? $"{s:0} s left" : "expired") : "expiry unset")}, " +
                            $"planter {banner.OwnerId}, at {banner.transform.position:F1} ({distance:F1} m from you)");
        }

        string result = $"nt_standards: {count} loaded war standard(s)\n{text}";
        Plugin.Log.LogInfo(result);
        foreach (string line in result.Split('\n'))
            if (line.Length > 0) Console.instance.Print(line);
    }
}
