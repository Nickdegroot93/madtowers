using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The nudge dash's body language (Tricky Towers style): the piece's SKIN dips its leading
/// edge, lags a beat behind the column it just jumped to and snaps in with an elastic
/// settle, while two or three fading ghost copies smear the path it took. Animates ONLY the
/// collider-less PieceSkin child (same contract as LandingSquashFx) - the body itself moves
/// the exact grid column in one physics step, so nothing here can change where the piece
/// lands (PHYSICS.md I1). A landing squash cancels a running lunge: touchdown owns the skin.
/// </summary>
public sealed class NudgeLungeFx : MonoBehaviour
{
    // Tilt: 0 at the tap, peaks ~8 degrees leading-edge-down a few frames in, springs back.
    private const float TiltPeakDegrees = 8f;
    private const float TiltDamping = 10f;
    private const float TiltFrequency = 18f;

    // Lag: the skin starts a full column behind (still drawn at the old column the frame the
    // body jumps), overshoots the new column by ~10% and settles.
    private const float LagDamping = 22f;
    private const float LagFrequency = 16f;

    private const float DoneSeconds = 0.35f; // exp(-10 * 0.35) ~ 3%: visually settled

    // exp(-d t) sin(w t) peaks at t = atan(w/d)/w; scale so that peak reads exactly TiltPeakDegrees.
    private static readonly float TiltNormalizer = 1f / PeakOfDampedSine(TiltDamping, TiltFrequency);

    private static float PeakOfDampedSine(float damping, float frequency)
    {
        float t = Mathf.Atan(frequency / damping) / frequency;
        return Mathf.Exp(-damping * t) * Mathf.Sin(frequency * t);
    }

    // Ghost trail: the "blur". Copies of the skin sprite along the jump, fading fast.
    private const int GhostCount = 3;
    private const float GhostLifetime = 0.16f;
    private const float GhostStartAlpha = 0.42f;

    private Transform _skin;
    private Quaternion _baseRotation;
    private Vector3 _basePosition;
    private Vector3 _lagWorld;      // world offset toward the OLD column (full lag at t = 0)
    private float _startBodyX;      // body X when the nudge was requested (before the grid step)
    private float _armThresholdX;   // the body must have moved at least this far to start
    private int _direction;
    private float _age;
    private bool _armed;            // false until the body has actually jumped columns
    private SpriteRenderer _skinRenderer;

    /// <summary>Play on a successful nudge. movedDirection: -1 left, +1 right (the direction
    /// the piece really dashes; Vortex inverts the input upstream).</summary>
    public static void Play(BlockController block, int movedDirection)
    {
        Transform skin = block != null ? block.PieceSkinTransform : null;
        if (skin == null || movedDirection == 0) return;

        NudgeLungeFx fx = skin.GetComponent<NudgeLungeFx>();
        if (fx == null)
        {
            fx = skin.gameObject.AddComponent<NudgeLungeFx>();
            fx._baseRotation = skin.localRotation;
        }
        else if (fx.enabled)
        {
            fx.Restore(); // a second dash mid-settle restarts from the rest pose
        }

        fx._skin = skin;
        fx._skinRenderer = skin.GetComponent<SpriteRenderer>();
        // The squash may be displacing the skin right now; its rest pose is the truth.
        fx._basePosition = LandingSquashFx.RestLocalPosition(skin);
        fx._direction = movedDirection > 0 ? 1 : -1;
        float spacing = Mathf.Max(0.05f, block.GridSpacing);
        fx._lagWorld = Vector3.left * (fx._direction * spacing);
        fx._startBodyX = block.transform.position.x;
        fx._armThresholdX = spacing * 0.4f;
        fx._age = 0f;
        fx._armed = false;
        fx.enabled = true;
    }

    /// <summary>Stop a running lunge and restore the skin's rest pose (landing takes over).</summary>
    public static void Cancel(Transform skin)
    {
        if (skin == null) return;
        NudgeLungeFx fx = skin.GetComponent<NudgeLungeFx>();
        if (fx == null || !fx.enabled) return;
        fx.Restore();
        fx.enabled = false;
    }

