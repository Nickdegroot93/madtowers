using System.Collections.Generic;
using UnityEngine;

public static class LevelMenuPresentation
{
    public readonly struct Snapshot
    {
        public Snapshot(string challengeLabel, string progressLabel)
        {
            ChallengeLabel = challengeLabel;
            ProgressLabel = progressLabel;
        }

        public string ChallengeLabel { get; }
        public string ProgressLabel { get; }
    }

    public static Snapshot Build(LevelDefinition level, bool completed)
    {
        ProgressStore.LevelBest best = ProgressStore.GetBest(level);
        ILevelMenuProgressProvider progressProvider = FindProgressProvider(level);

        string challengeLabel = ChallengeLabel(level, progressProvider);
        string progressLabel = progressProvider != null
            ? progressProvider.MenuProgressLabel(level, best, completed)
            : DefaultProgressLabel(level, best, completed);

        return new Snapshot(challengeLabel, progressLabel);
    }

    private static string ChallengeLabel(LevelDefinition level, ILevelMenuProgressProvider progressProvider)
    {
        if (level != null && !string.IsNullOrWhiteSpace(level.MenuChallengeLabelOverride))
        {
            return level.MenuChallengeLabelOverride.ToUpperInvariant();
        }

        if (progressProvider != null && !string.IsNullOrWhiteSpace(progressProvider.MenuChallengeLabel))
        {
            return progressProvider.MenuChallengeLabel.ToUpperInvariant();
        }

        if (level == null) return "ENDLESS";
        return level.TargetType switch
        {
            LevelTargetType.PlaceBlocks => "BLOCK COUNT",
            LevelTargetType.ReachHeight => "HEIGHT CHALLENGE",
            _ => "ENDLESS"
        };
    }

    private static string DefaultProgressLabel(LevelDefinition level, ProgressStore.LevelBest best, bool completed)
    {
        if (level == null) return completed ? "Completed" : "Free Play";

        int bestScore = best != null ? best.bestScore : 0;
        float bestHeight = best != null ? best.bestHeightMeters : 0f;

        if (level.TargetType == LevelTargetType.ReachHeight)
        {
            int reached = Mathf.RoundToInt(completed ? Mathf.Max(bestHeight, level.TargetValue) : bestHeight);
            return $"{reached}m / {Mathf.RoundToInt(level.TargetValue)}m";
        }

        if (level.TargetType == LevelTargetType.PlaceBlocks)
        {
            int target = Mathf.RoundToInt(level.TargetValue);
            int reached = completed ? Mathf.Max(bestScore, target) : bestScore;
            return $"{reached} / {target} Blocks";
        }

        return completed ? "Completed" : "Free Play";
    }

    private static ILevelMenuProgressProvider FindProgressProvider(LevelDefinition level)
    {
        IReadOnlyList<LevelModifier> modifiers = level != null ? level.Modifiers : null;
        if (modifiers == null) return null;

        for (int i = 0; i < modifiers.Count; i++)
        {
            if (modifiers[i] is ILevelMenuProgressProvider provider) return provider;
        }
        return null;
    }
}
