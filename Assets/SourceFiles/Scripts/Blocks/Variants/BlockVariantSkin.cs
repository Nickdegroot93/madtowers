using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared scaffold for a procedural brick "skin" - the per-cell overlay machinery every special block's
/// fixed look is built on (Anchor, Boulder, Vine, Magma). A skin draws one overlay quad per cell collider,
/// shaded by a procedural material loaded from Resources by name, sorted just above the chapter art.
/// Subclasses supply ONLY what's unique to their brick:
///   - <see cref="MaterialResource"/>  : which Resources material/shader to draw with
///   - <see cref="HidesChapterArt"/>   : replace the chapter art (Anchor/Boulder/Magma) or sit over it (Vine)
///   - <see cref="ConfigureCell"/>     : optional per-cell material props (a tint, a seed, a root direction)
///   - their own LateUpdate           : the motion (a flash, a slam, a wobble, a growth)
/// Build the overlays with <see cref="BuildCells"/>; animate via <see cref="SetCellsFloat"/> /
/// <see cref="ResetCellScales"/> and the <see cref="Cells"/> / <see cref="BaseScales"/> lists.
///
/// See BLOCKVARIANTS.md for the full "add a new brick" recipe. Purely cosmetic: overlays carry no
/// colliders and nothing here touches a physical body (PHYSICS.md).
/// </summary>
public abstract class BlockVariantSkin : MonoBehaviour
{
    // One material per Resources name, shared across every skin instance of that brick.
    private static readonly Dictionary<string, Material> MaterialCache = new Dictionary<string, Material>();

    /// <summary>The built overlay renderers and their un-animated local scales (parallel lists).</summary>
    protected readonly List<SpriteRenderer> Cells = new List<SpriteRenderer>();
    protected readonly List<Vector3> BaseScales = new List<Vector3>();

    private MaterialPropertyBlock _mpb;

    /// <summary>Resources path of the procedural material (or shader) this skin draws with, e.g. "Anchor".</summary>
    protected abstract string MaterialResource { get; }

    /// <summary>True hides the chapter art and replaces it (Anchor/Boulder/Magma); false overlays on top
    /// of it so the block keeps its chapter colour (Vine).</summary>
    protected virtual bool HidesChapterArt => true;

    /// <summary>Name of the per-cell overlay GameObjects; kept distinct so the sort scan skips them.</summary>
    protected virtual string CellName => "VariantCell";

    /// <summary>Whether the overlays have been built (idempotency guard for subclasses).</summary>
    protected bool IsBuilt => Cells.Count > 0;

    /// <summary>Optional hook to set per-cell material properties at build time. <paramref name="col"/>/
    /// <paramref name="row"/> are the cell's position in the piece's local grid (stable under movement and
    /// 90 deg rotation - for checkerboard-style variation); <paramref name="index"/> is build order.</summary>
    protected virtual void ConfigureCell(int index, int col, int row, SpriteRenderer overlay, MaterialPropertyBlock mpb) { }

    /// <summary>Build one overlay quad per cell collider. Idempotent.</summary>
    protected void BuildCells()
    {
        if (IsBuilt) return;

        Material material = LoadMaterial(MaterialResource);
        _mpb ??= new MaterialPropertyBlock();

        // First real piece renderer = the sort reference. Replace-mode skins disable the chapter art;
        // overlay-mode skins leave it. Placement beam / vector-guide ghosts and our own cells are skipped.
        SpriteRenderer skinSort = null;
        SpriteRenderer[] existing = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < existing.Length; i++)
        {
            SpriteRenderer sr = existing[i];
            if (sr == null || sr.sprite == null) continue;
            string n = sr.gameObject.name;
            if (n.Contains("PlacementBeam") || n.Contains("VectorGuide") || n == CellName) continue;
            if (skinSort == null) skinSort = sr;
            if (HidesChapterArt) sr.enabled = false;
            else break;
        }

        int sortingLayer = skinSort != null ? skinSort.sortingLayerID : 0;
        int sortingOrder = skinSort != null ? skinSort.sortingOrder : 0;

        BlockController controller = GetComponent<BlockController>();
        float spacing = Mathf.Max(0.01f, controller != null ? controller.GridSpacing : 1f);

        // Quantise cell centres to a local grid so col/row parity is stable under movement and rotation.
        BoxCollider2D[] cells = GetComponentsInChildren<BoxCollider2D>();
        Vector3[] local = new Vector3[cells.Length];
        float minX = float.MaxValue, minY = float.MaxValue;
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] == null || cells[i].isTrigger) continue;
            Vector3 world = cells[i].transform.TransformPoint(cells[i].offset);
            local[i] = transform.InverseTransformPoint(world);
            minX = Mathf.Min(minX, local[i].x);
            minY = Mathf.Min(minY, local[i].y);
        }

        int index = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            BoxCollider2D box = cells[i];
            if (box == null || box.isTrigger) continue;

            int col = Mathf.RoundToInt((local[i].x - minX) / spacing);
            int row = Mathf.RoundToInt((local[i].y - minY) / spacing);
            float cellSize = ResolveCellSize(box.GetComponent<SpriteRenderer>(), spacing);

            GameObject go = new GameObject(CellName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(local[i].x, local[i].y, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(cellSize, cellSize, 1f);

            SpriteRenderer overlay = go.AddComponent<SpriteRenderer>();
            overlay.sprite = RuntimeSprites.Square();
            if (material != null) overlay.sharedMaterial = material;
            overlay.sortingLayerID = sortingLayer;
            overlay.sortingOrder = sortingOrder + 2;

            overlay.GetPropertyBlock(_mpb);
            ConfigureCell(index, col, row, overlay, _mpb);
            overlay.SetPropertyBlock(_mpb);

            Cells.Add(overlay);
            BaseScales.Add(go.transform.localScale);
            index++;
        }
    }

    /// <summary>Set a float material property on every cell (for animating a flash, growth, etc.).</summary>
    protected void SetCellsFloat(int propertyId, float value)
    {
        _mpb ??= new MaterialPropertyBlock();
        for (int i = 0; i < Cells.Count; i++)
        {
            SpriteRenderer sr = Cells[i];
            if (sr == null) continue;
            sr.GetPropertyBlock(_mpb);
            _mpb.SetFloat(propertyId, value);
            sr.SetPropertyBlock(_mpb);
        }
    }

    /// <summary>Restore every cell to its un-animated scale.</summary>
    protected void ResetCellScales()
    {
        for (int i = 0; i < Cells.Count; i++)
            if (Cells[i] != null) Cells[i].transform.localScale = BaseScales[i];
    }

    // Cell native size is one grid unit; fall back to grid spacing when a cell has no own renderer
    // (pieces draw as one whole-piece sprite, so cells usually don't).
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

    private static Material LoadMaterial(string resource)
    {
        if (string.IsNullOrEmpty(resource)) return null;
        if (MaterialCache.TryGetValue(resource, out Material cached)) return cached;

        Material material = Resources.Load<Material>(resource);
        if (material == null)
        {
            Shader shader = Resources.Load<Shader>(resource);
            if (shader != null) material = new Material(shader);
        }
        MaterialCache[resource] = material;
        return material;
    }
}
