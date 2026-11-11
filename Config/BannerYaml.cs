using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NargTrophy.Config;

/// <summary>Parses <c>banners.yml</c> into <see cref="BannerDefinition"/>s. Invalid entries are skipped with a warning.</summary>
internal static class BannerYaml
{
    private static readonly Regex IdPattern = new("^[A-Za-z0-9_]+$");

    // Used when the file's defaults block leaves a value out. Placeholders from docs/01_Banners.md.
    private static readonly KindDto BuiltInPermanent = new()
    {
        Radius = 30f, DamageBonus = 0.15f, DamageReduction = 0.15f, Duration = 0f, Cooldown = 0f,
        Station = "piece_workbench", StationLevel = 1, Trophies = 8, SourcePrefab = "piece_banner01",
    };

    private static readonly KindDto BuiltInPortable = new()
    {
        Radius = 12f, DamageBonus = 0.30f, DamageReduction = 0.25f, Duration = 300f, Cooldown = 900f,
        Station = "piece_workbench", StationLevel = 1, Trophies = 10, SourcePrefab = "piece_banner01",
    };

    /// <summary>Parses the file text. Returns null if the YAML itself can't be read.</summary>
    public static List<BannerDefinition>? Parse(string text, string source)
    {
        FileDto? file;
        try
        {
            IDeserializer deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            file = deserializer.Deserialize<FileDto>(text);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Could not read {source}: {e.Message}");
            return null;
        }

        var result = new List<BannerDefinition>();
        if (file?.Banners == null)
        {
            Plugin.Log.LogWarning($"{source} defines no banners.");
            return result;
        }

        KindDto permanentDefaults = Merge(BuiltInPermanent, file.Defaults?.Permanent);
        KindDto portableDefaults = Merge(BuiltInPortable, file.Defaults?.Portable);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenCreatures = new Dictionary<string, string>(StringComparer.Ordinal);

        for (int i = 0; i < file.Banners.Count; i++)
        {
            BannerDto? dto = file.Banners[i];
            string where = $"{source}, banner #{i + 1}";
            if (dto == null) continue;

            if (string.IsNullOrWhiteSpace(dto.Id) || !IdPattern.IsMatch(dto.Id!))
            {
                Plugin.Log.LogWarning($"{where}: 'id' is missing or has characters other than letters, digits and '_'. Skipped.");
                continue;
            }
            where = $"{source}, banner '{dto.Id}'";
            if (!seenIds.Add(dto.Id!))
            {
                Plugin.Log.LogWarning($"{where}: duplicate id. Skipped.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(dto.Creature) || string.IsNullOrWhiteSpace(dto.Trophy))
            {
                Plugin.Log.LogWarning($"{where}: 'creature' and 'trophy' are required. Skipped.");
                continue;
            }

            var def = new BannerDefinition
            {
                Id = dto.Id!,
                Creature = dto.Creature!.Trim(),
                AlsoMatches = dto.AlsoMatches?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList() ?? new List<string>(),
                Trophy = dto.Trophy!.Trim(),
                Enabled = dto.Enabled ?? true,
                Permanent = ToValues(Merge(permanentDefaults, dto.Permanent), where + " (permanent)"),
                Portable = ToValues(Merge(portableDefaults, dto.Portable), where + " (portable)"),
            };

            if (!string.IsNullOrWhiteSpace(dto.Tint))
            {
                if (ColorUtility.TryParseHtmlString(dto.Tint, out Color tint)) def.Tint = tint;
                else Plugin.Log.LogWarning($"{where}: tint '{dto.Tint}' isn't a colour (use \"#rrggbb\"). No tint.");
            }

            def.CreatureHashes = new[] { def.Creature }.Concat(def.AlsoMatches)
                .Distinct().Select(name => name.GetStableHashCode()).ToArray();

            foreach (string creature in new[] { def.Creature }.Concat(def.AlsoMatches))
            {
                if (seenCreatures.TryGetValue(creature, out string other))
                    Plugin.Log.LogWarning($"{where}: creature '{creature}' is also covered by banner '{other}'. Both work; the stronger value applies.");
                else
                    seenCreatures[creature] = def.Id;
            }

            result.Add(def);
        }

        return result;
    }

    private static KindDto Merge(KindDto under, KindDto? over)
    {
        if (over == null) return under;
        return new KindDto
        {
            Radius = over.Radius ?? under.Radius,
            DamageBonus = over.DamageBonus ?? under.DamageBonus,
            DamageReduction = over.DamageReduction ?? under.DamageReduction,
            Duration = over.Duration ?? under.Duration,
            Cooldown = over.Cooldown ?? under.Cooldown,
            Station = string.IsNullOrWhiteSpace(over.Station) ? under.Station : over.Station,
            StationLevel = over.StationLevel ?? under.StationLevel,
            Trophies = over.Trophies ?? under.Trophies,
            Extra = over.Extra ?? under.Extra,
            SourcePrefab = string.IsNullOrWhiteSpace(over.SourcePrefab) ? under.SourcePrefab : over.SourcePrefab,
        };
    }

    private static BannerValues ToValues(KindDto dto, string where)
    {
        var values = new BannerValues
        {
            Radius = dto.Radius ?? 0f,
            DamageBonus = dto.DamageBonus ?? 0f,
            DamageReduction = dto.DamageReduction ?? 0f,
            Duration = dto.Duration ?? 0f,
            Cooldown = dto.Cooldown ?? 0f,
            Station = dto.Station?.Trim() ?? "",
            StationLevel = Math.Max(1, dto.StationLevel ?? 1),
            Trophies = dto.Trophies ?? 0,
            SourcePrefab = dto.SourcePrefab?.Trim() ?? "",
        };

        if (values.Radius <= 0f)
        {
            Plugin.Log.LogWarning($"{where}: radius must be above 0. Using 1.");
            values.Radius = 1f;
        }
        if (values.DamageBonus < 0f)
        {
            Plugin.Log.LogWarning($"{where}: damageBonus can't be negative. Using 0.");
            values.DamageBonus = 0f;
        }
        if (values.DamageReduction < 0f || values.DamageReduction > 1f)
        {
            Plugin.Log.LogWarning($"{where}: damageReduction must be between 0 and 1. Clamped.");
            values.DamageReduction = Mathf.Clamp01(values.DamageReduction);
        }
        if (values.Trophies < 1)
        {
            Plugin.Log.LogWarning($"{where}: trophies must be at least 1. Using 1.");
            values.Trophies = 1;
        }

        if (dto.Extra != null)
        {
            foreach (CostDto? cost in dto.Extra)
            {
                if (cost == null || string.IsNullOrWhiteSpace(cost.Item) || cost.Amount < 1)
                {
                    Plugin.Log.LogWarning($"{where}: an 'extra' cost needs an item and an amount of at least 1. Skipped.");
                    continue;
                }
                values.Extra.Add(new ExtraCost(cost.Item!.Trim(), cost.Amount));
            }
        }

        return values;
    }

    // ---- YAML shape. Nullable so "not set" can fall back to defaults. ----

    private sealed class FileDto
    {
        public DefaultsDto? Defaults { get; set; }
        public List<BannerDto?>? Banners { get; set; }
    }

    private sealed class DefaultsDto
    {
        public KindDto? Permanent { get; set; }
        public KindDto? Portable { get; set; }
    }

    private sealed class BannerDto
    {
        public string? Id { get; set; }
        public string? Creature { get; set; }
        public List<string>? AlsoMatches { get; set; }
        public string? Trophy { get; set; }
        public string? Tint { get; set; }
        public bool? Enabled { get; set; }
        public KindDto? Permanent { get; set; }
        public KindDto? Portable { get; set; }
    }

    private sealed class KindDto
    {
        public float? Radius { get; set; }
        public float? DamageBonus { get; set; }
        public float? DamageReduction { get; set; }
        public float? Duration { get; set; }
        public float? Cooldown { get; set; }
        public string? Station { get; set; }
        public int? StationLevel { get; set; }
        public int? Trophies { get; set; }
        public List<CostDto?>? Extra { get; set; }
        public string? SourcePrefab { get; set; }
    }

    private sealed class CostDto
    {
        public string? Item { get; set; }
        public int Amount { get; set; }
    }
}
