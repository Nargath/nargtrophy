using System;
using System.Collections.Generic;
using UnityEngine;

namespace NargTrophy.Config;

internal enum BannerKind
{
    Permanent,
    Portable,
}

/// <summary>One cost line on top of the trophies.</summary>
internal readonly struct ExtraCost
{
    public readonly string Item;
    public readonly int Amount;

    public ExtraCost(string item, int amount)
    {
        Item = item;
        Amount = amount;
    }
}

/// <summary>The values for one kind of banner (permanent or portable), with defaults already applied.</summary>
internal sealed class BannerValues
{
    public float Radius;
    public float DamageBonus;
    public float DamageReduction;
    public float Duration;
    public float Cooldown;
    public string Station = "";
    public int StationLevel;
    public int Trophies;
    public List<ExtraCost> Extra = new();
    public string SourcePrefab = "";

    // Trophy model on the crossbar (startup only, like SourcePrefab). Size is the longest side in metres,
    // before the war standard's 0.7 scale; offset and rotation adjust the automatic placement.
    public bool MountEnabled = true;
    public float MountSize;
    public Vector3 MountOffset;
    public Vector3 MountRotation;

    // Upgrades (phase 5). Level 1 is the built banner; MaxLevel more levels can be added. Each level adds the
    // Level* values. Cost to reach a level = (UpgradeTrophies trophies + UpgradeExtra) x the vanilla quality
    // multiplier (x1, x2, x4 for levels 2, 3, 4), the same formula vanilla item upgrades use.
    public int MaxLevel;
    public float LevelDamageBonus;
    public float LevelDamageReduction;
    public float LevelRadius;
    public int UpgradeTrophies;
    public List<ExtraCost> UpgradeExtra = new();

    // Balance (agreed 2026-11-13): kill targets from the availability tier. When set (and the drop chance is
    // known) they replace Trophies / UpgradeTrophies: trophies = kills x drop chance, rounded, at least 1.
    public float? Kills;
    public float? UpgradeKills;

    /// <summary>Trophies to build or craft, at this drop chance.</summary>
    public int TrophyCost(float dropChance) => FromKills(Kills, dropChance) ?? Trophies;

    /// <summary>Trophies per upgrade step (x1), at this drop chance.</summary>
    public int UpgradeStep(float dropChance) => FromKills(UpgradeKills, dropChance) ?? UpgradeTrophies;

    private static int? FromKills(float? kills, float dropChance) =>
        kills is float k && dropChance > 0f ? Math.Max(1, (int)Math.Round(k * dropChance, MidpointRounding.AwayFromZero)) : null;

    /// <summary>Highest level (base + upgrades).</summary>
    public int TopLevel => 1 + MaxLevel;

    /// <summary>Damage reduction never reaches 100%.</summary>
    public const float MaxReduction = 0.9f;

    /// <summary>The values at a level (clamped to 1..TopLevel).</summary>
    public LevelValues At(int level)
    {
        int steps = Mathf.Clamp(level, 1, TopLevel) - 1;
        return new LevelValues(
            Mathf.Clamp(level, 1, TopLevel),
            Radius + LevelRadius * steps,
            DamageBonus + LevelDamageBonus * steps,
            Mathf.Min(MaxReduction, DamageReduction + LevelDamageReduction * steps));
    }

    /// <summary>Vanilla's quality cost multiplier (Piece.Requirement.GetAmount): x1, x2, x4, then +0.5 per level.</summary>
    public static float UpgradeMultiplier(int targetLevel) =>
        targetLevel <= 1 ? 0f : targetLevel >= 4 ? 4f + (targetLevel - 4) / 2f : targetLevel - 1;

    /// <summary>Items needed to go from targetLevel - 1 to targetLevel (prefab name, amount).</summary>
    public List<ExtraCost> UpgradeCost(string trophy, int targetLevel, float dropChance)
    {
        float multiplier = UpgradeMultiplier(targetLevel);
        var cost = new List<ExtraCost>();
        int step = UpgradeStep(dropChance);
        if (step > 0) cost.Add(new ExtraCost(trophy, (int)Math.Floor(step * multiplier)));
        foreach (ExtraCost extra in UpgradeExtra) cost.Add(new ExtraCost(extra.Item, (int)Math.Floor(extra.Amount * multiplier)));
        cost.RemoveAll(c => c.Amount <= 0);
        return cost;
    }
}

/// <summary>A banner's values at one level.</summary>
internal readonly struct LevelValues
{
    public readonly int Level;
    public readonly float Radius;
    public readonly float DamageBonus;
    public readonly float DamageReduction;

    public LevelValues(int level, float radius, float bonus, float reduction)
    {
        Level = level;
        Radius = radius;
        DamageBonus = bonus;
        DamageReduction = reduction;
    }
}

/// <summary>
/// One parsed <c>banners.yml</c> entry with defaults applied. Structure (id, creature, trophy) is fixed
/// at startup because prefabs are built from it; values can change when the file reloads or the server syncs.
/// </summary>
internal sealed class BannerDefinition
{
    public string Id = "";
    public string Creature = "";
    public List<string> AlsoMatches = new();
    public string Trophy = "";
    public Color? Tint;

    /// <summary>Optional labels from the file. Biome picks extra costs and a fallback tint; availability picks kill targets.</summary>
    public string Biome = "";
    public string Availability = "";

    /// <summary>
    /// The creature's real chance to drop this banner's trophy (0 = unknown), read from the game when costs are
    /// worked out, so drop-rate mods count. Costs set by kill targets use it.
    /// </summary>
    public float DropChance;

    /// <summary>Plugin GUID this banner needs. Checked when banners are built: if that mod isn't loaded, the banner is skipped quietly.</summary>
    public string RequiresMod = "";

    /// <summary>From the file in force (local, or the server's when connected). Synced.</summary>
    public bool Enabled = true;

    public BannerValues Permanent = new();
    public BannerValues Portable = new();

    /// <summary>Stable hashes of <see cref="Creature"/> and <see cref="AlsoMatches"/>, as stored in ZDOs.</summary>
    public int[] CreatureHashes = new int[0];

    /// <summary>The creature's localisation token (e.g. <c>$enemy_greydwarf</c>), found when the banner is built.</summary>
    public string CreatureName = "";

    /// <summary>True once this client built the banner's prefabs. Banners can't be added after startup.</summary>
    public bool Registered;

    /// <summary>False when the server's file doesn't contain this banner.</summary>
    public bool KnownToServer = true;

    public bool IsActive => Registered && Enabled && KnownToServer;

    public BannerValues Get(BannerKind kind) => kind == BannerKind.Portable ? Portable : Permanent;
}
