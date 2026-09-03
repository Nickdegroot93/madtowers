using System.Collections.Generic;
using UnityEngine;

// Arcade placement ownership. A piece either seats once on the exact cell lattice or it belongs
// to free physics. Grid-stable pieces are never corrected over time: they stay kinematic until a
// failed support interface releases the affected branch, or an explicit force releases the
// connected structure it is meant to shake.
public partial class BlockController
{
    // Contact against an exact grid support should already be within floating-point noise of its
    // row. This tolerance accepts collider/contact slop, but cannot pull a visibly misplaced piece
    // up or down onto a different ledge.
    private const float GridSeatMaxCorrectionFraction = 0.12f;
    private const float GridSupportVerticalToleranceFraction = 0.12f;
    private const float GridSupportProbeHeightFraction = 0.12f;
    // Physics2D.Distance reports the two default 0.01 contact skins as -0.02 penetration at
    // mathematically touching poses. Stay just above that measured solver skin while still
    // rejecting any visible/interior overlap.
    private const float GridPenetrationToleranceFraction = 0.03f;

    // Reserve some real support inside the outer contact edge. Kinematic grid ownership removes
    // the tiny impacts and compliance that would topple a physically precarious tower, so merely
    // being a hair inside the mathematical edge is not enough for a multi-piece arcade structure.
    private const float GridStructuralEdgeReserveFraction = 0.15f;

    // A genuine ledge hook gets enough reach to hold its own authored L/S/Z geometry exactly,
    // but it is not an infinitely strong anchor. Beyond this distance from the real top contact,
    // accumulated load releases the hook and lets the connected branch topple.
    private const float GridHookMaxOverhangFraction = 0.40f;

    // Numerical tolerance only. It must stay well below both structural policy distances above.
    private const float GridBalanceToleranceFraction = 0.005f;

    private readonly struct GridSupportContact
    {
        public readonly BlockController LowerBlock;
        public readonly float MinX;
        public readonly float MaxX;

        public GridSupportContact(BlockController lowerBlock, float minX, float maxX)
        {
            LowerBlock = lowerBlock;
            MinX = minX;
            MaxX = maxX;
        }
    }

    private sealed class GridLoadNode
    {
        public readonly BlockController Block;
        public readonly List<GridSupportContact> Supports = new List<GridSupportContact>(4);
        public readonly HashSet<BlockController> LowerBlocks = new HashSet<BlockController>();
        public float Load;
        public float Moment;
        public int PendingUpperBlocks;

        public GridLoadNode(BlockController block)
        {
            Block = block;
        }
    }

    private bool _isGridStable;

    /// <summary>True only while this landed block is owned by the exact placement grid.</summary>
    public bool IsGridStable =>
        _isGridStable && _rb != null && _rb.bodyType == RigidbodyType2D.Kinematic;

    // Called while the incoming piece is still kinematic. This is the only post-contact place
    // where a block pose may be snapped. Failure restores the honest contact pose before the body
    // is handed to physics, so a bad placement is never rescued later.
    private bool TryEnterGridStablePlacement()
    {
        Vector3 contactPosition = transform.position;
        float contactRotation = _rb.rotation;

        SnapToColumnGrid();
        SetRotationZPreservingGridPivot(_targetAngleZ);
        Physics2D.SyncTransforms();

        Vector2 primary = _cellGeometry.GetPrimaryWorldCenter(_rb.position);
        float rowCorrection = SnapValue(primary.y, gridSpacing) - primary.y;
        if (Mathf.Abs(rowCorrection) > GridSeatMaxCorrectionFraction * gridSpacing)
        {
            RestoreContactPose(contactPosition, contactRotation);
            return false;
        }

        Vector3 gridPosition = transform.position;
        gridPosition.y += rowCorrection;
        SetPosition(gridPosition);
        Physics2D.SyncTransforms();

        if (HasMeaningfulGridPoseOverlap() || !HasGridSupportBelowAnyBottomCell(null))
        {
            RestoreContactPose(contactPosition, contactRotation);
            return false;
        }

        _isGridStable = true;
        ConfigureGridStableBody();
        return true;
    }

