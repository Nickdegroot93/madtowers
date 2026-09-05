using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared scaffold for a procedural brick "skin" - the per-cell overlay machinery every special block's
/// fixed look is built on (Anchor, Boulder, Vine, Magma). A skin draws one overlay quad per cell collider,
/// shaded by a procedural material loaded from Resources by name, sorted just above the chapter art.
/// Subclasses supply ONLY what's unique to their brick:
///   - <see cref="MaterialResource"/>  : which Resources material/shader to draw with
///   - <see cref="HidesChapterArt"/>   : replace the chapter art or sit over it (Vine spreading onto neighbours)
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

    /// <summary>The built overlay renderers and their un-animated local scales/positions (parallel lists).</summary>
    protected readonly List<SpriteRenderer> Cells = new List<SpriteRenderer>();
    protected readonly List<Vector3> BaseScales = new List<Vector3>();
    protected readonly List<Vector3> BasePositions = new List<Vector3>();

    private MaterialPropertyBlock _mpb;
    // Chapter-art renderers this skin disabled (HidesChapterArt). Recorded so Remove() can re-show
    // exactly them - used by Sanitize to strip a variant's look in place and reveal the plain brick.
    private readonly List<SpriteRenderer> _hiddenChapterArt = new List<SpriteRenderer>();

    /// <summary>Resources path of the procedural material (or shader) this skin draws with, e.g. "Anchor".</summary>
    protected abstract string MaterialResource { get; }

    /// <summary>True hides the chapter art and replaces it (Anchor/Boulder/Magma); false overlays on top
    /// of it so the block keeps its chapter colour (Vine spreading onto neighbours).</summary>
    protected virtual bool HidesChapterArt => true;

    /// <summary>Name of the per-cell overlay GameObjects; kept distinct so the sort scan skips them.</summary>
    protected virtual string CellName => "VariantCell";

    /// <summary>Overlay quad size as a multiple of the cell, letting a skin draw PAST the brick edge (the
    /// Maw's tentacles reach up). 1 = exactly the cell (default); the shader then insets the brick body to
    /// 0.5/CellScale so it still tiles.</summary>
    protected virtual float CellScale => 1f;

    /// <summary>How many sorting orders above the brick art the overlays draw. Default 2. Vine overrides
    /// this higher so it ALWAYS draws on top of any other variant's overlay (e.g. ice frost) - so a vine
    /// growing onto a brick is never tinted/washed out by that brick's own look. Applies to all bricks.</summary>
    protected virtual int SortOrderOffset => 2;

    /// <summary>Whether the overlays have been built (idempotency guard for subclasses).</summary>
    protected bool IsBuilt => Cells.Count > 0;

    /// <summary>True parents the overlay cells under the chapter PieceSkin child instead of the block
    /// root, so they inherit the LandingSquashFx squash-and-stretch and always deform WITH the chapter
    /// art beneath them (Ice). Only for skins whose motion never drives the PieceSkin or
    /// the cells' rotation itself - Locked flinch-rotates the PieceSkin AND its cells separately, so
    /// riding the skin would double-transform them. Default false = block root (status quo).</summary>
    protected virtual bool CellsFollowPieceSkin => false;

    /// <summary>Fixed-look identity bricks (replace-mode: Maw, Bomb, Curse, ...) refuse foreign
    /// cosmetic creep - a vine growing over the Curse's eye or the Bomb's fuse hides exactly
    /// the signal the brick exists to show (Nick 2026-08-02). Vine, Ice and Locked explicitly
    /// keep accepting independently of their fixed primary materials. Checked by whatever spreads looks onto neighbours (VineBlockBehaviour).</summary>
    public virtual bool BlocksForeignOverlays => HidesChapterArt;

    /// <summary>Promote an existing overlay to an opaque skin without rebuilding its cells.
    /// Record hidden art so Sanitize can restore it, just as for an initially opaque skin.</summary>
    protected void HideChapterArt()
    {
        foreach (SpriteRenderer sr in GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sr == null || sr.sprite == null || !sr.enabled) continue;
            string n = sr.gameObject.name;
            if (n.Contains("PlacementBeam") || n.Contains("VectorGuide") || n == CellName) continue;
            sr.enabled = false;
            _hiddenChapterArt.Add(sr);
        }
    }

    /// <summary>Optional hook to set per-cell material properties at build time. <paramref name="col"/>/
    /// <paramref name="row"/> are the cell's position in the piece's local grid (stable under movement and
    /// 90 deg rotation - for checkerboard-style variation); <paramref name="index"/> is build order.</summary>
    protected virtual void ConfigureCell(int index, int col, int row, SpriteRenderer overlay, MaterialPropertyBlock mpb) { }

    /// <summary>Build one overlay quad per cell collider. Idempotent.</summary>
    protected void BuildCells()
    {
        // ApplyData tints every existing renderer before invoking the new skin.
        // Fixed skins previously ignored RGB in their shaders. Keep that identity
        // when migrating to proper Unity 6 sprite colour support, including an
        // in-place reapply or an additive transmutation. Renderer alpha stays live.
        foreach (BlockVariantSkin skin in GetComponents<BlockVariantSkin>())
            foreach (SpriteRenderer cell in skin.Cells)
                if (cell != null) cell.color = new Color(1f, 1f, 1f, cell.color.a);
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
            if (!HidesChapterArt) break;
            // Disable + record ONLY renderers that are actually on (the chapter PieceSkin) - the bare
            // prefab cells are already off (ApplyBlockSkin hid them), so recording them would make
            // Remove() wrongly re-show white cells over the chapter art instead of the plain brick.
            if (sr.enabled) { sr.enabled = false; _hiddenChapterArt.Add(sr); }
        }

        int sortingLayer = skinSort != null ? skinSort.sortingLayerID : 0;
        int sortingOrder = skinSort != null ? skinSort.sortingOrder : 0;

        BlockController controller = GetComponent<BlockController>();
        float spacing = Mathf.Max(0.01f, controller != null ? controller.GridSpacing : 1f);

        // Cell parent: block root, or the PieceSkin child for skins that ride the landing squash
        // (CellsFollowPieceSkin). The PieceSkin sits at the piece's visual centre with identity
        // rotation and unit scale, so skin-local = root-local minus its REST position (the squash
        // displaces it transiently - never bake the live pose in). Demo puppets and sprite-less
        // fallback pieces have no PieceSkin and keep the root.
        Transform cellParent = transform;
        Vector3 parentOffset = Vector3.zero;
        if (CellsFollowPieceSkin && controller != null && controller.PieceSkinTransform != null)
        {
            cellParent = controller.PieceSkinTransform;
            parentOffset = LandingSquashFx.RestLocalPosition(cellParent);
        }

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
            go.transform.SetParent(cellParent, false);
            go.transform.localPosition = new Vector3(local[i].x - parentOffset.x, local[i].y - parentOffset.y, 0f);
            go.transform.localRotation = Quaternion.identity;
            float quad = cellSize * CellScale;
            go.transform.localScale = new Vector3(quad, quad, 1f);

            SpriteRenderer overlay = go.AddComponent<SpriteRenderer>();
            overlay.sprite = RuntimeSprites.Square();
            if (material != null) overlay.sharedMaterial = material;
            overlay.sortingLayerID = sortingLayer;
            overlay.sortingOrder = sortingOrder + SortOrderOffset;

            overlay.GetPropertyBlock(_mpb);
            ConfigureCell(index, col, row, overlay, _mpb);
            overlay.SetPropertyBlock(_mpb);

            Cells.Add(overlay);
            BaseScales.Add(go.transform.localScale);
            BasePositions.Add(go.transform.localPosition);
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

    /// <summary>Tear this skin down IN PLACE: re-show the chapter art it hid, drop its overlay
    /// cells, and remove itself - leaving the brick looking plain while keeping the same
    /// GameObject (transform, body, fall progress untouched). Used by Sanitize to strip a
    /// hazard's look without respawning the piece. Idempotent-safe.</summary>
    public void Remove()
    {
        for (int i = 0; i < _hiddenChapterArt.Count; i++)
            if (_hiddenChapterArt[i] != null) _hiddenChapterArt[i].enabled = true;
        _hiddenChapterArt.Clear();

        for (int i = 0; i < Cells.Count; i++)
        {
            if (Cells[i] == null) continue;
            Cells[i].enabled = false;          // hide this frame (Destroy is deferred to frame end)
            Destroy(Cells[i].gameObject);
        }
        Cells.Clear();

        Destroy(this);
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
        // Opt-in only: existing skins keep their materials until migrated.
        // Small offline-baked relief replaces fragment hash noise on mobile.
        if (material != null && material.HasProperty("_HazardSurface"))
            material.SetTexture("_HazardSurface", Resources.Load<Texture2D>("HazardSurface"));
        MaterialCache[resource] = material;
        return material;
    }
}
