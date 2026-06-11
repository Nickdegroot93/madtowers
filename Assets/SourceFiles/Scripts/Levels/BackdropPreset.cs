using UnityEngine;

/// <summary>
/// A theme's layered backdrop, as data: sky colors that crossfade with tower altitude,
/// drifting procedural clouds, ground-level hill silhouettes that fall away as you climb,
/// and optional ambient particles (snow, petals, embers - it's just color + motion).
/// Rendered by LevelPresentationController; no background images involved.
/// A theme without a preset gets the built-in classic dark sky (see Defaults).
/// </summary>
[CreateAssetMenu(fileName = "BackdropPreset", menuName = "Stacking/Levels/Backdrop Preset")]
public class BackdropPreset : ScriptableObject
{
    [Header("Sky (vertical gradient, crossfades with altitude)")]
    [SerializeField] private Color skyTopLow = new Color(0.10f, 0.14f, 0.20f);
    [SerializeField] private Color skyBottomLow = new Color(0.05f, 0.07f, 0.10f);
    [SerializeField] private Color skyTopHigh = new Color(0.06f, 0.08f, 0.14f);
    [SerializeField] private Color skyBottomHigh = new Color(0.03f, 0.04f, 0.07f);
    [Tooltip("Tower height (meters) over which the sky fades from the low to the high pair.")]
    [Min(1f)]
    [SerializeField] private float altitudeFadeMeters = 40f;

    public enum CloudStyle { Soft, Blocky }
    public enum HillStyle { Rolling, Mesa }

    [Header("Clouds (procedural, drift horizontally, recycled at all heights)")]
    [Min(0)]
    [SerializeField] private int cloudCount = 6;
    [SerializeField] private CloudStyle cloudStyle = CloudStyle.Soft;
    [SerializeField] private Color cloudColor = new Color(0.6f, 0.65f, 0.72f, 0.25f);
    [SerializeField] private float cloudDriftSpeed = 0.3f;
    [SerializeField] private Vector2 cloudScaleRange = new Vector2(1.5f, 3.5f);

    [Header("Hills (ground-level silhouettes, sink away as the tower climbs)")]
    [SerializeField] private bool hillsEnabled = false;
    [SerializeField] private HillStyle hillStyle = HillStyle.Rolling;
    [SerializeField] private Color hillFarColor = new Color(0.5f, 0.62f, 0.45f);
    [SerializeField] private Color hillNearColor = new Color(0.36f, 0.5f, 0.33f);

    [Header("Ambient particles (0 = off; snow, petals, embers... color + motion)")]
    [Min(0)]
    [SerializeField] private int particleCount = 0;
    [SerializeField] private Color particleColor = Color.white;
    [SerializeField] private float particleSize = 0.12f;
    [SerializeField] private float particleFallSpeed = 0.8f;
    [SerializeField] private float particleSwayAmount = 0.6f;

    public Color SkyTopLow => skyTopLow;
    public Color SkyBottomLow => skyBottomLow;
    public Color SkyTopHigh => skyTopHigh;
    public Color SkyBottomHigh => skyBottomHigh;
    public float AltitudeFadeMeters => altitudeFadeMeters;
    public int CloudCount => cloudCount;
    public CloudStyle Clouds => cloudStyle;
    public Color CloudColor => cloudColor;
    public float CloudDriftSpeed => cloudDriftSpeed;
    public Vector2 CloudScaleRange => cloudScaleRange;
    public bool HillsEnabled => hillsEnabled;
    public HillStyle Hills => hillStyle;
    public Color HillFarColor => hillFarColor;
    public Color HillNearColor => hillNearColor;
    public int ParticleCount => particleCount;
    public Color ParticleColor => particleColor;
    public float ParticleSize => particleSize;
    public float ParticleFallSpeed => particleFallSpeed;
    public float ParticleSwayAmount => particleSwayAmount;

    // The classic dark sky used by any theme without an authored preset.
    private static BackdropPreset _defaults;

    public static BackdropPreset Defaults
    {
        get
        {
            if (_defaults == null)
            {
                _defaults = CreateInstance<BackdropPreset>();
                _defaults.hideFlags = HideFlags.HideAndDontSave;
            }
            return _defaults;
        }
    }
}
