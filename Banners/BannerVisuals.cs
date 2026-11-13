using System.Collections.Generic;
using Jotunn.Managers;
using NargTrophy.Config;
using UnityEngine;

namespace NargTrophy.Banners;

/// <summary>
/// Stand-in looks, applied to the generated prefabs once at startup. Visual only: logic never depends on any of
/// it (docs/05). Everything is measured from the meshes, so another sourcePrefab or a modded trophy still fits.
/// </summary>
internal static class BannerVisuals
{
    private const float GroundClearance = 0.25f; // gap under the war standard's cloth, before the 0.7 scale
    private const float PoleThickness = 0.09f;
    private const string WardPrefab = "guard_stone";
    private const string WallStandPrefab = "itemstand"; // vanilla wall item stand: how trophies face out

    private static readonly Dictionary<(Material, Color), Material> TintedMaterials = new();

    /// <summary>
    /// Tints the cloth. Renderers with a Cloth component (or "cloth"/"banner"/"flag" in the name) count as cloth;
    /// if none match, everything is tinted.
    /// </summary>
    public static void ApplyTint(GameObject prefab, Color tint)
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

    /// <summary>
    /// War standard only (Agreed: the trophy banner hangs). Wall banners have their origin at the crossbar with
    /// the cloth hanging below, so planted at the feet the cloth ends up underground. Lifts every child so the
    /// lowest mesh sits just above the ground, then adds a wooden pole from the ground to the top, behind the
    /// cloth (the side away from the trophy mount).
    /// </summary>
    public static void MakeFreeStanding(GameObject prefab, int poleLayer)
    {
        Transform root = prefab.transform;
        if (!TryGetMeshBounds(root, root, out Bounds bounds, out Material? wood)) return;

        float lift = GroundClearance - bounds.min.y;
        foreach (Transform child in root) child.localPosition += Vector3.up * lift;
        float top = bounds.max.y + lift;

        // At one end of the crossbar, like a flag on its pole (the look agreed in phase 2).
        int along = TryGetCrossbarBounds(root, out Bounds bar) ? LongAxis(bar) : 2;
        var position = new Vector3(bounds.center.x, top / 2f, bounds.center.z);
        position[along] = bounds.min[along] - PoleThickness / 2f;

        GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pole.name = "nt_pole";
        pole.transform.SetParent(root, false);
        // Unity's cylinder is 2 units tall and 1 wide, centred on its origin.
        pole.transform.localPosition = position;
        pole.transform.localScale = new Vector3(PoleThickness, top / 2f + 0.05f, PoleThickness);
        if (wood != null) pole.GetComponent<MeshRenderer>().sharedMaterial = wood;
        // The pole's collider makes the banner easy to hover; non-solid so nobody gets stuck on it.
        if (poleLayer >= 0) pole.layer = poleLayer;
    }

