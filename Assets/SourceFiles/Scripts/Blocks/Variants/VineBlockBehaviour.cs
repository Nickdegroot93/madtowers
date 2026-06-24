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

        // Delay 0 = weld right here in OnLocked: the body has just gone Dynamic at its exact landed pose
        // (the lock path already SyncTransforms'd), so the joint is in place before the first settling
        // solve - the block can't tilt before gluing. The vine growth animation stays on its own clock.
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

        var weldedBodies = new HashSet<Rigidbody2D>();
        foreach (Collider2D hit in touching)
        {
            Rigidbody2D otherBody = hit.attachedRigidbody;
            if (otherBody == null || otherBody == ownBody) continue;
            if (!weldedBodies.Add(otherBody)) continue;

            FixedJoint2D joint = gameObject.AddComponent<FixedJoint2D>();
            joint.connectedBody = otherBody;
            joint.breakForce = _breakForce;
            joint.breakTorque = _breakForce;

            SpreadVineTo(otherBody);

            if (weldedBodies.Count >= MaxWelds) return;
        }
    }

    // Phase 2: creep vines onto a welded block from the contact side. Only real blocks (BlockController)
    // get vined - the floor and static islands are skipped. Idempotent per block (one already vined -
    // another vine, or a previous weld - keeps its existing vines via VineBlockSkin's own guard).
    private void SpreadVineTo(Rigidbody2D otherBody)
    {
        if (otherBody == null || !otherBody.TryGetComponent(out BlockController _)) return;

        if (!otherBody.TryGetComponent(out VineBlockSkin skin))
            skin = otherBody.gameObject.AddComponent<VineBlockSkin>();

        // Growth direction = from the vine block into the neighbour, so the vines root at the contact
        // edge and creep across. Expressed in the neighbour's local frame (its overlay quads align to it).
        Vector2 growth = ((Vector2)otherBody.transform.position - (Vector2)transform.position).normalized;
        Vector2 localDir = otherBody.transform.InverseTransformDirection(growth);
        skin.GrowFrom(localDir);
    }
}
