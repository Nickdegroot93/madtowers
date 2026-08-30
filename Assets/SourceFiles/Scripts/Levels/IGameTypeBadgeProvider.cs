using UnityEngine;

/// <summary>
/// A game-type modifier whose rule is INVISIBLE in-world claims a hazard badge. Void Zones and
/// Blackout announce themselves on screen; Airtight looks exactly like a classic block-count
/// level until the mistake is already made - the badge is the standing reminder. Two surfaces
/// read it: the level-summary modal shows the icon beside the challenge label (the level's
/// instruction line carries the explanation), and GameTypeBadgeHud echoes it as an in-run pill
/// (recall, plus the live danger pulse). Menu surfaces read the AUTHORED asset; the HUD reads
/// the per-run clone via <see cref="GameTypeBadgeHud.ActiveSource"/>.
/// </summary>
public interface IGameTypeBadgeProvider
{
    /// <summary>Badge art - a fixed, code-owned look (never per-chapter). Null = no badge
    /// anywhere; every reader must tolerate it (missing art degrades to today's plain label).</summary>
    Sprite BadgeIcon { get; }

    /// <summary>Uppercase pill text ("AIRTIGHT").</summary>
    string BadgeLabel { get; }

    /// <summary>0 = calm; rises toward 1 while a live hazard is burning (an armed pocket's
    /// fuse), driving the HUD pill's pulse. Meaningful only on the per-run clone.</summary>
    float BadgeDanger01 { get; }
}
