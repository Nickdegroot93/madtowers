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
    [Tooltip("Friction applied to the floor so blocks grip it instead of sliding. Should roughly match the block friction. The floor otherwise uses the slippery engine default (~0.4).")]
    [Range(0f, 1f)]
    [SerializeField] private float floorFriction = 0.7f;

    private PhysicsMaterial2D _floorMaterial;

    private void Awake()
    {
        ApplyConfig();
    }

    public void ApplyConfig()
    {
        IReadOnlyList<FloorSegmentConfig> configuredSegments = gameModeConfig != null
            ? gameModeConfig.FloorSegments
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
                    binding.FallbackColumnCount);
            }

            return;
        }

        Transform singleFloorTransform = floorTransform != null ? floorTransform : transform;
        ApplySegment(
            singleFloorTransform,
            GetConfiguredSegment(configuredSegments, 0),
            fallbackCenterColumn,
            fallbackColumnCount);
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
        int fallbackSegmentColumnCount)
    {
        float gridSpacing = gameModeConfig != null ? gameModeConfig.GridSpacing : 1f;
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
