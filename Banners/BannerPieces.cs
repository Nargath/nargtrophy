using System.Collections.Generic;
using System.Linq;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using NargTrophy.Config;
using UnityEngine;

namespace NargTrophy.Banners;

/// <summary>
/// Builds every banner from <see cref="BannerRegistry"/> once vanilla and other mods' prefabs are in memory.
/// Names are stable (see docs/04_Technical.md) so saves, RecipeManager and other mods can rely on them.
/// Per banner: the permanent piece, the portable item and its recipe, the planted portable prefab, and the
/// aura and cooldown status effects.
/// </summary>
internal static class BannerPieces
{
    // Own hammer tab (phase 3), so the banners don't flood Decor. Jötunn creates it from the name.
    // Named after the pieces in it; plain "Banners" would suggest vanilla's decor banners live there too.
    public const string Category = "Trophy banners";

    // Proposed stand-in (docs/05): the portable version is the same build, smaller.
    private const float PlantedScale = 0.7f;

    private static readonly List<(Piece piece, BannerDefinition def)> Built = new();
    private static readonly List<(CustomRecipe recipe, BannerDefinition def)> BuiltRecipes = new();
    private static readonly Dictionary<string, BannerDefinition> ByItemName = new();
    private static readonly Dictionary<string, GameObject> PlantedPrefabs = new();
    private static readonly Dictionary<string, float> PlantedFrontYaw = new();

    /// <summary>Yaw of the war standard's front (the trophy side) in its own space. See BannerVisuals.FrontOf.</summary>
    public static float FrontYaw(BannerDefinition def) => PlantedFrontYaw.TryGetValue(def.Id, out float yaw) ? yaw : 90f;
    private static readonly Dictionary<string, (Sprite? banner, Sprite? standard)> Icons = new();

    /// <summary>Trophy banner icon (cloth on a crossbar) and war standard icon (on a pole). See BannerIcons.</summary>
    public static Sprite? BannerIcon(BannerDefinition def) => Icons.TryGetValue(def.Id, out var icons) ? icons.banner : null;
    public static Sprite? StandardIcon(BannerDefinition def) => Icons.TryGetValue(def.Id, out var icons) ? icons.standard : null;

    public static string PermanentPrefabName(BannerDefinition def) => $"piece_nt_banner_{def.Id.ToLowerInvariant()}";
    public static string PlantedPrefabName(BannerDefinition def) => $"nt_banner_planted_{def.Id.ToLowerInvariant()}";
    public static string ItemPrefabName(BannerDefinition def) => $"NT_BannerItem_{def.Id}";
    public static string RecipeName(BannerDefinition def) => $"Recipe_NT_Banner_{def.Id}";
    public static string AuraEffectName(BannerDefinition def) => $"SE_NT_Banner_{def.Id}";
    public static string CooldownEffectName(BannerDefinition def) => $"SE_NT_Cooldown_{def.Id}";
    public static int AuraEffectHash(BannerDefinition def) => AuraEffectName(def).GetStableHashCode();
    public static int CooldownEffectHash(BannerDefinition def) => CooldownEffectName(def).GetStableHashCode();

    /// <summary>The banner a portable item belongs to, from the item's prefab name.</summary>
    public static bool TryGetPortable(ItemDrop.ItemData item, out BannerDefinition def)
    {
        def = null!;
        if (item == null) return false;
        GameObject? prefab = item.m_dropPrefab;
        if (prefab != null && ByItemName.TryGetValue(prefab.name, out def)) return true;
        // The crafting screen's preview uses the prefab's own item data, which may have no drop prefab set.
        // Every copy of an item shares the prefab's SharedData object, so match on that.
        return item.m_shared != null && ByShared.TryGetValue(item.m_shared, out def);
    }

    private static readonly Dictionary<ItemDrop.ItemData.SharedData, BannerDefinition> ByShared = new();

    public static GameObject? PlantedPrefab(BannerDefinition def) =>
        PlantedPrefabs.TryGetValue(def.Id, out GameObject prefab) ? prefab : null;

