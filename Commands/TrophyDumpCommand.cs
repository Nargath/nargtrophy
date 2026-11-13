using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Jotunn.Entities;
using NargTrophy.Config;
using UnityEngine;

namespace NargTrophy.Commands;

/// <summary>
/// <c>nt_trophies</c>: lists every creature whose drops include a trophy, with the real drop chance in this game
/// (after drop-table mods have changed drops, so run it in a world), whether it's a boss, and which banner covers
/// it. Read-only for the game.
///
/// Phase 4: also writes paste-ready lines for creatures with no banner to <c>suggested-banners.yml</c> in the
/// config folder (a name the banner loader ignores). Nothing goes live by itself: the user reviews the lines and
/// copies them into a banner file (agreed 2026-11-13). Only runs when typed, so it costs nothing during play.
/// </summary>
internal sealed class TrophyDumpCommand : ConsoleCommand
{
    public const string SuggestionsFile = "suggested-banners.yml";
    private static readonly Regex NotIdChar = new("[^A-Za-z0-9_]");
    private static readonly Regex RichText = new("<[^>]+>");

    public override string Name => "nt_trophies";

    public override string Help => "NargTrophy: list creatures that drop trophies (BepInEx log) and write suggested banner lines for uncovered ones (" + SuggestionsFile + ")";

    public override void Run(string[] args)
    {
        if (ZNetScene.instance == null)
        {
            Console.instance.Print("nt_trophies: load into a world first.");
            return;
        }

        var covered = new Dictionary<string, string>();
        var bannersByTrophy = new Dictionary<string, List<BannerDefinition>>();
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BannerDefinition def in BannerRegistry.All)
        {
            usedIds.Add(def.Id);
            foreach (string creature in new[] { def.Creature }.Concat(def.AlsoMatches)) covered[creature] = def.Id;
            if (!bannersByTrophy.TryGetValue(def.Trophy, out List<BannerDefinition> list)) bannersByTrophy[def.Trophy] = list = new List<BannerDefinition>();
            list.Add(def);
        }

        var rows = new List<string>();
        var suggestions = new List<(string sortKey, string line)>();
        var suggested = new HashSet<string>();
        foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
        {
            if (prefab == null || !prefab.TryGetComponent(out Character character)) continue;
            if (!prefab.TryGetComponent(out CharacterDrop drops) || drops.m_drops == null) continue;

            foreach (CharacterDrop.Drop drop in drops.m_drops)
            {
                if (drop?.m_prefab == null || !drop.m_prefab.TryGetComponent(out ItemDrop item)) continue;
                if (item.m_itemData.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Trophy) continue;

                string banner = covered.TryGetValue(prefab.name, out string id) ? id : "-";
                string name = RichText.Replace(Localization.instance.Localize(character.m_name), "");
                rows.Add($"{prefab.name,-24} {drop.m_prefab.name,-26} {drop.m_chance * 100f,5:0.#}%  {Bucket(drop.m_chance),-8} " +
                         $"{(character.m_boss ? "BOSS" : ""),-4}  banner: {banner,-18} {name}");

                if (banner == "-" && !character.m_boss && !BannerRegistry.NoBanner.Contains(prefab.name) && suggested.Add(prefab.name))
                    suggestions.Add((prefab.name, Suggest(prefab.name, drop.m_prefab.name, drop.m_chance, name, character.m_name, bannersByTrophy, usedIds)));
            }
        }

        rows.Sort();
        var text = new StringBuilder();
        text.AppendLine($"nt_trophies: {rows.Count} creature trophy drop(s). creature / trophy / chance / bucket / boss / banner / name");
        foreach (string row in rows) text.AppendLine(row);
        Plugin.Log.LogInfo(text.ToString());

