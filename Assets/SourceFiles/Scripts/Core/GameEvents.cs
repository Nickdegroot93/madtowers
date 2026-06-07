using System;
using UnityEngine;

public static class GameEvents
{
    public static event Action<int> ScoreChanged;
    public static event Action<int> LivesChanged;
    public static event Action<float> HeightChanged;
    public static event Action<string> NextBlockChanged;
    public static event Action<int, float> GameOver;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        ScoreChanged = null;
        LivesChanged = null;
        HeightChanged = null;
        NextBlockChanged = null;
        GameOver = null;
    }

    public static void RaiseScoreChanged(int score) => ScoreChanged?.Invoke(score);
    public static void RaiseLivesChanged(int lives) => LivesChanged?.Invoke(lives);
    public static void RaiseHeightChanged(float height) => HeightChanged?.Invoke(height);
    public static void RaiseNextBlockChanged(string blockName) => NextBlockChanged?.Invoke(blockName);
    public static void RaiseGameOver(int score, float maxHeight) => GameOver?.Invoke(score, maxHeight);
}
