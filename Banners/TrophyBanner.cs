using System;
using System.Collections.Generic;
using System.Text;
using NargTrophy.Config;
using UnityEngine;

namespace NargTrophy.Banners;

/// <summary>
/// On every banner prefab. Placed banners add themselves to <see cref="Active"/>, the same pattern vanilla
/// wards use for their global list. The aura reads that list; nothing here runs per frame.
///
/// Planted portable banners also carry their expiry (server time ticks) and planter in the ZDO. Whoever owns
/// the ZDO removes the banner once it has expired, so it disappears for everyone, including after relogs and
/// zone reloads (checked on load too). Other clients stop counting it as soon as it expires.
///
/// Upgrades (phase 5): both kinds have a level in the ZDO. A trophy banner is upgraded here with Use (E); a war
/// standard's level is its item's quality, written when planted. See <see cref="BannerUpgrades"/>.
/// </summary>
public class TrophyBanner : MonoBehaviour, Hoverable, Interactable
{
    public static readonly List<TrophyBanner> Active = new();

    internal const string ZdoExpire = "nt_expire";
    internal const string ZdoOwner = "nt_owner";
    internal const string ZdoOwnerName = "nt_owner_name";
    private const float ExpiryCheckSeconds = 1f;
    /// <summary>Seconds after expiry before the owner removes the standard: time for the poof and the sink.</summary>
    internal const float RemoveDelay = 3f;

    // Set on the prefab; Unity copies serialized fields to each placed instance.
    public string m_bannerId = "";
    public int m_kind;
    /// <summary>Child copy of the ward's area circle (inactive on the prefab). Unity remaps it per instance.</summary>
    public CircleProjector? m_areaMarker;

    private Piece? _piece;
    private ZNetView? _nview;
    private bool _listed;

    internal BannerKind Kind => (BannerKind)m_kind;

    private void Awake()
    {
        _piece = GetComponent<Piece>();

        // The build ghost and other non-networked copies have no ZDO; they must not give an aura.
        _nview = GetComponent<ZNetView>();
        if (_nview == null || _nview.GetZDO() == null)
        {
            // The hammer's placement ghost: show the area it will cover while placing.
            if (Kind == BannerKind.Permanent && ModConfig.ShowRadius.Value && m_areaMarker != null &&
                BannerRegistry.TryGet(m_bannerId, out BannerDefinition def))
            {
                m_areaMarker.m_radius = def.Permanent.Radius;
                m_areaMarker.gameObject.SetActive(true);
            }
            return;
        }

        Active.Add(this);
        _listed = true;
        BannerAura.MarkDirty();

        if (Kind == BannerKind.Portable)
        {
            ApplyProtection();
            // Expired while nobody was around: removed as soon as it loads. Later checks run every few seconds.
            CheckExpiry();
            InvokeRepeating(nameof(CheckExpiry), ExpiryCheckSeconds, ExpiryCheckSeconds);
        }
    }

    private void OnDestroy()
    {
        if (!_listed) return;
        Active.Remove(this);
        _listed = false;
        BannerAura.MarkDirty();
    }

    /// <summary>The definition and this banner's values at its level, if it's active.</summary>
    internal bool TryGetValues(out BannerDefinition def, out LevelValues values)
    {
        values = default;
        if (!BannerRegistry.TryGet(m_bannerId, out def) || !def.IsActive) return false;
        if (Kind == BannerKind.Portable && IsExpired()) return false;
        values = def.Get(Kind).At(Level(def));
        return true;
    }

    /// <summary>This banner's level (1 = not upgraded), clamped to the current config's cap.</summary>
    internal int Level(BannerDefinition def)
    {
        int stored = _nview != null && _nview.GetZDO() is { } zdo ? zdo.GetInt(BannerUpgrades.ZdoLevel, 1) : 1;
        return Mathf.Clamp(stored, 1, def.Get(Kind).TopLevel);
    }

    // ---- Portable timer ----

    /// <summary>Planter's player ID, or 0 for permanent banners.</summary>
    internal long OwnerId => _nview != null && _nview.GetZDO() is { } zdo ? zdo.GetLong(ZdoOwner) : 0L;

    /// <summary>Seconds until a portable banner expires. Null if not set (yet).</summary>
    internal float? SecondsLeft()
    {
        if (_nview == null || _nview.GetZDO() is not { } zdo) return null;
        long expire = zdo.GetLong(ZdoExpire);
        if (expire == 0L || !BannerCooldowns.TryGetNow(out long now)) return null;
        return (float)TimeSpan.FromTicks(expire - now).TotalSeconds;
    }

    internal bool IsExpired() => SecondsLeft() is <= 0f;

    /// <summary>Called by the planter right after spawning, before anyone else can see the banner.</summary>
    internal void Setup(Player planter, float duration, int level)
    {
        if (_nview == null || _nview.GetZDO() is not { } zdo || !BannerCooldowns.TryGetNow(out long now)) return;
        zdo.Set(ZdoExpire, now + TimeSpan.FromSeconds(duration).Ticks);
        zdo.Set(BannerUpgrades.ZdoLevel, Math.Max(1, level));
        zdo.Set(ZdoOwner, planter.GetPlayerID());
        zdo.Set(ZdoOwnerName, planter.GetPlayerName());
    }

