using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime half of Sandstone: reads the weight of the stack RESTING on the brick (re-sampled
/// every LoadSampleStride physics steps, smoothed between) by walking the support graph upward
/// (thin overlap probes at the resting plane above
/// each top-exposed cell, transitively), summing rigidbody mass in normal-brick-weight units,
/// and dividing each direct rester's branch by how many distinct supports that rester stands
/// on (a pure tower presses exact; a bridge presses its share). Structural rather than
/// solver-impulse based on purpose: the settle system force-sleeps quiet stacks
/// (Settling.cs, I3) and Box2D reports no contact impulses for sleeping islands, so a real
/// force gauge reads zero exactly when it matters. The structural sum is identical at rest,
/// deterministic, and mass-aware for free: a Boulder (4x) crushes instantly, Feathers (0.25x)
/// barely count. Static bodies are self-supporting terrain: a frozen block contributes no
/// load and SHIELDS everything above it - Freeze is a legitimate rescue for a sand floor.
/// Sandstone never burdens sandstone (the maw-on-maw precedent): each sand layer carries
/// only what rests directly on it, so sand stacks safely on sand.
/// The reading runs through an exponential smoother so cracks grow visibly rather than pop;
/// damage = worst sustained load / break load, RATCHETED (cracks never heal). Crumble =
/// shatter + dust through the standard destruction flow (BLOCKS.md accounting, neighbour
/// wake, live count -1) with NO life charge - the collapse above is the punishment.
/// </summary>
public sealed class SandstoneBlockBehaviour : MonoBehaviour
{
    private static readonly Color SandTint = new Color(0.82f, 0.68f, 0.42f, 1f);

    // Shared scratch for the support walk (single-threaded physics step).
    private static readonly Collider2D[] _overlaps = new Collider2D[16];
    private static readonly List<Vector2> _cellScratch = new List<Vector2>();
    private static readonly List<Vector2> _supportScratch = new List<Vector2>();
    private static readonly List<BlockController> _walkQueue = new List<BlockController>();
    private static readonly List<BlockController> _directResters = new List<BlockController>();
    private static readonly HashSet<BlockController> _walkVisited = new HashSet<BlockController>();
    private static readonly HashSet<Object> _supportSet = new HashSet<Object>();
    private static readonly ContactFilter2D _probeFilter = MakeProbeFilter();

    private static ContactFilter2D MakeProbeFilter()
    {
        var filter = new ContactFilter2D();
        filter.NoFilter();
        filter.useTriggers = false; // ability sensors ride on block bodies; only solid cells are support
        return filter;
    }

    // Ignore readings right after landing: give the piece above a beat to come to rest.
    private const float ArmGraceSeconds = 0.6f;
    // Subtracted from the authored break load: the smoother approaches N brick-weights
    // asymptotically and would never cross an exact-equality threshold, so N-1 bricks must
    // sit safely below the trigger while the Nth crosses it quickly.
    private const float BreakMarginBrickWeights = 0.45f;
    // The probe is a thin band hugging the top face of each cell. Cell colliders are rounded
    // (edge radius): the measured RESTING plane - where a block sitting on us actually
    // touches - is 0.60 step above our cell centre. Band = rise 0.66 +/- 0.12 step, so a
    // touching block always crosses it while a bridge/overhang hovering more than ~0.18 step
    // above the resting plane never does - occupancy of the cell above is NOT load.
    // Width is tight enough that a block on the NEIGHBOURING column needs a ~0.3-cell
    // overhang before it reads as our load (a real cantilever, not settle drift).
    private const float ProbeWidthFraction = 0.6f;
    private const float ProbeRiseFraction = 0.66f;
    private const float ProbeHeightFraction = 0.24f;
    // Safety valve for the walk - far above any real tower.
    private const int MaxWalkedBlocks = 256;
    // The walk is re-sampled every Nth physics step (staggered per instance); the smoother
    // bridges the gaps, so several sandstones under one tall tower stay cheap on mobile.
    private const int LoadSampleStride = 3;

    private BlockController _block;
    private Rigidbody2D _rb;
    private SandstoneBlockSkin _skin;
    private float _breakLoad;
    private float _smoothingSeconds;
    private float _age;
    private float _smoothedLoad;
    private float _lastRawLoad;
    private int _stepCount;
    private float _damageRatchet;
    private int _lastCrackStage;
    private bool _crumbling;

    public void Arm(float breakLoadBrickWeights, float loadSmoothingSeconds)
    {
        _block = GetComponent<BlockController>();
        _rb = GetComponent<Rigidbody2D>();
        _skin = GetComponent<SandstoneBlockSkin>();
        _breakLoad = Mathf.Max(1f, breakLoadBrickWeights - BreakMarginBrickWeights);
        _smoothingSeconds = Mathf.Max(0.1f, loadSmoothingSeconds);
        _stepCount = Mathf.Abs(GetInstanceID()) % LoadSampleStride; // desync sampling phases
    }

    private void FixedUpdate()
    {
        if (_crumbling || _rb == null || _block == null) return;
        // No crumbling behind the results screen (the Bomb precedent): the final collapse
        // must not keep bursting bricks and mutating the ledger after the run ended.
        // Push the pressure read-out to zero on every inert path (here, falling-away and
        // frozen below) so the skin's shiver/trickle never sticks at the last live value.
        if (GameManager.Instance != null && GameManager.Instance.isGameOver)
        {
            if (_skin != null) _skin.SetDamage(_damageRatchet, 0f);
            return;
        }
        // A sandstone knocked off the tower is debris: nothing truly presses in free fall,
        // and bursting mid-air would dodge the life the loss zone is about to charge.
        if (_block.IsFallingAway)
        {
            if (_skin != null) _skin.SetDamage(_damageRatchet, 0f);
            return;
        }
        // Frozen sandstone is preserved stone: it bears weight without feeling it. Push the
        // pressure read-out to zero so the skin stops shivering/trickling - the cracks stay.
        if (_rb.bodyType != RigidbodyType2D.Dynamic)
        {
            if (_skin != null) _skin.SetDamage(_damageRatchet, 0f);
            return;
        }

        _age += Time.fixedDeltaTime;

        if (_stepCount++ % LoadSampleStride == 0) _lastRawLoad = ComputeSupportedBrickWeights();
        float k = 1f - Mathf.Exp(-Time.fixedDeltaTime / _smoothingSeconds);
        _smoothedLoad = Mathf.Lerp(_smoothedLoad, _lastRawLoad, k);

        if (_age < ArmGraceSeconds) return;

        float current01 = Mathf.Clamp01(_smoothedLoad / _breakLoad);
        _damageRatchet = Mathf.Max(_damageRatchet, current01);
        if (_skin != null) _skin.SetDamage(_damageRatchet, current01);

        // A dry crack tick each time the damage crosses a new third - the audible warning.
        int stage = Mathf.Min(2, Mathf.FloorToInt(_damageRatchet * 3f));
        if (stage > _lastCrackStage)
        {
            _lastCrackStage = stage;
            SfxPlayer.Play("sandstone_crack", 0.6f, 0.06f, 1f - 0.08f * stage);
        }

        if (_smoothedLoad >= _breakLoad) Crumble();
    }

    // Total load (in brick weights) pressing on this brick: for each block resting DIRECTLY
    // on us, take the mass of its whole transitive stack and divide by how many distinct
    // supports that block itself stands on. A brick resting only on us presses fully (a pure
    // tower reads exact: 3 bricks = 3.0); a bridge across us and a neighbour column presses
    // half; an overhang tucked over us but held by two other columns presses a third. This
    // keeps the read-out physical where the player can see it, without a solver.
    private float ComputeSupportedBrickWeights()
    {
        _walkVisited.Clear();
        _walkVisited.Add(_block);
        _directResters.Clear();
        CollectResters(_block, _directResters);

        float total = 0f;
        for (int r = 0; r < _directResters.Count; r++)
        {
            BlockController rester = _directResters[r];
            if (rester == null) continue;
            float branch = 0f;
            if (rester.TryGetComponent(out Rigidbody2D resterBody)) branch += resterBody.mass;

            // The rester's transitive stack (shared visited set: nothing counts twice even
            // when branches merge above).
            _walkQueue.Clear();
            CollectResters(rester, _walkQueue);
            int guard = 0;
            while (_walkQueue.Count > 0 && guard++ < MaxWalkedBlocks)
            {
                BlockController carried = _walkQueue[_walkQueue.Count - 1];
                _walkQueue.RemoveAt(_walkQueue.Count - 1);
                if (carried == null) continue;
                if (carried.TryGetComponent(out Rigidbody2D carriedBody)) branch += carriedBody.mass;
                CollectResters(carried, _walkQueue);
            }

            total += branch / Mathf.Max(1, CountSupports(rester));
            // Past the break load only the crossing matters and the smoother saturates
            // identically - stop paying for the rest of a huge tower.
            if (total >= _breakLoad) return total;
        }
        return total;
    }

    private void CollectResters(BlockController below, List<BlockController> into)
    {
        float step = Mathf.Max(0.1f, below.GridSpacing);
        Vector2 probeSize = new Vector2(step * ProbeWidthFraction, step * ProbeHeightFraction);
        // Probe along WORLD up, always: gravity does not rotate with the piece. (Probing the
        // block's local up sounds like a tilt refinement but is catastrophically wrong for
        // the common case - a piece placed quarter-turned probed SIDEWAYS into its
        // neighbours' towers, and upside-down probed the tower below, both reading as
        // phantom load. The rare deeply-wedged diagonal under-reads instead; accepted.)
        _cellScratch.Clear();
        below.GetWorldCellCenters(_cellScratch);
        for (int c = 0; c < _cellScratch.Count; c++)
        {
            // Only TOP-EXPOSED cells probe. A lower cell of a tall piece has our OWN next
            // cell directly above it - anything else its probe could touch is a block BESIDE
            // us at that height (a flank neighbour bleeding into the band), never weight
            // resting on us. This is what made a vertical sandstone "crack from its
            // neighbours": its lower-cell probes sat at neighbour body heights.
            Vector2 cell = _cellScratch[c];
            Vector2 above = cell + Vector2.up * step;
            bool covered = false;
            for (int j = 0; j < _cellScratch.Count && !covered; j++)
            {
                if (j == c) continue;
                covered = (_cellScratch[j] - above).sqrMagnitude < 0.25f * step * step;
            }
            if (covered) continue;

            Vector2 probeCenter = cell + Vector2.up * (step * ProbeRiseFraction);
            int hits = Physics2D.OverlapBox(probeCenter, probeSize, 0f, _probeFilter, _overlaps);
            for (int i = 0; i < hits; i++)
            {
                if (_overlaps[i] == null) continue;
                // Two geometric truths tell a RESTER from a FLANK brick drifting into the
                // band: a rester's cell collider STARTS above our top face (its bottom sits
                // on the resting plane), and it covers a real share of the probe - a
                // neighbouring column's brick starts below our face and only grazes the
                // probe edge by a sliver (the "cracks from bricks beside it" bug).
                Bounds hitBounds = _overlaps[i].bounds;
                if (hitBounds.min.y < cell.y + step * 0.5f) continue;
                float probeHalfW = probeSize.x * 0.5f;
                float overlapX = Mathf.Min(cell.x + probeHalfW, hitBounds.max.x)
                               - Mathf.Max(cell.x - probeHalfW, hitBounds.min.x);
                if (overlapX < step * 0.15f) continue;
                Rigidbody2D otherBody = _overlaps[i].attachedRigidbody;
                if (otherBody == null) continue;
                if (!otherBody.TryGetComponent(out BlockController other)) continue;
                if (!_walkVisited.Add(other)) continue;
                if (!other.HasLanded) continue;      // falling pieces don't press yet
                if (other.IsFallingAway) continue;   // knocked-off debris passing by isn't load
                // Sandstone never burdens sandstone (the maw-on-maw precedent): each sand
                // layer carries only what rests directly on IT, so sand stacks safely on
                // sand - and shields the layer below, like frozen terrain does.
                if (other.TryGetComponent(out SandstoneBlockBehaviour _)) continue;
                // Static = frozen terrain: self-supporting, carries its own stack. Kinematic =
                // mid-animation (suck/devour handoffs), not structural weight either.
                if (otherBody.bodyType != RigidbodyType2D.Dynamic) continue;
                into.Add(other);
            }
        }
    }

    // How many distinct things a block stands on: the mirror of CollectResters, probing a
    // thin band under each bottom-exposed cell. Terrain and frozen blocks count (a support
    // is a support), and we are always among them, so the result is >= 1 in practice.
    private int CountSupports(BlockController block)
    {
        float step = Mathf.Max(0.1f, block.GridSpacing);
        Vector2 probeSize = new Vector2(step * ProbeWidthFraction, step * ProbeHeightFraction);
        _supportScratch.Clear();
        block.GetWorldCellCenters(_supportScratch);
        _supportSet.Clear();
        for (int c = 0; c < _supportScratch.Count; c++)
        {
            Vector2 cell = _supportScratch[c];
            Vector2 under = cell + Vector2.down * step;
            bool covered = false;
            for (int j = 0; j < _supportScratch.Count && !covered; j++)
            {
                if (j == c) continue;
                covered = (_supportScratch[j] - under).sqrMagnitude < 0.25f * step * step;
            }
            if (covered) continue;

            Vector2 probeCenter = cell + Vector2.down * (step * ProbeRiseFraction);
            int hits = Physics2D.OverlapBox(probeCenter, probeSize, 0f, _probeFilter, _overlaps);
            for (int i = 0; i < hits; i++)
            {
                if (_overlaps[i] == null) continue;
                Bounds hitBounds = _overlaps[i].bounds;
                if (hitBounds.max.y > cell.y - step * 0.5f) continue; // flank, not support
                float probeHalfW = probeSize.x * 0.5f;
                float overlapX = Mathf.Min(cell.x + probeHalfW, hitBounds.max.x)
                               - Mathf.Max(cell.x - probeHalfW, hitBounds.min.x);
                if (overlapX < step * 0.15f) continue;
                Rigidbody2D supportBody = _overlaps[i].attachedRigidbody;
                if (supportBody != null && supportBody.gameObject == block.gameObject) continue;
                // Distinct support = distinct body; bodiless colliders are terrain pieces.
                _supportSet.Add(supportBody != null ? (Object)supportBody : _overlaps[i]);
            }
        }
        return _supportSet.Count;
    }

    private void Crumble()
    {
        if (_crumbling) return;
        _crumbling = true;

        if (_block != null && _block.TryGetWorldBounds(out Bounds bounds))
        {
            BlockShatterFx.Spawn(bounds, SandTint, 14);
        }
        SfxPlayer.Play("sandstone_burst", 1f);
        TowerCameraController.Impact(0.12f, 0.18f);

        // The standard destruction flow: accounting + neighbour wake, then gone. No life
        // charge - crumbling is the brick's nature, the collapse above is the price.
        if (_block != null)
        {
            GameEvents.RaiseBlockDestroyed(_block);
            Destroy(_block.gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}
