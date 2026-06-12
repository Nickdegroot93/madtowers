using System;
using UnityEngine;

public static class GameEvents
{
    public static event Action<int> ScoreChanged;
    public static event Action<int> LivesChanged;
    /// <summary>A life was just charged (LivesChanged also fires; this one never fires for gains).</summary>
    public static event Action LifeLost;
    public static event Action<float> HeightChanged;
    public static event Action<string> NextBlockChanged;
    /// <summary>A new piece entered play, with the controller and the variant it rolled (null = normal).</summary>
    public static event Action<BlockController, BlockData> BlockSpawned;
    public static event Action<int, float> GameOver;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        ScoreChanged = null;
        LivesChanged = null;
        LifeLost = null;
        HeightChanged = null;
        NextBlockChanged = null;
        BlockSpawned = null;
        GameOver = null;
    }

    public static void RaiseScoreChanged(int score) => ScoreChanged?.Invoke(score);
    public static void RaiseLivesChanged(int lives) => LivesChanged?.Invoke(lives);
    public static void RaiseLifeLost() => LifeLost?.Invoke();
    public static void RaiseHeightChanged(float height) => HeightChanged?.Invoke(height);
    public static void RaiseNextBlockChanged(string blockName) => NextBlockChanged?.Invoke(blockName);
    public static void RaiseBlockSpawned(BlockController block, BlockData variant) => BlockSpawned?.Invoke(block, variant);
    public static void RaiseGameOver(int score, float maxHeight) => GameOver?.Invoke(score, maxHeight);
}