    private void CheckExpiry()
    {
        if (_nview == null || !_nview.IsValid()) return;
        ApplyProtection();
        if (!_nview.IsOwner()) return;

        ZDO zdo = _nview.GetZDO();
        if (zdo.GetLong(ZdoExpire) == 0L)
        {
            // Never set up (planter disconnected mid-plant, or spawned by a command): give it one normal duration.
            if (BannerCooldowns.TryGetNow(out long now) && BannerRegistry.TryGet(m_bannerId, out BannerDefinition def))
                zdo.Set(ZdoExpire, now + TimeSpan.FromSeconds(def.Portable.Duration).Ticks);
            return;
        }
        // The bonus ends at expiry (TryGetValues), but the standard stays a few seconds longer so every client
        // sees the poof and the sink into the ground (StandardExpiry) before it's removed.
        if (!(SecondsLeft() is float left && left < -RemoveDelay)) return;

        if (ZNetScene.instance != null) ZNetScene.instance.Destroy(gameObject);
        else Destroy(gameObject);
    }

    /// <summary>The protection setting is server-synced and may change while the banner stands.</summary>
    private void ApplyProtection()
    {
        if (_piece != null) _piece.m_canBeRemoved = !ModConfig.PortableProtected.Value;
    }

    // ---- Hover ----

    public string GetHoverName() => _piece != null ? _piece.m_name : m_bannerId;

    public string GetHoverText()
    {
        var text = new StringBuilder();
        text.Append(GetHoverName());

        if (!BannerRegistry.TryGet(m_bannerId, out BannerDefinition def))
        {
            text.Append("\n$nt_hover_unknown");
            return Localization.instance.Localize(text.ToString());
        }
        if (!def.IsActive)
        {
            text.Append("\n$nt_hover_inactive");
            return Localization.instance.Localize(text.ToString());
        }

        BannerValues kind = def.Get(Kind);
        LevelValues values = kind.At(Level(def));
        ShowAreaMarker(values.Radius);
        if (kind.TopLevel > 1) text.Append($"\n$nt_hover_level <color=orange>{values.Level}/{kind.TopLevel}</color>");
        text.Append($"\n<color=orange>+{Percent(values.DamageBonus)}%</color> $nt_hover_damage_dealt");
        text.Append($"\n<color=orange>-{Percent(values.DamageReduction)}%</color> $nt_hover_damage_taken");
        text.Append($"\n$nt_hover_radius: <color=orange>{values.Radius:0.#} m</color>");

        if (Kind == BannerKind.Permanent && kind.TopLevel > 1)
        {
            if (values.Level >= kind.TopLevel)
                text.Append("\n$nt_hover_max_level");
            else
                text.Append($"\n[<color=yellow><b>$KEY_Use</b></color>] $nt_hover_upgrade: {BannerUpgrades.Describe(kind.UpgradeCost(def.Trophy, values.Level + 1))}");
        }

        if (Kind == BannerKind.Portable)
        {
            float? left = SecondsLeft();
            if (left is <= 0f) text.Append("\n$nt_hover_expired");
            else if (left is float seconds) text.Append($"\n$nt_hover_time_left: <color=orange>{BannerCooldowns.Format(seconds)}</color>");

            string owner = _nview != null && _nview.GetZDO() is { } zdo ? zdo.GetString(ZdoOwnerName) : "";
            // Player names are user text: escape the rich-text brackets.
            if (!string.IsNullOrEmpty(owner)) text.Append($"\n$nt_hover_planted_by: {owner.Replace("<", "‹").Replace(">", "›")}");
        }
        return Localization.instance.Localize(text.ToString());
    }

    public float GetHoverOffset() => 0f;

    // ---- Upgrade (trophy banner) ----

    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        if (hold || Kind != BannerKind.Permanent || user is not Player player || player != Player.m_localPlayer) return false;
        if (_nview == null || !_nview.IsValid()) return false;
        if (!BannerRegistry.TryGet(m_bannerId, out BannerDefinition def) || !def.IsActive) return false;
        if (def.Permanent.TopLevel <= 1) return false;

        int before = BannerUpgrades.GetLevel(_nview, def);
        player.Message(MessageHud.MessageType.Center, BannerUpgrades.TryUpgrade(player, _nview, def));
        if (BannerUpgrades.GetLevel(_nview, def) > before)
        {
            if (_piece != null) _piece.m_placeEffect.Create(transform.position, transform.rotation, transform);
            BannerAura.MarkDirty();
        }
        return true;
    }

    public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

    /// <summary>Same pattern as the ward: hovering keeps the circle on; it hides half a second after.</summary>
    private void ShowAreaMarker(float radius)
    {
        if (m_areaMarker == null || !ModConfig.ShowRadius.Value) return;
        m_areaMarker.m_radius = radius;
        m_areaMarker.gameObject.SetActive(true);
        CancelInvoke(nameof(HideAreaMarker));
        Invoke(nameof(HideAreaMarker), 0.5f);
    }

    private void HideAreaMarker()
    {
        if (m_areaMarker != null) m_areaMarker.gameObject.SetActive(false);
    }

    internal static string Percent(float value) => Mathf.RoundToInt(value * 100f).ToString();
}