    private void RestoreContactPose(Vector3 position, float rotation)
    {
        SetPosition(position);
        SetRotationZ(rotation);
        Physics2D.SyncTransforms();
    }

    private void ConfigureGridStableBody()
    {
        if (_rb == null) return;

        _rb.linearVelocity = Vector2.zero;
        _rb.angularVelocity = 0f;
        if (_dynamicControlReady) _rb.centerOfMass = _originalCenterOfMass;
        _rb.gravityScale = 0f;
        _rb.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
        _rb.bodyType = RigidbodyType2D.Kinematic;
        _rb.constraints = RigidbodyConstraints2D.FreezeAll;
        _fallingAway = false;
    }

    private bool HasMeaningfulGridPoseOverlap()
    {
        IReadOnlyList<Collider2D> ownColliders = _cellGeometry.SolidColliders;
        float tolerance = GridPenetrationToleranceFraction * gridSpacing;

        for (int ownIndex = 0; ownIndex < ownColliders.Count; ownIndex++)
        {
            Collider2D own = ownColliders[ownIndex];
            if (own == null) continue;

            int count = own.Overlap(_contactFilter, _overlapResults);
            for (int hitIndex = 0; hitIndex < count; hitIndex++)
            {
                Collider2D other = _overlapResults[hitIndex];
                if (other == null || other.isTrigger || other.attachedRigidbody == _rb) continue;

                ColliderDistance2D distance = Physics2D.Distance(own, other);
                if (distance.isOverlapped && distance.distance < -tolerance) return true;
            }
        }

        return false;
    }

    private bool HasGridSupportBelowAnyBottomCell(BlockController ignoredBlock)
    {
        _cellGeometry.Refresh();
        IReadOnlyList<Vector2> cells = _cellGeometry.CellCenters;
        if (cells.Count == 0) return false;

        var ownKeys = new HashSet<Vector2Int>();
        for (int i = 0; i < cells.Count; i++) ownKeys.Add(ToPlacementGridKey(cells[i].x, cells[i].y));

        for (int i = 0; i < cells.Count; i++)
        {
            Vector2 cell = cells[i];
            Vector2Int belowKey = ToPlacementGridKey(cell.x, cell.y - gridSpacing);
            if (ownKeys.Contains(belowKey)) continue;
            if (TryGetExternalGridSupportInterval(cell, ignoredBlock, out _, out _, out _)) return true;
        }

        return false;
    }

    // Finds a full row-aligned support beneath one cell. Dynamic debris and sloped pieces are
    // deliberately rejected: landing on either remains a real physics event, never a grid lock.
    private bool TryGetExternalGridSupportInterval(
        Vector2 cell,
        BlockController ignoredBlock,
        out float supportMinX,
        out float supportMaxX,
        out BlockController supportingGridBlock)
    {
        supportMinX = float.MaxValue;
        supportMaxX = float.MinValue;
        supportingGridBlock = null;

        float grid = Mathf.Max(0.01f, gridSpacing);
        float interfaceY = cell.y - grid * 0.5f;
        float verticalTolerance = GridSupportVerticalToleranceFraction * grid;
        Vector2 probeSize = new Vector2(grid * 0.75f, grid * GridSupportProbeHeightFraction);
        Vector2 probeCenter = new Vector2(cell.x, interfaceY - probeSize.y * 0.25f);
        int count = Physics2D.OverlapBox(probeCenter, probeSize, 0f, _contactFilter, _overlapResults);

        bool found = false;
        float cellMinX = cell.x - grid * 0.5f;
        float cellMaxX = cell.x + grid * 0.5f;
        Vector2 expectedSupportCell = cell + Vector2.down * grid;

        for (int i = 0; i < count; i++)
        {
            Collider2D hit = _overlapResults[i];
            if (hit == null || hit.isTrigger || hit.attachedRigidbody == _rb) continue;
            if (LandableSlope.Covers(hit)) continue;

            BlockController otherBlock = hit.GetComponentInParent<BlockController>();
            if (otherBlock != null && otherBlock == ignoredBlock) continue;

            Rigidbody2D otherBody = hit.attachedRigidbody;
            bool accepted;
            if (otherBlock != null)
            {
                accepted = (otherBlock.IsGridStable || otherBlock.IsFrozenInPlace) &&
                           otherBlock.HasCellNear(expectedSupportCell, grid * 0.1f);
            }
            else
            {
                accepted = otherBody == null || otherBody.bodyType == RigidbodyType2D.Static;
            }

            if (!accepted) continue;

            Bounds bounds = hit.bounds;
            if (Mathf.Abs(bounds.max.y - interfaceY) > verticalTolerance) continue;

            float minX = Mathf.Max(cellMinX, bounds.min.x);
            float maxX = Mathf.Min(cellMaxX, bounds.max.x);
            if (maxX - minX < GetMinimumLandingSupportWidth()) continue;

            supportMinX = Mathf.Min(supportMinX, minX);
            supportMaxX = Mathf.Max(supportMaxX, maxX);
            if (otherBlock != null && otherBlock.IsGridStable)
            {
                supportingGridBlock = otherBlock;
            }
            found = true;
        }

        return found;
    }

