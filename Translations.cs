using System.Collections.Generic;
using Jotunn.Managers;

namespace NargTrophy;

internal static class Translations
{
    public static void Init()
    {
        LocalizationManager.Instance.GetLocalization().AddTranslation("English", new Dictionary<string, string>
        {
            // Piece names are "<creature token> $nt_banner", e.g. "Greydwarf banner".
            ["nt_banner"] = "banner",
            ["nt_banner_desc"] = "Players near this banner deal more damage to the creature it shows, and take less damage from it.",
            ["nt_aura"] = "Banner:",
            ["nt_hover_damage_dealt"] = "damage dealt",
            ["nt_hover_damage_taken"] = "damage taken",
            ["nt_hover_radius"] = "Radius",
            ["nt_hover_inactive"] = "<color=grey>Inactive on this server</color>",
            ["nt_hover_unknown"] = "<color=grey>Unknown banner</color>",
        });
    }
}
