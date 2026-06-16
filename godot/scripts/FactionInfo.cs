namespace LlmRts.Godot;

/// <summary>Presentation-layer faction identity blurbs (render-only; no sim/pack impact).
/// Shared by the start menu and the multiplayer lobby. Keyed by FactionDef.Id; unknown
/// (custom) packs get a generic line.</summary>
public static class FactionInfo
{
    private static readonly System.Collections.Generic.Dictionary<string, string> Blurbs = new()
    {
        ["reference"] = "Vanguard — balanced human military. A dependable all-rounder with no special mechanic: solid infantry, tanks, and turrets. Forgiving to learn and strong in every matchup.",
        ["concord"]   = "The Concord — synthetic energy. Few, expensive, durable units shielded by regenerating energy; every loss stings, so disengage to recharge. Quality over quantity.",
        ["driftborn"] = "The Driftborn — nomad scavengers. Cheap, fast, fragile units and quick-building structures. Hit-and-run raiders that snowball early but fold against static defense.",
        ["mycel"]     = "The Mycel — fungal swarm. The cheapest, most numerous units, and they regenerate health out of combat. Overwhelm with numbers, then pull back wounded units to heal.",
        ["sanguine"]  = "The Sanguine — vampiric predators. Aggressive flesh-and-bone units that heal whenever they land a hit, so they win prolonged brawls. Stay in the fight; they wither if kept out of combat.",
    };

    public static string BlurbFor(string id) =>
        Blurbs.TryGetValue(id, out var b) ? b : "A custom faction.";
}