    private void LateUpdate()
    {
        if (_skin == null || _skin.parent == null) { enabled = false; return; }

        // The nudge is requested in Update; the body jumps on the next physics step. Until the
        // body has really moved, hold still - lagging early would show the skin flinching
        // BACKWARD for a frame. If the step never materialises, give up quietly.
        if (!_armed)
        {
            if (Mathf.Abs(_skin.parent.position.x - _startBodyX) < _armThresholdX)
            {
                _age += Time.deltaTime;
                if (_age > 0.2f) { Restore(); enabled = false; }
                return;
            }
            _armed = true;
            _age = 0f;
            SpawnGhosts();
        }

        _age += Time.deltaTime;
        if (_age >= DoneSeconds)
        {
            Restore();
            enabled = false;
            return;
        }

        float decay = Mathf.Exp(-TiltDamping * _age);
        // Leading edge down: dashing right = clockwise = negative Z. Z rotations compose the
        // same in 2D whatever quarter turn the parent sits at, so local == world tilt here.
        float tilt = -_direction * TiltPeakDegrees * TiltNormalizer * decay * Mathf.Sin(TiltFrequency * _age);
        _skin.localRotation = _baseRotation * Quaternion.Euler(0f, 0f, tilt);

        float lag = Mathf.Exp(-LagDamping * _age) * Mathf.Cos(LagFrequency * _age);
        Vector3 worldOffset = _lagWorld * lag;
        _skin.localPosition = _basePosition + _skin.parent.InverseTransformVector(worldOffset);
    }

    private void Restore()
    {
        if (_skin == null) return;
        _skin.localRotation = _baseRotation;
        _skin.localPosition = _basePosition;
    }

    // ---- ghosts ---------------------------------------------------------------------------

    private void SpawnGhosts()
    {
        if (_skinRenderer == null || _skinRenderer.sprite == null) return;

        // World pose of the skin at the OLD column: the parent has already jumped, the skin
        // has no lag applied yet (Restore/base pose), so shift straight back by one column.
        Vector3 newWorld = _skin.parent.TransformPoint(_basePosition);
        Vector3 oldWorld = newWorld + _lagWorld;
        Quaternion worldRotation = _skin.parent.rotation * _baseRotation;

        for (int i = 0; i < GhostCount; i++)
        {
            float t = (i + 0.5f) / GhostCount;               // 1/6, 1/2, 5/6 along the jump
            float alpha = GhostStartAlpha * (0.45f + 0.55f * t); // denser nearer the piece
            NudgeGhost.Spawn(_skinRenderer, Vector3.Lerp(oldWorld, newWorld, t), worldRotation,
                _skin.lossyScale, alpha, GhostLifetime);
        }
    }

    /// <summary>One pooled, fading copy of the skin sprite. World-space (never a child of the
    /// piece), so it stays where the piece WAS.</summary>
    private sealed class NudgeGhost : MonoBehaviour
    {
        private static readonly Stack<NudgeGhost> Pool = new Stack<NudgeGhost>();

        private SpriteRenderer _renderer;
        private Color _color;
        private float _lifetime;
        private float _age;

        public static void Spawn(SpriteRenderer source, Vector3 position, Quaternion rotation,
            Vector3 scale, float alpha, float lifetime)
        {
            NudgeGhost ghost = Get();
            Transform t = ghost.transform;
            t.SetPositionAndRotation(position, rotation);
            t.localScale = scale;

            ghost._renderer.sprite = source.sprite;
            ghost._renderer.sortingLayerID = source.sortingLayerID;
            ghost._renderer.sortingOrder = source.sortingOrder - 1; // just under the piece
            ghost._renderer.flipX = source.flipX;
            ghost._renderer.flipY = source.flipY;
            Color c = source.color;
            ghost._color = new Color(c.r, c.g, c.b, alpha);
            ghost._renderer.color = ghost._color;
            ghost._lifetime = Mathf.Max(0.01f, lifetime);
            ghost._age = 0f;
        }

        private static NudgeGhost Get()
        {
            while (Pool.Count > 0)
            {
                NudgeGhost pooled = Pool.Pop();
                if (pooled == null) continue;
                pooled.gameObject.SetActive(true);
                return pooled;
            }

            GameObject go = new GameObject("NudgeGhost");
            NudgeGhost ghost = go.AddComponent<NudgeGhost>();
            ghost._renderer = go.AddComponent<SpriteRenderer>();
            return ghost;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float fade = 1f - _age / _lifetime;
            if (fade <= 0f)
            {
                gameObject.SetActive(false);
                Pool.Push(this);
                return;
            }
            Color c = _color;
            c.a = _color.a * fade * fade; // quick drop-off: a smear, not an afterimage
            _renderer.color = c;
        }
    }
}
