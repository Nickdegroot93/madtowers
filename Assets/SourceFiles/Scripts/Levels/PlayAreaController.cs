using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the level's floor. The scene's legacy "Base Platform" bar is now only the DATUM anchor -
/// its top edge defines floor height 0 (the origin for tower height, islands and backdrop
/// anchoring) - while the actual landable terrain (colliders + grounded visuals + fog) is built by
/// <see cref="FloorTerrain"/> from the active GameModeConfig's FloorSegmentConfigs: flat strips,
/// steps, valleys or free-standing pillars, all grounded to the bottom of the screen.
/// The bar's own renderer and collider are disabled at runtime; keeping the object lets old scenes
/// and the height systems keep their reference point without migration.
/// </summary>
public class PlayAreaController : MonoBehaviour
{
    [SerializeField] private GameModeConfig gameModeConfig;
    [SerializeField] private Transform floorTransform;
    [SerializeField] private int fallbackCenterColumn = 0;
    [Min(1)]
    [SerializeField] private int fallbackColumnCount = 9;
    [Tooltip("Pulls each floor run's collider in by this many world units per side so pieces don't snag on top corners (PHYSICS.md section 3). The visual spans the configured columns exactly.")]
    [SerializeField] private float floorColliderEdgeInset = 0.03f;
    [Tooltip("Friction applied to the floor so blocks grip it instead of sliding. Should roughly match the block friction (PHYSICS.md section 3).")]
    [Range(0f, 1f)]
    [SerializeField] private float floorFriction = 0.95f;

    private FloorTerrain _terrain;

    private void Awake()
    {
        ApplyConfig();
    }

    public void ApplyConfig()
    {
        GameModeConfig activeConfig = LevelSelectionState.ResolveGameMode(gameModeConfig);
        float gridSpacing = activeConfig != null ? activeConfig.GridSpacing : 1f;

        IReadOnlyList<FloorSegmentConfig> segments =
            activeConfig != null && activeConfig.FloorSegments != null && activeConfig.FloorSegments.Count > 0
                ? activeConfig.FloorSegments
                : new[] { new FloorSegmentConfig(fallbackCenterColumn, fallbackColumnCount) };

        float datumY = ResolveDatumY();
        DisableLegacyBar();

        _terrain = FloorTerrain.Build(
            _terrain, segments, datumY, gridSpacing, floorColliderEdgeInset, floorFriction, ResolveFog());
    }

    /// <summary>World Y of the floor datum (height-0 top surface) - the origin for tower height in
    /// meters. Raised terrain columns sit above this; the datum is always the lowest landable
    /// surface, so every consumer of a single "floor Y" keeps its meaning.</summary>
    public bool TryGetFloorTopWorldY(out float floorTopY)
    {
        floorTopY = ResolveDatumY();
        return true;
    }

    // The datum comes from the legacy bar's sprite bounds, not its collider: AutoSyncTransforms is
    // off project-wide (stale collider bounds), and the collider is disabled once the terrain owns
    // collision. Sprite math works before AND after ApplyConfig, whatever the Awake order.
    private float ResolveDatumY()
    {
        Transform target = floorTransform != null ? floorTransform : transform;
        SpriteRenderer bar = target.GetComponent<SpriteRenderer>();
        if (bar != null && bar.sprite != null)
        {
            return target.position.y + bar.sprite.bounds.extents.y * Mathf.Abs(target.lossyScale.y);
        }

        Collider2D collider = target.GetComponent<Collider2D>();
        if (collider != null) return collider.bounds.max.y;
        return target.position.y;
    }

    private void DisableLegacyBar()
    {
        Transform target = floorTransform != null ? floorTransform : transform;

        SpriteRenderer bar = target.GetComponent<SpriteRenderer>();
        if (bar != null) bar.enabled = false;

        Collider2D collider = target.GetComponent<Collider2D>();
        if (collider != null) collider.enabled = false;

        // The old plateau skin child, if a scene still carries one from a previous run.
        Transform plateau = target.Find("PlateauSkin");
        if (plateau != null) Destroy(plateau.gameObject);
    }

    // Fog look: resolved from the chapter's preset (explicit colours or derived from its near-hill
    // and sky colours) so every backdrop gets a plausible living fog without authoring.
    private FloorFogSettings ResolveFog()
    {
        ChapterDefinition chapter = Campaign.FindChapterOf(LevelSelectionState.SelectedLevel);
        BackdropPreset preset = chapter != null && chapter.Backdrop != null
            ? chapter.Backdrop
            : BackdropPreset.Defaults;
        return FloorFogSettings.Resolve(preset);
    }
}
