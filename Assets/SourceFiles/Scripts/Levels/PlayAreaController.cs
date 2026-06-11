using System.Collections.Generic;
using UnityEngine;

public class PlayAreaController : MonoBehaviour
{
    [System.Serializable]
    private sealed class FloorSegmentBinding
    {
        [SerializeField] private Transform floorTransform;
        [SerializeField] private int configIndex = 0;
        [SerializeField] private int fallbackCenterColumn = 0;
        [Min(1)]
        [SerializeField] private int fallbackColumnCount = 9;

        public Transform FloorTransform => floorTransform;
        public int ConfigIndex => Mathf.Max(0, configIndex);
        public int FallbackCenterColumn => fallbackCenterColumn;
        public int FallbackColumnCount => Mathf.Max(1, fallbackColumnCount);
    }

    [SerializeField] private GameModeConfig gameModeConfig;
    [SerializeField] private FloorSegmentBinding[] floorSegments;
    [SerializeField] private Transform floorTransform;
    [SerializeField] private int fallbackCenterColumn = 0;
    [Min(1)]
    [SerializeField] private int fallbackColumnCount = 9;
    [Tooltip("Pulls the floor's collider in by this many world units per side (the green visual is unchanged, so it still spans exactly its configured columns). Matches the block collider inset so pieces don't snag on the floor's top corners. Set to 0 to disable.")]
    [SerializeField] private float floorColliderEdgeInset = 0.03f;
    [Tooltip("Friction applied to the floor so blocks grip it instead of sliding. Should roughly match the block friction.")]
    [Range(0f, 1f)]
    [SerializeField] private float floorFriction = 0.95f;

    [Tooltip("Sorting order of the plateau skin: behind blocks (0), in front of the theme background (-100).")]
    [SerializeField] private int groundSkinSortingOrder = -50;

    private PhysicsMaterial2D _floorMaterial;

    private void Awake()
    {
        ApplyConfig();
    }

    public void ApplyConfig()
    {
        GameModeConfig activeConfig = LevelSelectionState.ResolveGameMode(gameModeConfig);
        IReadOnlyList<FloorSegmentConfig> configuredSegments = activeConfig != null
            ? activeConfig.FloorSegments
            : null;

        if (floorSegments != null && floorSegments.Length > 0)
        {
            for (int i = 0; i < floorSegments.Length; i++)
            {
                FloorSegmentBinding binding = floorSegments[i];
                if (binding == null) continue;

                Transform target = binding.FloorTransform != null ? binding.FloorTransform : transform;
                FloorSegmentConfig segment = GetConfiguredSegment(configuredSegments, binding.ConfigIndex);
                ApplySegment(
                    target,
                    segment,
                    binding.FallbackCenterColumn,
                    binding.FallbackColumnCount,
                    activeConfig);
            }

            return;
        }

        Transform singleFloorTransform = floorTransform != null ? floorTransform : transform;
        ApplySegment(
            singleFloorTransform,
            GetConfiguredSegment(configuredSegments, 0),
            fallbackCenterColumn,
            fallbackColumnCount,
            activeConfig);
    }

    /// <summary>World Y of the floor's top surface - the origin for tower height in meters.</summary>
    public bool TryGetFloorTopWorldY(out float floorTopY)
    {
        floorTopY = 0f;
        Transform target = floorTransform != null ? floorTransform : transform;
        if (floorSegments != null && floorSegments.Length > 0 && floorSegments[0] != null &&
            floorSegments[0].FloorTransform != null)
        {
            target = floorSegments[0].FloorTransform;
        }

        Collider2D floorCollider = target.GetComponent<Collider2D>();
        if (floorCollider == null) return false;

        floorTopY = floorCollider.bounds.max.y;
        return true;
    }

    private FloorSegmentConfig GetConfiguredSegment(IReadOnlyList<FloorSegmentConfig> configuredSegments, int index)
    {
        if (configuredSegments == null || index < 0 || index >= configuredSegments.Count) return null;
        return configuredSegments[index];
    }

