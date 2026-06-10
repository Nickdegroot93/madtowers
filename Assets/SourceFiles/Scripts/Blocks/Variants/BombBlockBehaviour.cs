using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime half of BombBlockData: pulses red while the fuse burns, then deletes this block and
/// every block touching it. Added to the block when it locks.
/// </summary>
public class BombBlockBehaviour : MonoBehaviour
{
    private readonly Collider2D[] _overlapBuffer = new Collider2D[24];

    private float _fuseSeconds;
    private float _touchRange;
    private float _elapsed;
    private SpriteRenderer[] _renderers;
    private Color[] _baseColors;

    public void Arm(float fuseSeconds, float touchRange)
    {
        _fuseSeconds = fuseSeconds;
        _touchRange = touchRange;

        _renderers = GetComponentsInChildren<SpriteRenderer>();
        _baseColors = new Color[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
        {
            _baseColors[i] = _renderers[i].color;
        }
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;

        // Accelerating red pulse so the player sees it coming.
        float pulse = Mathf.PingPong(_elapsed * (2f + 6f * _elapsed / _fuseSeconds), 1f);
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] == null) continue;
            _renderers[i].color = Color.Lerp(_baseColors[i], Color.red, pulse);
        }

        if (_elapsed >= _fuseSeconds)
        {
            Detonate();
        }
    }

    private void Detonate()
    {
        BlockController self = GetComponent<BlockController>();
        var touching = new HashSet<Collider2D>();
        BlockTouchScanner.CollectTouchingColliders(gameObject, _touchRange, touching, _overlapBuffer);

        var victims = new HashSet<BlockController>();
        foreach (Collider2D hit in touching)
        {
            BlockController other = hit.GetComponentInParent<BlockController>();
            if (other == null || other == self || !other.HasLanded) continue;

            victims.Add(other);
        }

        foreach (BlockController victim in victims)
        {
            Destroy(victim.gameObject);
        }

        Destroy(gameObject);
    }
}
