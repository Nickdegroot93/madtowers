using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime half of VineBlockData: creates breakable fixed joints to every rigidbody this block
/// touches (tower blocks and static platforms alike). By default it welds instantly on landing
/// (delay 0) so the block can't settle/tilt before gluing; a positive delay defers the weld.
/// </summary>
public class VineBlockBehaviour : MonoBehaviour
{
    private const int MaxWelds = 8;
    // A weld needs a REAL shared edge (world units of face-to-face overlap): a corner kiss
    // or a sliver graze must not glue (Nick 2026-08-30 - the vine's reach had crept up to
    // "anything near it"). ~40% of a cell keeps honest half-offset contacts welding while
    // corner-diagonal neighbours stay free.
    private const float MinSharedEdge = 0.4f;

    private readonly Collider2D[] _overlapBuffer = new Collider2D[24];

    private float _attachDelay;
    private float _breakForce;
    private float _touchRange;
    private float _elapsed;
    private bool _attached;

    public void Attach(float attachDelaySeconds, float breakForce, float touchRange)
    {
        _attachDelay = attachDelaySeconds;
        _breakForce = breakForce;
        _touchRange = touchRange;

        // Delay 0 = weld right here in OnLocked at the exact landed pose. If a grid-stable
        // structure is later released, the joint is already present for Dynamic physics.
        // The vine growth animation stays on its own clock.
        if (_attachDelay <= 0f)
        {
            _attached = true;
            WeldToContacts();
        }
    }

    private void Update()
    {
        if (_attached) return;

        _elapsed += Time.deltaTime;
        if (_elapsed < _attachDelay) return;

        _attached = true;
        WeldToContacts();
    }

    private void WeldToContacts()
    {
        Rigidbody2D ownBody = GetComponent<Rigidbody2D>();
        if (ownBody == null) return;

        var touching = new HashSet<Collider2D>();
        BlockTouchScanner.CollectTouchingColliders(gameObject, _touchRange, touching, _overlapBuffer);

        // The scanner's expanded box also catches corner-diagonal neighbours (it grows the
        // probe in BOTH axes) - vine demands a real shared edge on top (see MinSharedEdge).
        Collider2D[] ownColliders = GetComponentsInChildren<Collider2D>();

        var weldedBodies = new HashSet<Rigidbody2D>();
        foreach (Collider2D hit in touching)
        {
            Rigidbody2D otherBody = hit.attachedRigidbody;
            if (otherBody == null || otherBody == ownBody) continue;
            if (weldedBodies.Contains(otherBody)) continue;
            if (!SharesRealEdge(ownColliders, hit)) continue;
            weldedBodies.Add(otherBody);

            FixedJoint2D joint = gameObject.AddComponent<FixedJoint2D>();
            joint.connectedBody = otherBody;
            joint.breakForce = _breakForce;
            joint.breakTorque = _breakForce;

            SpreadVineTo(otherBody);

            if (weldedBodies.Count >= MaxWelds) return;
        }
    }

    // True when ANY of the vine's own colliders shares a real face with the hit: the pair's
    // bounds must genuinely overlap along one axis (>= MinSharedEdge) while merely meeting
    // (within touchRange) on the other. A corner contact fails both arms - its overlap is
    // ~0 on BOTH axes. Bounds are AABBs, so a strongly tilted brick is judged by its box -
    // acceptable: welds happen at lock, when the tower sits essentially axis-aligned.
    private bool SharesRealEdge(Collider2D[] ownColliders, Collider2D hit)
    {
        Bounds b = hit.bounds;
        for (int i = 0; i < ownColliders.Length; i++)
        {
            Collider2D own = ownColliders[i];
            if (own == null || own.isTrigger) continue;
            Bounds a = own.bounds;
            float overlapX = Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x);
            float overlapY = Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y);
            bool sideBySide = overlapX >= -_touchRange && overlapY >= MinSharedEdge;
            bool stacked = overlapY >= -_touchRange && overlapX >= MinSharedEdge;
            if (sideBySide || stacked) return true;
        }
        return false;
    }

    // Phase 2: creep vines onto a welded block from the contact side. Only real blocks (BlockController)
    // get vined - the floor and static islands are skipped, and so is any fixed-look identity
    // brick (Maw, Curse, Bomb, ... - BlocksForeignOverlays): the WELD still holds, but vines
    // must never grow over the face/fuse/eye those bricks exist to show. Idempotent per block
    // (one already vined - another vine, or a previous weld - keeps its existing vines via
    // VineBlockSkin's own guard).
    private void SpreadVineTo(Rigidbody2D otherBody)
    {
        if (otherBody == null || !otherBody.TryGetComponent(out BlockController _)) return;
        BlockVariantSkin[] skins = otherBody.GetComponents<BlockVariantSkin>();
        for (int i = 0; i < skins.Length; i++)
            if (skins[i] != null && skins[i].BlocksForeignOverlays) return;

        if (!otherBody.TryGetComponent(out VineBlockSkin skin))
            skin = otherBody.gameObject.AddComponent<VineBlockSkin>();

        // Growth direction = from the vine block into the neighbour, so the vines root at the contact
        // edge and creep across. Expressed in the neighbour's local frame (its overlay quads align to it).
        Vector2 growth = ((Vector2)otherBody.transform.position - (Vector2)transform.position).normalized;
        Vector2 localDir = otherBody.transform.InverseTransformDirection(growth);
        skin.GrowFrom(localDir);
    }
}