        string? written = WriteSuggestions(suggestions.OrderBy(s => s.sortKey, StringComparer.OrdinalIgnoreCase).Select(s => s.line).ToList());
        Console.instance.Print($"nt_trophies: {rows.Count} rows written to the BepInEx log (LogOutput.log).");
        Console.instance.Print(written != null
            ? $"nt_trophies: {suggestions.Count} suggested banner line(s) for uncovered creatures in {written}"
            : "nt_trophies: could not write the suggestions file; see the log.");
    }

    /// <summary>
    /// One paste-ready line. Commented out (with the reason) when it's probably not wanted: a variant with the same
    /// name and trophy as an existing banner (belongs in its alsoMatches), or a guaranteed drop (unique or quest creature).
    /// </summary>
    private static string Suggest(string prefab, string trophy, float chance, string name, string nameToken,
        Dictionary<string, List<BannerDefinition>> bannersByTrophy, HashSet<string> usedIds)
    {
        string comment = $"{name}, {chance * 100f:0.#}% drop";

        if (bannersByTrophy.TryGetValue(trophy, out List<BannerDefinition> sameTrophy))
        {
            BannerDefinition? sameName = sameTrophy.FirstOrDefault(d => d.CreatureName == nameToken);
            if (sameName != null)
                return $"  # {prefab}: same name and trophy as banner '{sameName.Id}'. Probably add '{prefab}' to its alsoMatches. ({comment})";
        }

        string id = UniqueId(NotIdChar.Replace(prefab, ""), usedIds);
        string line = $"  - {{ id: {id}, creature: {prefab}, trophy: {trophy}, rarity: {Bucket(chance)} }}  # {comment}";
        return chance >= 1f ? $"  # guaranteed drop: unique or quest creature?\n  #{line.Substring(1)}" : line;
    }

    private static string UniqueId(string baseId, HashSet<string> usedIds)
    {
        if (baseId.Length == 0) baseId = "Creature";
        string id = baseId;
        for (int n = 2; usedIds.Contains(id); n++) id = baseId + n;
        usedIds.Add(id);
        return id;
    }

    private static string? WriteSuggestions(List<string> lines)
    {
        string path = Path.Combine(BannerRegistry.Folder, SuggestionsFile);
        var text = new StringBuilder();
        text.AppendLine("# Suggested NargTrophy banners, written by the nt_trophies console command.");
        text.AppendLine("# NOT loaded by the mod. Review the lines you want and copy them under 'banners:' in a banner file:");
        text.AppendLine("# banners.yml, or a file of your own such as banners.SomeMod.yml (any banners.<name>.yml is loaded).");
        text.AppendLine("#");
        text.AppendLine("# For each line you keep:");
        text.AppendLine("#   - add  biome: <Meadows, BlackForest, ...>  for extra materials and a cloth colour (see banners.yml defaults);");
        text.AppendLine("#   - check the rarity: it comes from the drop chance in this world;");
        text.AppendLine("#   - optionally put  requiresMod: <plugin GUID>  at the top of the file, so its banners are skipped");
        text.AppendLine("#     quietly when that mod isn't installed.");
        text.AppendLine("# Restart the game after adding banners.");
        text.AppendLine("#");
        text.AppendLine("# Left out: bosses, and creatures listed under 'noBanner:' in any banner file (add a creature there");
        text.AppendLine("# to stop it being suggested). Run nt_trophies again to refresh this file.");
        text.AppendLine();
        text.AppendLine("banners:");
        if (lines.Count == 0) text.AppendLine("  # Every creature with a trophy already has a banner.");
        foreach (string line in lines) text.AppendLine(line);

        try
        {
            File.WriteAllText(path, text.ToString());
            return path;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"nt_trophies: could not write {path}: {e.Message}");
            return null;
        }
    }

    /// <summary>Same thresholds as docs/02: common ≥ 25%, uncommon ≥ 10%, rare below.</summary>
    private static string Bucket(float chance) => chance >= 0.25f ? "common" : chance >= 0.10f ? "uncommon" : "rare";
}
