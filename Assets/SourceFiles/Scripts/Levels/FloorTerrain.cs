using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds and owns the GROUNDED floor terrain for a level. For every FloorSegmentConfig it raises
/// ground columns at their configured per-column heights, running from the landable top all the way
/// down past the bottom of the screen, where they dissolve into a chapter-tinted fog bank - the
/// floor is earth, never a floating strip (Tricky-Towers style).
///
/// Physics (PHYSICS.md section 3): one STATIC BoxCollider2D per contiguous run of equal-height
/// columns, sharing the 0.95-friction material, with the 0.03 edge inset per side so pieces never
/// snag a top corner. Landing, casts, reach bounds and the camera are already collider-generic, so
/// the runs need no further registration. All visuals are collider-free children (cosmetic only).
///
/// Visual stack per run (sorting orders, STYLE.md): tiled masonry fill (ground_fill, -50), a depth
/// shade ramp (-49), the walkable cap band (ground_cap, -48) and near-black silhouette outline
/// strips on exposed sides (-48). Terrain-wide: a fade-to-fog ramp (-46), a back fog band + wisps
/// (-45/-44) and a FRONT fog band + wisps (44/45) that swallow pieces falling into pillar gaps.
/// Wisps drift on scaled time, so a pause freezes them.
///
/// The floor DATUM (height 0) is the lowest landable surface; column heights are always >= 0, so
/// GameManager.floorOriginY keeps its meaning for tower height, islands and backdrop anchoring.
/// </summary>
public sealed class FloorTerrain : MonoBehaviour
{
    private const int SortFill = -50;
    private const int SortShade = -49;
    private const int SortDetail = -48;
    private const int SortFade = -45;
    private const int SortBackFog = -44;
    private const int SortBackWisp = -43;
    private const int SortFrontFog = 44;
    private const int SortFrontWisp = 45;

    // Integer depth keeps the 1-unit masonry courses aligned at every run top (tile phase shifts
    // by whole tiles). Deep enough to cover the widest zoom-out; the fog hides the cut-off.
    private const float GroundVisualDepth = 8f;
    private const float ColliderDepth = 24f;
    private const float OutlineWidth = 0.09f;
    private const float CapHeight = 0.5f;

    // Pocket-entry leniency, copied from the support islands (PHYSICS.md I4/section 3): the boxes
    // around a pocket get island-style rounded corners ("shave past and slide in", exactly what
    // makes a 1-cell island pocket nudgeable), and the pocket CEILING is raised a touch so the
    // 1.0-tall piece cell has real clearance while the pocket floor stays grid-exact to rest on.
    // Plain floor spans keep sharp merged boxes - their proven landing behaviour is untouched.
    private const float PocketCornerRadiusFraction = 0.06f;
    private const float PocketMouthClearance = 0.05f;

    // Fog geometry, relative to the datum. The camera shows only ~2 units below the datum at
    // tight zoom - the fade must land inside that band; the deeper reaches appear on zoom-out.
    private const float FadeTopBelowDatum = 1.6f;
    private const float FadeBottomBelowDatum = 5f;
    private const float FogExtentMargin = 5f;

    // Each fog band continues past its ramp as a SOLID run of the same colour/alpha, so a band's
    // bottom edge can never show as a hard horizontal line and the masonry/backdrop below the
    // fog is never exposed. Sized for the deepest screen bottom the camera can ever reach:
    // MinimumCameraY (0 = the datum) minus MaximumCameraSize (24), with margin.
    private const float FogSolidDepth = 40f;

    private static readonly Color OutlineColor = new Color(0.03f, 0.028f, 0.035f, 0.92f);
    private static readonly Color FillFallbackColor = new Color(0.30f, 0.27f, 0.24f);

    private readonly List<Transform> _wisps = new List<Transform>();
    private readonly List<Vector4> _wispMotion = new List<Vector4>(); // baseX, amp, speed, phase
    private readonly List<float> _wispBaseY = new List<float>();

