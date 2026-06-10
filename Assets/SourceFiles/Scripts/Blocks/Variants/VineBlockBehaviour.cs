using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime half of VineBlockData: after a short settling delay, creates breakable fixed joints
/// to every rigidbody this block touches (tower blocks and static platforms alike).
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

        ContactFilter2D filter = new ContactFilter2D
        {
            useTriggers = false,
            useLayerMask = false
        };

        var weldedBodies = new HashSet<Rigidbody2D>();
        Collider2D[] ownColliders = GetComponentsInChildren<Collider2D>();
        for (int colliderIndex = 0; colliderIndex < ownColliders.Length; colliderIndex++)
        {
            Collider2D own = ownColliders[colliderIndex];
            if (own == null || own.isTrigger) continue;

            Bounds bounds = own.bounds;
            Vector2 probeSize = (Vector2)bounds.size + Vector2.one * (2f * _touchRange);
            int count = Physics2D.OverlapBox(bounds.center, probeSize, 0f, filter, _overlapBuffer);
            for (int i = 0; i < count; i++)
            {
                Collider2D hit = _overlapBuffer[i];
                if (hit == null) continue;

                Rigidbody2D otherBody = hit.attachedRigidbody;
                if (otherBody == null || otherBody == ownBody) continue;
                if (!weldedBodies.Add(otherBody)) continue;

                FixedJoint2D joint = gameObject.AddComponent<FixedJoint2D>();
                joint.connectedBody = otherBody;
                joint.breakForce = _breakForce;
                joint.breakTorque = _breakForce;

                if (weldedBodies.Count >= MaxWelds) return;
            }
        }
    }
}
