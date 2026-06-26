using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime half of the Maw: once it lands it NEVER stops eating. On a steady cadence it probes just above
/// its own top cells; any landed block resting there is devoured - shattered (which removes it from the
/// live count, BLOCKS.md) and it costs the player a LIFE (GameManager.LoseLifeToHazard). So you must build
/// AROUND a maw, never on it - and a stack that collapses onto it gets eaten one block at a time. The one
/// exception is another maw: maws never eat each other, so they can be safely stacked (otherwise two maws
/// dealt back-to-back could be unplaceable).
///
/// Velocity/anim only on the maw itself; the prey is removed through the sanctioned shatter path. The
/// "resting on top" probe excludes the maw's own cells (and other maws), so it only ever fires on an
/// EXTERNAL, non-maw block landing on the piece's actual top surface.
///
/// On landing it also WELDS to any maw it touches (FixedJoint2D, unbreakable - the Vine weld pattern, but
/// maw-only and permanent): a stack of maws fuses into one rigid "huge maw" so it can't be toppled by the
/// loose-leaning that plagued separate bodies. The weld is maw↔maw only; normal blocks are never welded
/// (they'd just be eaten anyway).
/// </summary>
public class MawBlockBehaviour : MonoBehaviour
{
    private const float BiteInterval = 0.55f;                 // seconds between bites (one block at a time)
    private const float FirstBiteDelay = 0.35f;               // a beat after landing before the first bite
    private const float WeldTouchRange = 0.15f;               // neighbour-touch clearance (matches Vine)
    private const int MaxWelds = 8;                           // cap joints per maw (same as Vine)
    private static readonly Color ShardTint = new Color(0.45f, 0.08f, 0.10f, 1f); // gore-red shards

    private readonly Collider2D[] _buffer = new Collider2D[16];
    private readonly Collider2D[] _weldBuffer = new Collider2D[24];

    private MawBlockSkin _skin;
    private GameObject _eatEffect;
    private float _eatScale;
    private BlockController _self;
    private BoxCollider2D[] _cells; // the maw's own cell colliders, cached (fixed once it has landed)
    private ContactFilter2D _filter;
    private float _timer;

    public void Begin(MawBlockSkin skin, GameObject eatEffect, float eatScale)
    {
        _skin = skin;
        _eatEffect = eatEffect;
        _eatScale = eatScale;
        _self = GetComponent<BlockController>();
        _cells = GetComponentsInChildren<BoxCollider2D>();
        _filter = new ContactFilter2D { useTriggers = false, useLayerMask = false };
        _timer = FirstBiteDelay;

        // Weld right here on lock: the body has just gone Dynamic at its exact landed pose (the lock path
        // already SyncTransforms'd), so the joint forms before the first settling solve - a maw landing on
        // a maw can't tilt before fusing. Same timing rationale as Vine's delay-0 weld.
        WeldToMaws();
    }

    // Glue this maw to every maw it's touching with an unbreakable FixedJoint2D, so a stack of maws behaves
    // as one rigid brick. Maw-only and permanent (Infinity break force); normal blocks are never welded.
    private void WeldToMaws()
    {
        Rigidbody2D ownBody = GetComponent<Rigidbody2D>();
        if (ownBody == null) return;

        var touching = new HashSet<Collider2D>();
        BlockTouchScanner.CollectTouchingColliders(gameObject, WeldTouchRange, touching, _weldBuffer);

        var welded = new HashSet<Rigidbody2D>();
        foreach (Collider2D hit in touching)
        {
            Rigidbody2D otherBody = hit.attachedRigidbody;
            if (otherBody == null || otherBody == ownBody) continue;
            if (otherBody.GetComponent<MawBlockSkin>() == null) continue; // only ever weld maw to maw
            if (!welded.Add(otherBody)) continue;

            FixedJoint2D joint = gameObject.AddComponent<FixedJoint2D>();
            joint.connectedBody = otherBody;
            joint.breakForce = Mathf.Infinity;  // permanent: the cluster is "one huge maw brick"
            joint.breakTorque = Mathf.Infinity;

            if (welded.Count >= MaxWelds) return;
        }
    }

    private void FixedUpdate()
    {
        _timer -= Time.fixedDeltaTime;
        if (_timer > 0f) return;
        _timer = BiteInterval;

        BlockController prey = FindPreyOnTop();
        if (prey != null) Eat(prey);
    }

    // The first external, landed block resting on any of this piece's top cells.
    private BlockController FindPreyOnTop()
    {
        for (int c = 0; c < _cells.Length; c++)
        {
            BoxCollider2D cell = _cells[c];
            if (cell == null || cell.isTrigger) continue;

            Bounds b = cell.bounds;
            var probeCenter = new Vector2(b.center.x, b.max.y + 0.06f);
            var probeSize = new Vector2(b.size.x * 0.85f, 0.12f);
            int n = Physics2D.OverlapBox(probeCenter, probeSize, 0f, _filter, _buffer);
            for (int i = 0; i < n; i++)
            {
                Collider2D hit = _buffer[i];
                if (hit == null || hit.transform.IsChildOf(transform)) continue; // skip our own cells
                BlockController bc = hit.GetComponentInParent<BlockController>();
                if (bc == null || bc == _self || !bc.HasLanded) continue;
                if (bc.GetComponent<MawBlockSkin>() != null) continue; // a maw never eats another maw - they stack
                return bc;
            }
        }
        return null;
    }

    private void Eat(BlockController prey)
    {
        _skin?.PlayChomp();
        if (_eatEffect != null && prey.TryGetWorldBounds(out Bounds pb))
            Vfx.Spawn(_eatEffect, pb.center, _eatScale, 2f); // subtle one-shot disintegrate (assigned on the asset)

        ImpactFx.ImpactPunch(0.03f, 0.10f, 0.12f);          // the bite has weight
        ImpactFx.DestroyBlockWithShatter(prey, ShardTint);  // shatter + remove from the live count
        if (GameManager.Instance != null) GameManager.Instance.LoseLifeToHazard(); // every devour costs a life
    }
}
