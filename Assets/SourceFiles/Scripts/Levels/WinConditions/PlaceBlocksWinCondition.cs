using UnityEngine;

/// <summary>Win by keeping this many blocks STANDING at once. Lives on the live count (not cumulative
/// score), so destroying or dropping placed blocks genuinely sets the goal back; it re-arms for free
/// because the standing-count signal re-crosses the target on its own.</summary>
public sealed class PlaceBlocksWinCondition : WinCondition
{
    private readonly float _target;

    public PlaceBlocksWinCondition(float target) => _target = target;

    public override bool SpeedCapChasesTiers => true; // speed IS block count's difficulty

    public override bool IsMet(in WinContext ctx)
        => ctx.GameManager != null && ctx.GameManager.placedBlocks >= _target;

    public override float RunProgress01(GameManager gameManager)
        => gameManager != null ? Mathf.Clamp01(gameManager.score / _target) : 0f;

    // Cumulative placement score over the target, unclamped - Keep Playing past the goal
    // keeps counting, and a collapse never rewinds it (matches the reported metric below).
    public override float RunProgressRaw(GameManager gameManager)
        => gameManager != null ? gameManager.score / _target : 0f;

    // "Blocks we've had": the cumulative placement score, matching what the menu shows as the
    // level's best - not the live standing count, which a collapse rewinds right before the end.
    public override ResultMetric EndOfRunMetric(RunResult result, ProgressStore.LevelBest best)
        => new ResultMetric("BLOCKS", result.Score, best != null ? best.bestScore : 0f,
            isMeters: false);

    public override string MenuChallengeLabel => "BLOCK COUNT";

    public override (string primary, string suffix) MenuProgress(ProgressStore.LevelBest best, bool completed)
    {
        int target = Mathf.RoundToInt(_target);
        int bestScore = best != null ? best.bestScore : 0;
        int reached = bestScore > 0 ? bestScore : (completed ? target : 0);
        return completed ? (reached.ToString(), "Blocks") : (reached.ToString(), $"/ {target} Blocks");
    }

    public override (string target, string best) TargetAndBest(ProgressStore.LevelBest best, bool completed, bool attempted)
    {
        int target = Mathf.RoundToInt(_target);
        int reached = best != null && best.bestScore > 0 ? best.bestScore : target;
        return ($"{target} Blocks", attempted ? $"{reached} Blocks" : "-");
    }
}
