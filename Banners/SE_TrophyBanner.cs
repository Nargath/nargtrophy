using NargTrophy.Config;

namespace NargTrophy.Banners;

/// <summary>
/// HUD feedback only: shows which creatures the player is covered against. It changes nothing by itself;
/// the damage patches read <see cref="BannerAura"/>'s cache. (SE_Stats modifiers work per damage type,
/// not per creature, so they can't express a banner.)
/// </summary>
public class SE_TrophyBanner : StatusEffect
{
    public string m_bannerId = "";

    public override string GetTooltipString()
    {
        if (!BannerRegistry.TryGet(m_bannerId, out BannerDefinition def)) return base.GetTooltipString();
        BannerAura.Effect effect = BannerAura.GetEffectFor(def);
        return Localization.instance.Localize(
            $"+{TrophyBanner.Percent(effect.Bonus)}% $nt_hover_damage_dealt\n" +
            $"-{TrophyBanner.Percent(effect.Reduction)}% $nt_hover_damage_taken");
    }
}
