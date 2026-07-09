public struct RunResult
{
    public RunResult(int score, int lives, int standingBlocks, int totalPlacedBlocks, float maxHeight,
        int coinsEarned = 0)
    {
        Score = score;
        Lives = lives;
        StandingBlocks = standingBlocks;
        TotalPlacedBlocks = totalPlacedBlocks;
        MaxHeight = maxHeight;
        CoinsEarned = coinsEarned;
    }

    public int Score { get; }
    public int Lives { get; }
    public int StandingBlocks { get; }
    public int TotalPlacedBlocks { get; }
    public float MaxHeight { get; }
    /// <summary>Skill coins minted during the run (JUICE.md Phase 3); excludes the win bonus.</summary>
    public int CoinsEarned { get; }
}
