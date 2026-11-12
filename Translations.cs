using System.Collections.Generic;
using Jotunn.Managers;

namespace NargTrophy;

internal static class Translations
{
    public static void Init()
    {
        LocalizationManager.Instance.GetLocalization().AddTranslation("English", new Dictionary<string, string>
        {
            // Names (Agreed 2026-11-13): "trophy banner" for the hanging piece, "war standard" for the portable one.
            // Neither is a vanilla word on its own, so they can't be mistaken for vanilla decor banners.
            // Piece: "<creature token> $nt_banner", e.g. "Greydwarf trophy banner".
            ["nt_banner"] = "trophy banner",
            ["nt_banner_desc"] = "Players near this banner deal more damage to the creature it shows, and take less damage from it.",
            ["nt_hover_damage_dealt"] = "damage dealt",
            ["nt_hover_damage_taken"] = "damage taken",
            ["nt_hover_radius"] = "Radius",
            ["nt_hover_inactive"] = "<color=grey>Inactive on this server</color>",
            ["nt_hover_unknown"] = "<color=grey>Unknown banner</color>",

            // Item and planted standard: "<creature token> $nt_portable", e.g. "Greydwarf war standard".
            ["nt_portable"] = "war standard",
            ["nt_portable_desc"] = "Use it to plant the standard at your feet for a short time. Players near it deal more damage to the creature it shows, and take less damage from it. The item is kept; it can be planted again after a cooldown.",
            ["nt_hover_time_left"] = "Time left",
            ["nt_hover_planted_by"] = "Planted by",
            ["nt_hover_expired"] = "<color=grey>Fading</color>",

            // HUD row labels (Agreed 2026-11-13): one word, a state like vanilla's "Rested"; the trophy icon shows
            // the creature. Active Effects (no icons) gets "<label>: <full name>" and the details.
            ["nt_hud_guarded"] = "Guarded",
            ["nt_hud_rallied"] = "Rallied",
            ["nt_hud_replant"] = "Replant",
            ["nt_source"] = "From",
            ["nt_source_permanent"] = "trophy banner",
            ["nt_source_portable"] = "war standard",
            ["nt_source_both"] = "trophy banner and war standard",
            ["nt_cooldown_tooltip"] = "Can be planted again in $1",

            ["nt_msg_cooldown"] = "This war standard can be planted again in $1",
            ["nt_msg_cap"] = "You already have $1 war standards planted",
            ["nt_msg_air"] = "You need solid ground to plant a war standard",
            ["nt_msg_slope"] = "Too steep to plant a war standard",
            ["nt_msg_ship"] = "You can't plant a war standard on a ship",
            ["nt_msg_water"] = "You can't plant a war standard in water",
            ["nt_msg_ward"] = "You can't plant a war standard inside this ward",
            ["nt_msg_inactive"] = "This war standard is inactive on this server",
            ["nt_msg_planted"] = "War standard planted",
        });
    }
}