    private void CollectGridSupportContacts(
        BlockController ignoredBlock,
        List<GridSupportContact> contacts)
    {
        contacts.Clear();
        _cellGeometry.Refresh();
        IReadOnlyList<Vector2> cells = _cellGeometry.CellCenters;
        if (cells.Count == 0) return;

        var ownKeys = new HashSet<Vector2Int>();
        for (int i = 0; i < cells.Count; i++)
        {
            ownKeys.Add(ToPlacementGridKey(cells[i].x, cells[i].y));
        }

        for (int i = 0; i < cells.Count; i++)
        {
            Vector2 cell = cells[i];
            if (ownKeys.Contains(ToPlacementGridKey(cell.x, cell.y - gridSpacing))) continue;

            if (TryGetExternalGridSupportInterval(
                    cell,
                    ignoredBlock,
                    out float minX,
                    out float maxX,
                    out BlockController lowerBlock))
            {
                contacts.Add(new GridSupportContact(lowerBlock, minX, maxX));
            }
        }
    }

    private bool HasCellNear(Vector2 expectedCenter, float tolerance)
    {
        _cellGeometry.Refresh();
        float sqrTolerance = tolerance * tolerance;
        IReadOnlyList<Vector2> cells = _cellGeometry.CellCenters;
        for (int i = 0; i < cells.Count; i++)
        {
            if ((cells[i] - expectedCenter).sqrMagnitude <= sqrTolerance) return true;
        }
        return false;
    }

    // A newly seated block may connect formerly separate columns. Every support interface in that
    // connected structure must carry its real downward load before the next physics step.
    private void ValidateGridStructureAfterLanding()
    {
        if (!IsGridStable) return;
        RevalidateGridCandidates(CollectGridStableComponent(this, null), null);
    }

    private static List<BlockController> CollectGridStableComponent(
        BlockController seed,
        BlockController ignoredBlock)
    {
        var component = new List<BlockController>();
        if (seed == null || seed == ignoredBlock || !seed.IsGridStable) return component;

        float grid = Mathf.Max(0.01f, seed.gridSpacing);
        var occupancy = new Dictionary<Vector2Int, List<BlockController>>();
        for (int i = 0; i < TrackedBlocks.Count; i++)
        {
            BlockController block = TrackedBlocks[i];
            if (block == null || block == ignoredBlock || !block.IsGridStable) continue;

            block._cellGeometry.Refresh();
            IReadOnlyList<Vector2> cells = block._cellGeometry.CellCenters;
            for (int c = 0; c < cells.Count; c++)
            {
                Vector2Int key = seed.ToPlacementGridKey(cells[c].x, cells[c].y);
                if (!occupancy.TryGetValue(key, out List<BlockController> occupants))
                {
                    occupants = new List<BlockController>(1);
                    occupancy.Add(key, occupants);
                }
                if (!occupants.Contains(block)) occupants.Add(block);
            }
        }

        var visited = new HashSet<BlockController>();
        var queue = new Queue<BlockController>();
        visited.Add(seed);
        queue.Enqueue(seed);

        while (queue.Count > 0)
        {
            BlockController block = queue.Dequeue();
            component.Add(block);
            block._cellGeometry.Refresh();
            IReadOnlyList<Vector2> cells = block._cellGeometry.CellCenters;

            for (int c = 0; c < cells.Count; c++)
            {
                Vector2 cell = cells[c];
                AddVerticalGridNeighbours(
                    seed.ToPlacementGridKey(cell.x, cell.y + grid), occupancy, visited, queue);
                AddVerticalGridNeighbours(
                    seed.ToPlacementGridKey(cell.x, cell.y - grid), occupancy, visited, queue);
            }
        }

        return component;
    }

