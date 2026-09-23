using System;
using System.Collections.Generic;
using UnityEngine;

// A conservative fallback for the fast, top-down load path. Contact reactions are not unique:
// rejecting one distribution does not establish that a carefully packed tower must collapse.
// This runs only during structural validation, never during FixedUpdate or Dynamic settling.
public partial class BlockController
{
    private readonly struct GridReaction
    {
        public readonly int Upper;
        public readonly int Lower;
        public readonly double X;
        public readonly double GroundMoment;

        public GridReaction(int upper, int lower, double x, double groundMoment = 0)
        {
            Upper = upper;
            Lower = lower;
            X = x;
            GroundMoment = groundMoment;
        }
    }

    private static bool CanBalanceGridStructure(
        List<BlockController> component, BlockController ignoredBlock)
    {
        var indices = new Dictionary<BlockController, int>(component.Count);
        var blocks = new List<BlockController>(component.Count);
        foreach (BlockController block in component)
        {
            if (block == null || block == ignoredBlock || !block.IsGridStable) continue;
            blocks.Add(block);
        }
        if (blocks.Count == 0) return false;
        // Neighbour traversal/creation order must not choose a different numerical basis.
        // Compare lattice roots rather than sub-ULP transform noise at quarter turns.
        float cellSize = Mathf.Max(0.01f, blocks[0].gridSpacing);
        blocks.Sort((a, b) =>
        {
            int row = Mathf.RoundToInt(a._rb.position.y / cellSize)
                .CompareTo(Mathf.RoundToInt(b._rb.position.y / cellSize));
            if (row != 0) return row;
            return Mathf.RoundToInt(a._rb.position.x / cellSize)
                .CompareTo(Mathf.RoundToInt(b._rb.position.x / cellSize));
        });
        for (int i = 0; i < blocks.Count; i++) indices.Add(blocks[i], i);

        var centers = new double[blocks.Count];
        var masses = new double[blocks.Count];
        var allowedMinima = new double[blocks.Count];
        var allowedMaxima = new double[blocks.Count];
        var minimumRows = new int[blocks.Count];
        var maximumRows = new int[blocks.Count];
        int rowCount = blocks.Count * 2;
        var reactions = new List<GridReaction>(blocks.Count * 4);
        var contacts = new List<GridSupportContact>(4);
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            centers[i] = block.GetGridMassCenterX();
            masses[i] = Math.Max(0.01, block._rb.mass);
            block.CollectGridSupportContacts(ignoredBlock, contacts);
            if (!TryGetGridSupportSpan(contacts, out float minX, out float maxX)) return false;
            contacts.Sort((a, b) => a.MinX.CompareTo(b.MinX));

            double grid = Math.Max(0.01, block.gridSpacing);
            double tolerance = GridBalanceToleranceFraction * grid;
            double reserve = GridStructuralEdgeReserveFraction * grid;
            double hookReach = GridHookMaxOverhangFraction * grid;
            // Do not probe Ice's hook method: it also records the direction of its release slip.
            // The fast path owns that state, and current Ice cannot use a hook in this proof.
            bool hookLeft = !(block._appliedData is IceBlockData) &&
                            block.HasGridHookAnchor(-1, ignoredBlock);
            bool hookRight = !(block._appliedData is IceBlockData) &&
                             block.HasGridHookAnchor(1, ignoredBlock);
            bool pocketLeft = block.HasStaticPocketBrace(-1);
            bool pocketRight = block.HasStaticPocketBrace(1);
            allowedMinima[i] = minX + (hookLeft ? -hookReach : reserve) - tolerance;
            allowedMaxima[i] = maxX + (hookRight ? hookReach : -reserve) + tolerance;
            minimumRows[i] = pocketLeft ? -1 : rowCount++;
            maximumRows[i] = pocketRight ? -1 : rowCount++;

            foreach (GridSupportContact contact in contacts)
            {
                int lower = -1;
                if (contact.LowerBlock != null && !indices.TryGetValue(contact.LowerBlock, out lower))
                    return false; // A grid block outside this component is not immutable terrain.

                double left = contact.MinX;
                double right = contact.MaxX;
                // A verified hook may transmit an eccentric reaction, with its full moment,
                // through the outer contact. It cannot discard that moment at a lower block.
                if (hookLeft && contact.MinX <= minX + tolerance) left -= hookReach + tolerance;
                if (hookRight && contact.MaxX >= maxX - tolerance) right += hookReach + tolerance;
                reactions.Add(new GridReaction(i, lower, left));
                if (right - left > 1e-9) reactions.Add(new GridReaction(i, lower, right));
            }

            // Only a verified STATIC terrain socket can react a pure moment into the world.
            // A block sandwich, a hook or a cycle never receives this unlimited ground brace.
            if (pocketLeft) reactions.Add(new GridReaction(i, -1, 0, -1));
            if (pocketRight) reactions.Add(new GridReaction(i, -1, 0, 1));
        }

