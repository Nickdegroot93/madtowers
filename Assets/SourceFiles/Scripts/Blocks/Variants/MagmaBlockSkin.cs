using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gives the Magma block a FIXED, theme-independent look: it hides the chapter skin and builds one
/// stone overlay per cell, alternating BLACK and RED across the piece (a 1x4 reads black-red-black-
/// red; 2D shapes checkerboard). The overlays use the procedural Resources/Lava shader, which draws
/// each as a shaded rounded brick in the game's own style (so it still looks like one of this game's
/// blocks, just molten) - red cells get a faint ember shimmer. The piece also gently wobbles.
///
/// This is the pattern for all unique blocks (anvil, anchor, stubborn, ...): a unique block should
/// be instantly recognisable regardless of the chapter theme. Mirrors Frost's BuildFrostOverlay for
/// the per-cell construction. Purely cosmetic - overlays carry no colliders, so PHYSICS.md is safe.
/// When the magma melts, the resulting 1x1 cells are normal bricks in the level's own skin (by design).
/// </summary>
public sealed class MagmaBlockSkin : MonoBehaviour
{
    private static Material _lavaMaterial;
    private static bool _loaded;
    private static readonly int StoneColorId = Shader.PropertyToID("_StoneColor");

    // Stone tints. Red is a bright fire orange-red (the shader adds bloom glow on top, so it reads
    // as molten lava, not dark blood); black is clean dark stone.
    private static readonly Color RedStone = new Color(0.93f, 0.18f, 0.08f, 1f);
    private static readonly Color BlackStone = new Color(0.12f, 0.10f, 0.11f, 1f);

    private const float WobbleAmp = 0.07f;
    private const float WobbleSpeed = 5.5f;

    private readonly List<Transform> _overlays = new List<Transform>();
    private readonly List<Vector3> _baseScales = new List<Vector3>();
    private readonly List<float> _phases = new List<float>();

    /// <summary>Build the magma overlays. Safe to call right after the chapter skin is built
    /// (OnApplied) - the cell colliders and skin renderer already exist by then.</summary>
    public void Apply()
    {
        Material lava = LoadLavaMaterial();

        SpriteRenderer skinSort = null;
        SpriteRenderer[] existing = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < existing.Length; i++)
        {
            SpriteRenderer sr = existing[i];
            if (sr == null || sr.sprite == null) continue;
            string n = sr.gameObject.name;
            if (n.Contains("PlacementBeam") || n.Contains("VectorGuide")) continue;
            if (skinSort == null) skinSort = sr;
            sr.enabled = false; // hide the chapter art - the overlays are the magma look
        }

        int sortingLayer = skinSort != null ? skinSort.sortingLayerID : 0;
        int sortingOrder = skinSort != null ? skinSort.sortingOrder : 0;
        var mpb = new MaterialPropertyBlock();

        BoxCollider2D[] cells = GetComponentsInChildren<BoxCollider2D>();
        // Quantise cell centres to a local grid so the black/red parity is stable under movement and
        // 90 degrees rotation (relative to the piece, not the world).
        BlockController controller = GetComponent<BlockController>();
        float spacing = Mathf.Max(0.01f, controller != null ? controller.GridSpacing : 1f);
        Vector3[] local = new Vector3[cells.Length];
        float minX = float.MaxValue, minY = float.MaxValue;
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] == null || cells[i].isTrigger) continue;
            Vector3 wc = cells[i].transform.TransformPoint(cells[i].offset);
            local[i] = transform.InverseTransformPoint(wc);
            minX = Mathf.Min(minX, local[i].x);
            minY = Mathf.Min(minY, local[i].y);
        }

        for (int i = 0; i < cells.Length; i++)
        {
            BoxCollider2D box = cells[i];
            if (box == null || box.isTrigger) continue;

            int col = Mathf.RoundToInt((local[i].x - minX) / spacing);
            int row = Mathf.RoundToInt((local[i].y - minY) / spacing);
            bool hot = ((col + row) & 1) == 0;

            SpriteRenderer cellRenderer = box.GetComponent<SpriteRenderer>();
            float cellSize = ResolveCellSize(cellRenderer, spacing);

            GameObject go = new GameObject("MagmaCell");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(local[i].x, local[i].y, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(cellSize, cellSize, 1f);

            SpriteRenderer overlay = go.AddComponent<SpriteRenderer>();
            overlay.sprite = RuntimeSprites.Square();
            if (lava != null) overlay.sharedMaterial = lava;
            overlay.sortingLayerID = sortingLayer;
            overlay.sortingOrder = sortingOrder + 2;

            // Per-cell stone tint via a property block (shares the one Lava material, like FrostFx) -
            // robust to vertex-colour quirks.
            mpb.SetColor(StoneColorId, hot ? RedStone : BlackStone);
            overlay.SetPropertyBlock(mpb);

            _overlays.Add(go.transform);
            _baseScales.Add(go.transform.localScale);
            _phases.Add((col * 1.7f + row * 0.9f)); // slight per-cell phase so it bubbles, not in lockstep
        }
    }

    // The molten "alive" wobble: a volume-preserving squash/stretch per cell, gently out of phase.
    // Cosmetic only (overlays have no colliders), scaled time so a pause freezes it (PHYSICS.md).
    private void LateUpdate()
    {
        for (int i = 0; i < _overlays.Count; i++)
        {
            Transform t = _overlays[i];
            if (t == null) continue;
            float w = Mathf.Sin(Time.time * WobbleSpeed + _phases[i]) * WobbleAmp;
            Vector3 b = _baseScales[i];
            t.localScale = new Vector3(b.x * (1f + w), b.y * (1f - w), b.z);
        }
    }

    private static float ResolveCellSize(SpriteRenderer cellRenderer, float spacing)
    {
        if (cellRenderer != null && cellRenderer.sprite != null)
        {
            Bounds b = cellRenderer.sprite.bounds;
            Vector3 s = cellRenderer.transform.localScale;
            float size = Mathf.Max(Mathf.Abs(b.size.x * s.x), Mathf.Abs(b.size.y * s.y));
            if (size > 0.01f) return size;
        }
        return spacing;
    }

    private static Material LoadLavaMaterial()
    {
        if (_loaded) return _lavaMaterial;
        _loaded = true;

        _lavaMaterial = Resources.Load<Material>("Lava");
        if (_lavaMaterial == null)
        {
            Shader shader = Resources.Load<Shader>("Lava");
            if (shader != null) _lavaMaterial = new Material(shader);
        }
        return _lavaMaterial;
    }
}