    public static void Init()
    {
        PrefabManager.OnVanillaPrefabsAvailable += Register;
        PieceManager.OnPiecesRegistered += RefreshCosts;
        ItemManager.OnItemsRegistered += RefreshCosts;
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
        var missingMods = new Dictionary<string, int>();
        foreach (BannerDefinition def in BannerRegistry.All)
        {
            if (!def.Enabled)
            {
                Plugin.Log.LogInfo($"Banner '{def.Id}' is disabled in the banner files. Not built.");
                continue;
            }
            // requiresMod: that mod isn't installed, so skip quietly (counted, one line per mod below).
            if (def.RequiresMod.Length > 0 && !BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(def.RequiresMod))
            {
                missingMods[def.RequiresMod] = missingMods.TryGetValue(def.RequiresMod, out int n) ? n + 1 : 1;
                continue;
            }
            if (!TryResolve(def, out ItemDrop trophy, out Character creature)) continue;
            WarnUnknownCosts(def);

            def.CreatureName = string.IsNullOrEmpty(creature.m_name) ? def.Creature : creature.m_name;
            Sprite? trophyIcon = trophy.m_itemData.m_shared.m_icons is { Length: > 0 } icons ? icons[0] : null;
            Sprite? bannerIcon = BannerIcons.Banner(trophyIcon, def.Tint);
            Sprite? standardIcon = BannerIcons.Standard(trophyIcon, def.Tint);
            Icons[def.Id] = (bannerIcon, standardIcon);

            if (!BuildPermanent(def, trophy, bannerIcon)) continue;
            AddAuraEffect(def, bannerIcon);
            // A banner whose portable version fails still works as a permanent piece.
            if (BuildPlanted(def, trophy, creature, standardIcon) && BuildPortableItem(def, trophy, standardIcon)) AddCooldownEffect(def, standardIcon);
            def.Registered = true;
            built++;
        }
        foreach (KeyValuePair<string, int> missing in missingMods)
            Plugin.Log.LogInfo($"Skipped {missing.Value} banner(s) that need mod '{missing.Key}', which isn't installed.");
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

    /// <summary>Unknown extra cost items are dropped from the cost when it's applied; say so once, at startup.</summary>
    private static void WarnUnknownCosts(BannerDefinition def)
    {
        foreach ((string kind, BannerValues values) in new[] { ("permanent", def.Permanent), ("portable", def.Portable) })
        {
            foreach (ExtraCost extra in values.Extra)
            {
                GameObject? item = PrefabManager.Instance.GetPrefab(extra.Item);
                if (item == null || item.GetComponent<ItemDrop>() == null)
                    Plugin.Log.LogWarning($"Banner '{def.Id}': {kind} cost item '{extra.Item}' not found. It will be left out of the cost.");
            }
        }
    }

    private static bool BuildPermanent(BannerDefinition def, ItemDrop trophy, Sprite? icon)
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

        if (def.Tint is Color tint) BannerVisuals.ApplyTint(prefab, tint);
        BannerVisuals.MountTrophy(prefab, trophy, values, def.Id);
        banner.m_areaMarker = BannerVisuals.AddAreaMarker(prefab);

        var config = new PieceConfig
        {
            Name = $"{def.CreatureName} $nt_banner",
            Description = "$nt_banner_desc",
            PieceTable = PieceTables.Hammer,
            Category = Category,
            CraftingStation = values.Station,
            Icon = icon,
            Requirements = PieceCosts(def, values),
        };
        PieceManager.Instance.AddPiece(new CustomPiece(prefab, false, config));
        Built.Add((prefab.GetComponent<Piece>(), def));
        return true;
    }

