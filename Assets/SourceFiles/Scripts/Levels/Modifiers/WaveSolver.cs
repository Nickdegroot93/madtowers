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
///     bounds outward building is SUPPORT, not reach: a few rows of rise buy one more column of
///     genuinely supported width per side. So capacity grows ~quadratically with height, which
///     is what a real pyramid does. HOW MANY rows per column is per rank
///     (<see cref="CantileverRowsPerStepFor"/>) - a shallower credited flank is the mode's main
///     difficulty relief, because it pushes the line up instead of pushing the player out.
///
/// This replaced a fixed "3 columns per side, each costing 3 cells of rise" overhang term
/// (July 2026). That term understated reachable space badly, so the solver had to push the
/// line HIGH to find enough capacity, and every puzzle level played loose - "needed 8, could
/// have placed 20" (Nick). An honest capacity model makes the mode harder by lowering the
/// line, without any fudge factor: at wave 5 on the standard floor it lands ~25% lower.
/// The follow-up playtest showed the steepest flank (1 column per row, now rank 5 only) is
/// past the human limit from wave 5 on, hence the per-rank ladder.
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
    // multiply is no longer a large underestimate. Each rank pairs its density with a flank slope
    // (CantileverRowsPerStepByRank) - the two screws move together, so the rank is a single honest
    // dial: 3 = the shipped standard, 4 = tight, 5 = the "nearly impossible from wave 5" tier
    // (kept intact as the top of the ladder), 1-2 = the easy-mode tail. Rank is the per-chapter
    // dial, alongside the shape bag.
    private static readonly float[] DensityStartByRank = { 0.48f, 0.54f, 0.60f, 0.66f, 0.72f };
    private static readonly float[] DensityCapByRank = { 0.56f, 0.62f, 0.68f, 0.74f, 0.80f };
    public const float DensityRampPerWave = 0.012f;

    // ---- Cantilever growth: THE second difficulty screw (see the capacity model above) -------
    /// <summary>Columns of supported width bought per <see cref="CantileverRowsPerStepFor"/> rows
    /// of rise, per side.</summary>
    public const int CantileverColumnsPerStep = 1;

    /// <summary>Rows of rise that buy one column of outward width, PER RANK - the flank slope the
    /// solver believes the player can build.
    ///
    /// This is the screw that makes the mode playable or not, and it used to be a single global
    /// constant at its steepest setting (1 column per row - a perfect ~25%-overhang pyramid).
    /// Geometrically defensible, but it credits capacity a human never reaches with random
    /// shapes: on the chapter-1 floor it put wave 5's laser at 12 rows with 45 blocks to stand,
    /// of which 36 cells PER SIDE had to hang past the 9-column floor (a sustained 3-column
    /// overhang) at 77% fill. Nick, playtesting: "around wave 5 they become nearly impossible".
    ///
    /// A shallower credited slope shrinks capacity, so the solver must push the line HIGHER for
    /// the same ask - which is exactly the relief needed, and it arrives as tower HEIGHT rather
    /// than as outward reach. That matters twice over: height is what the mode is about, and the
    /// camera frames horizontal span only (TowerCameraController), so a taller narrower tower
    /// costs nothing on screen while forced cantilevering zooms the player out.
    ///
    /// Paired with the density table above this turns difficultyRank into a real ladder - both
    /// screws move together, rank 5 is bit-identical to what shipped. On the chapter-1 floor,
    /// wave 5 goes from 36 cells per side on the flanks (rank 5) to 27 (rank 4) to 14 (rank 3).
    /// Note that quotas are NOT a dial here: they are coupled to the line, so asking for fewer
    /// blocks lowers the line to match and changes nothing about tightness.</summary>
    private static readonly float[] CantileverRowsPerStepByRank = { 3f, 2.5f, 2f, 1.5f, 1f };

    public static float CantileverRowsPerStepFor(int difficultyRank)
        => CantileverRowsPerStepByRank[Mathf.Clamp(difficultyRank, 1, 5) - 1];

    // Numerical pool for the solver only - NOT a game rule (nothing stops a player building
    // wider). Sized so the pool cannot bind any plausible solved line: at the steepest slope
    // (one column per row) the deepest column a line of height h reaches is h, shallower ranks
    // reach fewer, and even deep endless waves at rank 1 top out around 40 rows. A binding pool
    // would understate capacity and silently raise the
    // line (easier), so it is deliberately far oversized - the cost is one extra sort of a
    // few hundred floats, once per level.
    private const int MaxCantileverColumnsPerSide = 256;

    /// <summary>The line always visibly moves between waves, whatever the math says. ONE cell:
    /// at shipped density a wave's solved rise can legitimately be tiny, and padding it to two
    /// was measurable free headroom (Nick's "way too easy" read, July 2026).</summary>
    public const float MinRiseCells = 1f;
    public const float FallbackCellsPerPiece = 4f;

    // ---- Where the laser actually hangs -----------------------------------------------------
    /// <summary>Clear space between the topmost full row that fits and the drawn/zapping laser.
    /// Half a cell: enough that a flush-full tower can settle and wobble without grazing the
    /// laser, never enough for another row (a row is a whole cell).</summary>
    public const float LaserGraceCells = 0.5f;

    /// <summary>Laser height (cells above the datum) for a solved capacity height: half a cell
    /// above the NEAREST row boundary. The grace is measured from a row boundary, never from the
    /// raw solved height - that is the whole point.
    ///
    /// Block cells and every floor top are whole cells (FloorSegmentConfig heights are ints), so
    /// a stacked tower's top can only land on a row boundary. Solved heights are continuous
    /// (capacity is solved exactly), so a bare `solved + 0.5` leaves a clearance of
    /// `1 - frac(solved)` above the topmost row that fits - which on the shipped 9-wide flat
    /// floor is 0.08-0.34 cells, and hits ZERO whenever a solve ends in ~.5: the laser then sits
    /// exactly on the top of the block that legally filled that row, so the slightest settle
    /// jiggle zaps it (Nick, 2026-07-29 - LEVELS.md flagged the same trap back when heights were
    /// hand-authored, where the workaround was "author integer heights only"). Snapping makes the
    /// clearance always exactly half a cell, whatever the solve returns.
    ///
    /// NEAREST, not ceiling, and half-up (Mathf.Floor(x + 0.5f), deliberately not Mathf.Round's
    /// banker's rounding): `floor(solved + 0.5)` is precisely the number of whole rows that fit
    /// under the OLD laser, so the snap leaves the mode's usable capacity - its difficulty -
    /// completely untouched and only converts a stingy 0.08-cell margin into an honest half-cell
    /// one. Rounding up instead would hand back a whole free row wherever a solve ends in ~.0
    /// (wave 1 of every shipped level, which lands at 3.02).</summary>
    public static float LaserCellsForSolvedHeight(float solvedCells)
        => Mathf.Floor(solvedCells + 0.5f) + LaserGraceCells;

    /// <summary>Sentinel for <c>growthFreezeWave</c>: no freeze - every wave grows as authored.</summary>
    public const int NoGrowthFreeze = int.MaxValue;

    // ---- Overtime waves (MEDALS): waves past a ClearWaves level's bronze wave are stretch
    // content (silver = +1 wave, gold = +2), so difficulty growth FREEZES there: every wave past
    // growthFreezeWave asks the frozen wave's quota at the frozen wave's density. Because the
    // line is solved from cumulative quota / density, freezing BOTH keeps each overtime wave the
    // same incremental length and tightness as the bronze wave - freezing quota alone would
    // shrink the ask AND lower the line into a tighter squeeze (the coupling on the class doc).
    // min(n, freeze) == n at or below the freeze wave, so authored behavior is bit-identical there.

    public static int QuotaForWave(int waveNumber, int growthFreezeWave = NoGrowthFreeze)
    {
        int effective = Mathf.Min(waveNumber, growthFreezeWave);
        return Mathf.Min(QuotaCap, FirstWaveQuota + Mathf.RoundToInt(QuotaGrowthPerWave * (effective - 1)));
    }

    /// <summary>Standing blocks required to have CLEARED wave n (1-based).</summary>
    public static int CumulativeQuota(int waveNumber, int growthFreezeWave = NoGrowthFreeze)
    {
        int total = 0;
        for (int n = 1; n <= waveNumber; n++) total += QuotaForWave(n, growthFreezeWave);
        return total;
    }

    public static float DensityForWave(int difficultyRank, int waveNumber, int growthFreezeWave = NoGrowthFreeze)
    {
        int rank = Mathf.Clamp(difficultyRank, 1, 5) - 1;
        int effective = Mathf.Min(waveNumber, growthFreezeWave);
        return Mathf.Min(DensityCapByRank[rank],
            DensityStartByRank[rank] + DensityRampPerWave * (effective - 1));
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
        int difficultyRank, int count, List<float> into, int growthFreezeWave = NoGrowthFreeze)
    {
        into.Clear();
        for (int n = 1; n <= count; n++)
        {
            float neededCells = CumulativeQuota(n, growthFreezeWave) * avgCellsPerPiece
                / DensityForWave(difficultyRank, n, growthFreezeWave);
            float solved = SolveHeightForCapacity(sortedColumnTops, neededCells);
            float previous = n > 1 ? into[n - 2] : 0f;
            into.Add(Mathf.Max(solved, previous + MinRiseCells));
        }
    }

    /// <summary>One entry per playable grid column, in CELLS above the datum, sorted ascending.
    /// See the capacity model on the class.</summary>
    /// <param name="difficultyRank">Sets the credited flank slope
    /// (<see cref="CantileverRowsPerStepFor"/>) - the second difficulty screw, so the column tops
    /// themselves are rank-dependent.</param>
    /// <param name="includeCantilever">False builds the FOOTPRINT only (covered + gap columns,
    /// no outward growth) - what fits without ever building past the floor edges. The editor
    /// report uses it to show when a wave forces the player outward.</param>
    public static void BuildColumnTops(IReadOnlyList<FloorSegmentConfig> segments, List<float> into,
        int difficultyRank, bool includeCantilever = true)
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
        // CantileverColumnsPerStep columns walked outward from the edge, where a step costs the
        // rank's flank slope in rows.
        int perStep = Mathf.Max(1, CantileverColumnsPerStep);
        float rowsPerStep = CantileverRowsPerStepFor(difficultyRank);
        for (int j = 1; j <= MaxCantileverColumnsPerSide; j++)
        {
            float rise = Mathf.Ceil(j / (float)perStep) * rowsPerStep;
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
