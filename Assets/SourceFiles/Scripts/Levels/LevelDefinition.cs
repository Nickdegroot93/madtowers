using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "LevelDefinition", menuName = "Stacking/Levels/Level Definition")]
public class LevelDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string displayName = "Level";

    [Header("Rules")]
    [SerializeField] private GameModeConfig gameModeConfig;

    [Header("Goal")]
    [SerializeField] private LevelTargetType targetType = LevelTargetType.Endless;
    [Tooltip("Blocks to place or height in meters, depending on the target type.")]
    [Min(1)]
    [SerializeField] private float targetValue = 10f;
    [Tooltip("One-sentence player instruction shown as a banner when the level starts. Empty = no banner.")]
    [SerializeField] private string instruction = "";

    [Header("Custom Behaviour")]
    [Tooltip("Optional composable behaviours beyond settings (earthquakes, wind, ...). See LevelModifier.")]
    [SerializeField] private LevelModifier[] modifiers;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public GameModeConfig GameModeConfig => gameModeConfig;
    public LevelTargetType TargetType => targetType;
    public float TargetValue => Mathf.Max(1f, targetValue);
    public string Instruction => instruction;
    public IReadOnlyList<LevelModifier> Modifiers => modifiers;
}