    private static void AddVerticalGridNeighbours(
        Vector2Int key,
        Dictionary<Vector2Int, List<BlockController>> occupancy,
        HashSet<BlockController> visited,
        Queue<BlockController> queue)
    {
        if (!occupancy.TryGetValue(key, out List<BlockController> neighbours)) return;
        for (int i = 0; i < neighbours.Count; i++)
        {
            BlockController neighbour = neighbours[i];
            if (neighbour != null && visited.Add(neighbour)) queue.Enqueue(neighbour);
        }
    }

    // Propagate weight from the top of an acyclic support graph toward terrain. Each block must
    // balance its own mass plus the reactions delivered by blocks above across the exact contacts
    // immediately beneath it. This is the missing local-torque test that a whole-tower COM cannot
    // provide: a broad foundation at ground level may not rescue a one-cell cantilever ten rows up.
    private static BlockController FindUnstableGridBlock(
        List<BlockController> component,
        BlockController ignoredBlock)
    {
        if (component == null || component.Count == 0) return null;

        var nodes = new Dictionary<BlockController, GridLoadNode>(component.Count);
        for (int i = 0; i < component.Count; i++)
        {
            BlockController block = component[i];
            if (block != null && block.IsGridStable)
            {
                nodes.Add(block, new GridLoadNode(block));
            }
        }

        if (nodes.Count == 0) return null;
        float tolerance = GridBalanceToleranceFraction *
                          Mathf.Max(0.01f, component[0].gridSpacing);

        foreach (GridLoadNode node in nodes.Values)
        {
            node.Block.CollectGridSupportContacts(ignoredBlock, node.Supports);
            if (node.Supports.Count == 0) return node.Block;

            float mass = node.Block._rb != null ? Mathf.Max(0.01f, node.Block._rb.mass) : 1f;
            float centerX = node.Block.GetGridMassCenterX();
            node.Load = mass;
            node.Moment = mass * centerX;

            // Do this before load propagation as well. It catches a bad top piece immediately
            // and also gives cyclic/interlocked support graphs a safe local requirement.
            if (!node.Block.IsGridResultantSupported(
                    node.Supports, centerX, tolerance, ignoredBlock)) return node.Block;

            for (int s = 0; s < node.Supports.Count; s++)
            {
                BlockController lower = node.Supports[s].LowerBlock;
                if (lower != null && nodes.ContainsKey(lower)) node.LowerBlocks.Add(lower);
            }
        }

        foreach (GridLoadNode node in nodes.Values)
        {
            foreach (BlockController lower in node.LowerBlocks)
            {
                nodes[lower].PendingUpperBlocks++;
            }
        }

        var ready = new Queue<GridLoadNode>();
        foreach (GridLoadNode node in nodes.Values)
        {
            if (node.PendingUpperBlocks == 0) ready.Enqueue(node);
        }

        int processed = 0;
        while (ready.Count > 0)
        {
            GridLoadNode node = ready.Dequeue();
            processed++;

            float resultantX = node.Moment / Mathf.Max(0.01f, node.Load);
            if (!node.Block.IsGridResultantSupported(
                    node.Supports, resultantX, tolerance, ignoredBlock)) return node.Block;

            // A hook can carry a resultant outside its top contact by reacting against the
            // vertical ledge, but that reaction does not erase the overhang's torque. Preserve
            // the original line of action while passing the load downward. Clamping it to the
            // ledge edge makes every hook an impossible moment sink; a chain of individually
            // accepted hooks can then hold an arbitrarily lopsided tower forever.
            DistributeGridLoad(node, nodes, resultantX, tolerance);
            foreach (BlockController lower in node.LowerBlocks)
            {
                GridLoadNode lowerNode = nodes[lower];
                lowerNode.PendingUpperBlocks--;
                if (lowerNode.PendingUpperBlocks == 0) ready.Enqueue(lowerNode);
            }
        }

        if (processed == nodes.Count) return null;

        // Mutually interlocked shapes can produce a support cycle. Their local checks above still
        // apply. Test the unresolved cluster as one body against only contacts that leave it;
        // internal reactions cancel and must not masquerade as a foundation.
        var unresolved = new HashSet<BlockController>();
        float unresolvedLoad = 0f;
        float unresolvedMoment = 0f;
        foreach (GridLoadNode node in nodes.Values)
        {
            if (node.PendingUpperBlocks <= 0) continue;
            unresolved.Add(node.Block);
            unresolvedLoad += node.Load;
            unresolvedMoment += node.Moment;
        }

        var outgoingSupports = new List<GridSupportContact>();
        BlockController releaseCandidate = null;
        foreach (BlockController block in unresolved)
        {
            GridLoadNode node = nodes[block];
            for (int i = 0; i < node.Supports.Count; i++)
            {
                GridSupportContact support = node.Supports[i];
                if (support.LowerBlock != null && unresolved.Contains(support.LowerBlock)) continue;
                outgoingSupports.Add(support);
                if (releaseCandidate == null) releaseCandidate = block;
            }
        }

        if (outgoingSupports.Count == 0 || unresolvedLoad <= 0f) return releaseCandidate ?? component[0];
        float unresolvedX = unresolvedMoment / unresolvedLoad;
        float edgeReserve = GridStructuralEdgeReserveFraction *
                            Mathf.Max(0.01f, component[0].gridSpacing);
        return IsGridResultantInsideSupport(outgoingSupports, unresolvedX, tolerance, edgeReserve)
            ? null
            : releaseCandidate ?? component[0];
    }

