public interface ILevelMenuProgressProvider
{
    string MenuChallengeLabel { get; }
    string MenuProgressLabel(LevelDefinition level, ProgressStore.LevelBest best, bool completed);
}
