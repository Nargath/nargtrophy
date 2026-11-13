using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NargTrophy.Config;

/// <summary>One banner file's name (for messages) and text.</summary>
internal readonly struct BannerFile
{
    public readonly string Name;
    public readonly string Text;

    public BannerFile(string name, string text)
    {
        Name = name;
        Text = text;
    }
}

/// <summary>
/// Parses the banner files into <see cref="BannerDefinition"/>s. Invalid entries are skipped with a warning.
/// The first file is <c>banners.yml</c>: its <c>defaults</c> (kinds, rarity, biomes) apply to every file. Extra
/// files (<c>banners.*.yml</c>, e.g. one per mod) hold <c>banners</c> and an optional file-wide
/// <c>requiresMod</c>; a <c>defaults</c> block in them is ignored. Ids must be unique across all files.
/// </summary>
internal static class BannerYaml
{
    private static readonly Regex IdPattern = new("^[A-Za-z0-9_]+$");

    // Used when the file's defaults block leaves a value out. Placeholders from docs/01_Banners.md.
    private static readonly KindDto BuiltInPermanent = new()
    {
        Radius = 30f, DamageBonus = 0.15f, DamageReduction = 0.15f, Duration = 0f, Cooldown = 0f,
        Station = "piece_workbench", StationLevel = 1, Trophies = 8, SourcePrefab = "piece_banner01",
        TrophyMount = new MountDto { Enabled = true, Size = 0.9f, Offset = new() { 0f, 0f, 0f }, Rotation = new() { 0f, 0f, 0f } },
    };

    private static readonly KindDto BuiltInPortable = new()
    {
        Radius = 12f, DamageBonus = 0.30f, DamageReduction = 0.25f, Duration = 300f, Cooldown = 900f,
        Station = "piece_workbench", StationLevel = 1, Trophies = 10, SourcePrefab = "piece_banner01",
        TrophyMount = new MountDto { Enabled = true, Size = 0.9f, Offset = new() { 0f, 0f, 0f }, Rotation = new() { 0f, 0f, 0f } },
    };

    /// <summary>One file: banners.yml alone (kept for simple callers and tests).</summary>
    public static List<BannerDefinition>? Parse(string text, string source) =>
        ParseAll(new List<BannerFile> { new(source, text) }, "");

    /// <summary>
    /// Parses every file. Returns null if the main file (the first) can't be read; an unreadable extra file is
    /// skipped with an error. <paramref name="label"/> prefixes file names in messages (e.g. "server ").
    /// </summary>
    public static List<BannerDefinition>? ParseAll(IReadOnlyList<BannerFile> files, string label)
    {
        if (files.Count == 0) return null;
        FileDto? main = Deserialize(files[0], label);
        if (main == null) return null;

        var result = new List<BannerDefinition>();
        var context = new Context(main.Defaults);
        for (int f = 0; f < files.Count; f++)
        {
            FileDto? file = f == 0 ? main : Deserialize(files[f], label);
            if (file == null) continue;
            string source = label + files[f].Name;
            if (f > 0 && file.Defaults != null)
                Plugin.Log.LogWarning($"{source}: 'defaults' only counts in {files[0].Name}. Ignored here.");
            if (file.Banners == null || file.Banners.Count == 0)
            {
                if (f == 0) Plugin.Log.LogWarning($"{source} defines no banners.");
                continue;
            }
            ParseBanners(file, source, context, result);
        }
        return result;
    }

