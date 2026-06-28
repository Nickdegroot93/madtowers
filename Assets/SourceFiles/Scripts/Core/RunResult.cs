public struct RunResult
{
    public RunResult(int score, int lives, int standingBlocks, int totalPlacedBlocks, float maxHeight)
    {
        Score = score;
        Lives = lives;
        StandingBlocks = standingBlocks;
        TotalPlacedBlocks = totalPlacedBlocks;
        MaxHeight = maxHeight;
    }

    public int Score { get; }
    public int Lives { get; }
    public int StandingBlocks { get; }
    public int TotalPlacedBlocks { get; }
    public float MaxHeight { get; }
}
