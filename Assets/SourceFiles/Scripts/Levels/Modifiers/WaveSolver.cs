using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The puzzle-mode wave math, as pure functions (LEVELS.md "Height-Limit Waves details").
/// HeightLimitWavesModifier runs it at level start; the editor's Puzzle Wave Report runs the
/// SAME functions over every authored level, so a printed table and a played run can never
/// disagree. Nothing here touches scene state - inputs are the floor, the shape bag and the
/// difficulty rank, which is exactly why wave heights are deterministic per level and
/// leaderboard runs race identical waves.
///
/// CAPACITY MODEL. capacity(h) = for every playable grid column, the cells between its top
/// surface and the line. Three kinds of column:
///   - covered: at its real top height (segment base + steps);
///   - interior gap: from the height it becomes bridgeable (the LOWER flanking covered top -
///     a brick cantilevers across once level with the shorter pillar);
///   - cantilever: outside the footprint. Width is NOT fixed. Reach already ratchets outward
///     from every landed block plus a 4-column buffer (BlockController.Placement), so what
///     bounds outward building is SUPPORT, not reach: each row of rise buys about one more
///     column of genuinely supported width per side (a 1x4 laid flat with ~25% overhanging is
///     stable; 50% is the tipping point). So capacity grows ~quadratically with height, which
///     is what a real pyramid does.
///
/// This replaced a fixed "3 columns per side, each costing 3 cells of rise" overhang term
/// (July 2026). That term understated reachable space badly, so the solver had to push the
/// line HIGH to find enough capacity, and every puzzle level played loose - "needed 8, could
/// have placed 20" (Nick). An honest capacity model makes the mode harder by lowering the
/// line, without any fudge factor: at wave 5 on the standard floor it lands ~25% lower.
/// </summary>
public static class WaveSolver
{
    // ---- Quotas: net new STANDING blocks asked per wave -------------------------------------
    // Gentle growth keeps deep endless waves a session-sized ask, not an hour. Quota and line
    // height are COUPLED (the line is solved from the cumulative ask), so raising quotas alone
    // lengthens waves at identical tightness - it is not a difficulty dial. Difficulty is
    // density plus the honesty of the capacity model above.
    public const int FirstWaveQuota = 6;
    public const float QuotaGrowthPerWave = 1.5f;
    public const int QuotaCap = 24;

    // ---- Required packing density per rank (start -> cap, ramping per wave) -----------------
    // Recalibrated with the cantilever capacity model (July 2026). These are now much closer to
    // TRUE fractions of buildable space than the old nominal figures, because the capacity they
    // multiply is no longer a large underestimate. Rank 5 (every shipped asset) runs
    // 0.72 -> 0.80: the opening wave is unchanged from the old model, mid and late waves land
    // about a quarter lower. Push rank down per chapter if a chapter plays too tight - that is
    // the intended per-chapter dial, alongside the shape bag.
    private static readonly float[] DensityStartByRank = { 0.48f, 0.54f, 0.60f, 0.66f, 0.72f };
    private static readonly float[] DensityCapByRank = { 0.56f, 0.62f, 0.68f, 0.74f, 0.80f };
    public const float DensityRampPerWave = 0.012f;

    // ---- Cantilever growth (see the capacity model above) -----------------------------------
    /// <summary>Columns of supported width bought per <see cref="CantileverRowsPerStep"/> of
    /// rise, per side. THE calibration constant for how hard the mode plays: 1 column per row
    /// is the physically defensible rate (a ~25%-overhanging 1x4). Raising it to 2 models a
    /// 50%-overhang tipping-point stack and drops every line another ~20%.</summary>
    public const int CantileverColumnsPerStep = 1;
    public const float CantileverRowsPerStep = 1f;
    // Numerical pool for the solver only - NOT a game rule (nothing stops a player building
    // wider). Sized so the pool cannot bind any plausible solved line: at one column per row
    // the deepest column a line of height h reaches is h, and deep endless waves top out
    // around 25-30 cells. A binding pool would understate capacity and silently raise the
    // line (easier), so it is deliberately far oversized - the cost is one extra sort of a
    // few hundred floats, once per level.
    private const int MaxCantileverColumnsPerSide = 256;

