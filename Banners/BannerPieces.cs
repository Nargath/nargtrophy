using System.Collections.Generic;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using NargTrophy.Config;
using UnityEngine;

namespace NargTrophy.Banners;

/// <summary>
/// Builds every banner from <see cref="BannerRegistry"/> once vanilla and other mods' prefabs are in memory.
/// Names are stable (see docs/04_Technical.md) so saves, RecipeManager and other mods can rely on them.
/// Phase 1 builds the permanent piece and its aura status effect only.
/// </summary>
internal static class BannerPieces
{
    // Phase 1 uses a vanilla hammer tab ("Furniture" in code, shown as "Decor" in game).
    // A dedicated "Banners" tab is Proposed for phase 3.
    private const string Category = "Furniture";

    private static readonly List<(Piece piece, BannerDefinition def)> Built = new();
    private static readonly Dictionary<(Material, Color), Material> TintedMaterials = new();

    public static string PermanentPrefabName(BannerDefinition def) => $"piece_nt_banner_{def.Id.ToLowerInvariant()}";
    public static string AuraEffectName(BannerDefinition def) => $"SE_NT_Banner_{def.Id}";
    public static int AuraEffectHash(BannerDefinition def) => AuraEffectName(def).GetStableHashCode();

    public static void Init()
    {
        PrefabManager.OnVanillaPrefabsAvailable += Register;
        PieceManager.OnPiecesRegistered += RefreshCosts;
        BannerRegistry.ValuesChanged += () =>
        {
            RefreshCosts();
            BannerAura.MarkDirty();
        };
    }

    private static void Register()
    {
        PrefabManager.OnVanillaPrefabsAvailable -= Register;

        int built = 0;
        foreach (BannerDefinition def in BannerRegistry.All)
        {
            if (!def.Enabled)
            {
                Plugin.Log.LogInfo($"Banner '{def.Id}' is disabled in {BannerRegistry.FileName}. Not built.");
                continue;
            }
            if (!TryResolve(def, out ItemDrop trophy, out Character creature)) continue;

            def.CreatureName = string.IsNullOrEmpty(creature.m_name) ? def.Creature : creature.m_name;
            Sprite? icon = trophy.m_itemData.m_shared.m_icons is { Length: > 0 } icons ? icons[0] : null;

            if (!BuildPermanent(def, icon)) continue;
            AddAuraEffect(def, icon);
            def.Registered = true;
            built++;
        }
        Plugin.Log.LogInfo($"Built {built} banner(s).");
    }

    /// <summary>Checks the creature and trophy exist. A missing one (mod not installed) skips the banner with a warning.</summary>
    private static bool TryResolve(BannerDefinition def, out ItemDrop trophy, out Character creature)
    {
        trophy = null!;
        creature = null!;

        GameObject? trophyPrefab = PrefabManager.Instance.GetPrefab(def.Trophy);
        if (trophyPrefab == null || !trophyPrefab.TryGetComponent(out trophy))
        {
            Plugin.Log.LogWarning($"Banner '{def.Id}': trophy '{def.Trophy}' not found (is its mod installed?). Skipped.");
            return false;
        }

        GameObject? creaturePrefab = PrefabManager.Instance.GetPrefab(def.Creature);
        if (creaturePrefab == null || !creaturePrefab.TryGetComponent(out creature))
        {
            Plugin.Log.LogWarning($"Banner '{def.Id}': creature '{def.Creature}' not found (is its mod installed?). Skipped.");
            return false;
        }

        foreach (string extra in def.AlsoMatches)
        {
            if (PrefabManager.Instance.GetPrefab(extra) == null)
                Plugin.Log.LogWarning($"Banner '{def.Id}': alsoMatches creature '{extra}' not found. Kept, in case it registers later.");
        }
        return true;
    }

