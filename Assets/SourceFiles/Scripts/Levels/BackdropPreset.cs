using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A chapter's layered backdrop, as data: sky colors that crossfade with tower altitude,
/// drifting procedural clouds or imported sprite layers, ground-level hill silhouettes
/// that fall away as you climb, and optional ambient particles (snow, petals, embers -
/// it's just color + motion). Rendered by LevelPresentationController.
/// A chapter without a preset gets the built-in classic dark sky (see Defaults).
/// </summary>
[CreateAssetMenu(fileName = "BackdropPreset", menuName = "Stacking/Levels/Backdrop Preset")]
public class BackdropPreset : ScriptableObject
{
    [System.Serializable]
    public sealed class SpriteBackdropLayer
    {
        [SerializeField] private Sprite sprite;
        [Tooltip("World height for this layer. 0 = fit the camera height.")]
        [SerializeField] private float worldHeight = 0f;
        [Tooltip("Y offset from the floor to the bottom of the layer.")]
        [SerializeField] private float floorOffsetY = 0f;
        [Tooltip("World X offset from the camera center.")]
        [SerializeField] private float worldOffsetX = 0f;
        [Tooltip("How many duplicate tiles to place to each side. 0 = draw this sprite once. Use >0 for cropped horizontal pack layers.")]
        [Min(0)]
        [SerializeField] private int horizontalTileRadius = 2;
        [Tooltip("World units to overlap neighboring tiles. Small overlap hides tiny transparent/cropped seams.")]
        [Min(0f)]
        [SerializeField] private float horizontalTileOverlap = 0.08f;
        [Tooltip("How much this layer follows the camera as the tower climbs. 0 = pinned to the floor, 1 = glued to the camera.")]
        [Range(0f, 1f)]
        [SerializeField] private float verticalParallax = 0.15f;
        [Tooltip("How much this layer follows the camera as it pans sideways (building wide / opening pan). 1 = glued to the camera (no sideways parallax, the old behaviour); lower = the layer lags, reading as depth; 0 = fixed in the world (maximum parallax). Near layers want low values, far layers high.")]
        [Range(0f, 1f)]
        [SerializeField] private float horizontalParallax = 1f;
        [Tooltip("Constant sideways scroll in world units/sec (clouds, mist, fog banks). The tile row wraps around itself, so the motion loops forever. Positive = rightward. Keep it subtle (~0.1). Needs horizontalTileRadius >= 1 so wrapped tiles cover the view. 0 = static. Ignored for a fill layer.")]
        [SerializeField] private float driftSpeedX = 0f;
        [Tooltip("Vertical bob amplitude in world units (0 = off). The layer floats up and down on a smooth sine - hovering craft, flying pyramids, drifting balloons. Ignored for a fill layer.")]
        [Min(0f)]
        [SerializeField] private float hoverAmount = 0f;
        [Tooltip("Seconds per full up-down hover cycle.")]
        [Min(0.5f)]
        [SerializeField] private float hoverPeriodSeconds = 6f;
        [Tooltip("Optional flipbook frames for this layer (vendor sprite animations - a retro sun's scrolling bands, a blinking sign). Plays as an endless loop at animationFps; the layer's `sprite` field stays the fallback/first frame and defines the layout. Frames must share the base sprite's size. Empty = static layer.")]
        [SerializeField] private Sprite[] animationFrames;
        [Tooltip("Flipbook frames per second. 0 = animation off, even if frames are assigned.")]
        [Min(0f)]
        [SerializeField] private float animationFps = 0f;
        [Tooltip("Treat this layer as a full-screen panorama (the back-most sky/atmosphere). It is always scaled to blanket the whole camera view like the sky, so its edges can never enter the frame - it never cuts off at the top however high the tower climbs. Parallax/tiling/offset are ignored for a fill layer.")]
        [SerializeField] private bool fillView = false;
        [Tooltip("If alpha > 0, a solid apron of this colour fills the area below this layer down past the screen bottom, so an opaque ground layer never shows a seam or a plain gap beneath it. Set it to the layer's solid ground colour. Ignored for a fill layer.")]
        [SerializeField] private Color groundFillColor = new Color(0f, 0f, 0f, 0f);
        [Range(0f, 1f)]
        [SerializeField] private float alpha = 1f;

        public Sprite Sprite => sprite;
        public float WorldHeight => worldHeight;
        public float FloorOffsetY => floorOffsetY;
        public float WorldOffsetX => worldOffsetX;
        public int HorizontalTileRadius => Mathf.Clamp(horizontalTileRadius, 0, 8);
        public float HorizontalTileOverlap => Mathf.Max(0f, horizontalTileOverlap);
        public float VerticalParallax => verticalParallax;
        public float HorizontalParallax => horizontalParallax;
        public float DriftSpeedX => driftSpeedX;
        public float HoverAmount => Mathf.Max(0f, hoverAmount);
        public float HoverPeriodSeconds => Mathf.Max(0.5f, hoverPeriodSeconds);
        public IReadOnlyList<Sprite> AnimationFrames => animationFrames;
        public float AnimationFps => Mathf.Max(0f, animationFps);
        public bool FillView => fillView;
        public Color GroundFillColor => groundFillColor;
        public float Alpha => alpha;
    }