    // Fog BANDS are atmosphere, not world objects: they follow the camera horizontally (like the
    // sky gradient) so a pan can never reveal their side edges as hard vertical lines.
    private readonly List<SpriteRenderer> _cameraBands = new List<SpriteRenderer>();
    private Camera _fogCamera;

    private PhysicsMaterial2D _material;

    /// <summary>Build (or rebuild) the terrain. Destroys and replaces <paramref name="existing"/>.</summary>
    public static FloorTerrain Build(
        FloorTerrain existing,
        IReadOnlyList<FloorSegmentConfig> segments,
        float datumY,
        float gridSpacing,
        float edgeInset,
        float friction,
        Color fogColor)
    {
        if (existing != null) Destroy(existing.gameObject);

        var go = new GameObject("FloorTerrain");
        FloorTerrain terrain = go.AddComponent<FloorTerrain>();
        terrain.BuildInternal(segments, datumY, gridSpacing, edgeInset, friction, fogColor);
        return terrain;
    }

    private void BuildInternal(
        IReadOnlyList<FloorSegmentConfig> segments,
        float datumY,
        float gridSpacing,
        float edgeInset,
        float friction,
        Color fogColor)
    {
        _material = new PhysicsMaterial2D("FloorFriction") { friction = friction, bounciness = 0f };

        float grid = Mathf.Max(0.01f, gridSpacing);
        float minLeft = float.MaxValue;
        float maxRight = float.MinValue;

        Sprite fill = ChapterSkins.LoadGroundFill();
        Sprite cap = ChapterSkins.LoadGroundCap();

        for (int s = 0; s < segments.Count; s++)
        {
            FloorSegmentConfig segment = segments[s];
            if (segment == null) continue;
            BuildSegment(segment, datumY, grid, edgeInset, fill, cap, fogColor);
            minLeft = Mathf.Min(minLeft, (segment.LeftColumn - 0.5f) * grid);
            maxRight = Mathf.Max(maxRight, (segment.RightColumn + 0.5f) * grid);
        }

        if (minLeft <= maxRight) BuildFog(minLeft, maxRight, datumY, fogColor);
    }

    // ---- one segment: colliders + grounded column visuals ---------------------------------

