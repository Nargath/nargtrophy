using System.Collections.Generic;
using System.Linq;
using System.Text;
using Jotunn.Entities;
using NargTrophy.Config;
using UnityEngine;

namespace NargTrophy.Commands;

/// <summary>
/// <c>nt_trophies</c>: lists every creature whose drops include a trophy, with the real drop chance in this
/// game (after DropNSpawn or any other mod has changed drops, so run it in a world), whether it's a boss, and
/// which banner covers it. Read-only. Used to check rarity buckets in banners.yml; an early slice of the
/// phase 4 nt_generate tool.
/// </summary>
internal sealed class TrophyDumpCommand : ConsoleCommand
{
    public override string Name => "nt_trophies";

    public override string Help => "NargTrophy: list creatures that drop trophies, with drop chance and banner coverage (written to the BepInEx log)";

    public override void Run(string[] args)
    {
        if (ZNetScene.instance == null)
        {
            Console.instance.Print("nt_trophies: load into a world first.");
            return;
        }

        var covered = new Dictionary<string, string>();
        foreach (BannerDefinition def in BannerRegistry.All)
            foreach (string creature in new[] { def.Creature }.Concat(def.AlsoMatches))
                covered[creature] = def.Id;

        var rows = new List<string>();
        foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
        {
            if (prefab == null || !prefab.TryGetComponent(out Character character)) continue;
            if (!prefab.TryGetComponent(out CharacterDrop drops) || drops.m_drops == null) continue;

            foreach (CharacterDrop.Drop drop in drops.m_drops)
            {
                if (drop?.m_prefab == null || !drop.m_prefab.TryGetComponent(out ItemDrop item)) continue;
                if (item.m_itemData.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Trophy) continue;

                string banner = covered.TryGetValue(prefab.name, out string id) ? id : "-";
                string name = Localization.instance.Localize(character.m_name);
                rows.Add($"{prefab.name,-24} {drop.m_prefab.name,-26} {drop.m_chance * 100f,5:0.#}%  {Bucket(drop.m_chance),-8} " +
                         $"{(character.m_boss ? "BOSS" : ""),-4}  banner: {banner,-18} {name}");
            }
        }

        rows.Sort();
        var text = new StringBuilder();
        text.AppendLine($"nt_trophies: {rows.Count} creature trophy drop(s). creature / trophy / chance / bucket / boss / banner / name");
        foreach (string row in rows) text.AppendLine(row);
        Plugin.Log.LogInfo(text.ToString());
        Console.instance.Print($"nt_trophies: {rows.Count} rows written to the BepInEx log (LogOutput.log).");
    }

    /// <summary>Same thresholds as docs/02: common ≥ 25%, uncommon ≥ 10%, rare below.</summary>
    private static string Bucket(float chance) => chance >= 0.25f ? "common" : chance >= 0.10f ? "uncommon" : "rare";
}
