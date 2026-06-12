using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Spawns the floating support islands (Tricky Towers' "sky stones"): static 1x1 cells,
/// alone or in small clusters, that pieces can land on. Generation is TOWER-driven:
/// every row up to a lead above the tower's peak is rolled exactly once, so islands form
/// a frontier that materializes (staggered pop + sound, IslandPopFx) just ahead of the
/// build instead of cluttering sky nobody can reach yet. Each block lock raises the peak;
/// the newly-in-range rows pop on the next Update. Rows roll independently per SIDE BAND
/// (the columns flanking the center clear lane), which gives the Tricky-Towers look:
/// stones lining both flanks, none in the falling lane.
///
/// Under a height-limit level (TowerHeightLimit.CeilingY), generation is additionally
/// capped a safe margin below the line, so a rising laser line reveals the next band the
/// same way. Pops are visuals only - colliders are full-size from frame one. Visuals are
/// themed (ThemeSkins.LoadIsland, random variant + 90-degree rotation); all per-level
/// dials live on GameModeConfig (see LEVELS.md).
/// </summary>
public class StaticSupportIslandManager : MonoBehaviour
{
    [SerializeField] private GameModeConfig gameModeConfig;
    [SerializeField] private GameObject _staticBlockPrefab;
    [Tooltip("Surface friction of support islands. Matches the block friction so pieces grip the island instead of shearing off it.")]
    [Range(0f, 1f)]
    [SerializeField] private float _islandFriction = 0.95f;
    [Tooltip("Rounds island collider corners as a fraction of one cell, matching the falling blocks.")]
    [Range(0f, 0.12f)]
    [SerializeField] private float _islandCornerRadiusFraction = 0.06f;
    [Tooltip("Effective physics footprint of an island cell as a fraction of the visual cell. Must match the blocks' footprint scale so pieces fit cleanly beside and between islands.")]
    [Range(0.85f, 1f)]
    [SerializeField] private float _islandFootprintScale = 0.94f;

    // An island's top must leave room for one landed block below the limit line,
    // or every island near the line would be a zap trap (in cells).
    private const float LineHeadroomCells = 1.5f;
    private const float PopStaggerSeconds = 0.07f;
    private const int ColumnAttemptsPerSpawn = 4;

    // Islands exist to let the tower grow WIDER than the floor - directly above the
    // floor they're just obstacles in the landing lane. Per-column weights, scaled by
    // how far the column sits past the floor's edge: over the floor almost never, the
    // first column beyond the edge sparingly, properly clear of it at full density.
    private const float OverFloorWeight = 0.12f;
    private const float FloorEdgePlusOneWeight = 0.5f;

    private readonly RuntimeObjectPool _pool = new RuntimeObjectPool();
    private readonly Collider2D[] _overlapResults = new Collider2D[8];
    private PhysicsMaterial2D _islandMaterial;
    private ContactFilter2D _solidFilter;
    private Sprite[] _islandSprites;
    private Camera _camera;
    private float _generatedUpToY;
    private bool _initialFillDone;
    private bool _initialFill;
    private int _popsThisBurst;
    private int _floorMinColumn; // resolved once per generation burst (see ColumnWeight)
    private int _floorMaxColumn;
    private GameModeConfig ActiveGameModeConfig => LevelSelectionState.ResolveGameMode(gameModeConfig);

    private void Start()
    {
        _solidFilter = new ContactFilter2D
        {
            useTriggers = false,
            useLayerMask = false
        };

        // The themed looks, loaded once (GameManager.Awake applied the skin already).
        // Only non-null variants are kept, so a random pick can never come up empty.
        var sprites = new List<Sprite>(ThemeSkins.IslandVariantCount);
        for (int i = 1; i <= ThemeSkins.IslandVariantCount; i++)
        {
            Sprite sprite = ThemeSkins.LoadIsland(i);
            if (sprite != null) sprites.Add(sprite);
        }
        _islandSprites = sprites.ToArray();

        GameModeConfig activeConfig = ActiveGameModeConfig;
        float grid = activeConfig != null ? activeConfig.GridSpacing : 1f;
        float floorY = GameManager.Instance != null ? GameManager.Instance.floorOriginY : 0f;
        float firstHeight = activeConfig != null ? activeConfig.StaticSupportIslandFirstHeight : 3f;
        _generatedUpToY = Mathf.Round((floorY + firstHeight) / grid) * grid;
    }

    private void Update()
    {
        // The initial fill waits for the FIRST Update: all Start()s have run by then, so
        // a height-limit modifier has published its ceiling (Start order across components
        // is undefined - filling in our own Start could put islands above the laser line).
        // Still before the first rendered frame: everything visible simply already exists.
        if (!_initialFillDone)
        {
            _initialFillDone = true;
            _initialFill = true;
            GenerateUpToTarget();
            _initialFill = false;
            return;
        }

        GenerateUpToTarget();
    }

    // Roll every ungenerated row up to the current target (each row exactly once,
    // _generatedUpToY is monotonic). The target is the tower's peak plus a lead - or the
    // height-limit ceiling while a laser line is active, whichever is lower. A lock that
    // raises the peak (or a rising line) brings the next rows into range, and those rows
    // pop in on screen. The camera no longer drives generation - it only decides whether
    // a reveal is visible (pop + sound) or silently pre-exists (initial fill, off-screen).
    private void GenerateUpToTarget()
    {
        GameModeConfig activeConfig = ActiveGameModeConfig;
        if (activeConfig == null || !activeConfig.StaticSupportIslandsEnabled) return;
        if (_staticBlockPrefab == null) return;
        if (GameManager.Instance == null || GameManager.Instance.isGameOver) return;

        float cameraTop = CameraTopY();
        float towerPeakY = GameManager.Instance.maxHeight; // floor top until the first block lands
        float grid = activeConfig.GridSpacing;
        float rowStep = Mathf.Max(grid, Mathf.Round(activeConfig.StaticSupportIslandHeightInterval / grid) * grid);
        float target = Mathf.Min(
            towerPeakY + activeConfig.StaticSupportIslandSpawnAheadHeight,
            TowerHeightLimit.CeilingY - LineHeadroomCells * grid);

        _popsThisBurst = 0;
        GetFloorColumnSpan(activeConfig, out _floorMinColumn, out _floorMaxColumn);
        while (_generatedUpToY <= target)
        {
            GenerateRow(activeConfig, _generatedUpToY, cameraTop, grid);
            _generatedUpToY += rowStep;
        }
    }

    // Each side band rolls independently: per row, per side, one chance for one cluster.
    private void GenerateRow(GameModeConfig config, float rowY, float cameraTop, float grid)
    {
        int clearColumns = config.StaticSupportIslandCenterClearColumns;
        int clearMin = -(clearColumns / 2);
        int clearMax = clearMin + clearColumns - 1;

        TrySpawnInBand(config, rowY, cameraTop, grid, config.StaticSupportIslandMinColumn, clearMin - 1);
        TrySpawnInBand(config, rowY, cameraTop, grid, clearMax + 1, config.StaticSupportIslandMaxColumn);
    }

    private void TrySpawnInBand(GameModeConfig config, float rowY, float cameraTop, float grid,
        int bandMinColumn, int bandMaxColumn)
    {
        if (bandMinColumn > bandMaxColumn) return;

        // Per-column density = spawnChance / bandSize * columnWeight: scaling the row roll
        // by the band's average weight keeps the far columns at exactly the configured
        // density while the columns over the floor contribute almost nothing.
        float weightSum = 0f;
        int bandSize = bandMaxColumn - bandMinColumn + 1;
        for (int column = bandMinColumn; column <= bandMaxColumn; column++)
        {
            weightSum += ColumnWeight(column);
        }
        if (Random.value > config.StaticSupportIslandSpawnChance * (weightSum / bandSize)) return;

        // Tall shapes may not fit under the height-limit ceiling; pick among those that do.
        int rowsAboveAllowed = float.IsPositiveInfinity(TowerHeightLimit.CeilingY)
            ? int.MaxValue
            : Mathf.FloorToInt((TowerHeightLimit.CeilingY - LineHeadroomCells * grid - rowY) / grid);
        if (!TryChooseShape(config, rowsAboveAllowed, out StaticSupportIslandShapeConfig shape)) return;

        // The whole cluster must stay inside the band (off the clear lane AND the outer limit).
        GetShapeExtents(shape, out int minOffsetX, out int maxOffsetX);
        int baseMin = bandMinColumn - minOffsetX;
        int baseMax = bandMaxColumn - maxOffsetX;
        if (baseMin > baseMax) return;

        // Never materialize inside the falling piece, the tower, or another island - a few
        // weighted column attempts; on a crowded row the island simply doesn't appear.
        for (int attempt = 0; attempt < ColumnAttemptsPerSpawn; attempt++)
        {
            int baseColumn = WeightedColumnPick(baseMin, baseMax);
            if (!IsIslandAreaClear(shape, baseColumn, rowY, grid)) continue;

            SpawnCluster(shape, baseColumn, rowY, grid, popIn: !_initialFill && rowY < cameraTop);
            return;
        }
    }

    // How welcome an island is at this column, by distance past the FLOOR's edge (not the
    // screen, not the clear lane; span cached per burst in GenerateUpToTarget). Derived per
    // mode from its floor segments, so a narrow Spire floor keeps full side density while
    // Classic's wide floor stays clean above.
    private float ColumnWeight(int column)
    {
        int beyondEdge = column > _floorMaxColumn ? column - _floorMaxColumn
            : column < _floorMinColumn ? _floorMinColumn - column
            : 0;
        if (beyondEdge == 0) return OverFloorWeight;
        return beyondEdge == 1 ? FloorEdgePlusOneWeight : 1f;
    }

    private static void GetFloorColumnSpan(GameModeConfig config, out int floorMin, out int floorMax)
    {
        floorMin = 0;
        floorMax = 0;
        IReadOnlyList<FloorSegmentConfig> segments = config.FloorSegments;
        if (segments == null || segments.Count == 0) return;

        floorMin = int.MaxValue;
        floorMax = int.MinValue;
        for (int i = 0; i < segments.Count; i++)
        {
            if (segments[i] == null) continue;
            floorMin = Mathf.Min(floorMin, segments[i].LeftColumn);
            floorMax = Mathf.Max(floorMax, segments[i].RightColumn);
        }
        if (floorMin > floorMax) { floorMin = 0; floorMax = 0; }
    }

    private int WeightedColumnPick(int baseMin, int baseMax)
    {
        float total = 0f;
        for (int column = baseMin; column <= baseMax; column++)
        {
            total += ColumnWeight(column);
        }

        float roll = Random.value * total;
        for (int column = baseMin; column <= baseMax; column++)
        {
            roll -= ColumnWeight(column);
            if (roll <= 0f) return column;
        }
        return baseMax;
    }

    private bool TryChooseShape(GameModeConfig config, int maxRowsAbove, out StaticSupportIslandShapeConfig shape)
    {
        shape = null;
        IReadOnlyList<StaticSupportIslandShapeConfig> shapes = config.StaticSupportIslandShapes;
        if (shapes == null || shapes.Count == 0) return false;

        int totalWeight = 0;
        for (int i = 0; i < shapes.Count; i++)
        {
            if (IsShapeEligible(shapes[i], maxRowsAbove)) totalWeight += shapes[i].Weight;
        }
        if (totalWeight <= 0) return false;

        int roll = Random.Range(0, totalWeight);
        for (int i = 0; i < shapes.Count; i++)
        {
            if (!IsShapeEligible(shapes[i], maxRowsAbove)) continue;
            if (roll < shapes[i].Weight)
            {
                shape = shapes[i];
                return true;
            }
            roll -= shapes[i].Weight;
        }
        return false;
    }

    private static bool IsShapeEligible(StaticSupportIslandShapeConfig shape, int maxRowsAbove)
    {
        if (shape == null || !shape.HasCells || shape.Weight <= 0) return false;

        IReadOnlyList<Vector2Int> offsets = shape.CellOffsets;
        for (int i = 0; i < offsets.Count; i++)
        {
            if (offsets[i].y > maxRowsAbove) return false;
        }
        return true;
    }

    private static void GetShapeExtents(StaticSupportIslandShapeConfig shape, out int minOffsetX, out int maxOffsetX)
    {
        minOffsetX = int.MaxValue;
        maxOffsetX = int.MinValue;
        IReadOnlyList<Vector2Int> offsets = shape.CellOffsets;
        for (int i = 0; i < offsets.Count; i++)
        {
            minOffsetX = Mathf.Min(minOffsetX, offsets[i].x);
            maxOffsetX = Mathf.Max(maxOffsetX, offsets[i].x);
        }
    }

    private void SpawnCluster(StaticSupportIslandShapeConfig shape, int baseColumn, float baseY,
        float grid, bool popIn)
    {
        GameObject islandRoot = new GameObject($"Static Support Island - {shape.DisplayName}");
        islandRoot.transform.SetParent(transform);
        islandRoot.transform.position = Vector3.zero;

        float popDelay = PopStaggerSeconds * _popsThisBurst;
        if (popIn) _popsThisBurst++;

        IReadOnlyList<Vector2Int> offsets = shape.CellOffsets;
        for (int i = 0; i < offsets.Count; i++)
        {
            Vector3 cellPosition = new Vector3(
                (baseColumn + offsets[i].x) * grid,
                baseY + offsets[i].y * grid,
                0f);

            GameObject cell = _pool.Get(_staticBlockPrefab, cellPosition, Quaternion.identity, islandRoot.transform);
            ConfigureIslandCellPhysics(cell, grid);
            // A multi-cell cluster pops as one: all cells animate, only the first one sounds.
            ConfigureIslandCellVisual(cell, popIn, popDelay, withSound: i == 0);
        }
    }

    private bool IsIslandAreaClear(StaticSupportIslandShapeConfig shape, int baseColumn, float baseY, float gridSpacing)
    {
        IReadOnlyList<Vector2Int> offsets = shape.CellOffsets;
        Vector2 probeSize = Vector2.one * (gridSpacing * 0.95f);

        for (int i = 0; i < offsets.Count; i++)
        {
            Vector2 cellCenter = new Vector2(
                (baseColumn + offsets[i].x) * gridSpacing,
                baseY + offsets[i].y * gridSpacing);

            if (Physics2D.OverlapBox(cellCenter, probeSize, 0f, _solidFilter, _overlapResults) > 0)
            {
                return false;
            }
        }

        return true;
    }

    // Islands are the tower's sturdy anchors, so their surfaces must behave like block surfaces:
    // real friction (the prefab has none, which leaves engine-default 0.4 and lets pieces shear
    // off) and rounded corners with an exact cell-sized footprint, matching the falling blocks.
    private void ConfigureIslandCellPhysics(GameObject cell, float gridSpacing)
    {
        if (cell == null) return;

        if (_islandMaterial == null)
        {
            _islandMaterial = new PhysicsMaterial2D("SupportIslandMaterial")
            {
                friction = _islandFriction,
                bounciness = 0f
            };
        }

        BoxCollider2D[] boxes = cell.GetComponentsInChildren<BoxCollider2D>();
        for (int i = 0; i < boxes.Length; i++)
        {
            BoxCollider2D box = boxes[i];
            box.sharedMaterial = _islandMaterial;

            // Pooled cells come back already shrunk; only resize once.
            if (box.edgeRadius > 0f) continue;

            Vector2 targetSize = box.size * Mathf.Clamp(_islandFootprintScale, 0.85f, 1f);
            float requestedRadius = Mathf.Max(0f, _islandCornerRadiusFraction) * gridSpacing;
            float radius = Mathf.Min(requestedRadius, Mathf.Min(targetSize.x, targetSize.y) * 0.45f);
            if (radius <= 0f) continue;

            box.size = new Vector2(
                Mathf.Max(0.05f, targetSize.x - 2f * radius),
                Mathf.Max(0.05f, targetSize.y - 2f * radius));
            box.edgeRadius = radius;
        }
    }

    // Themed look on a VISUAL CHILD (random variant, random 90-degree rotation - the art is
    // rotation-safe, giving 12 looks per theme), so the pop animation can scale the sprite
    // while the collider stays full-size. The prefab's own gray renderer becomes the
    // fallback for a (never-shipped) theme with no island art at all.
    private void ConfigureIslandCellVisual(GameObject cell, bool popIn, float popDelay, bool withSound)
    {
        if (cell == null) return;

        Sprite sprite = _islandSprites != null && _islandSprites.Length > 0
            ? _islandSprites[Random.Range(0, _islandSprites.Length)]
            : null;

        SpriteRenderer rootRenderer = cell.GetComponent<SpriteRenderer>();
        if (rootRenderer != null) rootRenderer.enabled = sprite == null;
        if (sprite == null) return;

        Transform visual = cell.transform.Find("IslandVisual");
        if (visual == null)
        {
            visual = new GameObject("IslandVisual").transform;
            visual.SetParent(cell.transform, false);
            visual.gameObject.AddComponent<SpriteRenderer>();
            visual.gameObject.AddComponent<IslandPopFx>();
        }

        SpriteRenderer renderer = visual.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 0; // world level, same plane as the blocks
        visual.localRotation = Quaternion.Euler(0f, 0f, 90f * Random.Range(0, 4));

        IslandPopFx pop = visual.GetComponent<IslandPopFx>();
        if (popIn) pop.Play(popDelay, withSound);
        else pop.Skip();
    }

    private float CameraTopY()
    {
        if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
        if (_camera == null) return float.NegativeInfinity; // no camera: generate nothing
        return _camera.transform.position.y + (_camera.orthographic ? _camera.orthographicSize : 10f);
    }
}
