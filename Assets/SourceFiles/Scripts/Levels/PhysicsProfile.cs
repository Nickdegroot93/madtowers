/// <summary>
/// The active-piece control / settle thresholds that PHYSICS.md (binding) requires to be IDENTICAL
/// in every game mode. They live here as code-owned constants - one canonical source that cannot
/// drift - instead of as per-asset [SerializeField]s on each GameModeConfig (where a stray edit, or
/// the serialized-default staleness trap, could silently desync one mode from the contract).
///
/// GameModeConfig's matching getters forward to these, so existing callers (BlockController.Setup,
/// ComboDetector) are unchanged. Tuning the contract means editing one value here - and it applies
/// everywhere at once, by construction. (All shipped mode assets already held exactly these values.)
/// </summary>
public static class PhysicsProfile
{
    /// <summary>How close (world units) support must be below a piece before control hands to physics.</summary>
    public const float GroundedCheckDistance = 0.03f;
    /// <summary>Maximum downward velocity kept when control hands off to physics.</summary>
    public const float MaxLandingImpactSpeed = 2f;
    /// <summary>A landed piece is "settled" once its linear speed (units/sec) drops below this.</summary>
    public const float SettleLinearThreshold = 0.08f;
    /// <summary>...and its spin (degrees/sec) drops below this.</summary>
    public const float SettleAngularThreshold = 8f;
    /// <summary>How long a landed piece must stay settled before maintenance micro-aligns/sleeps it.</summary>
    public const float SettleTime = 0.35f;
    /// <summary>Sleep settled dynamic blocks when control finishes (prevents tiny drift; future contacts wake them).</summary>
    public const bool SleepSettledBlocksOnLock = true;
    /// <summary>After a block genuinely settles, correct tiny X/rotation drift back to the placement grid.</summary>
    public const bool MicroAlignSettledBlocks = true;
    /// <summary>Maximum X correction for settled micro-alignment, as a fraction of one grid cell.</summary>
    public const float MicroAlignMaxColumnFraction = 0.08f;
    /// <summary>Maximum rotation correction for settled micro-alignment, in degrees.</summary>
    public const float MicroAlignMaxRotationDegrees = 4f;
    /// <summary>Safety cap: lock a piece after this many seconds even if it never finds a normal landing.</summary>
    public const float MaxControlTime = 12f;
}
