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
/// </summary>
public class MawBlockBehaviour : MonoBehaviour
{
    private const float BiteInterval = 0.55f;                 // seconds between bites (one block at a time)
    private const float FirstBiteDelay = 0.35f;               // a beat after landing before the first bite
    private static readonly Color ShardTint = new Color(0.45f, 0.08f, 0.10f, 1f); // gore-red shards

    private readonly Collider2D[] _buffer = new Collider2D[16];

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