    [Header("Sky (vertical gradient, crossfades with altitude)")]
    [SerializeField] private Color skyTopLow = new Color(0.10f, 0.14f, 0.20f);
    [SerializeField] private Color skyBottomLow = new Color(0.05f, 0.07f, 0.10f);
    [SerializeField] private Color skyTopHigh = new Color(0.06f, 0.08f, 0.14f);
    [SerializeField] private Color skyBottomHigh = new Color(0.03f, 0.04f, 0.07f);
    [Tooltip("Tower height (meters) over which the sky fades from the low to the high pair.")]
    [Min(1f)]
    [SerializeField] private float altitudeFadeMeters = 40f;
    [Tooltip("Optional variation while climbing: the low/high blend oscillates gently (darker, lighter, darker...) instead of fading once. 0 = plain fade.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float skyShimmerAmount = 0f;
    [Tooltip("Meters per shimmer cycle.")]
    [Min(2f)]
    [SerializeField] private float skyShimmerPeriodMeters = 20f;

    [Header("Sun (optional faint disc, revealed as the tower climbs)")]
    [SerializeField] private bool sunEnabled = false;
    [SerializeField] private Color sunColor = new Color(1f, 0.95f, 0.85f, 0.3f);
    [Tooltip("World diameter of the disc.")]
    [SerializeField] private float sunSize = 3f;
    [Tooltip("Horizontal screen position (0 = left, 1 = right).")]
    [Range(0f, 1f)]
    [SerializeField] private float sunScreenX = 0.72f;
    [Tooltip("Tower height (meters) where the sun's center sits. It drifts slowly relative to the camera, so it stays in view for a long band of the climb.")]
    [SerializeField] private float sunHeightMeters = 30f;

    [Header("Ground props (cacti etc. at floor level, sink away as you climb)")]
    [Min(0)]
    [SerializeField] private int propCount = 0;
    [SerializeField] private Color propColor = new Color(0.42f, 0.55f, 0.38f);
    [SerializeField] private Vector2 propScaleRange = new Vector2(1.4f, 2.2f);

    public enum CloudStyle { Soft, Blocky, Streak }
    public enum HillStyle { Rolling, Mesa }

    [Header("Clouds (procedural, drift horizontally, recycled at all heights)")]
    [Min(0)]
    [SerializeField] private int cloudCount = 6;
    [SerializeField] private CloudStyle cloudStyle = CloudStyle.Soft;
    [SerializeField] private Color cloudColor = new Color(0.6f, 0.65f, 0.72f, 0.25f);
    [SerializeField] private float cloudDriftSpeed = 0.3f;
    [SerializeField] private Vector2 cloudScaleRange = new Vector2(1.5f, 3.5f);

    // Defaults double as the built-in classic dark sky: subtle dark mountains anchor
    // the scene now that floors carry no buildings - scenery is the backdrop's job.
    [Header("Hills (ground-level silhouettes, sink away as the tower climbs)")]
    [SerializeField] private bool hillsEnabled = true;
    [SerializeField] private HillStyle hillStyle = HillStyle.Mesa;
    [SerializeField] private Color hillFarColor = new Color(0.17f, 0.21f, 0.29f);
    [SerializeField] private Color hillNearColor = new Color(0.13f, 0.16f, 0.23f);

    [Header("Ground fog (the haze the floor terrain dissolves into at the screen bottom)")]
    [Tooltip("Fog bank colour at the base of the floor columns. Leave alpha at 0 to auto-derive a haze from the near-hill colour.")]
    [SerializeField] private Color groundFogColor = new Color(0f, 0f, 0f, 0f);
    [Tooltip("Lit haze at the TOP of the fog bank. Leave alpha at 0 to auto-derive (fog colour lifted toward the low sky).")]
    [SerializeField] private Color groundFogLightColor = new Color(0f, 0f, 0f, 0f);
    [Tooltip("Deep shade at the BOTTOM of the fog bank. Leave alpha at 0 to auto-derive (fog colour pulled toward the near-hill shade / black).")]
    [SerializeField] private Color groundFogDeepColor = new Color(0f, 0f, 0f, 0f);
    [Tooltip("Horizontal drift of the fog pattern in world units/s (back layer drifts one way, front layers the other). 0 = default (0.35). Visible within a couple of seconds, never distracting.")]
    [Min(0)]
    [SerializeField] private float groundFogDriftSpeed = 0f;
    [Tooltip("Scale of the fog's noise pattern. 0 = default (1). Above 1 = smaller, busier wisps; below 1 = broad slow swells.")]
    [Min(0)]
    [SerializeField] private float groundFogNoiseScale = 0f;
    [Tooltip("Thickness multiplier of the fog's soft top (how high the haze climbs and how far the top edge wanders). 0 = default (1). 0.5..2.5.")]
    [Min(0)]
    [SerializeField] private float groundFogThickness = 0f;

    [Header("Imported backdrop layers (optional, far to near)")]
    [SerializeField] private SpriteBackdropLayer[] spriteBackdropLayers;

    [Header("Ambient particles (0 = off; snow, petals, embers... color + motion)")]
    [Min(0)]
    [SerializeField] private int particleCount = 0;
    [SerializeField] private Color particleColor = Color.white;
    [SerializeField] private float particleSize = 0.12f;
    [SerializeField] private float particleFallSpeed = 0.8f;
    [SerializeField] private float particleSwayAmount = 0.6f;
    [Tooltip("0 = round mote. > 0 = each particle is a streak this many world units long, angled along its fall direction - rain, driving snow.")]
    [SerializeField] private float particleStreakLength = 0f;
    [Tooltip("Constant horizontal wind (world units/s) on every particle - slants rain and snow. 0 = none.")]
    [SerializeField] private float particleWindX = 0f;
    [Tooltip("Render particles in front of ALL imported backdrop layers (still behind gameplay). Weather like rain wants this; ambient motes usually sit among the layers.")]
    [SerializeField] private bool particlesInFront = false;

    [Header("Heat haze (0 = off; hot-air shimmer that arrives in gusts, never constant)")]
    [Tooltip("Peak shimmer strength for a hot chapter. A slow gust envelope drives it between zero and this value, and it fades out entirely as the tower climbs away from the ground.")]
    [Range(0f, 1f)]
    [SerializeField] private float heatHazeAmount = 0f;

    [Header("Flybys (0 = off; rare bird silhouettes crossing the sky)")]
    [Tooltip("Birds per crossing. Small quick flock = songbirds; 1 big slow dark one = a vulture; a few pale slow ones = cranes.")]
    [Min(0)]
    [SerializeField] private int flybyFlockSize = 0;
    [SerializeField] private Color flybyColor = new Color(0.08f, 0.08f, 0.12f, 0.85f);
    [Tooltip("Seconds between crossings (min..max, re-rolled each time). Keep flybys RARE - intermittent motion reads as alive, constant motion reads as wallpaper.")]
    [SerializeField] private Vector2 flybyIntervalSeconds = new Vector2(25f, 55f);
    [Tooltip("Horizontal speed in world units/sec.")]
    [SerializeField] private float flybySpeed = 2.2f;
    [Tooltip("Bird size multiplier. 1 = small songbird; 1.3-1.8 = crane / vulture. Bigger birds automatically flap slower.")]
    [SerializeField] private float flybyScale = 1f;

    public Color SkyTopLow => skyTopLow;
    public Color SkyBottomLow => skyBottomLow;
    public Color SkyTopHigh => skyTopHigh;
    public Color SkyBottomHigh => skyBottomHigh;
    public float AltitudeFadeMeters => altitudeFadeMeters;
    public float SkyShimmerAmount => skyShimmerAmount;
    public float SkyShimmerPeriodMeters => skyShimmerPeriodMeters;
    public bool SunEnabled => sunEnabled;
    public Color SunColor => sunColor;
    public float SunSize => sunSize;
    public float SunScreenX => sunScreenX;
    public float SunHeightMeters => sunHeightMeters;
    public int PropCount => propCount;
    public Color PropColor => propColor;
    public Vector2 PropScaleRange => propScaleRange;
    public int CloudCount => cloudCount;
    public CloudStyle Clouds => cloudStyle;
    public Color CloudColor => cloudColor;
    public float CloudDriftSpeed => cloudDriftSpeed;
    public Vector2 CloudScaleRange => cloudScaleRange;
    public bool HillsEnabled => hillsEnabled;
    public HillStyle Hills => hillStyle;
    public Color HillFarColor => hillFarColor;
    public Color HillNearColor => hillNearColor;
    public Color GroundFogColor => groundFogColor;
    public Color GroundFogLightColor => groundFogLightColor;
    public Color GroundFogDeepColor => groundFogDeepColor;
    public float GroundFogDriftSpeed => groundFogDriftSpeed;
    public float GroundFogNoiseScale => groundFogNoiseScale;
    public float GroundFogThickness => groundFogThickness;
    public IReadOnlyList<SpriteBackdropLayer> SpriteBackdropLayers => spriteBackdropLayers;
    public int ParticleCount => particleCount;
    public Color ParticleColor => particleColor;
    public float ParticleSize => particleSize;
    public float ParticleFallSpeed => particleFallSpeed;
    public float ParticleSwayAmount => particleSwayAmount;
    public float ParticleStreakLength => particleStreakLength;
    public float ParticleWindX => particleWindX;
    public bool ParticlesInFront => particlesInFront;
    public float HeatHazeAmount => heatHazeAmount;
    public int FlybyFlockSize => flybyFlockSize;
    public Color FlybyColor => flybyColor;
    public Vector2 FlybyIntervalSeconds => flybyIntervalSeconds;
    public float FlybySpeed => flybySpeed;
    public float FlybyScale => flybyScale;

    // The classic dark sky used by any chapter without an authored preset.
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