    private void BuildSegment(
        FloorSegmentConfig segment,
        float datumY,
        float grid,
        float edgeInset,
        Sprite fill,
        Sprite cap,
        Color fogColor)
    {
        int count = segment.ColumnCount;
        float segLeft = (segment.LeftColumn - 0.5f) * grid;
        float bottomY = datumY - GroundVisualDepth;

        // Contiguous runs of equal column height.
        int runStart = 0;
        var runs = new List<(float left, float right, float topY)>();
        for (int i = 1; i <= count; i++)
        {
            if (i < count && segment.GetColumnHeightCells(i) == segment.GetColumnHeightCells(runStart)) continue;
            float left = segLeft + runStart * grid;
            float right = segLeft + i * grid;
            float topY = datumY + segment.GetColumnHeightCells(runStart) * grid;
            runs.Add((left, right, topY));
            runStart = i;
        }

        // Pocket spans per segment-local column, as world-Y (top, bottom) pairs.
        var pocketSpans = new Dictionary<int, List<(float top, float bottom)>>();
        if (segment.Pockets != null)
        {
            for (int p = 0; p < segment.Pockets.Count; p++)
            {
                FloorPocketConfig pocket = segment.Pockets[p];
                if (pocket == null || pocket.Column >= count) continue;
                float colTop = datumY + segment.GetColumnHeightCells(pocket.Column) * grid;
                float top = colTop - (pocket.DepthCells - 1) * grid;
                if (!pocketSpans.TryGetValue(pocket.Column, out var list))
                    pocketSpans[pocket.Column] = list = new List<(float, float)>();
                list.Add((top, top - grid));
            }
        }

        // A FLOATING segment (FloorSegmentConfig.FloatingFragment) has no bedrock: carve
        // every column from the datum down past both the visual body and the collider
        // depth. The synthetic span rides the exact same machinery as authored pockets -
        // masonry cut, collider split, cap strips, side-outline gaps - and the merge pass
        // below fuses it with any datum-touching hole into one open-bottomed shaft.
        // (Outside the Pockets null-check on purpose: a floating segment needs no
        // authored pockets to float.)
        if (segment.FloatingFragment)
        {
            for (int i = 0; i < count; i++)
            {
                if (!pocketSpans.TryGetValue(i, out var list))
                    pocketSpans[i] = list = new List<(float, float)>();
                list.Add((datumY, datumY - ColliderDepth - 1f));
            }
        }

        if (pocketSpans.Count > 0)
        {
            foreach (var list in pocketSpans.Values) list.Sort((a, b) => b.top.CompareTo(a.top));
            // Adjacent carved cells merge into ONE hole: stacked pockets (the floating-
            // fragment recipe carves several cells of a column) used to render top/bottom
            // outline strips PER CELL - ladder rungs drawn across an empty shaft (Nick
            // 2026-08-24: "weird black lines between the empty spots", every level with
            // stacked pockets). A merged span outlines only its true solid borders, and
            // the colliders, masonry cut, cap strips and side-outline gaps all read the
            // same contiguous hole instead of N slivers.
            foreach (var list in pocketSpans.Values)
            {
                for (int s = list.Count - 2; s >= 0; s--)
                {
                    if (list[s].bottom <= list[s + 1].top + 0.01f)
                    {
                        list[s] = (list[s].top, list[s + 1].bottom);
                        list.RemoveAt(s + 1);
                    }
                }
            }
        }

        // Colliders: one static box per column, split vertically around its pockets. Column boxes
        // are (grid - 2*inset) wide - the same "narrower than the visual cell" rule blocks and
        // islands follow (PHYSICS.md I4), so pieces slide along and into pockets without wedging.
        // Pocketed columns get island-style rounded boxes + a raised pocket ceiling (see the
        // leniency constants above); plain columns stay sharp.
        float colliderBottom = datumY - ColliderDepth;
        for (int i = 0; i < count; i++)
        {
            float colCenterX = (segment.LeftColumn + i) * grid;
            float top = datumY + segment.GetColumnHeightCells(i) * grid;
            float cursor = top;
            bool hasPockets = pocketSpans.TryGetValue(i, out var spans);
            if (hasPockets)
            {
                for (int p = 0; p < spans.Count; p++)
                {
                    if (spans[p].top < cursor - 0.01f)
                        CreateColumnCollider(colCenterX, spans[p].top + PocketMouthClearance, cursor, grid, edgeInset, rounded: true);
                    cursor = Mathf.Min(cursor, spans[p].bottom);
                }
            }
            if (cursor > colliderBottom + 0.01f)
                CreateColumnCollider(colCenterX, colliderBottom, cursor, grid, edgeInset, rounded: hasPockets);
        }

        for (int r = 0; r < runs.Count; r++)
        {
            (float left, float right, float topY) = runs[r];
            int firstCol = Mathf.RoundToInt((left - segLeft) / grid);
            int lastCol = Mathf.RoundToInt((right - segLeft) / grid) - 1;
            float width = right - left;
            float centerX = (left + right) * 0.5f;
            float fillHeight = topY - bottomY;

            // Masonry fill: strips of pocket-free columns, plus split rects around each pocket -
            // a pocket is a REAL hole in the geometry, the backdrop shows through it. All rect
            // edges share the same half-unit lattice, so the tiled brick courses stay aligned.
            int stripStart = firstCol;
            for (int c = firstCol; c <= lastCol + 1; c++)
            {
                bool pocketed = c <= lastCol && pocketSpans.ContainsKey(c);
                if (!pocketed && c <= lastCol) continue;
                if (c > stripStart)
                    CreateFillRect(fill, segLeft + stripStart * grid, segLeft + c * grid, topY, bottomY);
                if (pocketed)
                {
                    float colLeft = segLeft + c * grid;
                    float cursor = topY;
                    var spans = pocketSpans[c];
                    for (int p = 0; p < spans.Count; p++)
                    {
                        if (spans[p].top < cursor - 0.01f)
                            CreateFillRect(fill, colLeft, colLeft + grid, cursor, spans[p].top);
                        cursor = Mathf.Min(cursor, spans[p].bottom);
                    }
                    if (cursor > bottomY + 0.01f)
                        CreateFillRect(fill, colLeft, colLeft + grid, cursor, bottomY);
                }
                stripStart = c + 1;
            }

            // Depth shade: darker toward the base so the column reads massive. One quad per run -
            // its faint tint over a hole reads as depth haze, not paint.
            SpriteRenderer shade = CreateChild("GroundShade", new Vector3(centerX, topY - fillHeight * 0.5f, 0f), SortShade);
            shade.sprite = RuntimeSprites.AlphaRamp();
            shade.color = new Color(0f, 0f, 0f, 0.38f);
            ScaleToRect(shade, width, fillHeight);

            // Walkable cap band, skipping columns whose pocket notches the surface (depth 1).
            if (cap != null)
            {
                stripStart = firstCol;
                for (int c = firstCol; c <= lastCol + 1; c++)
                {
                    bool notched = false;
                    if (c <= lastCol && pocketSpans.TryGetValue(c, out var spans))
                        for (int p = 0; p < spans.Count; p++)
                            if (spans[p].top >= topY - 0.01f) notched = true;
                    if (!notched && c <= lastCol) continue;
                    if (c > stripStart)
                        CreateCapStrip(cap, segLeft + stripStart * grid, segLeft + c * grid, topY);
                    stripStart = c + 1;
                }
            }

            // Silhouette outlines on exposed vertical faces, split where a pocket opens through them.
            float leftNeighbourTop = r > 0 ? runs[r - 1].topY : bottomY;
            float rightNeighbourTop = r < runs.Count - 1 ? runs[r + 1].topY : bottomY;
            if (leftNeighbourTop < topY)
                CreateSideOutline(left, leftNeighbourTop, topY, inward: true,
                    gaps: pocketSpans.TryGetValue(firstCol, out var lg) ? lg : null);
            if (rightNeighbourTop < topY)
                CreateSideOutline(right, rightNeighbourTop, topY, inward: false,
                    gaps: pocketSpans.TryGetValue(lastCol, out var rg) ? rg : null);
        }

        // Pocket hole outlines: dark strips on every hole edge that borders SOLID ground, so the
        // silhouette wraps into the notch; open faces (side/top openings) stay clean.
        foreach (var kv in pocketSpans)
        {
            int c = kv.Key;
            float colLeft = segLeft + c * grid;
            float colTop = datumY + segment.GetColumnHeightCells(c) * grid;
            float leftTop = c > 0 ? datumY + segment.GetColumnHeightCells(c - 1) * grid : float.MinValue;
            float rightTop = c < count - 1 ? datumY + segment.GetColumnHeightCells(c + 1) * grid : float.MinValue;
            for (int p = 0; p < kv.Value.Count; p++)
            {
                (float top, float bottom) = kv.Value[p];
                float w = OutlineWidth;
                // A neighbour tall enough to border the hole may be CARVED over the same
                // span (a 2-wide floating slab hollows both columns): hollow-on-hollow is
                // one shared hole, and a strip on the shared boundary drew a vertical bar
                // down the middle of the shaft (Nick 2026-08-24, the 1-brick-gap twin of
                // the merged-span fix above).
                bool solidLeft = leftTop >= top - 0.01f && !HollowOver(pocketSpans, c - 1, top, bottom);
                bool solidRight = rightTop >= top - 0.01f && !HollowOver(pocketSpans, c + 1, top, bottom);
                if (top < colTop - 0.01f)                                     // solid above the hole
                    CreateOutlineRect(colLeft, colLeft + grid, top + w, top);
                if (bottom > datumY - 0.01f)                                  // floor of the hole -
                    CreateOutlineRect(colLeft, colLeft + grid, bottom, bottom - w); // none on an
                    // open-bottomed shaft (floating fragments carve past the datum into void)
                if (solidLeft) CreateOutlineRect(colLeft - w, colLeft, top, bottom);
                if (solidRight) CreateOutlineRect(colLeft + grid, colLeft + grid + w, top, bottom);
            }
        }

        // Bottom fade into the fog colour, spanning the whole segment. A floating segment
        // has nothing below the datum to fade - open air stays open.
        if (segment.FloatingFragment) return;
        float segRight = segLeft + count * grid;
        float fadeTop = datumY - FadeTopBelowDatum;
        float fadeBottom = bottomY - 0.5f;
        SpriteRenderer fade = CreateChild("GroundFade",
            new Vector3((segLeft + segRight) * 0.5f, (fadeTop + fadeBottom) * 0.5f, 0f), SortFade);
        fade.sprite = RuntimeSprites.AlphaRamp();
        fade.color = new Color(fogColor.r, fogColor.g, fogColor.b, 1f);
        ScaleToRect(fade, segRight - segLeft, fadeTop - fadeBottom);
    }

