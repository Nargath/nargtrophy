using System.Collections.Generic;
using System.Text;
using NargTrophy.Config;
using UnityEngine;

namespace NargTrophy.Banners;

/// <summary>
/// On every banner prefab. Placed banners add themselves to <see cref="Active"/>, the same pattern vanilla
/// wards use for their global list. The aura reads that list; nothing here runs per frame.
/// </summary>
public class TrophyBanner : MonoBehaviour, Hoverable
{
    public static readonly List<TrophyBanner> Active = new();

    // Set on the prefab; Unity copies serialized fields to each placed instance.
    public string m_bannerId = "";
    public int m_kind;

    private Piece? _piece;
    private bool _listed;

    internal BannerKind Kind => (BannerKind)m_kind;

    private void Awake()
    {
        _piece = GetComponent<Piece>();

        // The build ghost and other non-networked copies have no ZDO; they must not give an aura.
        var nview = GetComponent<ZNetView>();
        if (nview == null || nview.GetZDO() == null) return;

        Active.Add(this);
        _listed = true;
        BannerAura.MarkDirty();
    }

    private void OnDestroy()
    {
        if (!_listed) return;
        Active.Remove(this);
        _listed = false;
        BannerAura.MarkDirty();
    }

    /// <summary>The definition and values this banner uses right now, if it's active.</summary>
    internal bool TryGetValues(out BannerDefinition def, out BannerValues values)
    {
        values = null!;
        if (!BannerRegistry.TryGet(m_bannerId, out def) || !def.IsActive) return false;
        values = def.Get(Kind);
        return true;
    }

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

        BannerValues values = def.Get(Kind);
        text.Append($"\n<color=orange>+{Percent(values.DamageBonus)}%</color> $nt_hover_damage_dealt");
        text.Append($"\n<color=orange>-{Percent(values.DamageReduction)}%</color> $nt_hover_damage_taken");
        text.Append($"\n$nt_hover_radius: <color=orange>{values.Radius:0.#} m</color>");
        return Localization.instance.Localize(text.ToString());
    }

    public float GetHoverOffset() => 0f;

    internal static string Percent(float value) => Mathf.RoundToInt(value * 100f).ToString();
}
