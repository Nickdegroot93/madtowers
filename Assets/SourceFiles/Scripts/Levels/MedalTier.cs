/// <summary>The three per-level achievement tiers (MEDALS design, 2026-08). Bronze is the level's
/// authored target and is what "completed" has always meant - it unlocks the next level. Silver and
/// gold are stretch targets derived from the bronze value (LevelTiers). Ordered: casting to int gives
/// the ladder index, and tier + 1 is the next rung. The top rung and the count live in
/// LevelTiers.MaxTier / TierCount - extending the ladder starts there (MEDALS.md).</summary>
public enum MedalTier
{
    Bronze = 0,
    Silver = 1,
    Gold = 2,
}
