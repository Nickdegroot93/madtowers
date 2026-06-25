/// <summary>Free play: no victory rule. The tower stands until it falls; nothing ever "completes".</summary>
public sealed class EndlessWinCondition : WinCondition
{
    public override bool HasGoal => false;
    public override bool IsMet(in WinContext ctx) => false;
    public override float RunProgress01(GameManager gameManager) => 0f;

    public override string MenuChallengeLabel => "ENDLESS";

    public override (string primary, string suffix) MenuProgress(ProgressStore.LevelBest best, bool completed)
        => completed ? ("Completed", "") : ("Free", "Play");

    // Endless has no goal, so the menu's summary routes it through the provider/presentation default
    // path (HasGoal == false) rather than calling this; kept complete for safety.
    public override (string target, string best) TargetAndBest(ProgressStore.LevelBest best, bool completed, bool attempted)
        => ("Endless", attempted ? "Free" : "-");
}