    /// <summary>
    /// The banner a portable item plants: a networked prefab, not in any build table. Same stand-in as the
    /// permanent piece, smaller. Not a target for monsters, walk-through, and protected by config (see patches).
    /// </summary>
    private static bool BuildPlanted(BannerDefinition def, ItemDrop trophy, Character creature, Sprite? icon)
    {
        BannerValues values = def.Portable;
        GameObject? source = PrefabManager.Instance.GetPrefab(values.SourcePrefab);
        if (source == null || source.GetComponent<Piece>() == null || source.GetComponent<ZNetView>() == null)
        {
            Plugin.Log.LogWarning($"Banner '{def.Id}': portable sourcePrefab '{values.SourcePrefab}' is not a piece. No portable banner.");
            return false;
        }

        GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(PlantedPrefabName(def), source);
        prefab.transform.localScale = source.transform.localScale * PlantedScale;

        var banner = prefab.AddComponent<TrophyBanner>();
        banner.m_bannerId = def.Id;
        banner.m_kind = (int)BannerKind.Portable;

        Piece piece = prefab.GetComponent<Piece>();
        piece.m_name = $"{def.CreatureName} $nt_portable";
        piece.m_description = "$nt_portable_desc";
        piece.m_icon = icon;
        piece.m_resources = new Piece.Requirement[0]; // nothing to refund: the item was kept
        piece.m_canBeRemoved = false;                 // updated from config on each placed banner
        // No creator is set when planting, so monsters don't pick it as a target; these make sure.
        piece.m_randomTarget = false;
        piece.m_primaryTarget = false;
        piece.m_targetNonPlayerBuilt = false;

        if (prefab.TryGetComponent(out WearNTear wear)) wear.m_noSupportWear = false;

        // Never blocks movement, so it can't trap players or serve as a wall. Still hoverable (the interact
        // ray includes this layer).
        int nonSolid = LayerMask.NameToLayer("piece_nonsolid");
        if (nonSolid >= 0)
        {
            foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
                if (!collider.isTrigger) collider.gameObject.layer = nonSolid;
        }

        if (def.Tint is Color tint) BannerVisuals.ApplyTint(prefab, tint);
        BannerVisuals.MakeFreeStanding(prefab, nonSolid);
        BannerVisuals.MountTrophy(prefab, trophy, values, def.Id);
        banner.m_areaMarker = BannerVisuals.AddAreaMarker(prefab);
        // Same poof for every standard, sized to the standard (all war standards share one look and scale).
        float height = BannerVisuals.TryGetMeshBounds(prefab.transform, prefab.transform, out Bounds standardBounds, out _)
            ? standardBounds.max.y * prefab.transform.localScale.y
            : 1.8f;
        prefab.AddComponent<StandardExpiry>().m_poof = BannerVisuals.MakePoof(creature, height);

        PrefabManager.Instance.AddPrefab(prefab);
        PlantedPrefabs[def.Id] = prefab;
        PlantedFrontYaw[def.Id] = BannerVisuals.FrontYaw(BannerVisuals.FrontOf(prefab));
        return true;
    }

    /// <summary>
    /// The reusable portable item. A clone of the trophy (so it drops as the trophy model) turned into a
    /// consumable, so it can be used from the inventory or hotbar without equipping. The use is intercepted
    /// (Patches/PortablePatches) and the item is never removed.
    /// </summary>
    private static bool BuildPortableItem(BannerDefinition def, ItemDrop trophy, Sprite? icon)
    {
        string name = ItemPrefabName(def);
        GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(name, trophy.gameObject);
        ItemDrop.ItemData.SharedData shared = prefab.GetComponent<ItemDrop>().m_itemData.m_shared;

        shared.m_name = $"{def.CreatureName} $nt_portable";
        shared.m_description = "$nt_portable_desc";
        shared.m_itemType = ItemDrop.ItemData.ItemType.Consumable;
        shared.m_maxStackSize = 1;
        shared.m_maxQuality = def.Portable.TopLevel; // upgrades: vanilla item quality (phase 5)
        shared.m_value = 0;          // not sold to traders
        shared.m_questItem = false;
        shared.m_food = 0f;
        shared.m_foodStamina = 0f;
        shared.m_foodEitr = 0f;
        shared.m_foodBurnTime = 0f;
        shared.m_foodRegen = 0f;
        shared.m_consumeStatusEffect = null;
        if (icon != null) shared.m_icons = new[] { icon };

        ItemManager.Instance.AddItem(new CustomItem(prefab, false));

        BannerValues values = def.Portable;
        var recipe = new CustomRecipe(new RecipeConfig
        {
            Name = RecipeName(def),
            Item = name,
            Amount = 1,
            CraftingStation = values.Station,
            MinStationLevel = values.StationLevel,
            Requirements = RecipeCosts(def, values),
        });
        ItemManager.Instance.AddRecipe(recipe);
        BuiltRecipes.Add((recipe, def));
        ByItemName[name] = def;
        ByShared[shared] = def;
        return true;
    }