    /// <summary>The line always visibly moves between waves, whatever the math says. ONE cell:
    /// at shipped density a wave's solved rise can legitimately be tiny, and padding it to two
    /// was measurable free headroom (Nick's "way too easy" read, July 2026).</summary>
    public const float MinRiseCells = 1f;
    public const float FallbackCellsPerPiece = 4f;

    public static int QuotaForWave(int waveNumber)
        => Mathf.Min(QuotaCap, FirstWaveQuota + Mathf.RoundToInt(QuotaGrowthPerWave * (waveNumber - 1)));

    /// <summary>Standing blocks required to have CLEARED wave n (1-based).</summary>
    public static int CumulativeQuota(int waveNumber)
    {
        int total = 0;
        for (int n = 1; n <= waveNumber; n++) total += QuotaForWave(n);
        return total;
    }

    public static float DensityForWave(int difficultyRank, int waveNumber)
    {
        int rank = Mathf.Clamp(difficultyRank, 1, 5) - 1;
        return Mathf.Min(DensityCapByRank[rank],
            DensityStartByRank[rank] + DensityRampPerWave * (waveNumber - 1));
    }

    /// <summary>Buildable cells between the column tops and height <paramref name="h"/>.</summary>
    public static float CapacityAt(IReadOnlyList<float> columnTops, float h)
    {
        if (columnTops == null) return 0f;
        float total = 0f;
        for (int i = 0; i < columnTops.Count; i++)
        {
            float free = h - columnTops[i];
            if (free > 0f) total += free;
        }
        return total;
    }

    // capacity(h) is piecewise linear and increasing in h, so walk the sorted tops and solve
    // the closing stretch exactly rather than iterating heights.
    public static float SolveHeightForCapacity(List<float> sortedColumnTops, float neededCells)
    {
        if (sortedColumnTops == null || sortedColumnTops.Count == 0) return neededCells;

        float capacityAtStart = 0f;
        for (int k = 1; k <= sortedColumnTops.Count; k++)
        {
            float start = sortedColumnTops[k - 1];
            float end = k < sortedColumnTops.Count ? sortedColumnTops[k] : float.PositiveInfinity;
            float capacityAtEnd = float.IsPositiveInfinity(end)
                ? float.PositiveInfinity
                : capacityAtStart + k * (end - start);
            if (neededCells <= capacityAtEnd)
            {
                return start + (neededCells - capacityAtStart) / k;
            }
            capacityAtStart = capacityAtEnd;
        }
        return sortedColumnTops[sortedColumnTops.Count - 1]; // unreachable: last stretch is unbounded
    }

    /// <summary>Solved line heights (cells above the floor datum) for waves 1..count, each at
    /// least <see cref="MinRiseCells"/> above the previous.</summary>
    public static void SolveLineHeights(List<float> sortedColumnTops, float avgCellsPerPiece,
        int difficultyRank, int count, List<float> into)
    {
        into.Clear();
        for (int n = 1; n <= count; n++)
        {
            float neededCells = CumulativeQuota(n) * avgCellsPerPiece / DensityForWave(difficultyRank, n);
            float solved = SolveHeightForCapacity(sortedColumnTops, neededCells);
            float previous = n > 1 ? into[n - 2] : 0f;
            into.Add(Mathf.Max(solved, previous + MinRiseCells));
        }
    }