    private float GetGridMassCenterX()
    {
        // Unity reports a Kinematic Rigidbody2D's body origin here rather than the custom COM.
        // All normal block cells have equal density, so their cell-centre average is the authored
        // physical COM and remains valid while the grid owns the body.
        _cellGeometry.Refresh();
        IReadOnlyList<Vector2> cells = _cellGeometry.CellCenters;
        if (cells.Count == 0) return transform.position.x;
        float sum = 0f;
        for (int i = 0; i < cells.Count; i++) sum += cells[i].x;
        return sum / cells.Count;
    }

    private bool IsGridResultantSupported(
        List<GridSupportContact> supports,
        float resultantX,
        float tolerance,
        BlockController ignoredBlock)
    {
        if (!TryGetGridSupportSpan(supports, out float minX, out float maxX)) return false;

        float grid = Mathf.Max(0.01f, gridSpacing);
        float edgeReserve = GridStructuralEdgeReserveFraction * grid;
        float reservedMinX = minX + edgeReserve;
        float reservedMaxX = maxX - edgeReserve;
        if (reservedMinX <= reservedMaxX &&
            resultantX >= reservedMinX - tolerance &&
            resultantX <= reservedMaxX + tolerance)
        {
            return true;
        }

        int escapeDirection;
        if (reservedMinX > reservedMaxX)
        {
            escapeDirection = resultantX < (minX + maxX) * 0.5f ? -1 : 1;
        }
        else
        {
            escapeDirection = resultantX < reservedMinX ? -1 : 1;
        }

        float overhangPastContact = escapeDirection < 0
            ? minX - resultantX
            : resultantX - maxX;
        float hookAllowance = GridHookMaxOverhangFraction * grid;
        if (overhangPastContact > hookAllowance + tolerance) return false;
        return HasGridHookAnchor(escapeDirection, ignoredBlock);
    }

