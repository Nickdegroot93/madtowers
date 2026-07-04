using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates a RANDOM floor layout per run from designer constraints - pillar count, width and
/// height ranges, "one pillar must be at least this wide", "the tallest must stand N cells above
/// the rest", gap widths, pocket odds - and applies it through the runtime floor override
/// (GameModeConfig.SetRuntimeFloorOverride + PlayAreaController.ApplyConfig), so the asset is
/// never touched and every floor consumer (terrain, camera framing, reach bounds, islands) sees
/// the same generated layout. Attach the asset to a level's Modifiers list; every run of that
/// level gets a fresh floor (or a fixed one, with a non-zero Seed). Full authoring guide:
/// FLOORS.md. Cosmetic caveat: backdrop prop spacing samples the floor width before OnLevelStart,
/// so props may sit as if the floor were the asset's default - scenery only, never landable.
/// </summary>
[CreateAssetMenu(menuName = "MadTowers/Modifiers/Procedural Floor")]
public sealed class ProceduralFloorModifier : LevelModifier
{
    [Header("Pillars")]
    [Range(1, 5)] [SerializeField] private int pillarCountMin = 2;
    [Range(1, 5)] [SerializeField] private int pillarCountMax = 3;
    [Range(1, 9)] [SerializeField] private int pillarWidthMin = 2;
    [Range(1, 9)] [SerializeField] private int pillarWidthMax = 4;
    [Tooltip("At least one pillar is guaranteed to be this wide (0 = no guarantee).")]
    [Range(0, 9)] [SerializeField] private int guaranteeOneWidthAtLeast = 3;

    [Header("Heights (cells above the datum)")]
    [Range(0, 8)] [SerializeField] private int pillarHeightMin = 0;
    [Range(0, 8)] [SerializeField] private int pillarHeightMax = 6;
    [Tooltip("The tallest pillar stands at least this many cells above every other pillar (0 = off).")]
    [Range(0, 6)] [SerializeField] private int tallestExceedsOthersBy = 2;

    [Header("Gaps between pillars (void columns)")]
    [Range(1, 6)] [SerializeField] private int gapMin = 2;
    [Range(1, 6)] [SerializeField] private int gapMax = 3;

    [Header("Pockets (nudge-in niches, see FLOORS.md)")]
    [Range(0f, 1f)] [SerializeField] private float pocketChancePerPillar = 0.5f;
    [Range(1, 6)] [SerializeField] private int pocketDepthMin = 1;
    [Range(1, 6)] [SerializeField] private int pocketDepthMax = 3;

    [Header("Determinism")]
    [Tooltip("0 = a fresh random floor every run; any other value = the same floor every run.")]
    [SerializeField] private int seed = 0;

    private GameModeConfig _overriddenConfig;

    public override void OnLevelStart(LevelModifierContext context)
    {
        GameModeConfig config = context?.GameManager != null ? context.GameManager.ActiveConfig : null;
        PlayAreaController playArea = Object.FindAnyObjectByType<PlayAreaController>();
        if (config == null || playArea == null) return;

        var rng = new System.Random(seed != 0 ? seed : System.Environment.TickCount);
        config.SetRuntimeFloorOverride(GenerateSegments(rng));
        _overriddenConfig = config;
        playArea.ApplyConfig();
    }

    public override void OnLevelEnd(LevelModifierContext context)
    {
        // The config asset instance outlives the scene in the editor - a stale override would
        // leak into the next run (or another level sharing this config).
        if (_overriddenConfig != null) _overriddenConfig.SetRuntimeFloorOverride(null);
        _overriddenConfig = null;
    }

    /// <summary>Pure layout generation - public so tests and tools can exercise it directly.</summary>
    public FloorSegmentConfig[] GenerateSegments(System.Random rng)
    {
        int count = RangeInclusive(rng, Mathf.Min(pillarCountMin, pillarCountMax), Mathf.Max(pillarCountMin, pillarCountMax));

        // Widths, with the "one at least this wide" guarantee on a random pillar.
        int widthLo = Mathf.Min(pillarWidthMin, pillarWidthMax);
        int widthHi = Mathf.Max(pillarWidthMin, pillarWidthMax);
        var widths = new int[count];
        for (int i = 0; i < count; i++) widths[i] = RangeInclusive(rng, widthLo, widthHi);
        if (guaranteeOneWidthAtLeast > 0)
        {
            int wide = rng.Next(count);
            widths[wide] = Mathf.Max(guaranteeOneWidthAtLeast, widths[wide]);
        }

        // Heights, then enforce the tallest-margin rule by picking a champion and capping the rest.
        int heightLo = Mathf.Min(pillarHeightMin, pillarHeightMax);
        int heightHi = Mathf.Max(pillarHeightMin, pillarHeightMax);
        var heights = new int[count];
        for (int i = 0; i < count; i++) heights[i] = RangeInclusive(rng, heightLo, heightHi);
        if (tallestExceedsOthersBy > 0 && count > 1)
        {
            int champion = rng.Next(count);
            heights[champion] = Mathf.Max(heights[champion], Mathf.Min(heightHi, heightLo + tallestExceedsOthersBy));
            int cap = Mathf.Max(0, heights[champion] - tallestExceedsOthersBy);
            for (int i = 0; i < count; i++)
                if (i != champion) heights[i] = Mathf.Min(heights[i], cap);
        }

        // Lay the pillars out left to right with random void gaps, then centre the arrangement.
        var gaps = new int[Mathf.Max(0, count - 1)];
        int totalWidth = 0;
        for (int i = 0; i < count; i++) totalWidth += widths[i];
        for (int i = 0; i < gaps.Length; i++)
        {
            gaps[i] = RangeInclusive(rng, Mathf.Min(gapMin, gapMax), Mathf.Max(gapMin, gapMax));
            totalWidth += gaps[i];
        }

        var segments = new FloorSegmentConfig[count];
        int cursor = -totalWidth / 2;
        for (int i = 0; i < count; i++)
        {
            int left = cursor;
            int center = left + widths[i] / 2;

            FloorPocketConfig[] pockets = null;
            if (rng.NextDouble() < pocketChancePerPillar)
            {
                // Carve into a random outer column. Keep the pocket steerable: it may reach at
                // most ~3 cells below the datum (the descent bailout, see FLOORS.md).
                int column = rng.Next(2) == 0 ? 0 : widths[i] - 1;
                int maxDepth = heights[i] + 3;
                int depth = Mathf.Min(RangeInclusive(rng, Mathf.Min(pocketDepthMin, pocketDepthMax),
                    Mathf.Max(pocketDepthMin, pocketDepthMax)), maxDepth);
                pockets = new[] { new FloorPocketConfig(column, depth) };
            }

            segments[i] = new FloorSegmentConfig(center, widths[i], heights[i], null, pockets);
            cursor += widths[i] + (i < gaps.Length ? gaps[i] : 0);
        }
        return segments;
    }

    private static int RangeInclusive(System.Random rng, int lo, int hi) => rng.Next(lo, hi + 1);
}
