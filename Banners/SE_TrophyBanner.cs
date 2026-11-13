using NargTrophy.Config;

namespace NargTrophy.Banners;

/// <summary>
/// HUD feedback only: shows which creatures the player is covered against. It changes nothing by itself;
/// the damage patches read <see cref="BannerAura"/>'s cache. (SE_Stats modifiers work per damage type,
/// not per creature, so they can't express a banner.)
/// </summary>
public class SE_TrophyBanner : StatusEffect, IBannerEffect
{
    public string m_bannerId = "";

    /// <summary>Shown in the Active Effects list instead of the short HUD name.</summary>
    public string m_longName = "";
    public string LongName => m_longName;

    public override string GetTooltipString()
    {
        if (!BannerRegistry.TryGet(m_bannerId, out BannerDefinition def)) return base.GetTooltipString();
        BannerAura.Effect effect = BannerAura.GetEffectFor(def);
        string source = effect.FromPermanent && effect.FromPortable ? "$nt_source_both"
            : effect.FromPortable ? "$nt_source_portable" : "$nt_source_permanent";
        return Localization.instance.Localize(
            $"$nt_source: {source}\n" +
            $"+{TrophyBanner.Percent(effect.Bonus)}% $nt_hover_damage_dealt\n" +
            $"-{TrophyBanner.Percent(effect.Reduction)}% $nt_hover_damage_taken");
    }
}

/// <summary>
/// HUD feedback for a portable banner's cooldown. Shows the timer (no vanilla cooldown X); the tooltip
/// in Active Effects says when it can be planted again.
/// </summary>
public class SE_BannerCooldown : StatusEffect, IBannerEffect
{
    public string m_longName = "";
    public string LongName => m_longName;

    public override string GetTooltipString() =>
        Localization.instance.Localize("$nt_cooldown_tooltip", BannerCooldowns.Format(m_ttl - m_time));
}

/// <summary>Our status effects: a short name for the HUD row, a full one for the Active Effects list.</summary>
public interface IBannerEffect
{
    string LongName { get; }
}
