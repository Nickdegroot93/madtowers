using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds the Freeze power-up's crawling-ice overlay on a landed block, then hands the panes to a
/// <see cref="FrostFx"/> to animate the crawl. One frost pane per physical cell (not one outline
/// around the whole tetromino); each pane samples its colour from the current chapter's piece art so
/// the ice reads as having frozen over THAT brick, and the Frost shader turns it into cloudy ice with
/// bevels, scratches and internal cracks.
///
/// This is the Freeze ability's VISUAL, so it lives beside <see cref="FrostFx"/> in Abilities/Effects -
/// not inside the BlockController physics partial. <see cref="BlockController.Freeze"/> keeps only the
/// physics lock and delegates the look here. Uses the tweakable Resources/Frost.mat (falls back to
/// building from the shader); if neither exists the physics lock still happens, just without the visual.
///
/// Distinct from <see cref="IceBlockSkin"/> on purpose: the Ice variant is a static, fully-iced
/// overlay-on-top built via BlockVariantSkin; Freeze samples-and-tints per cell and crawls in over time.
/// </summary>
public static class FreezeFrost
{
    private static bool _loaded;
    private static Material _template;

    /// <summary>Frost <paramref name="block"/> over <paramref name="seconds"/>. Idempotent: a block that
    /// already carries a frost overlay is left alone.</summary>
    public static void Apply(BlockController block, float seconds)
    {
        if (block == null) return;
        if (block.GetComponentInChildren<FrostFx>() != null) return; // already frosted

        if (!_loaded)
        {
            _loaded = true;
            _template = Resources.Load<Material>("Frost");
            if (_template == null)
            {
                Shader shader = Resources.Load<Shader>("Frost");
                if (shader != null) _template = new Material(shader);
            }
        }
        if (_template == null) return;

        Transform root = block.transform;
        var overlays = new List<SpriteRenderer>();
        SpriteRenderer pieceRenderer = FindPieceSkinRenderer(block);
        SpriteRenderer sortSource = pieceRenderer != null ? pieceRenderer : block.GetComponentInChildren<SpriteRenderer>();
        int sortingLayerId = sortSource != null ? sortSource.sortingLayerID : 0;
        int sortingOrder = sortSource != null ? sortSource.sortingOrder : 0;

        BoxCollider2D[] colliders = block.GetComponentsInChildren<BoxCollider2D>();
        for (int i = 0; i < colliders.Length; i++)
        {
            BoxCollider2D box = colliders[i];
            if (box == null || box.isTrigger) continue;

            Vector3 worldCenter = box.transform.TransformPoint(box.offset);
            Vector3 localCenter = root.InverseTransformPoint(worldCenter);
            SpriteRenderer cellRenderer = box.GetComponent<SpriteRenderer>();
            float cellSize = ResolveFrostCellSize(cellRenderer, block.GridSpacing);

            GameObject go = new GameObject("FrostOverlay");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(localCenter.x, localCenter.y, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(cellSize, cellSize, 1f);

            SpriteRenderer overlay = go.AddComponent<SpriteRenderer>();
            overlay.sprite = RuntimeSprites.Square();
            overlay.sharedMaterial = _template;
            overlay.sortingLayerID = sortingLayerId;
            overlay.sortingOrder = sortingOrder + 2; // above the chapter skin and any old cell renderers
            overlay.color = ResolveFrostCellTint(pieceRenderer, worldCenter,
                cellRenderer != null ? cellRenderer.color : Color.white);
            overlays.Add(overlay);
        }

        if (overlays.Count == 0)
        {
            // Fallback for a malformed prefab: frost whatever visible sprite it has instead of
            // silently losing the visual.
            SpriteRenderer[] renderers = block.GetComponentsInChildren<SpriteRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer sr = renderers[i];
                if (sr == null || !sr.enabled || sr.sprite == null) continue;
                if (sr.gameObject.name == "FrostOverlay") continue;

                GameObject go = new GameObject("FrostOverlay");
                go.transform.SetParent(sr.transform, false);
                SpriteRenderer overlay = go.AddComponent<SpriteRenderer>();
                overlay.sprite = sr.sprite;
                overlay.sharedMaterial = _template;
                overlay.sortingLayerID = sr.sortingLayerID;
                overlay.sortingOrder = sr.sortingOrder + 2;
                overlay.color = sr.color;
                overlays.Add(overlay);
            }
        }

        if (overlays.Count == 0) return;

        FrostFx fx = block.gameObject.AddComponent<FrostFx>();
        fx.Play(overlays, seconds, Random.value * 50f); // per-block seed varies the crawl pattern
    }

