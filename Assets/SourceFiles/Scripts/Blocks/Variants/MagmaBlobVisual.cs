using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Visual for a single melting Magma cell - a stone Pip dropped by MagmaMeltSession. The cell
/// glows molten while it flows down, then on its own lock event splats and fuses back to its
/// NATURAL colour (the level's ordinary 1x1 look) - molten lava cooling into normal stone. The
/// landed cells are therefore whatever the chapter's 1x1 brick looks like, by design.
///
/// Purely cosmetic: it tints renderers and squash-stretches the collider-less skin transform
/// only, never anything physical (PHYSICS.md).
/// </summary>
public sealed class MagmaBlobVisual : MonoBehaviour
{
    private SpriteRenderer[] _renderers;
    private Color[] _baseColors;       // the natural (cooled) colours to fuse back to
    private Transform[] _wobble;       // collider-less skin transforms (scaled on the splat)
    private Vector3[] _baseScales;

    private Color _moltenColor;
    private GameObject _solidifyEffect;
    private float _solidifyEffectScale;

    private BlockController _block;
    private bool _solidifying;
    private float _solidifyAge;

    private const float SolidifySeconds = 0.32f;

    public void InitMeltCell(Color moltenColor, GameObject solidifyEffect, float solidifyEffectScale)
    {
        _moltenColor = moltenColor;
        _solidifyEffect = solidifyEffect;
        _solidifyEffectScale = solidifyEffectScale;
        CacheVisuals();

        ApplyTint(_moltenColor); // glow molten while falling

        _block = GetComponent<BlockController>();
        if (_block != null) _block.OnBlockLocked += HandleLocked;
    }

    private void CacheVisuals()
    {
        var tinted = new List<SpriteRenderer>();
        var colors = new List<Color>();
        var wobble = new List<Transform>();
        SpriteRenderer[] all = GetComponentsInChildren<SpriteRenderer>();
        for (int i = 0; i < all.Length; i++)
        {
            SpriteRenderer sr = all[i];
            if (sr == null || !sr.enabled || sr.sprite == null) continue;
            string n = sr.gameObject.name;
            if (n.Contains("PlacementBeam") || n.Contains("VectorGuide")) continue;
            tinted.Add(sr);
            colors.Add(sr.color);                                  // natural colour BEFORE molten tint
            if (sr.GetComponent<Collider2D>() == null) wobble.Add(sr.transform);
        }
        _renderers = tinted.ToArray();
        _baseColors = colors.ToArray();
        _wobble = wobble.ToArray();
        _baseScales = new Vector3[_wobble.Length];
        for (int i = 0; i < _wobble.Length; i++) _baseScales[i] = _wobble[i].localScale;
    }

    private void OnDestroy()
    {
        if (_block != null) _block.OnBlockLocked -= HandleLocked;
    }

    private void HandleLocked()
    {
        if (_solidifying) return;
        _solidifying = true;
        _solidifyAge = 0f;

        if (_block != null && _block.TryGetWorldBounds(out Bounds b))
        {
            float scale = Mathf.Max(0.1f, _solidifyEffectScale * Mathf.Max(b.size.x, b.size.y));
            Vfx.Spawn(_solidifyEffect, b.center, scale); // null-safe
        }
        SfxPlayer.Play("impact_soft_01", 0.6f, 0.07f);
    }

    private void LateUpdate()
    {
        if (!_solidifying) return; // before landing: just glow molten, no shape change

        _solidifyAge += Time.deltaTime;
        float t = Mathf.Clamp01(_solidifyAge / SolidifySeconds);

        // Splat: flatten on impact, elastic settle to a solid cube; molten -> natural colour.
        float e = FxKit.Elastic(t, 0.35f, 5f, 18f);
        SetScale(e, 2f - e);
        for (int i = 0; i < _renderers.Length; i++)
            if (_renderers[i] != null) _renderers[i].color = Color.Lerp(_moltenColor, _baseColors[i], t);

        if (t >= 1f)
        {
            SetScale(1f, 1f);
            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null) _renderers[i].color = _baseColors[i];
            enabled = false; // settled stone - stop animating
        }
    }

    private void SetScale(float mulX, float mulY)
    {
        for (int i = 0; i < _wobble.Length; i++)
        {
            if (_wobble[i] == null) continue;
            Vector3 b = _baseScales[i];
            _wobble[i].localScale = new Vector3(b.x * mulX, b.y * mulY, b.z);
        }
    }

    private void ApplyTint(Color c)
    {
        for (int i = 0; i < _renderers.Length; i++)
            if (_renderers[i] != null) _renderers[i].color = c;
    }
}
