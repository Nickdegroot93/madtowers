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

    [Header("Custom Behaviour")]
    [Tooltip("Optional composable behaviours beyond settings (earthquakes, wind, ...). See LevelModifier.")]
    [SerializeField] private LevelModifier[] modifiers;

    [Header("Presentation (overrides the theme's, if set)")]
    [SerializeField] private Sprite backgroundImage;
    [SerializeField] private Color backgroundTint = Color.white;
    [SerializeField] private AudioClip music;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public GameModeConfig GameModeConfig => gameModeConfig;
    public LevelTargetType TargetType => targetType;
    public float TargetValue => Mathf.Max(1f, targetValue);
    public IReadOnlyList<LevelModifier> Modifiers => modifiers;
    public Sprite BackgroundImage => backgroundImage;
    public Color BackgroundTint => backgroundTint;
    public AudioClip Music => music;
}