    private static bool BuildPermanent(BannerDefinition def, Sprite? icon)
    {
        BannerValues values = def.Permanent;
        string name = PermanentPrefabName(def);

        GameObject? source = PrefabManager.Instance.GetPrefab(values.SourcePrefab);
        if (source == null || source.GetComponent<Piece>() == null)
        {
            Plugin.Log.LogWarning($"Banner '{def.Id}': sourcePrefab '{values.SourcePrefab}' is not a piece. Skipped.");
            return false;
        }

        GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(name, source);

        var banner = prefab.AddComponent<TrophyBanner>();
        banner.m_bannerId = def.Id;
        banner.m_kind = (int)BannerKind.Permanent;

        // Stand-in: vanilla banners hang from walls and break without support. This one stands on its own.
        // (Vanilla's m_noSupportWear means "wears when unsupported", despite the name.)
        if (prefab.TryGetComponent(out WearNTear wear)) wear.m_noSupportWear = false;

        if (def.Tint is Color tint) ApplyTint(prefab, tint);

        var config = new PieceConfig
        {
            Name = $"{def.CreatureName} $nt_banner",
            Description = "$nt_banner_desc",
            PieceTable = PieceTables.Hammer,
            Category = Category,
            CraftingStation = values.Station,
            Icon = icon,
            Requirements = Costs(def, values),
        };
        PieceManager.Instance.AddPiece(new CustomPiece(prefab, false, config));
        Built.Add((prefab.GetComponent<Piece>(), def));
        return true;
    }

    private static void AddAuraEffect(BannerDefinition def, Sprite? icon)
    {
        var effect = ScriptableObject.CreateInstance<SE_TrophyBanner>();
        effect.name = AuraEffectName(def);
        effect.m_bannerId = def.Id;
        effect.m_name = $"$nt_aura {def.CreatureName}";
        effect.m_icon = icon;
        effect.m_ttl = 0f;
        ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(effect, false));
    }

    private static RequirementConfig[] Costs(BannerDefinition def, BannerValues values)
    {
        var costs = new List<RequirementConfig> { new(def.Trophy, values.Trophies, 0, true) };
        foreach (ExtraCost extra in values.Extra) costs.Add(new RequirementConfig(extra.Item, extra.Amount, 0, true));
        return costs.ToArray();
    }

    /// <summary>Re-applies costs and stations after a reload or server sync. Needs ObjectDB to resolve items.</summary>
    private static void RefreshCosts()
    {
        if (ObjectDB.instance == null || ZNetScene.instance == null) return;
        foreach ((Piece piece, BannerDefinition def) in Built)
        {
            if (piece == null) continue;
            BannerValues values = def.Permanent;
            piece.m_resources = ToRequirements(Costs(def, values), def.Id);

            GameObject? station = ZNetScene.instance.GetPrefab(values.Station);
            if (station != null && station.TryGetComponent(out CraftingStation craftingStation)) piece.m_craftingStation = craftingStation;
            else if (!string.IsNullOrEmpty(values.Station))
                Plugin.Log.LogWarning($"Banner '{def.Id}': station '{values.Station}' not found. Station unchanged.");
        }
    }

    private static Piece.Requirement[] ToRequirements(RequirementConfig[] costs, string id)
    {
        var requirements = new List<Piece.Requirement>();
        foreach (RequirementConfig req in costs)
        {
            GameObject item = ObjectDB.instance.GetItemPrefab(req.Item);
            if (item == null)
            {
                Plugin.Log.LogWarning($"Banner '{id}': unknown item '{req.Item}' in its cost. Skipped.");
                continue;
            }
            requirements.Add(new Piece.Requirement
            {
                m_resItem = item.GetComponent<ItemDrop>(),
                m_amount = req.Amount,
                m_amountPerLevel = req.AmountPerLevel,
                m_recover = req.Recover,
            });
        }
        return requirements.ToArray();
    }

    /// <summary>
    /// Tints the cloth. Renderers with a Cloth component (or "cloth"/"banner"/"flag" in the name) count as cloth;
    /// if none match, everything is tinted. Visual only; logic never depends on it.
    /// </summary>
    private static void ApplyTint(GameObject prefab, Color tint)
    {
        Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
        var cloth = new List<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            string lower = renderer.gameObject.name.ToLowerInvariant();
            if (renderer.GetComponent<Cloth>() != null || lower.Contains("cloth") || lower.Contains("banner") || lower.Contains("flag"))
                cloth.Add(renderer);
        }
        foreach (Renderer renderer in cloth.Count > 0 ? cloth : new List<Renderer>(renderers))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null || !materials[i].HasProperty("_Color")) continue;
                if (!TintedMaterials.TryGetValue((materials[i], tint), out Material tinted))
                {
                    tinted = new Material(materials[i]) { color = tint };
                    TintedMaterials[(materials[i], tint)] = tinted;
                }
                materials[i] = tinted;
            }
            renderer.sharedMaterials = materials;
        }
    }
}