    // A real hook wraps around a support corner: one cell rests on top, a connected cell extends
    // past that edge on the same row, and another own cell continues down beside the support.
    // This is form-locked geometry, not friction. It lets S/Z and J/L ledge hooks remain exactly
    // grid-owned without legalizing flat I/O/L overhangs that have nothing below the ledge.
    private bool HasGridHookAnchor(int direction, BlockController ignoredBlock)
    {
        direction = direction < 0 ? -1 : 1;
        float grid = Mathf.Max(0.01f, gridSpacing);

        _cellGeometry.Refresh();
        IReadOnlyList<Vector2> cells = _cellGeometry.CellCenters;
        var ownKeys = new HashSet<Vector2Int>();
        for (int i = 0; i < cells.Count; i++)
        {
            ownKeys.Add(ToPlacementGridKey(cells[i].x, cells[i].y));
        }

        for (int i = 0; i < cells.Count; i++)
        {
            Vector2 supportedCell = cells[i];
            if (ownKeys.Contains(ToPlacementGridKey(
                    supportedCell.x, supportedCell.y - grid))) continue;
            if (!TryGetExternalGridSupportInterval(
                    supportedCell, ignoredBlock, out _, out _, out _)) continue;

            float outsideX = supportedCell.x + direction * grid;
            if (!ownKeys.Contains(ToPlacementGridKey(outsideX, supportedCell.y))) continue;
            if (ownKeys.Contains(ToPlacementGridKey(outsideX, supportedCell.y - grid))) return true;
        }

        return false;
    }

    private static bool IsGridResultantInsideSupport(
        List<GridSupportContact> supports,
        float resultantX,
        float tolerance,
        float edgeReserve)
    {
        if (!TryGetGridSupportSpan(supports, out float minX, out float maxX)) return false;
        float reservedMinX = minX + edgeReserve;
        float reservedMaxX = maxX - edgeReserve;
        return reservedMinX <= reservedMaxX &&
               resultantX >= reservedMinX - tolerance &&
               resultantX <= reservedMaxX + tolerance;
    }

    private static bool TryGetGridSupportSpan(
        List<GridSupportContact> supports,
        out float minX,
        out float maxX)
    {
        minX = float.MaxValue;
        maxX = float.MinValue;
        if (supports == null || supports.Count == 0) return false;

        for (int i = 0; i < supports.Count; i++)
        {
            minX = Mathf.Min(minX, supports[i].MinX);
            maxX = Mathf.Max(maxX, supports[i].MaxX);
        }

        return minX <= maxX;
    }

    private static void DistributeGridLoad(
        GridLoadNode upper,
        Dictionary<BlockController, GridLoadNode> nodes,
        float resultantX,
        float tolerance)
    {
        int containing = -1;
        for (int i = 0; i < upper.Supports.Count; i++)
        {
            GridSupportContact support = upper.Supports[i];
            if (resultantX >= support.MinX - tolerance &&
                resultantX <= support.MaxX + tolerance)
            {
                containing = i;
                break;
            }
        }

        if (containing >= 0)
        {
            GridSupportContact support = upper.Supports[containing];
            AddGridLoad(nodes, support.LowerBlock, upper.Load, resultantX);
            return;
        }

        int leftIndex = -1;
        int rightIndex = -1;
        float leftX = float.MinValue;
        float rightX = float.MaxValue;
        for (int i = 0; i < upper.Supports.Count; i++)
        {
            GridSupportContact support = upper.Supports[i];
            if (support.MaxX <= resultantX && support.MaxX > leftX)
            {
                leftX = support.MaxX;
                leftIndex = i;
            }
            if (support.MinX >= resultantX && support.MinX < rightX)
            {
                rightX = support.MinX;
                rightIndex = i;
            }
        }

        if (leftIndex < 0 || rightIndex < 0)
        {
            // The resultant lies beyond every top contact. Reaching this branch is legal only
            // for a verified ledge hook. Apply its weight to the nearest supporting block while
            // retaining the original application X: the offset from the ledge represents the
            // reaction couple that the lower structure must resist.
            int hookSupportIndex = leftIndex >= 0 ? leftIndex : rightIndex;
            GridSupportContact hookSupport = upper.Supports[hookSupportIndex];
            AddGridLoad(nodes, hookSupport.LowerBlock, upper.Load, resultantX);
            return;
        }

        if (rightX - leftX <= 0.0001f)
        {
            AddGridLoad(nodes, upper.Supports[leftIndex].LowerBlock, upper.Load, resultantX);
            return;
        }

        float rightLoad = upper.Load * Mathf.Clamp01((resultantX - leftX) / (rightX - leftX));
        float leftLoad = upper.Load - rightLoad;
        AddGridLoad(nodes, upper.Supports[leftIndex].LowerBlock, leftLoad, leftX);
        AddGridLoad(nodes, upper.Supports[rightIndex].LowerBlock, rightLoad, rightX);
    }