    private void ApplySegment(
        Transform target,
        FloorSegmentConfig segment,
        int fallbackSegmentCenterColumn,
        int fallbackSegmentColumnCount,
        GameModeConfig activeConfig = null)
    {
        if (activeConfig == null)
        {
            activeConfig = LevelSelectionState.ResolveGameMode(gameModeConfig);
        }
        float gridSpacing = activeConfig != null ? activeConfig.GridSpacing : 1f;
        float width = segment != null
            ? segment.GetWidth(gridSpacing)
            : Mathf.Max(1, fallbackSegmentColumnCount) * gridSpacing;
        float centerX = segment != null
            ? segment.GetCenterX(gridSpacing)
            : GetFallbackCenterX(fallbackSegmentCenterColumn, fallbackSegmentColumnCount, gridSpacing);

        Vector3 scale = target.localScale;
        scale.x = width;
        target.localScale = scale;

        Vector3 position = target.position;
        position.x = centerX;
        target.position = position;

        ApplyFloorColliderInset(target, width);
        ApplyFloorFriction(target);
        ApplyGroundSkin(target, width);
    }

    // Visual only: replaces the floating floor bar with the theme's plateau strip -
    // plateau.png TILED to the floor width (never stretched; outlined end caps mark its
    // boundary, preserved by the sprite border). The visual matches the landable collider
    // width EXACTLY: what you see is what you can land on. Theme scenery (hills, dunes,
    // mountains) lives in the backdrop, never attached to the floor - nothing decorative
    // can be mistaken for a landing surface. Collider, inset and friction are untouched.
    private void ApplyGroundSkin(Transform target, float width)
    {
        SpriteRenderer floorRenderer = target.GetComponent<SpriteRenderer>();
        if (floorRenderer == null) return;

        Sprite plateau = ThemeSkins.LoadPlateau();
        if (plateau == null) return;

        Transform existing = target.Find("PlateauSkin");
        SpriteRenderer skin;
        if (existing != null)
        {
            skin = existing.GetComponent<SpriteRenderer>();
        }
        else
        {
            GameObject go = new GameObject("PlateauSkin");
            go.transform.SetParent(target, false);
            skin = go.AddComponent<SpriteRenderer>();
        }

        skin.sprite = plateau;
        skin.drawMode = SpriteDrawMode.Tiled;
        skin.sortingLayerID = floorRenderer.sortingLayerID;
        skin.sortingOrder = groundSkinSortingOrder;

        // Counter the floor bar's stretched scale; size in world units via Tiled mode.
        Vector3 parentScale = target.lossyScale;
        skin.transform.localScale = new Vector3(
            1f / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
            1f / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
            1f);
        float plateauHeight = plateau.bounds.size.y;
        skin.size = new Vector2(width, plateauHeight);

        // Floor top from the bar sprite's local bounds and scale (not collider bounds:
        // AutoSyncTransforms is off project-wide, so those can be stale right after the
        // move above; not renderer.bounds either, as the renderer is disabled on re-runs).
        float floorTopY = target.position.y
            + floorRenderer.sprite.bounds.extents.y * Mathf.Abs(target.lossyScale.y);
        skin.transform.position = new Vector3(
            target.position.x, floorTopY - plateauHeight * 0.5f, target.position.z);

        floorRenderer.enabled = false;
    }

    // Gives the floor a real friction material so blocks grip it instead of sliding off the
    // engine default. Shared across all floor segments.
    private void ApplyFloorFriction(Transform target)
    {
        Collider2D floorCollider = target.GetComponent<Collider2D>();
        if (floorCollider == null) return;

        if (_floorMaterial == null)
        {
            _floorMaterial = new PhysicsMaterial2D("FloorFriction")
            {
                friction = floorFriction,
                bounciness = 0f
            };
        }

        floorCollider.sharedMaterial = _floorMaterial;
    }

    // Shrinks the floor collider horizontally a touch (leaving the sprite at full width) so its
    // collision edge sits just inside the column boundary - matching the block collider inset -
    // and pieces in the next column over fall cleanly instead of catching on the floor's corner.
    private void ApplyFloorColliderInset(Transform target, float width)
    {
        if (floorColliderEdgeInset <= 0f) return;

        BoxCollider2D box = target.GetComponent<BoxCollider2D>();
        if (box == null) return;

        float scaleX = Mathf.Abs(target.localScale.x);
        if (scaleX < 0.0001f) return;

        float desiredWorldWidth = Mathf.Max(0.1f, width - 2f * floorColliderEdgeInset);
        Vector2 size = box.size;
        size.x = desiredWorldWidth / scaleX;
        box.size = size;
    }

    private float GetFallbackCenterX(int centerColumn, int columnCount, float gridSpacing)
    {
        int count = Mathf.Max(1, columnCount);
        int leftColumn = centerColumn - count / 2;
        int rightColumn = leftColumn + count - 1;
        return (leftColumn + rightColumn) * 0.5f * gridSpacing;
    }
}
