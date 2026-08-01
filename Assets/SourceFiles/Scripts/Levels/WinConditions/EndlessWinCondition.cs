/// <summary>Free play: no victory rule. The tower stands until it falls; nothing ever "completes".</summary>
public sealed class EndlessWinCondition : WinCondition
{
    // A "full-credit run" for XP purposes (XP.md): endless has no target, so this many
    // cumulative blocks counts as reaching one. Sized to a solid free-play tower.
    private const float XpReferenceBlocks = 25f;

    public override bool HasGoal => false;
    public override bool IsMet(in WinContext ctx) => false;
    public override float RunProgress01(GameManager gameManager) => 0f;

    // Rarity escalation stays at 0 (no goal to approach) but play still earns XP: progress
    // is measured against the fixed reference instead of a target.
    public override float RunProgressRaw(GameManager gameManager)
        => gameManager != null ? gameManager.score / XpReferenceBlocks : 0f;

    // Free play has no goal, so the card leads with the cumulative blocks placed (the score),
    // with the run's height shown separately as the endless-only secondary line.
    public override ResultMetric EndOfRunMetric(RunResult result, ProgressStore.LevelBest best)
        => new ResultMetric("BLOCKS", result.Score, best != null ? best.bestScore : 0f,
            isMeters: false, targetText: null);

    public override string MenuChallengeLabel => "ENDLESS";

    public override (string primary, string suffix) MenuProgress(ProgressStore.LevelBest best, bool completed)
        => completed ? ("Completed", "") : ("Free", "Play");

    // Endless has no goal, so the menu's summary routes it through the provider/presentation default
    // path (HasGoal == false) rather than calling this; kept complete for safety.
    public override (string target, string best) TargetAndBest(ProgressStore.LevelBest best, bool completed, bool attempted)
        => ("Endless", attempted ? "Free" : "-");
}
