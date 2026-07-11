/// <summary>
/// A LevelModifier that claims part (or all) of its level's MENU + RESULTS presentation.
/// This is how a modifier names the GAME TYPE it turns the level into: the challenge label
/// resolution is level override > first providing modifier > the goal's default (see
/// LevelMenuPresentation.ChallengeLabel), so an Airtight level reads "AIRTIGHT" with the
/// block goal as its progress line, exactly like Height-Limit Waves reads "PUZZLE WAVES".
///
/// A provider may claim ONLY the type name: return null from MenuProgressLabel /
/// EndOfRunMetric and the presentation falls through to the level's WinCondition, so
/// label-only types (Airtight, Void Zones) never re-implement goal formatting. A provider
/// that owns a bespoke metric (waves) returns real values from all three.
/// </summary>
public interface ILevelMenuProgressProvider
{
    /// <summary>The game-type name shown as the level's challenge label ("PUZZLE WAVES",
    /// "AIRTIGHT"). Null/empty = don't claim it.</summary>
    string MenuChallengeLabel { get; }

    /// <summary>Menu progress text ("3 / 5 Waves"). Null/empty = the goal's default
    /// progress is shown instead.</summary>
    string MenuProgressLabel(LevelDefinition level, ProgressStore.LevelBest best, bool completed);

    /// <summary>End-of-run card metric override: a modifier that owns the level's progress
    /// presentation also owns what the results screen leads with (waves, not raw blocks).
    /// Null = the goal's default metric.</summary>
    ResultMetric? EndOfRunMetric(LevelDefinition level, RunResult result, ProgressStore.LevelBest best);
}
