public interface ILevelMenuProgressProvider
{
    string MenuChallengeLabel { get; }
    string MenuProgressLabel(LevelDefinition level, ProgressStore.LevelBest best, bool completed);

    /// <summary>End-of-run card metric override: a modifier that owns the level's progress
    /// presentation also owns what the results screen leads with (waves, not raw blocks).</summary>
    ResultMetric EndOfRunMetric(LevelDefinition level, RunResult result, ProgressStore.LevelBest best);
}
