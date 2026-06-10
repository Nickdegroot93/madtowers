using UnityEngine;

public static class LevelSelectionState
{
    public static LevelDefinition SelectedLevel { get; private set; }
    public static bool IsSelectionPending { get; private set; }

    public static void BeginSelectionIfNeeded()
    {
        IsSelectionPending = SelectedLevel == null;
    }

    public static void SelectLevel(LevelDefinition level)
    {
        SelectedLevel = level;
        IsSelectionPending = false;
    }

    public static void ClearSelection()
    {
        SelectedLevel = null;
        IsSelectionPending = false;
    }

    public static GameModeConfig ResolveGameMode(GameModeConfig fallback)
    {
        return SelectedLevel != null && SelectedLevel.GameModeConfig != null
            ? SelectedLevel.GameModeConfig
            : fallback;
    }
}