    private void CreateColumnCollider(float centerX, float bottomY, float topY, float grid, float edgeInset,
        bool rounded = false)
    {
        var go = new GameObject("FloorColumnCollider");
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(centerX, (topY + bottomY) * 0.5f, 0f);
        var box = go.AddComponent<BoxCollider2D>();
        float width = Mathf.Max(0.1f, grid - 2f * edgeInset);
        float height = topY - bottomY;
        if (rounded)
        {
            // Island prescription (PHYSICS.md section 3): shrink by 2r, add edgeRadius r - the
            // footprint stays identical, the corners round so a nudged piece slides in, not catches.
            float radius = Mathf.Min(PocketCornerRadiusFraction * grid, Mathf.Min(width, height) * 0.45f);
            box.size = new Vector2(Mathf.Max(0.05f, width - 2f * radius), Mathf.Max(0.05f, height - 2f * radius));
            box.edgeRadius = radius;
        }
        else
        {
            box.size = new Vector2(width, height);
        }
        box.sharedMaterial = _material;
    }

    private void CreateFillRect(Sprite fill, float left, float right, float topY, float bottomY)
    {
        float width = right - left;
        float height = topY - bottomY;
        if (width <= 0.01f || height <= 0.01f) return;

        SpriteRenderer sr = CreateChild("GroundFill",
            new Vector3((left + right) * 0.5f, (topY + bottomY) * 0.5f, 0f), SortFill);
        if (fill != null)
        {
            sr.sprite = fill;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = new Vector2(width, height);
        }
        else
        {
            sr.sprite = RuntimeSprites.Square();
            sr.color = FillFallbackColor;
            sr.transform.localScale = new Vector3(width, height, 1f);
        }
    }