        int boundCount = rowCount - blocks.Count * 2;
        int variableCount = reactions.Count + boundCount;
        var equations = new double[rowCount, variableCount];
        var required = new double[rowCount];
        for (int i = 0; i < blocks.Count; i++) required[i * 2] = masses[i];
        // Edge reserve constrains the TOTAL resultant, not every individual reaction.
        // Cutting each patch short would reject valid load sharing between distant contacts.
        // Nonnegative slack encodes each bound without adding force or torque to the tower.
        for (int i = 0; i < boundCount; i++)
            equations[blocks.Count * 2 + i, reactions.Count + i] = -1;
        for (int j = 0; j < reactions.Count; j++)
        {
            GridReaction reaction = reactions[j];
            int upperRow = reaction.Upper * 2;
            int minRow = minimumRows[reaction.Upper];
            int maxRow = maximumRows[reaction.Upper];
            if (reaction.GroundMoment != 0)
            {
                equations[upperRow + 1, j] = reaction.GroundMoment;
                if (minRow >= 0) equations[minRow, j] = reaction.GroundMoment;
                if (maxRow >= 0) equations[maxRow, j] = -reaction.GroundMoment;
                continue;
            }
            equations[upperRow, j] = 1;
            equations[upperRow + 1, j] = reaction.X - centers[reaction.Upper];
            if (minRow >= 0) equations[minRow, j] = reaction.X - allowedMinima[reaction.Upper];
            if (maxRow >= 0) equations[maxRow, j] = allowedMaxima[reaction.Upper] - reaction.X;
            if (reaction.Lower < 0) continue;
            int lowerRow = reaction.Lower * 2;
            equations[lowerRow, j] = -1;
            equations[lowerRow + 1, j] = -(reaction.X - centers[reaction.Lower]);
        }

