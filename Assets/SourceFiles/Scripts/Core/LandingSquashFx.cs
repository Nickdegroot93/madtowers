using UnityEngine;

/// <summary>
/// Tier-0 landing squash-and-stretch (JUICE.md): on touchdown the piece's SKIN compresses
/// world-vertically and bulges sideways, then springs back with the shared FxKit elastic.
/// Animates ONLY the collider-less PieceSkin child, so physics never sees it (PHYSICS.md I1);
/// blocks with no skin (plain-cell fallback) simply skip the effect - their renderers live
/// on the collider objects and must not be scaled.
/// </summary>
public sealed class LandingSquashFx : MonoBehaviour
{
    private const float Damping = 9f;
    private const float Frequency = 24f;
    private const float DoneSeconds = 0.45f; // exp(-9 * 0.45) < 2%: visually settled

    private Transform _skin;
    private Vector3 _baseScale;
    private Vector3 _basePosition;
    private float _worldHalfHeight; // sprite half-height at rest, for the bottom-pin below
    private bool _squashLocalX;     // quarter-turned piece: local X is world-vertical
    private float _amplitude;
    private float _age;

    /// <summary>Rest local position of a skin transform that may be mid-squash (the squash
    /// displaces the skin to pin its bottom edge, so anything deriving skin-space positions
    /// must use the rest pose, never the live one).</summary>
    public static Vector3 RestLocalPosition(Transform skin)
    {
        if (skin == null) return Vector3.zero;
        LandingSquashFx fx = skin.GetComponent<LandingSquashFx>();
        return fx != null ? fx._basePosition : skin.localPosition;
    }

    /// <summary>Squash the block's skin. hardness01 = 0 soft landing .. 1 flick slam.</summary>
    public static void Play(BlockController block, float hardness01)
    {
        Transform skin = block != null ? block.PieceSkinTransform : null;
        if (skin == null) return;

        LandingSquashFx fx = skin.GetComponent<LandingSquashFx>();
        if (fx == null)
        {
            fx = skin.gameObject.AddComponent<LandingSquashFx>();
            // Captured once, before any squash distorts them.
            fx._baseScale = skin.localScale;
            fx._basePosition = skin.localPosition;
        }

        fx._skin = skin;
        fx._squashLocalX = block.IsAtQuarterTurn();
        var renderer = skin.GetComponent<SpriteRenderer>();
        fx._worldHalfHeight = renderer != null ? renderer.bounds.extents.y : 0f;
        fx._amplitude = Mathf.Lerp(0.05f, 0.14f, Mathf.Clamp01(hardness01));
        fx._age = 0f;
        fx.enabled = true;
        fx.Apply();
    }

    private void Update()
    {
        _age += Time.deltaTime;
        if (_age >= DoneSeconds)
        {
            if (_skin != null)
            {
                _skin.localScale = _baseScale;
                _skin.localPosition = _basePosition;
            }
            enabled = false;
            return;
        }

        Apply();
    }

    private void Apply()
    {
        if (_skin == null) { enabled = false; return; }

        // Vertical squash that springs back through a slight stretch; horizontal bulges the
        // opposite way at reduced amplitude (volume roughly preserved, cartoon-style).
        float vertical = FxKit.Elastic(_age, -_amplitude, Damping, Frequency);
        float horizontal = FxKit.Elastic(_age, _amplitude * 0.7f, Damping, Frequency);

        _skin.localScale = _squashLocalX
            ? new Vector3(_baseScale.x * vertical, _baseScale.y * horizontal, _baseScale.z)
            : new Vector3(_baseScale.x * horizontal, _baseScale.y * vertical, _baseScale.z);

        // Pin the sprite's BOTTOM edge: squashing around the sprite centre would lift the
        // visual off the contact point and flash a gap under the block. The drop is applied
        // in world space and mapped through the (possibly rotated, possibly toppling) parent.
        Vector3 worldDrop = Vector3.down * (_worldHalfHeight * (1f - vertical));
        Transform parent = _skin.parent;
        _skin.localPosition = _basePosition +
            (parent != null ? parent.InverseTransformVector(worldDrop) : worldDrop);
    }
}
