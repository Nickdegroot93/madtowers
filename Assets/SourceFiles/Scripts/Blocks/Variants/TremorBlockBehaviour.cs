using UnityEngine;

/// <summary>
/// Runtime half of TremorBlockData: the actual quake. A single velocity bump (the old behaviour) just
/// re-settles in a frame and reads as nothing, so this delivers a short BURST of kicks over ~half a
/// second - a real shake. Each kick:
///   - reverses direction (a coherent back-and-forth ground shake),
///   - adds a per-block random lateral + tiny upward component, so blocks move RELATIVE to each other
///     (that shear is what unseats and topples a badly-placed block; a uniform push only slides the
///     whole tower together),
///   - falls off with distance from the brick (the epicenter), so it's worst right around the Tremor
///     and still felt across the tower,
///   - decays over the burst so it eases out instead of cutting off.
/// Velocity-impulse only (ApplyJolt) - never positions (PHYSICS.md I1). Anchored/frozen (Static) blocks
/// ignore it by body type. Added to the block on lock; self-destroys when the burst ends.
/// </summary>
public class TremorBlockBehaviour : MonoBehaviour
{
    private const float KickInterval = 0.07f; // seconds between kicks (~7 kicks over a 0.5s burst)
    private const float VerticalBias = 0.25f; // small upward component (fraction of strength) to break grip
    private const float Randomness = 0.6f;    // per-block random lateral fraction - the shear that topples
    private const float MinFalloff = 0.35f;   // the farthest blocks still get this fraction of the shake

    private float _strength;
    private float _duration;
    private float _radius;
    private Vector2 _epicenter;
    private BlockController _self;
    private float _elapsed;
    private float _nextKick;
    private int _kickIndex;

    public void Arm(Vector2 epicenter, float strength, float duration, float radius)
    {
        _epicenter = epicenter;
        _strength = strength;
        _duration = Mathf.Max(0.05f, duration);
        _radius = Mathf.Max(0.5f, radius);
        _self = GetComponent<BlockController>();
        Kick();                   // hit instantly on landing...
        _nextKick = KickInterval; // ...then the next kick is a full interval out (no double-hit at t=0)
    }

    private void FixedUpdate()
    {
        _elapsed += Time.fixedDeltaTime;

        if (_elapsed >= _nextKick && _elapsed < _duration)
        {
            _nextKick += KickInterval;
            Kick();
        }

        if (_elapsed >= _duration) Destroy(this);
    }

    private void Kick()
    {
        float envelope = Mathf.Clamp01(1f - _elapsed / _duration); // strongest at the start, eases out
        float baseSign = (_kickIndex % 2 == 0) ? 1f : -1f;         // alternating shake direction over time
        _kickIndex++;

        var blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController b = blocks[i];
            if (b == null || b == _self || !b.HasLanded) continue;

            float dist = Vector2.Distance(_epicenter, b.transform.position);
            float falloff = Mathf.Lerp(1f, MinFalloff, Mathf.Clamp01(dist / _radius));
            float mag = _strength * envelope * falloff;

            float vx = baseSign + Random.Range(-Randomness, Randomness); // coherent sway + per-block shear
            float vy = Random.Range(0f, VerticalBias);                   // a touch of lift to unseat grip
            b.ApplyJolt(new Vector2(vx * mag, vy * mag));
        }
    }
}