        // Each reaction is compressive (nonnegative). At BOTH bodies it has equal/opposite
        // force and the same world-space line of action. There is no glue or hidden torque sink.
        return HasNonnegativeGridReactions(equations, required);
    }

    // Phase-I simplex: artificial variables provide an initial basis for A*x=b, x>=0.
    // Eliminate all artificial force before accepting. Choose the largest objective reduction
    // instead of pivoting on tiny reduced costs in redundant contact rows; ties use stable IDs.
    // The pivot budget bounds degenerate cases. Moments use local COM
    // coordinates above, avoiding large world-coordinate cancellation in tall/off-center towers.
    private static bool HasNonnegativeGridReactions(double[,] equations, double[] required)
    {
        // Collider bounds originate as floats. Smaller pivots amplify their rounding noise
        // into large artificial forces in nearly redundant contact rows.
        const double pivotTolerance = 1e-7;
        const double feasibilityTolerance = 1e-9;
        const double residualTolerance = 1e-6;
        int rows = required.Length;
        int variables = equations.GetLength(1);
        // Artificial variables never re-enter, so only their basis IDs are needed. Omitting
        // their columns keeps the landing-time tableau and every pivot substantially smaller.
        int rhs = variables;
        var tableau = new double[rows + 1][];
        for (int r = 0; r <= rows; r++) tableau[r] = new double[rhs + 1];
        var basis = new int[rows];
        var isBasic = new bool[variables];
        for (int r = 0; r < rows; r++)
        {
            // Required force is positive and required moment is zero, so all artificial
            // starting values are nonnegative without flipping any constraint rows.
            for (int c = 0; c < variables; c++)
            {
                tableau[r][c] = equations[r, c];
                tableau[rows][c] -= equations[r, c];
            }
            tableau[r][rhs] = required[r];
            tableau[rows][rhs] -= required[r];
            basis[r] = variables + r;
        }

        // A pathological input must not stall a landing. Exhaustion conservatively declines
        // the proof and keeps the existing physical release; it never certifies an unstable tower.
        const int maxPivots = 10000;
        for (int iteration = 0; iteration < maxPivots; iteration++)
        {
            int entering = -1;
            double bestCost = -pivotTolerance;
            for (int c = 0; c < variables; c++)
            {
                if (isBasic[c]) continue;
                if (tableau[rows][c] >= bestCost) continue;
                entering = c;
                bestCost = tableau[rows][c];
            }
            if (entering < 0)
            {
                if (Math.Abs(tableau[rows][rhs]) > residualTolerance) return false;
                return VerifyGridReactionSolution(equations, required, tableau, basis, rhs);
            }

            // Nearly tied limiting rows are common in exact cell stacks. Choosing a tiny
            // pivot just because its ratio is microscopically smaller amplifies collider
            // float noise. First find the feasible step with a small numerical allowance,
            // then use the strongest pivot among the rows that limit that same step.
            double stepLimit = double.PositiveInfinity;
            for (int r = 0; r < rows; r++)
            {
                if (tableau[r][entering] <= pivotTolerance) continue;
                double relaxedRatio = (Math.Max(0, tableau[r][rhs]) + feasibilityTolerance) /
                                      tableau[r][entering];
                stepLimit = Math.Min(stepLimit, relaxedRatio);
            }
            int leaving = -1;
            double bestPivot = 0;
            for (int r = 0; r < rows; r++)
            {
                double coefficient = tableau[r][entering];
                if (coefficient <= pivotTolerance) continue;
                double ratio = tableau[r][rhs] / tableau[r][entering];
                if (ratio > stepLimit) continue;
                if (coefficient > bestPivot ||
                    (coefficient == bestPivot &&
                     (leaving < 0 || basis[r] < basis[leaving])))
                {
                    bestPivot = coefficient;
                    leaving = r;
                }
            }
            if (leaving < 0) return false;

            double[] pivotRow = tableau[leaving];
            double pivot = pivotRow[entering];
            for (int c = 0; c <= rhs; c++) pivotRow[c] /= pivot;
            for (int r = 0; r <= rows; r++)
            {
                if (r == leaving) continue;
                double[] row = tableau[r];
                double scale = row[entering];
                if (scale == 0) continue;
                for (int c = 0; c <= rhs; c++) row[c] -= scale * pivotRow[c];
                row[entering] = 0;
            }
            pivotRow[entering] = 1;
            if (basis[leaving] < variables) isBasic[basis[leaving]] = false;
            basis[leaving] = entering;
            isBasic[entering] = true;
        }
        return false;
    }

    private static bool VerifyGridReactionSolution(
        double[,] equations, double[] required, double[][] tableau, int[] basis, int rhs)
    {
        int variables = equations.GetLength(1);
        var forces = new double[variables];
        for (int r = 0; r < basis.Length; r++)
        {
            double force = tableau[r][rhs];
            if (double.IsNaN(force) || double.IsInfinity(force) || force < -1e-7) return false;
            if (basis[r] < variables) forces[basis[r]] = Math.Max(0, force);
        }
        // Validate against the original equations, not the pivoted tableau/objective alone.
        // Every body must balance force AND torque; rounding cannot create a spurious anchor.
        for (int r = 0; r < required.Length; r++)
        {
            double sum = 0;
            for (int c = 0; c < variables; c++) sum += equations[r, c] * forces[c];
            if (double.IsNaN(sum) || Math.Abs(sum - required[r]) > 1e-6) return false;
        }
        return true;
    }
}