    /// <summary>
    /// Mounts a copy of the trophy's display model (the "attach" child item stands use) on the front of the
    /// crossbar, centred, looking straight out: the same rotation the vanilla wall item stand gives a trophy,
    /// including the trophy's own item-stand offsets. Scaled so its longest side is
    /// <see cref="BannerValues.MountSize"/>; offset and rotation from banners.yml adjust it. Colliders,
    /// rigidbodies and scripts are stripped: it's only a model.
    /// </summary>
    /// <returns>A one-line description of the placement, for diagnostics (nt_remount); null if nothing was mounted.</returns>
    public static string? MountTrophy(GameObject prefab, ItemDrop trophy, BannerValues values, string bannerId)
    {
        Transform root = prefab.transform;
        // Re-runnable on a placed banner (nt_remount): replace any existing mount.
        Transform? old = root.Find(MountName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        if (!values.MountEnabled) return null;
        GameObject? attach = ItemStand.GetAttachPrefab(trophy.gameObject);
        if (attach == null)
        {
            Plugin.Log.LogWarning($"Banner '{bannerId}': trophy '{trophy.name}' has no display model ('attach'). No trophy mount.");
            return null;
        }
        if (!TryGetCrossbarBounds(root, out Bounds bar)) return null;

        (Quaternion standRotation, Vector3 outward) = WallStand();
        ItemStand.OrientationSettings offsets = WallOffsets(trophy);

        var mount = new GameObject(MountName);
        mount.transform.SetParent(root, false);
        // The stand's "out" and the banner's front are different local axes: turn about the vertical from one
        // to the other (a pure yaw, so the trophy stays upright).
        Vector3 front = FrontOf(bar);
        float yaw = Vector3.SignedAngle(outward, front, Vector3.up);
        mount.transform.localRotation = Quaternion.Euler(values.MountRotation) * Quaternion.AngleAxis(yaw, Vector3.up) * standRotation;

        GameObject model = Object.Instantiate(ItemStand.GetAttachGameObject(attach), mount.transform, false);
        model.name = "nt_trophy";
        model.transform.localPosition = attach.transform.localPosition;
        model.transform.localRotation = attach.transform.localRotation * Quaternion.Euler(offsets.m_rotation);
        model.transform.localScale = attach.transform.localScale;
        StripToVisuals(model);

        if (!TryGetMeshBounds(mount.transform, root, out Bounds trophyBounds, out _))
        {
            Object.DestroyImmediate(mount);
            return null;
        }

        // The mount sits at the root's origin with scale 1, so scaling it scales these bounds about the origin.
        float longest = Mathf.Max(trophyBounds.size.x, Mathf.Max(trophyBounds.size.y, trophyBounds.size.z));
        float scale = longest > 0.0001f ? values.MountSize / longest : 1f;
        Vector3 min = trophyBounds.min * scale, max = trophyBounds.max * scale, centre = trophyBounds.center * scale;

        // Centred on the crossbar, the back of the trophy set into the bar to its middle, so it reads as mounted
        // on it (matches the placement tuned in game, 2026-11-13).
        Vector3 position = bar.center - centre;
        (int axis, float sign) = OutAxis(front);
        position[axis] = sign > 0f ? bar.center[axis] - min[axis] : bar.center[axis] - max[axis];

        mount.transform.localScale = Vector3.one * scale;
        mount.transform.localPosition = position + values.MountOffset;

        bool skinned = model.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;
        return $"{bannerId}: bar centre {bar.center:F2} size {bar.size:F2} | stand rot {standRotation.eulerAngles:F0} out {outward:F2} " +
               $"| attach rot {attach.transform.localRotation.eulerAngles:F0} offsets rot {offsets.m_rotation:F0} | yaw {yaw:F0} " +
               $"| trophy bounds centre {trophyBounds.center:F2} size {trophyBounds.size:F2} skinned {skinned} " +
               $"| scale {scale:F2} pos {mount.transform.localPosition:F2} rot {mount.transform.localRotation.eulerAngles:F0}";
    }

    public const string MountName = "nt_trophy_mount";

    /// <summary>
    /// The banner's front, in its local space: the horizontal direction across the crossbar, on the positive side.
    /// Measured, not assumed: piece_banner01's crossbar runs along local z (checked in game 2026-11-13), so its
    /// front is +x. The hammer piece is turned by the player while placing; a war standard is planted turned so
    /// this side faces the planter (see <see cref="FrontYaw"/>). trophyMount.rotation [0, 180, 0] flips it.
    /// </summary>
    public static Vector3 FrontOf(Bounds bar) => LongAxis(bar) == 2 ? Vector3.right : Vector3.forward;

    /// <summary>The banner's front for a built prefab (its crossbar measured), or +x if it has none.</summary>
    public static Vector3 FrontOf(GameObject prefab) =>
        TryGetCrossbarBounds(prefab.transform, out Bounds bar) ? FrontOf(bar) : Vector3.right;

    /// <summary>Yaw of a local direction, in degrees (0 = +z, 90 = +x), the way Unity's y rotation counts.</summary>
    public static float FrontYaw(Vector3 front) => Mathf.Atan2(front.x, front.z) * Mathf.Rad2Deg;

    /// <summary>The horizontal axis the crossbar runs along: 0 = x, 2 = z.</summary>
    private static int LongAxis(Bounds bar) => bar.size.x >= bar.size.z ? 0 : 2;

    private static (Quaternion rotation, Vector3 outward)? _wallStand;

    /// <summary>
    /// From the vanilla wall item stand: the attach point's rotation relative to the stand (how a mounted
    /// trophy is turned to face out of the wall) and the direction it sticks out, in the stand's local space.
    /// It is NOT the same axis as the banner's front (checked in game 2026-11-13), so MountTrophy turns from
    /// one to the other. Falls back to "no turn, out along +z".
    /// </summary>
    private static (Quaternion rotation, Vector3 outward) WallStand()
    {
        if (_wallStand is { } cached) return cached;
        (Quaternion, Vector3) result = (Quaternion.identity, Vector3.forward);
        GameObject? prefab = PrefabManager.Instance.GetPrefab(WallStandPrefab);
        if (prefab != null && prefab.TryGetComponent(out ItemStand stand) && stand.m_attachOther != null)
        {
            Transform standRoot = prefab.transform;
            Transform point = stand.m_attachOther;
            Quaternion rotation = Quaternion.Inverse(standRoot.rotation) * point.rotation;
            Vector3 outward = standRoot.InverseTransformPoint(point.position);
            outward.y = 0f;
            result = (rotation, outward.sqrMagnitude > 0.0001f ? outward.normalized : Vector3.forward);
        }
        else
        {
            Plugin.Log.LogWarning($"Wall item stand '{WallStandPrefab}' not found. Trophy mounts use a default facing; " +
                                  "adjust trophyMount.rotation in banners.yml.");
        }
        _wallStand = result;
        return result;
    }

    /// <summary>The trophy's own offsets for a wall (vertical) item stand, as vanilla applies them.</summary>
    private static ItemStand.OrientationSettings WallOffsets(ItemDrop trophy)
    {
        foreach (ItemStand.OrientationSettings settings in trophy.m_itemData.m_shared.m_itemStandOffsets)
            if ((settings.m_orientations & ItemStand.Orientation.Vertical) != 0) return settings;
        return new ItemStand.OrientationSettings();
    }

    /// <summary>The horizontal axis (0 = x, 2 = z) closest to <paramref name="direction"/>, and its sign.</summary>
    private static (int axis, float sign) OutAxis(Vector3 direction) =>
        Mathf.Abs(direction.x) > Mathf.Abs(direction.z) ? (0, Mathf.Sign(direction.x)) : (2, Mathf.Sign(direction.z));

    /// <summary>The crossbar: the mesh that reaches highest (ignoring this mod's pole and area marker).</summary>
    private static bool TryGetCrossbarBounds(Transform root, out Bounds bar)
    {
        bar = default;
        bool any = false;
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.name == "nt_pole") continue;
            if (IsUnder(filter.transform, MountName, root)) continue; // the trophy head can reach higher than the bar
            if (filter.GetComponentInParent<CircleProjector>(true) != null) continue;
            Bounds bounds = MeshBounds(filter.transform, filter.sharedMesh, root);
            if (!any || bounds.max.y > bar.max.y)
            {
                bar = bounds;
                any = true;
            }
        }
        return any;
    }

    private static bool IsUnder(Transform transform, string name, Transform stopAt)
    {
        for (Transform? t = transform; t != null && t != stopAt; t = t.parent)
            if (t.name == name) return true;
        return false;
    }

    private static Bounds MeshBounds(Transform meshTransform, Mesh mesh, Transform space)
    {
        Bounds local = mesh.bounds;
        Bounds result = default;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? local.min.x : local.max.x,
                (i & 2) == 0 ? local.min.y : local.max.y,
                (i & 4) == 0 ? local.min.z : local.max.z);
            Vector3 point = space.InverseTransformPoint(meshTransform.TransformPoint(corner));
            if (i == 0) result = new Bounds(point, Vector3.zero);
            else result.Encapsulate(point);
        }
        return result;
    }

    /// <summary>
    /// A copy of the ward's area circle, inactive. TrophyBanner shows it while hovered or while it's the build
    /// ghost. Counter-scaled so the war standard's 0.7 scale doesn't shrink the circle's segments.
    /// </summary>
    public static CircleProjector? AddAreaMarker(GameObject prefab)
    {
        GameObject? ward = PrefabManager.Instance.GetPrefab(WardPrefab);
        CircleProjector? source = ward != null && ward.TryGetComponent(out PrivateArea area) ? area.m_areaMarker : null;
        if (source == null)
        {
            Plugin.Log.LogWarning($"Ward prefab '{WardPrefab}' has no area marker. Banners won't show their radius.");
            return null;
        }

        GameObject marker = Object.Instantiate(source.gameObject, prefab.transform, false);
        marker.name = "nt_area_marker";
        marker.transform.localPosition = Vector3.zero;
        marker.transform.localRotation = Quaternion.identity;
        Vector3 rootScale = prefab.transform.localScale;
        marker.transform.localScale = new Vector3(1f / rootScale.x, 1f / rootScale.y, 1f / rootScale.z);
        marker.SetActive(false);
        return marker.GetComponent<CircleProjector>();
    }

    /// <summary>Colour of the expiry poof: warm gold, unlike the grey corpse smoke.</summary>
    public static readonly Color PoofColour = new(1f, 0.78f, 0.35f);

    private static GameObject? _poof;
    private static bool _poofBuilt;

    /// <summary>Creature whose corpse effect every war standard's expiry poof uses (agreed 2026-11-13).</summary>
    private const string PoofCreature = "Bjorn";

    /// <summary>
    /// The expiry "poof", one for every war standard (agreed in game 2026-11-13): the bear's corpse-vanishing effect
    /// (its smoke is bright and takes the gold tint well; other creatures' effects vary from a huge cloud to a dark
    /// puff), tinted gold and shrunk so the cloud is about half the standard's height. Played locally on every
    /// client at the synced expiry time, so the network part is stripped and it removes itself on a timer.
    /// Falls back to <paramref name="fallback"/>'s own corpse effect if the bear isn't there. Null if neither has one.
    /// </summary>
    /// <param name="standardHeight">Height of the planted standard in metres; the cloud is sized to half of it.</param>
    public static GameObject? MakePoof(Character fallback, float standardHeight)
    {
        if (_poofBuilt) return _poof;

        GameObject? source = null;
        if (PrefabManager.Instance.GetPrefab(PoofCreature) is { } bear && bear.TryGetComponent(out Character bearCharacter))
            source = CorpseEffect(bearCharacter);
        source ??= CorpseEffect(fallback);
        if (source == null) return null;
        _poofBuilt = true;

        GameObject poof = PrefabManager.Instance.CreateClonedPrefab($"nt_poof_{source.name}", source);
        foreach (ZSyncTransform sync in poof.GetComponentsInChildren<ZSyncTransform>(true)) Object.DestroyImmediate(sync);
        foreach (ZNetView view in poof.GetComponentsInChildren<ZNetView>(true)) Object.DestroyImmediate(view);
        if (poof.GetComponent<TimedDestruction>() == null)
        {
            TimedDestruction timer = poof.AddComponent<TimedDestruction>();
            timer.m_timeout = 6f;
            timer.m_triggerOnAwake = true;
        }

        ParticleSystem[] systems = poof.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem particles in systems)
        {
            // Tint, don't replace: multiplying keeps each system's own brightness and transparency.
            ParticleSystem.MainModule main = particles.main;
            ParticleSystem.MinMaxGradient start = main.startColor;
            start.color *= PoofColour;
            start.colorMin *= PoofColour;
            start.colorMax *= PoofColour;
            main.startColor = start;
        }
        // The effect's own light follows the same colour.
        foreach (Light light in poof.GetComponentsInChildren<Light>(true)) light.color = PoofColour;

        // Size: scaling the transform isn't reliable for particles (each system has its own scaling mode), so the
        // effect's own numbers are scaled instead. Reach = how far its particles get from the centre, estimated
        // from shape size, start speed x lifetime and start size. The cloud's height is about twice the reach.
        float reach = EstimateReach(systems);
        float target = Mathf.Max(0.2f, standardHeight * 0.5f) / 2f;
        float factor = reach > 0.01f ? Mathf.Clamp(target / reach, 0.02f, 1f) : 1f;
        ScaleEffect(poof, systems, factor);
        Plugin.Log.LogInfo($"War standard poof: '{source.name}' scaled x{factor:0.###} (estimated reach {reach:0.##} m -> {target:0.##} m).");

        _poof = poof;
        return poof;
    }

    private static GameObject? CorpseEffect(Character creature)
    {
        foreach (EffectList.EffectData death in creature.m_deathEffects.m_effectPrefabs)
        {
            if (death?.m_prefab == null || !death.m_prefab.TryGetComponent(out Ragdoll ragdoll)) continue;
            foreach (EffectList.EffectData remove in ragdoll.m_removeEffect.m_effectPrefabs)
                if (remove?.m_prefab != null) return remove.m_prefab;
        }
        return null;
    }

    private static float EstimateReach(ParticleSystem[] systems)
    {
        float reach = 0f;
        foreach (ParticleSystem particles in systems)
        {
            ParticleSystem.MainModule main = particles.main;
            ParticleSystem.ShapeModule shape = particles.shape;
            float shapeSize = shape.enabled ? Mathf.Max(shape.radius, Mathf.Max(shape.scale.x, shape.scale.z) * 0.5f) : 0f;
            float travel = main.startSpeed.constantMax * main.startLifetime.constantMax;
            float size = main.startSize.constantMax * 0.5f;
            reach = Mathf.Max(reach, particles.transform.localPosition.magnitude + shapeSize + travel + size);
        }
        return reach;
    }

    private static void ScaleEffect(GameObject poof, ParticleSystem[] systems, float factor)
    {
        foreach (ParticleSystem particles in systems)
        {
            ParticleSystem.MainModule main = particles.main;
            main.startSizeMultiplier *= factor;
            main.startSpeedMultiplier *= factor;
            main.gravityModifierMultiplier *= factor;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.radius *= factor;
            shape.scale *= factor;
            shape.position *= factor;

            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            if (velocity.enabled)
            {
                velocity.xMultiplier *= factor;
                velocity.yMultiplier *= factor;
                velocity.zMultiplier *= factor;
            }
        }
        foreach (Transform child in poof.GetComponentsInChildren<Transform>(true))
            if (child != poof.transform) child.localPosition *= factor;
        foreach (Light light in poof.GetComponentsInChildren<Light>(true)) light.range *= factor;
    }

    private static void StripToVisuals(GameObject model)
    {
        foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
        foreach (Rigidbody body in model.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(body);
        foreach (MonoBehaviour script in model.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(script);
    }

    /// <summary>
    /// Combined bounds of every mesh under <paramref name="search"/>, expressed in <paramref name="space"/>'s
    /// local space, plus the first non-cloth material found (the wood).
    /// </summary>
    public static bool TryGetMeshBounds(Transform search, Transform space, out Bounds bounds, out Material? wood)
    {
        Bounds result = default;
        wood = null;
        bool any = false;

        void Add(Transform meshTransform, Mesh? mesh)
        {
            if (mesh != null) AddBox(meshTransform, mesh.bounds);
        }

        void AddBox(Transform meshTransform, Bounds local)
        {
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? local.min.x : local.max.x,
                    (i & 2) == 0 ? local.min.y : local.max.y,
                    (i & 4) == 0 ? local.min.z : local.max.z);
                Vector3 point = space.InverseTransformPoint(meshTransform.TransformPoint(corner));
                if (any) result.Encapsulate(point);
                else result = new Bounds(point, Vector3.zero);
                any = true;
            }
        }

        foreach (MeshFilter filter in search.GetComponentsInChildren<MeshFilter>(true))
        {
            // Skip helper objects added by this mod that shouldn't count (the area marker's segments).
            if (filter.GetComponentInParent<CircleProjector>(true) != null) continue;
            Add(filter.transform, filter.sharedMesh);
            if (wood == null && filter.TryGetComponent(out MeshRenderer renderer) && renderer.GetComponent<Cloth>() == null)
                wood = renderer.sharedMaterial;
        }
        // A skinned mesh is drawn where its bones put it, not where its raw vertices are: use the renderer's own
        // bounds, which Unity keeps relative to the root bone.
        foreach (SkinnedMeshRenderer skinned in search.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            AddBox(skinned.rootBone != null ? skinned.rootBone : skinned.transform, skinned.localBounds);

        bounds = result;
        return any;
    }
}