    private static void AddGridLoad(
        Dictionary<BlockController, GridLoadNode> nodes,
        BlockController lowerBlock,
        float load,
        float applicationX)
    {
        if (lowerBlock == null || load <= 0f || !nodes.TryGetValue(lowerBlock, out GridLoadNode lower)) return;
        lower.Load += load;
        lower.Moment += load * applicationX;
    }

    private static void ReleaseGridComponent(List<BlockController> component)
    {
        if (component == null) return;
        for (int i = 0; i < component.Count; i++) component[i]?.ReleaseGridOwnership();
        InvalidateReachGeometry();
    }

    private void ReleaseGridOwnership()
    {
        if (!IsGridStable) return;

        _isGridStable = false;
        _rb.constraints = RigidbodyConstraints2D.None;
        _rb.bodyType = RigidbodyType2D.Dynamic;
        _rb.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
        if (_dynamicControlReady) _rb.centerOfMass = _originalCenterOfMass;
        _rb.gravityScale = ResolveLandedGravityScale();
        _rb.WakeUp();
    }

    private void ReleaseGridStructureForForce()
    {
        if (!IsGridStable) return;
        ReleaseGridComponent(CollectGridStableComponent(this, null));
    }

    // Support removal can invalidate more than one disconnected structure. The removed block is
    // ignored even though Destroy() may not have removed its colliders until the end of the frame.
    private static void RevalidateAllGridStructures(BlockController ignoredBlock)
    {
        RevalidateGridCandidates(TrackedBlocks, ignoredBlock);
    }

    // Release one failed interface at a time, then rebuild the remaining support graph in the
    // same frame. Blocks above the failure therefore release if they lost their only support,
    // while a bridge with another honest support may remain grid-stable.
    private static void RevalidateGridCandidates(
        IReadOnlyList<BlockController> candidates,
        BlockController ignoredBlock)
    {
        if (candidates == null || candidates.Count == 0) return;

        var candidateSnapshot = new List<BlockController>(candidates.Count);
        for (int i = 0; i < candidates.Count; i++)
        {
            BlockController block = candidates[i];
            if (block != null && block != ignoredBlock) candidateSnapshot.Add(block);
        }

        bool releasedAny = false;
        int releaseBudget = candidateSnapshot.Count;
        while (releaseBudget-- > 0)
        {
            var checkedBlocks = new HashSet<BlockController>();
            BlockController unstable = null;
            for (int i = 0; i < candidateSnapshot.Count; i++)
            {
                BlockController seed = candidateSnapshot[i];
                if (seed == null || !seed.IsGridStable || checkedBlocks.Contains(seed)) continue;

                List<BlockController> component = CollectGridStableComponent(seed, ignoredBlock);
                for (int c = 0; c < component.Count; c++) checkedBlocks.Add(component[c]);
                unstable = FindUnstableGridBlock(component, ignoredBlock);
                if (unstable != null) break;
            }

            if (unstable == null) break;
            unstable.ReleaseGridOwnership();
            releasedAny = true;
        }

        if (releasedAny) InvalidateReachGeometry();
    }

    // Failed nudges are impulses, not persistent control. A stable structure is released once,
    // then receives the same physical hit the previous fully-dynamic implementation used.
    private void ApplyLandingImpulse(Vector2 impulse)
    {
        if (_rb == null || IsFrozenInPlace) return;
        ReleaseGridStructureForForce();
        if (_rb.bodyType != RigidbodyType2D.Dynamic) return;

        _rb.WakeUp();
        _rb.AddForce(impulse, ForceMode2D.Impulse);
    }
}
