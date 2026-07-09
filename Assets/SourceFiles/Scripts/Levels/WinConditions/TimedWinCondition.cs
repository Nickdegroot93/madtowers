using UnityEngine;

/// <summary>
/// Adds a main run timer to an ordinary goal. The wrapped goal still owns arming, hold-steady
/// verification, progress scaling and saved-best presentation; the runtime controller owns the
/// per-run clock state so this condition stays immutable like the other win conditions.
/// </summary>
public sealed class TimedWinCondition : WinCondition
{
    private readonly WinCondition _inner;
    private readonly float _timeLimitSeconds;

    public TimedWinCondition(WinCondition inner, float timeLimitSeconds)
    {
        _inner = inner ?? new EndlessWinCondition();
        _timeLimitSeconds = Mathf.Max(1f, timeLimitSeconds);
    }

    public override bool HasGoal => _inner.HasGoal;
    public override bool HasTimeLimit => HasGoal;
    public override float TimeLimitSeconds => _timeLimitSeconds;
    public override bool ReArmsByPolling => _inner.ReArmsByPolling;

    public override bool IsMet(in WinContext ctx) => _inner.IsMet(in ctx);
    public override bool IsStillHeld(in WinContext ctx) => _inner.IsStillHeld(in ctx);
    public override float RunProgress01(GameManager gameManager) => _inner.RunProgress01(gameManager);

    public override ResultMetric EndOfRunMetric(RunResult result, ProgressStore.LevelBest best)
        => _inner.EndOfRunMetric(result, best);

    public override string MenuChallengeLabel
    {
        get
        {
            if (_inner is PlaceBlocksWinCondition) return "TIMED BLOCKS";
            if (_inner is ReachHeightWinCondition) return "TIMED HEIGHT";
            return $"TIMED {_inner.MenuChallengeLabel}";
        }
    }

    public override (string primary, string suffix) MenuProgress(ProgressStore.LevelBest best, bool completed)
        => _inner.MenuProgress(best, completed);

    public override (string target, string best) TargetAndBest(
        ProgressStore.LevelBest best, bool completed, bool attempted)
    {
        (string target, string bestText) = _inner.TargetAndBest(best, completed, attempted);
        return ($"{target} in {FormatDuration(_timeLimitSeconds)}", bestText);
    }

    public static string FormatDuration(float seconds)
    {
        int total = Mathf.CeilToInt(Mathf.Max(0f, seconds));
        int minutes = total / 60;
        int secs = total % 60;
        return $"{minutes}:{secs:00}";
    }
}
