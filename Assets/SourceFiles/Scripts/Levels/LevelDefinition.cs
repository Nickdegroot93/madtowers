using UnityEngine;

[CreateAssetMenu(fileName = "LevelDefinition", menuName = "Stacking/Levels/Level Definition")]
public class LevelDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string displayName = "Level";

    [Header("Rules")]
    [SerializeField] private GameModeConfig gameModeConfig;

    [Header("Presentation")]
    [SerializeField] private Sprite backgroundImage;
    [SerializeField] private Color backgroundTint = Color.white;
    [SerializeField] private AudioClip music;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public GameModeConfig GameModeConfig => gameModeConfig;
    public Sprite BackgroundImage => backgroundImage;
    public Color BackgroundTint => backgroundTint;
    public AudioClip Music => music;
}