    /// <summary>One entry per playable grid column, in CELLS above the datum, sorted ascending.
    /// See the capacity model on the class.</summary>
    /// <param name="includeCantilever">False builds the FOOTPRINT only (covered + gap columns,
    /// no outward growth) - what fits without ever building past the floor edges. The editor
    /// report uses it to show when a wave forces the player outward.</param>
    public static void BuildColumnTops(IReadOnlyList<FloorSegmentConfig> segments, List<float> into,
        bool includeCantilever = true)
    {
        into.Clear();

        var covered = new SortedDictionary<int, float>();
        if (segments != null)
        {
            for (int s = 0; s < segments.Count; s++)
            {
                FloorSegmentConfig segment = segments[s];
                if (segment == null) continue;
                for (int i = 0; i < segment.ColumnCount; i++)
                {
                    int column = segment.LeftColumn + i;
                    float top = segment.GetColumnHeightCells(i);
                    covered[column] = covered.TryGetValue(column, out float existing)
                        ? Mathf.Max(existing, top)
                        : top;
                }
            }
        }
        if (covered.Count == 0)
        {
            // No floor data (shouldn't happen): pretend the classic 9-column flat floor.
            for (int i = 0; i < 9; i++) covered[i - 4] = 0f;
        }

        int left = int.MaxValue, right = int.MinValue;
        foreach (KeyValuePair<int, float> entry in covered)
        {
            left = Mathf.Min(left, entry.Key);
            right = Mathf.Max(right, entry.Key);
        }

        float previousCoveredTop = covered[left];
        for (int column = left; column <= right; column++)
        {
            if (covered.TryGetValue(column, out float top))
            {
                into.Add(top);
                previousCoveredTop = top;
                continue;
            }
            // Gap: bridgeable from the lower of the two flanking covered tops.
            float nextCoveredTop = previousCoveredTop;
            for (int probe = column + 1; probe <= right; probe++)
            {
                if (covered.TryGetValue(probe, out float probeTop)) { nextCoveredTop = probeTop; break; }
            }
            into.Add(Mathf.Min(previousCoveredTop, nextCoveredTop));
        }

        if (!includeCantilever)
        {
            into.Sort();
            return;
        }

        // Cantilever columns per side: column j becomes usable one step of rise per
        // CantileverColumnsPerStep columns walked outward from the edge.
        int perStep = Mathf.Max(1, CantileverColumnsPerStep);
        for (int j = 1; j <= MaxCantileverColumnsPerSide; j++)
        {
            float rise = Mathf.Ceil(j / (float)perStep) * CantileverRowsPerStep;
            into.Add(covered[left] + rise);
            into.Add(covered[right] + rise);
        }

        into.Sort();
    }

    /// <summary>Bag-weighted average cell count of a level's pieces, read straight off the
    /// prefabs (each cell is one SpriteRenderer child; the runtime skin child doesn't exist on
    /// the asset). <paramref name="magmaRate"/> inflates counted blocks x4 per piece
    /// (PROGRESSION.md), so each COUNTED block occupies proportionally fewer cells.</summary>
    public static float AverageCellsPerPiece(IReadOnlyList<BlockDefinition> bag, float magmaRate)
    {
        float weightedCells = 0f;
        int totalCopies = 0;
        if (bag != null)
        {
            for (int i = 0; i < bag.Count; i++)
            {
                BlockDefinition definition = bag[i];
                if (definition == null) continue;
                int cells = definition.Prefab != null
                    ? definition.Prefab.GetComponentsInChildren<SpriteRenderer>(true).Length
                    : 0;
                if (cells <= 0) cells = (int)FallbackCellsPerPiece;
                weightedCells += cells * definition.BagCopies;
                totalCopies += definition.BagCopies;
            }
        }
        float average = totalCopies > 0 ? weightedCells / totalCopies : FallbackCellsPerPiece;
        return average / (1f + 3f * Mathf.Clamp01(magmaRate));
    }

    /// <summary>Summed ambient chance of the magma variant on a mode - the x4 counted-block
    /// inflation the solver divides out.</summary>
    public static float MagmaRate(GameModeConfig config)
    {
        float rate = 0f;
        IReadOnlyList<AmbientBlockVariantChance> chances = config != null ? config.AmbientBlockVariantChances : null;
        if (chances == null) return rate;
        for (int i = 0; i < chances.Count; i++)
        {
            if (chances[i] != null && chances[i].Variant is MagmaBlockData) rate += chances[i].ChancePerBlock;
        }
        return rate;
    }
}