    /// <summary>
    /// Every file's <c>noBanner</c> list combined: creatures deliberately left without a banner (bosses aside),
    /// so <c>nt_trophies</c> doesn't keep suggesting them. Unreadable files are skipped quietly here (ParseAll
    /// reports them).
    /// </summary>
    public static HashSet<string> ReadNoBanner(IReadOnlyList<BannerFile> files)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (BannerFile file in files)
        {
            FileDto? dto;
            try
            {
                dto = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .IgnoreUnmatchedProperties().Build().Deserialize<FileDto>(file.Text);
            }
            catch
            {
                continue;
            }
            if (dto?.NoBanner == null) continue;
            foreach (string name in dto.NoBanner)
                if (!string.IsNullOrWhiteSpace(name)) result.Add(name.Trim());
        }
        return result;
    }

    private static FileDto? Deserialize(BannerFile file, string label)
    {
        try
        {
            IDeserializer deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            return deserializer.Deserialize<FileDto>(file.Text) ?? new FileDto();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Could not read {label}{file.Name}: {e.Message}");
            return null;
        }
    }

    /// <summary>Defaults from the main file, plus what must be unique across all files.</summary>
    private sealed class Context
    {
        public readonly KindDto PermanentDefaults;
        public readonly KindDto PortableDefaults;
        public readonly Dictionary<string, RarityDto> Rarities;
        public readonly Dictionary<string, BiomeDto> Biomes;
        public readonly HashSet<string> SeenIds = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> SeenCreatures = new(StringComparer.Ordinal);

        public Context(DefaultsDto? defaults)
        {
            PermanentDefaults = Merge(BuiltInPermanent, defaults?.Permanent);
            PortableDefaults = Merge(BuiltInPortable, defaults?.Portable);
            Rarities = new Dictionary<string, RarityDto>((defaults?.Rarity ?? new Dictionary<string, RarityDto?>())
                .Where(pair => pair.Value != null).ToDictionary(pair => pair.Key, pair => pair.Value!), StringComparer.OrdinalIgnoreCase);
            Biomes = new Dictionary<string, BiomeDto>((defaults?.Biomes ?? new Dictionary<string, BiomeDto?>())
                .Where(pair => pair.Value != null).ToDictionary(pair => pair.Key, pair => pair.Value!), StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void ParseBanners(FileDto file, string source, Context context, List<BannerDefinition> result)
    {
        KindDto permanentDefaults = context.PermanentDefaults;
        KindDto portableDefaults = context.PortableDefaults;
        Dictionary<string, RarityDto> rarities = context.Rarities;
        Dictionary<string, BiomeDto> biomes = context.Biomes;
        HashSet<string> seenIds = context.SeenIds;
        Dictionary<string, string> seenCreatures = context.SeenCreatures;

        for (int i = 0; i < file.Banners!.Count; i++)
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
                Plugin.Log.LogWarning($"{where}: duplicate id (ids must be unique across all banner files). Skipped.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(dto.Creature) || string.IsNullOrWhiteSpace(dto.Trophy))
            {
                Plugin.Log.LogWarning($"{where}: 'creature' and 'trophy' are required. Skipped.");
                continue;
            }

            ParseOne(dto, where, source, file, permanentDefaults, portableDefaults, rarities, biomes, seenCreatures, result);
        }
    }

    private static void ParseOne(BannerDto dto, string where, string source, FileDto file, KindDto permanentDefaults, KindDto portableDefaults,
        Dictionary<string, RarityDto> rarities, Dictionary<string, BiomeDto> biomes, Dictionary<string, string> seenCreatures,
        List<BannerDefinition> result)
    {
            // Layers, weakest first: built-in < defaults < biome < entry. Rarity sets the trophy count unless
            // the entry gives one itself.
            BiomeDto? biome = null;
            string biomeName = dto.Biome?.Trim() ?? "";
            if (biomeName.Length > 0 && !biomes.TryGetValue(biomeName, out biome))
                Plugin.Log.LogWarning($"{where}: biome '{biomeName}' isn't in defaults.biomes. Using plain defaults.");
            RarityDto? rarity = null;
            string rarityName = dto.Rarity?.Trim() ?? "";
            if (rarityName.Length > 0 && !rarities.TryGetValue(rarityName, out rarity))
                Plugin.Log.LogWarning($"{where}: rarity '{rarityName}' isn't in defaults.rarity. Using plain defaults.");

            KindDto permanent = Merge(Merge(permanentDefaults, biome?.Permanent), dto.Permanent);
            KindDto portable = Merge(Merge(portableDefaults, biome?.Portable), dto.Portable);
            // Merged as a new layer: Merge can return a shared defaults object, which must not be changed.
            if (dto.Permanent?.Trophies == null && rarity?.Permanent != null)
                permanent = Merge(permanent, new KindDto { Trophies = rarity.Permanent });
            if (dto.Portable?.Trophies == null && rarity?.Portable != null)
                portable = Merge(portable, new KindDto { Trophies = rarity.Portable });

            var def = new BannerDefinition
            {
                Id = dto.Id!,
                Creature = dto.Creature!.Trim(),
                AlsoMatches = dto.AlsoMatches?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList() ?? new List<string>(),
                Trophy = dto.Trophy!.Trim(),
                Biome = biomeName,
                Rarity = rarityName,
                Enabled = dto.Enabled ?? true,
                RequiresMod = (string.IsNullOrWhiteSpace(dto.RequiresMod) ? file.RequiresMod : dto.RequiresMod)?.Trim() ?? "",
                Permanent = ToValues(permanent, where + " (permanent)"),
                Portable = ToValues(portable, where + " (portable)"),
            };

            // Tint: the entry's own, else its biome's (fallback for modded entries), else the source's colours.
            string? tintText = !string.IsNullOrWhiteSpace(dto.Tint) ? dto.Tint : biome?.Tint;
            if (!string.IsNullOrWhiteSpace(tintText))
            {
                if (ColorUtility.TryParseHtmlString(tintText, out Color tint)) def.Tint = tint;
                else Plugin.Log.LogWarning($"{where}: tint '{tintText}' isn't a colour (use \"#rrggbb\"). No tint.");
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
            TrophyMount = MergeMount(under.TrophyMount, over.TrophyMount),
        };
    }

    /// <summary>Field by field, so an entry can change just the size or just the offset.</summary>
    private static MountDto? MergeMount(MountDto? under, MountDto? over)
    {
        if (over == null) return under;
        if (under == null) return over;
        return new MountDto
        {
            Enabled = over.Enabled ?? under.Enabled,
            Size = over.Size ?? under.Size,
            Offset = over.Offset ?? under.Offset,
            Rotation = over.Rotation ?? under.Rotation,
        };
    }

    private static Vector3 ToVector(List<float>? values, string where, string field)
    {
        if (values == null) return Vector3.zero;
        if (values.Count == 3) return new Vector3(values[0], values[1], values[2]);
        Plugin.Log.LogWarning($"{where}: trophyMount.{field} needs three numbers [x, y, z]. Using [0, 0, 0].");
        return Vector3.zero;
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
            MountEnabled = dto.TrophyMount?.Enabled ?? true,
            MountSize = dto.TrophyMount?.Size ?? 0.9f,
            MountOffset = ToVector(dto.TrophyMount?.Offset, where, "offset"),
            MountRotation = ToVector(dto.TrophyMount?.Rotation, where, "rotation"),
        };
        if (values.MountSize <= 0f)
        {
            Plugin.Log.LogWarning($"{where}: trophyMount.size must be above 0. Using 0.9.");
            values.MountSize = 0.9f;
        }

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
        /// <summary>Plugin GUID: every banner in this file needs that mod loaded (an entry can set its own).</summary>
        public string? RequiresMod { get; set; }
        /// <summary>Creature prefabs deliberately left without a banner; nt_trophies doesn't suggest them.</summary>
        public List<string>? NoBanner { get; set; }
        public DefaultsDto? Defaults { get; set; }
        public List<BannerDto?>? Banners { get; set; }
    }

    private sealed class DefaultsDto
    {
        public KindDto? Permanent { get; set; }
        public KindDto? Portable { get; set; }
        public Dictionary<string, RarityDto?>? Rarity { get; set; }
        public Dictionary<string, BiomeDto?>? Biomes { get; set; }
    }

    /// <summary>Trophy counts for a rarity bucket.</summary>
    private sealed class RarityDto
    {
        public int? Permanent { get; set; }
        public int? Portable { get; set; }
    }

    /// <summary>Per-biome layer: a fallback tint and any kind values (usually the extra materials).</summary>
    private sealed class BiomeDto
    {
        public string? Tint { get; set; }
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
        public string? Biome { get; set; }
        public string? Rarity { get; set; }
        public string? RequiresMod { get; set; }
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
        public MountDto? TrophyMount { get; set; }
    }

    private sealed class MountDto
    {
        public bool? Enabled { get; set; }
        public float? Size { get; set; }
        public List<float>? Offset { get; set; }
        public List<float>? Rotation { get; set; }
    }

    private sealed class CostDto
    {
        public string? Item { get; set; }
        public int Amount { get; set; }
    }
}
