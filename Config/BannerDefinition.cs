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