    private static void AddAuraEffect(BannerDefinition def, Sprite? icon)
    {
        var effect = ScriptableObject.CreateInstance<SE_TrophyBanner>();
        effect.name = AuraEffectName(def);
        effect.m_bannerId = def.Id;
        // HUD label and Active Effects name are set per tick by BannerAura (Guarded / Rallied).
        effect.m_name = "$nt_hud_guarded";
        effect.m_longName = $"$nt_hud_guarded: {def.CreatureName} $nt_banner";
        effect.m_icon = icon;
        effect.m_ttl = 0f;
        ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(effect, false));
    }

    /// <summary>
    /// HUD only. A one-word label in the HUD row next to the trophy icon, and the timer. No vanilla cooldown
    /// overlay (m_cooldownIcon): it draws an X, which next to "Replant" reads as "can't" or "cancelled" (agreed
    /// 2026-11-13). The Active Effects list gets a full name. The time left is set when it's
    /// applied (see BannerCooldowns).
    /// </summary>
    private static void AddCooldownEffect(BannerDefinition def, Sprite? icon)
    {
        var effect = ScriptableObject.CreateInstance<SE_BannerCooldown>();
        effect.name = CooldownEffectName(def);
        effect.m_name = "$nt_hud_replant";
        effect.m_longName = $"$nt_hud_replant: {def.CreatureName} $nt_portable"; // Active Effects (Patches/HudPatches)
        effect.m_icon = icon;
        effect.m_ttl = 0f;
        ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(effect, false));
    }

    private static RequirementConfig[] PieceCosts(BannerDefinition def, BannerValues values)
    {
        var costs = new List<RequirementConfig> { new(def.Trophy, values.Trophies, 0, true) };
        foreach (ExtraCost extra in values.Extra) costs.Add(new RequirementConfig(extra.Item, extra.Amount, 0, true));
        return costs.ToArray();
    }

    /// <summary>
    /// The war standard's recipe: crafting amounts plus per-level upgrade amounts, which vanilla multiplies for
    /// each quality (x1, x2, x4). An item used only for upgrades gets a crafting amount of 0.
    /// </summary>
    private static RequirementConfig[] RecipeCosts(BannerDefinition def, BannerValues values)
    {
        var amounts = new List<(string item, int craft, int perLevel)> { (def.Trophy, values.Trophies, values.UpgradeTrophies) };
        void Add(string item, int craft, int perLevel)
        {
            int i = amounts.FindIndex(a => a.item == item);
            if (i < 0) amounts.Add((item, craft, perLevel));
            else amounts[i] = (item, amounts[i].craft + craft, amounts[i].perLevel + perLevel);
        }
        foreach (ExtraCost extra in values.Extra) Add(extra.Item, extra.Amount, 0);
        if (values.TopLevel > 1)
            foreach (ExtraCost extra in values.UpgradeExtra) Add(extra.Item, 0, extra.Amount);
        if (values.TopLevel <= 1) amounts = amounts.Select(a => (a.item, a.craft, 0)).ToList();
        return amounts.Select(a => new RequirementConfig(a.item, a.craft, a.perLevel, true)).ToArray();
    }

    /// <summary>Re-applies costs and stations after a reload or server sync. Needs ObjectDB to resolve items.</summary>
    private static void RefreshCosts()
    {
        if (ObjectDB.instance == null || ZNetScene.instance == null) return;
        foreach ((Piece piece, BannerDefinition def) in Built)
        {
            if (piece == null) continue;
            BannerValues values = def.Permanent;
            piece.m_resources = ToRequirements(PieceCosts(def, values), def.Id);
            if (FindStation(def, values.Station) is { } station) piece.m_craftingStation = station;
        }
        foreach ((CustomRecipe custom, BannerDefinition def) in BuiltRecipes)
        {
            Recipe? recipe = custom.Recipe;
            if (recipe == null) continue;
            BannerValues values = def.Portable;
            recipe.m_resources = ToRequirements(RecipeCosts(def, values), def.Id);
            recipe.m_minStationLevel = values.StationLevel;
            if (recipe.m_item != null) recipe.m_item.m_itemData.m_shared.m_maxQuality = values.TopLevel;
            if (FindStation(def, values.Station) is { } station) recipe.m_craftingStation = station;
        }
    }

    private static CraftingStation? FindStation(BannerDefinition def, string name)
    {
        GameObject? prefab = ZNetScene.instance.GetPrefab(name);
        if (prefab != null && prefab.TryGetComponent(out CraftingStation station)) return station;
        if (!string.IsNullOrEmpty(name))
            Plugin.Log.LogWarning($"Banner '{def.Id}': station '{name}' not found. Station unchanged.");
        return null;
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
}
