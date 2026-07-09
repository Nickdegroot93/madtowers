using System.Collections.Generic;

public static class LevelMenuPresentation
{
    public readonly struct Snapshot
    {
        public Snapshot(string challengeLabel, string progressPrimary, string progressSuffix)
        {
            ChallengeLabel = challengeLabel;
            ProgressPrimary = progressPrimary;
            ProgressSuffix = progressSuffix;
            ProgressLabel = string.IsNullOrWhiteSpace(progressSuffix)
                ? progressPrimary
                : $"{progressPrimary} {progressSuffix}";
        }

        public string ChallengeLabel { get; }
        public string ProgressLabel { get; }
        public string ProgressPrimary { get; }
        public string ProgressSuffix { get; }
    }

    public static Snapshot Build(LevelDefinition level, bool completed)
    {
        ProgressStore.LevelBest best = ProgressStore.GetBest(level);
        ILevelMenuProgressProvider progressProvider = FindProgressProvider(level);

        string challengeLabel = ChallengeLabel(level, progressProvider);
        ProgressParts progress = Progress(level, best, completed, progressProvider);

        return new Snapshot(challengeLabel, progress.Primary, progress.Suffix);
    }

    private readonly struct ProgressParts
    {
        public ProgressParts(string primary, string suffix)
        {
            Primary = primary;
            Suffix = suffix;
        }

        public string Primary { get; }
        public string Suffix { get; }
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
        return level.WinCondition.MenuChallengeLabel;
    }

    private static ProgressParts Progress(LevelDefinition level, ProgressStore.LevelBest best, bool completed,
        ILevelMenuProgressProvider progressProvider)
    {
        if (level == null) return new ProgressParts(completed ? "Completed" : "Free", completed ? "" : "Play");

        if (progressProvider != null)
        {
            return ParseProgressLabel(progressProvider.MenuProgressLabel(level, best, completed), completed);
        }

        (string primary, string suffix) = level.WinCondition.MenuProgress(best, completed);
        return new ProgressParts(primary, suffix);
    }

    private static ProgressParts ParseProgressLabel(string label, bool completed)
    {
        if (string.IsNullOrWhiteSpace(label)) return new ProgressParts("", "");

        int slash = label.IndexOf('/');
        if (slash > 0)
        {
            string primary = label.Substring(0, slash).Trim();
            string suffix = label.Substring(slash + 1).Trim();
            if (!completed) return new ProgressParts(primary, $"/ {suffix}");

            int unitStart = suffix.IndexOf(' ');
            string unit = unitStart >= 0 ? suffix.Substring(unitStart + 1).Trim() : "";
            return new ProgressParts(primary, unit);
        }

        string trimmed = label.Trim();
        int split = trimmed.IndexOf(' ');
        if (split < 0) return new ProgressParts(trimmed, "");
        return new ProgressParts(trimmed.Substring(0, split), trimmed.Substring(split + 1).Trim());
    }

    /// <summary>The modifier that owns this level's progress presentation, if any. Shared by the
    /// menu card and the end-of-run results card so both resolve the level's metric identically.</summary>
    public static ILevelMenuProgressProvider FindProgressProvider(LevelDefinition level)
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