    private static SpriteRenderer FindPieceSkinRenderer(BlockController block)
    {
        SpriteRenderer[] renderers = block.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer sr = renderers[i];
            if (sr != null && sr.gameObject.name == "PieceSkin") return sr;
        }
        return null;
    }

    private static float ResolveFrostCellSize(SpriteRenderer cellRenderer, float gridSpacing)
    {
        if (cellRenderer != null && cellRenderer.sprite != null)
        {
            Bounds spriteBounds = cellRenderer.sprite.bounds;
            Vector3 scale = cellRenderer.transform.localScale;
            float width = Mathf.Abs(spriteBounds.size.x * scale.x);
            float height = Mathf.Abs(spriteBounds.size.y * scale.y);
            float size = Mathf.Max(width, height);
            if (size > 0.01f) return size;
        }

        return Mathf.Max(0.01f, gridSpacing);
    }

    private static Color ResolveFrostCellTint(SpriteRenderer pieceRenderer, Vector3 worldCenter, Color fallback)
    {
        Color tint = fallback;
        if (pieceRenderer != null && pieceRenderer.sprite != null && pieceRenderer.sprite.texture != null)
        {
            Sprite sprite = pieceRenderer.sprite;
            Texture2D texture = sprite.texture;
            if (!texture.isReadable) return tint;

            try
            {
                Vector3 localCenter = pieceRenderer.transform.InverseTransformPoint(worldCenter);
                Color sampled = SampleBestPieceColor(sprite, texture, localCenter);
                if (sampled.a > 0.05f)
                {
                    Color rendererColor = pieceRenderer.color;
                    tint = new Color(
                        sampled.r * rendererColor.r,
                        sampled.g * rendererColor.g,
                        sampled.b * rendererColor.b,
                        1f);
                }
            }
            catch (UnityException)
            {
                // Non-readable art still freezes; it just uses the renderer tint fallback.
            }
        }

        tint.a = 1f;
        return tint;
    }

    private static Color SampleBestPieceColor(Sprite sprite, Texture2D texture, Vector3 localCenter)
    {
        Color best = Color.clear;
        float bestScore = -1f;
        Rect textureRect = sprite.rect;
        Bounds bounds = sprite.bounds;

        for (int i = 0; i < 5; i++)
        {
            Vector2 offset = Vector2.zero;
            if (i == 1) offset = new Vector2(-0.22f, 0.16f);
            else if (i == 2) offset = new Vector2(0.22f, 0.16f);
            else if (i == 3) offset = new Vector2(-0.18f, -0.18f);
            else if (i == 4) offset = new Vector2(0.18f, -0.18f);

            Vector2 local = new Vector2(localCenter.x + offset.x, localCenter.y + offset.y);
            float u = Mathf.InverseLerp(bounds.min.x, bounds.max.x, local.x);
            float v = Mathf.InverseLerp(bounds.min.y, bounds.max.y, local.y);
            Color sample = texture.GetPixelBilinear(
                (textureRect.x + Mathf.Clamp01(u) * textureRect.width) / texture.width,
                (textureRect.y + Mathf.Clamp01(v) * textureRect.height) / texture.height);
            if (sample.a <= 0.05f) continue;

            float max = Mathf.Max(sample.r, Mathf.Max(sample.g, sample.b));
            float min = Mathf.Min(sample.r, Mathf.Min(sample.g, sample.b));
            float saturation = max - min;
            float luminance = sample.r * 0.299f + sample.g * 0.587f + sample.b * 0.114f;
            float score = sample.a * (saturation * 0.75f + luminance * 0.25f);
            if (score <= bestScore) continue;

            best = sample;
            bestScore = score;
        }

        return best;
    }
}