    private void CreateCapStrip(Sprite cap, float left, float right, float topY)
    {
        float width = right - left;
        if (width <= 0.01f) return;

        SpriteRenderer sr = CreateChild("GroundCap",
            new Vector3((left + right) * 0.5f, topY - CapHeight * 0.5f, 0f), SortDetail);
        sr.sprite = cap;
        sr.drawMode = SpriteDrawMode.Tiled;
        sr.size = new Vector2(width, CapHeight);
    }

    /// <summary>Is this column carved hollow anywhere over the given vertical span?
    /// Open-interval overlap with a hair of tolerance - touching edges don't count.</summary>
    private static bool HollowOver(Dictionary<int, List<(float top, float bottom)>> spans,
        int column, float top, float bottom)
    {
        if (!spans.TryGetValue(column, out var list)) return false;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].top > bottom + 0.01f && list[i].bottom < top - 0.01f) return true;
        }
        return false;
    }

    private void CreateOutlineRect(float left, float right, float topY, float bottomY)
    {
        float width = right - left;
        float height = topY - bottomY;
        if (width <= 0.005f || height <= 0.005f) return;

        SpriteRenderer sr = CreateChild("GroundOutline",
            new Vector3((left + right) * 0.5f, (topY + bottomY) * 0.5f, 0f), SortDetail);
        sr.sprite = RuntimeSprites.Square();
        sr.color = OutlineColor;
        sr.transform.localScale = new Vector3(width, height, 1f);
    }

    private void CreateSideOutline(float edgeX, float fromY, float toY, bool inward,
        List<(float top, float bottom)> gaps = null)
    {
        if (toY - fromY <= 0.01f) return;

        float x0 = inward ? edgeX : edgeX - OutlineWidth;
        float x1 = x0 + OutlineWidth;

        // Emit strip pieces top-down, skipping any pocket span that opens through this face.
        float cursor = toY;
        if (gaps != null)
        {
            for (int i = 0; i < gaps.Count; i++)     // gaps are sorted top-down
            {
                (float gTop, float gBottom) = gaps[i];
                if (gBottom >= cursor || gTop <= fromY) continue;
                CreateOutlineRect(x0, x1, cursor, Mathf.Max(gTop, fromY));
                cursor = gBottom;
            }
        }
        if (cursor > fromY) CreateOutlineRect(x0, x1, cursor, fromY);
    }

    // ---- fog bank --------------------------------------------------------------------------

    private void BuildFog(float left, float right, float datumY, Color fogColor)
    {
        left -= FogExtentMargin;
        right += FogExtentMargin;
        var rng = new System.Random(913);

        // Bands follow the camera (width fitted every frame), so only their vertical softness shows.
        // Back band softens the base line behind the columns; the front band is in front of the
        // blocks, so pieces falling into gaps sink INTO the fog. A second, lower front band makes
        // the very bottom read properly dense.
        CreateCameraFogBand("FogBack", datumY - 1.6f, datumY - 6f, fogColor, 0.85f, SortBackFog);
        CreateCameraFogBand("FogFront", datumY - 2.2f, datumY - 7f, fogColor, 0.9f, SortFrontFog - 1);
        CreateCameraFogBand("FogFrontDense", datumY - 3.2f, datumY - 8f, fogColor, 1f, SortFrontFog - 1);

        // Wisps are world-anchored (they parallax naturally under a pan): big, soft, slow, layered
        // in three depth rows - denser and darker toward the bottom.
        int count = Mathf.Clamp(Mathf.RoundToInt((right - left) / 2.2f), 10, 26);
        for (int i = 0; i < count; i++)
        {
            bool front = (i % 2) == 1;
            float depth = (float)rng.NextDouble();                 // 0 = high faint, 1 = low dense
            float wx = Mathf.Lerp(left, right, (i + (float)rng.NextDouble()) / count);
            float wy = datumY - Mathf.Lerp(front ? 2.0f : 1.5f, front ? 4.6f : 3.6f, depth);
            float wispWidth = Mathf.Lerp(4.5f, 9f, (float)rng.NextDouble());
            float alpha = Mathf.Lerp(0.22f, front ? 0.6f : 0.45f, depth);

            SpriteRenderer sr = CreateChild(front ? "FogWispFront" : "FogWispBack",
                new Vector3(wx, wy, 0f), front ? SortFrontWisp : SortBackWisp);
            sr.sprite = RuntimeSprites.SoftBlob();
            sr.color = new Color(fogColor.r, fogColor.g, fogColor.b, alpha);
            sr.transform.localScale = new Vector3(wispWidth * 0.5f,
                Mathf.Lerp(1.2f, 2.2f, (float)rng.NextDouble()), 1f);

            _wisps.Add(sr.transform);
            _wispBaseY.Add(wy);
            _wispMotion.Add(new Vector4(
                wx,
                Mathf.Lerp(0.5f, 1.4f, (float)rng.NextDouble()),   // drift amplitude
                Mathf.Lerp(0.05f, 0.14f, (float)rng.NextDouble()), // drift speed
                (float)rng.NextDouble() * 6.2831f));               // phase
        }
    }

    private void CreateCameraFogBand(string name, float topY, float bottomY, Color fogColor, float alpha, int order)
    {
        SpriteRenderer sr = CreateChild(name, new Vector3(0f, (topY + bottomY) * 0.5f, 0f), order);
        sr.sprite = RuntimeSprites.AlphaRamp();
        sr.color = new Color(fogColor.r, fogColor.g, fogColor.b, alpha);
        ScaleToRect(sr, 40f, topY - bottomY); // width refitted to the camera every frame
        _cameraBands.Add(sr);

        // The ramp is fully opaque at its bottom row; the solid continues that exact colour/alpha
        // downward past the deepest possible screen bottom, so zooming out never reveals the
        // band's end as a hard line (or the raw backdrop under it).
        float solidBottom = bottomY - FogSolidDepth;
        SpriteRenderer solid = CreateChild(name + "Solid",
            new Vector3(0f, (bottomY + solidBottom) * 0.5f, 0f), order);
        solid.sprite = RuntimeSprites.Square();
        solid.color = sr.color;
        ScaleToRect(solid, 40f, bottomY - solidBottom);
        _cameraBands.Add(solid);
    }

    // ---- plumbing ----------------------------------------------------------------------------

    private SpriteRenderer CreateChild(string name, Vector3 position, int sortingOrder)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = position;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = sortingOrder;
        return sr;
    }

    private static void ScaleToRect(SpriteRenderer sr, float width, float height)
    {
        Vector2 native = sr.sprite.bounds.size;
        sr.transform.localScale = new Vector3(
            width / Mathf.Max(0.0001f, native.x),
            height / Mathf.Max(0.0001f, native.y),
            1f);
    }

    private void LateUpdate()
    {
        // Glue the fog bands to the camera horizontally so a pan/zoom never reveals a band edge.
        if (_cameraBands.Count > 0)
        {
            if (_fogCamera == null) _fogCamera = Camera.main;
            if (_fogCamera != null)
            {
                float camWidth = _fogCamera.orthographicSize * 2f * _fogCamera.aspect + 4f;
                for (int i = 0; i < _cameraBands.Count; i++)
                {
                    SpriteRenderer band = _cameraBands[i];
                    if (band == null) continue;
                    Vector3 p = band.transform.position;
                    p.x = _fogCamera.transform.position.x;
                    band.transform.position = p;
                    Vector3 s = band.transform.localScale;
                    float nativeWidth = band.sprite.bounds.size.x;
                    s.x = camWidth / Mathf.Max(0.0001f, nativeWidth);
                    band.transform.localScale = s;
                }
            }
        }

        if (_wisps.Count == 0) return;

        float t = Time.time; // scaled - a pause freezes the fog (cosmetic-only rule)
        for (int i = 0; i < _wisps.Count; i++)
        {
            Transform wisp = _wisps[i];
            if (wisp == null) continue;
            Vector4 m = _wispMotion[i];
            Vector3 p = wisp.position;
            p.x = m.x + Mathf.Sin(t * m.z + m.w) * m.y;
            p.y = _wispBaseY[i] + Mathf.Sin(t * m.z * 1.7f + m.w * 2.1f) * 0.06f;
            wisp.position = p;
        }
    }
}
