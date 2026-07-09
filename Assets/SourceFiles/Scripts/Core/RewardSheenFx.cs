using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The game's ONE visual word for "this earned gold" (JUICE.md Phase 3, Nick-approved): a
/// soft white reflection band - same family as the epic ability-card sheen - that sweeps
/// lower-left to upper-right across every block that participated in the earn. Two nested
/// bricks read as one piece under one continuous sweep; a completed row reads as one long
/// glint. Silent, ~0.4s, clipped to the bricks' own sprites.
///
/// Implementation: each participant's collider-less PieceSkin gets a temporary SpriteMask
/// (sprite = its own skin art), and a single light bar with VisibleInsideMask sweeps the
/// merged bounds - so the band only ever draws INSIDE the participating bricks. The masks
/// use a custom sorting range around the bar's order, so nothing else can be clipped.
/// </summary>
public sealed class RewardSheenFx : MonoBehaviour
{
    private const float SweepSeconds = 0.4f;
    private const float PeakAlpha = 0.6f;
    private const float BandWorldWidth = 0.55f;
    private const int BarSortingOrder = 44; // above blocks/skins (0), below the laser line (50)

    private static readonly Stack<RewardSheenFx> Pool = new Stack<RewardSheenFx>();
    private static readonly Vector2 TravelDirection = new Vector2(1f, 1f).normalized;

    private SpriteRenderer _bar;
    private readonly List<SpriteMask> _masks = new List<SpriteMask>(8);
    private float _age;
    private float _travelHalf;
    private Vector3 _center;
    private BlockController _follow; // keeps the sweep glued to a still-FALLING brick (golden glints)

    /// <summary>Sweep across all given blocks as one shape. Blocks without a skin are
    /// skipped (their area just stays dark); null entries are fine.</summary>
    public static void Play(IReadOnlyList<BlockController> blocks, Color? tint = null)
    {
        if (!SettingsService.VisualEffects || blocks == null) return;

        // Merged bounds of every participant that can actually show the sheen.
        Bounds merged = default;
        bool hasBounds = false;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block == null || block.PieceSkinTransform == null) continue;
            if (!block.TryGetWorldBounds(out Bounds bounds)) continue;
            if (!hasBounds) { merged = bounds; hasBounds = true; }
            else merged.Encapsulate(bounds);
        }
        if (!hasBounds) return;

        RewardSheenFx fx = Get();
        fx.Build(blocks, merged, tint ?? Color.white);
    }

    private static RewardSheenFx Get()
    {
        while (Pool.Count > 0)
        {
            RewardSheenFx pooled = Pool.Pop();
            if (pooled == null) continue;
            pooled.gameObject.SetActive(true);
            return pooled;
        }

        GameObject go = new GameObject("RewardSheenFx");
        return go.AddComponent<RewardSheenFx>();
    }

    private void Build(IReadOnlyList<BlockController> blocks, Bounds merged, Color tint)
    {
        _age = 0f;
        _center = merged.center;
        transform.position = _center;
        // Single-block sweeps track their block (a falling golden brick moves a visible
        // distance during the 0.4s pass); multi-block shapes are settled and stay put.
        _follow = blocks.Count == 1 ? blocks[0] : null;

        EnsureBar();
        // The band is oriented across the travel diagonal and long enough to cover the shape
        // at any point of the sweep.
        float diagonal = new Vector2(merged.size.x, merged.size.y).magnitude;
        _bar.transform.localRotation = Quaternion.Euler(0f, 0f, 135f);
        _bar.transform.localScale = new Vector3(diagonal * 1.6f, BandWorldWidth / 0.125f, 1f);
        _bar.color = new Color(tint.r, tint.g, tint.b, 0f);
        _travelHalf = diagonal * 0.5f + BandWorldWidth;

        _masks.Clear();
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            Transform skin = block != null ? block.PieceSkinTransform : null;
            if (skin == null) continue;
            SpriteRenderer skinRenderer = skin.GetComponent<SpriteRenderer>();
            if (skinRenderer == null || skinRenderer.sprite == null) continue;

            SpriteMask mask = skin.GetComponent<SpriteMask>();
            if (mask == null) mask = skin.gameObject.AddComponent<SpriteMask>();
            mask.sprite = skinRenderer.sprite;
            // Constrain the mask to the bar's order alone - it must never clip other sprites
            // (which default to None interaction anyway; this is the second seatbelt).
            mask.isCustomRangeActive = true;
            mask.frontSortingOrder = BarSortingOrder + 1;
            mask.backSortingOrder = BarSortingOrder - 1;
            mask.enabled = true;
            _masks.Add(mask);
        }

        Apply(0f);
    }

    private void EnsureBar()
    {
        if (_bar != null) return;

        GameObject bar = new GameObject("SheenBar");
        bar.transform.SetParent(transform, false);
        _bar = bar.AddComponent<SpriteRenderer>();
        _bar.sprite = RuntimeSprites.WindStreak(); // soft-edged both ways: a clean light band
        _bar.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
        _bar.sortingOrder = BarSortingOrder;
    }

    private void Update()
    {
        _age += Time.deltaTime;
        if (_age >= SweepSeconds)
        {
            Release();
            return;
        }

        Apply(_age / SweepSeconds);
    }

    private void Apply(float t)
    {
        if (_follow != null && _follow.TryGetWorldBounds(out Bounds followBounds))
        {
            _center = followBounds.center;
        }

        // Travel lower-left -> upper-right; brightness swells in the middle of the pass.
        float offset = Mathf.Lerp(-_travelHalf, _travelHalf, t);
        _bar.transform.position = _center + (Vector3)(TravelDirection * offset);

        Color c = _bar.color;
        c.a = PeakAlpha * Mathf.Sin(t * Mathf.PI);
        _bar.color = c;
    }

    private void Release()
    {
        for (int i = 0; i < _masks.Count; i++)
        {
            if (_masks[i] != null) _masks[i].enabled = false;
        }
        _masks.Clear();
        _follow = null;
        gameObject.SetActive(false);
        Pool.Push(this);
    }
}
