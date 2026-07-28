using UnityEngine;

/// <summary>Win by clearing this many puzzle waves (LevelTargetType.ClearWaves). The wave engine
/// lives on the level's HeightLimitWavesModifier, which publishes its live run; this condition
/// only reads it, so the thresholds have one source of truth. Like PlaceBlocks it is met by the
/// LIVE standing count crossing the last required wave's cumulative block target - a collapse
/// during verification genuinely un-meets the goal, and the standing-count signal re-arms it for
/// free. Stored/board scores for wave levels are ENCODED (waves x1000 + in-wave progress, see
/// HeightLimitWavesModifier); every read here decodes.</summary>
public sealed class ClearWavesWinCondition : WinCondition
{
    private readonly int _wavesToWin;

    public ClearWavesWinCondition(float wavesToWin) => _wavesToWin = Mathf.Max(1, Mathf.RoundToInt(wavesToWin));

    public override bool IsMet(in WinContext ctx)
    {
        HeightLimitWavesModifier run = HeightLimitWavesModifier.ActiveRun;
        return run != null && ctx.GameManager != null &&
            ctx.GameManager.placedBlocks >= run.StandingTargetForWave(_wavesToWin);
    }

    // PEAK standing, not live: rarity escalation must never rewind when blocks are lost
    // (BLOCKS.md - losing blocks must not revoke an earned picker tier).
    public override float RunProgress01(GameManager gameManager)
    {
        HeightLimitWavesModifier run = HeightLimitWavesModifier.ActiveRun;
        if (run == null || gameManager == null) return 0f;
        return Mathf.Clamp01((float)run.PeakStanding / run.StandingTargetForWave(_wavesToWin));
    }

    public override ResultMetric EndOfRunMetric(RunResult result, ProgressStore.LevelBest best)
    {
        HeightLimitWavesModifier run = HeightLimitWavesModifier.ActiveRun;
        int waves = run != null ? run.WavesCleared : 0;
        int bestWaves = HeightLimitWavesModifier.DecodeWaves(best != null ? best.bestScore : 0);
        return new ResultMetric("WAVES CLEARED", waves, bestWaves, isMeters: false,
            targetText: $"{_wavesToWin} WAVES");
    }

    public override string MenuChallengeLabel => "PUZZLE WAVES";

    public override (string primary, string suffix) MenuProgress(ProgressStore.LevelBest best, bool completed)
    {
        int bestWaves = HeightLimitWavesModifier.DecodeWaves(best != null ? best.bestScore : 0);
        int reached = bestWaves > 0 ? bestWaves : (completed ? _wavesToWin : 0);
        return completed ? (reached.ToString(), "Waves") : (reached.ToString(), $"/ {_wavesToWin} Waves");
    }

    public override (string target, string best) TargetAndBest(ProgressStore.LevelBest best, bool completed, bool attempted)
    {
        int bestWaves = HeightLimitWavesModifier.DecodeWaves(best != null ? best.bestScore : 0);
        return ($"{_wavesToWin} Waves", attempted ? $"{bestWaves} Waves" : "-");
    }

    public override string FormatBoardScore(int bestScore)
        => HeightLimitWavesModifier.DecodeWaves(bestScore).ToString();
}
