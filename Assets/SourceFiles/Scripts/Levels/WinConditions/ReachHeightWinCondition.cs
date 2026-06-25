using UnityEngine;

/// <summary>Win by reaching this tower height (m above the floor) and HOLDING it. Checks the live
/// standing tower (the recorded max is monotonic and would stay "met" after a collapse), so it re-arms
/// by polling, and tolerates a little slack during the hold so a wobbling peak block can't flicker the
/// countdown off.</summary>
public sealed class ReachHeightWinCondition : WinCondition
{
    // Slack below target during the hold-steady countdown; re-arming still needs the full target
    // again (hysteresis), which the controller enforces by only polling IsMet (no tolerance) to re-arm.
    private const float AbortTolerance = 0.25f;

    private readonly float _target;

    public ReachHeightWinCondition(float target) => _target = target;

    public override bool IsMet(in WinContext ctx) => ctx.LiveTowerHeight >= _target;
    public override bool IsStillHeld(in WinContext ctx) => ctx.LiveTowerHeight >= _target - AbortTolerance;
    public override bool ReArmsByPolling => true; // height record is monotonic; re-arm from the live tower

    public override float RunProgress01(GameManager gameManager)
        => gameManager != null ? Mathf.Clamp01(gameManager.towerHeight / _target) : 0f;

    public override string MenuChallengeLabel => "HEIGHT CHALLENGE";

    public override (string primary, string suffix) MenuProgress(ProgressStore.LevelBest best, bool completed)
    {
        int target = Mathf.RoundToInt(_target);
        float bestHeight = best != null ? best.bestHeightMeters : 0f;
        int reached = Mathf.RoundToInt(bestHeight > 0f ? bestHeight : (completed ? _target : 0f));
        return completed ? ($"{reached}m", "Reached") : ($"{reached}m", $"/ {target}m");
    }

    public override (string target, string best) TargetAndBest(ProgressStore.LevelBest best, bool completed, bool attempted)
    {
        int target = Mathf.RoundToInt(_target);
        float reached = best != null && best.bestHeightMeters > 0f ? best.bestHeightMeters : _target;
        return ($"{target}m", attempted ? $"{Mathf.RoundToInt(reached)}m" : "-");
    }
}
