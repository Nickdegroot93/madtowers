using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Runtime-only driver for the Zap consumable. The active piece has already vanished; this owns
/// the shot:
///  1. Withholds bag pieces (<see cref="Spawner.SetAutoSpawnSuspended"/>) so the field holds still.
///  2. Summons a vertical <see cref="ZapBeam"/> the player AIMS left/right (drag the pointer, or arrow
///     keys) while it charges from wide-and-loose to a thin needle over <see cref="ChargeDuration"/>s.
///  3. Each frame it casts straight down the aimed column and stops the beam on the FIRST
///     destructible landed block it reaches (or the floor if the column is empty).
///  4. On full charge it detonates whatever block the beam is currently on (shared shatter path), or a
///     soft dud for an empty column, then resumes normal spawning.
///
/// Charge runs on SCALED time and is held while paused / during win verification, so a Zap can never
/// fire behind those screens (PHYSICS.md).
/// </summary>
public sealed class ZapSession : AbilitySessionBase
{
    private const float ChargeDuration = 3f;
    private const float FireFlashTime = 0.3f;
    private const float TopMargin = 0.6f;     // beam enters from just above the screen top
    private const float InputGrace = 0.15f;   // ignore the pointer briefly so the activation tap can't yank aim
    private const float KeyAimSpeed = 7f;     // columns/sec for arrow-key aiming

    public static bool IsActive => IsSessionActive<ZapSession>();
    protected override bool SeizesActivePiece => true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState() => ResetSessionState<ZapSession>();

    private Spawner _spawner;
    private ZapBeam _beam;
    private GameObject _detonateEffect;
    private float _detonateScale;
    private float _spacing = 1f;
    private float _beamX;
    private BlockController _target;
    private float _charge;
    private float _age;
    private bool _fired;
    private float _fireAge;

    private ContactFilter2D _filter;
    private readonly RaycastHit2D[] _hits = new RaycastHit2D[24];

    private Vector2 _surfaceNormal = Vector2.up; // of the centerline contact - tilts the tip visuals

    public static void Begin(Spawner spawner, GameObject detonateEffect, float detonateScale, Color color, Color accent)
    {
        if (IsActive || spawner == null) return;

        GameObject go = new GameObject("ZapSession");
        go.AddComponent<ZapSession>().StartSession(spawner, detonateEffect, detonateScale, color, accent);
    }

    private void StartSession(Spawner spawner, GameObject detonateEffect, float detonateScale, Color color, Color accent)
    {
        if (!BeginSessionLifecycle())
        {
            Destroy(gameObject);
            return;
        }
        _spawner = spawner;
        _detonateEffect = detonateEffect;
        _detonateScale = detonateScale;
        _filter = new ContactFilter2D { useTriggers = false, useLayerMask = false };

        BlockController active = BlockController.ActiveControlled;
        if (active == null) { Finish(); return; }
        _beamX = active.transform.position.x; // start aimed at the column the piece was in
        _spacing = active.GridSpacing > 0.01f ? active.GridSpacing : 1f;

        _spawner.SetAutoSpawnSuspended(true);
        if (!_spawner.DestroyActivePieceWithoutLock())
        {
            _spawner.SetAutoSpawnSuspended(false);
            Finish();
            return;
        }

        // The charge sound is authored at EXACTLY ChargeDuration (3.0 s, ElevenLabs) - it builds
        // to the verge of firing in sync with the beam converging. No pitch jitter: jitter would
        // desync its length from the visual charge.
        SfxPlayer.Play("zap_charge", 0.75f, 0f);

        _beam = new GameObject("ZapBeam").AddComponent<ZapBeam>();
        _beam.Configure(color, accent);
        UpdateBeam();
    }

    private void Update()
    {
        if (IsFinishing) return;

        GameManager gm = GameManager.Instance;
        if (gm != null && gm.isGameOver) { Finish(); return; }

        _age += Time.deltaTime;

        if (!_fired)
        {
            bool frozen = gm != null && (gm.IsGamePaused || gm.CurrentPhase != GamePhase.Playing);
            if (!frozen)
            {
                _charge = Mathf.Min(ChargeDuration, _charge + Time.deltaTime);
                _beamX = ReadAim(_beamX);
            }
            UpdateBeam();
            if (_charge >= ChargeDuration) Fire();
        }
        else
        {
            _fireAge += Time.deltaTime;
            if (_beam != null) _beam.FireFlash = Mathf.Clamp01(1f - _fireAge / FireFlashTime);
            if (_fireAge >= FireFlashTime) Finish();
        }
    }

    // Pointer drag (mouse or touch, via the new Input System) follows the column; arrow keys nudge it.
    // Clamped to the visible width. A short grace ignores the pointer so the activation tap can't yank it.
    private float ReadAim(float x)
    {
        Camera cam = Camera.main;
        if (cam == null) return x;

        Pointer pointer = Pointer.current;
        if (_age > InputGrace && pointer != null && pointer.press.isPressed)
        {
            Vector2 sp = pointer.position.ReadValue();
            x = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, Mathf.Abs(cam.transform.position.z))).x;
        }

        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            float dir = (kb.leftArrowKey.isPressed ? -1f : 0f) + (kb.rightArrowKey.isPressed ? 1f : 0f);
            if (dir != 0f) x += dir * _spacing * KeyAimSpeed * Time.deltaTime;
        }

        float half = cam.orthographicSize * cam.aspect - _spacing * 0.3f;
        return Mathf.Clamp(x, cam.transform.position.x - half, cam.transform.position.x + half);
    }

    private void UpdateBeam()
    {
        if (_beam == null) return;
        Camera cam = Camera.main;
        float topY = cam != null ? cam.transform.position.y + cam.orthographicSize + TopMargin : 12f;
        float floorY = cam != null ? cam.transform.position.y - cam.orthographicSize - 1f : -12f;

        _target = CastColumn(_beamX, topY, floorY, out float bottomY);

        _beam.BeamX = _beamX;
        _beam.TopY = topY;
        _beam.BottomY = bottomY;
        _beam.Charge = _charge / ChargeDuration;
        _beam.SurfaceAngleDeg = Vector2.SignedAngle(Vector2.up, _surfaceNormal);
    }


    // First dynamic landed block straight down the aimed column; out the Y to stop the beam at (its top,
    // or the floor when the column is empty).
    private BlockController CastColumn(float x, float topY, float floorY, out float bottomY)
    {
        Vector2 origin = new Vector2(x, topY);
        Vector2 size = new Vector2(Mathf.Max(0.05f, _spacing * 0.3f), 0.04f);
        int n = Physics2D.BoxCast(origin, size, 0f, Vector2.down, _filter, _hits, topY - floorY);

        BlockController best = null;
        float bestDist = float.MaxValue;
        float bestPointY = 0f;
        for (int i = 0; i < n; i++)
        {
            Collider2D col = _hits[i].collider;
            if (col == null) continue;
            BlockController bc = col.GetComponentInParent<BlockController>();
            if (bc == null || !bc.HasLanded || bc.IsFrozenInPlace) continue;
            if (_hits[i].distance < bestDist)
            {
                bestDist = _hits[i].distance;
                best = bc;
                bestPointY = _hits[i].point.y;
            }
        }

        // Endpoint = where the beam's CENTERLINE meets the target block, never bounds or the
        // box's first graze: the cell AABB top is its highest corner, and even the box
        // contact point can be a corner-graze while the beam centre still hangs over the
        // tilted face's slope - both left the tip floating in air (Nick 2026-08-30). A thin
        // ray down the exact centre, filtered to the chosen block, finds the true surface;
        // when the centre misses entirely (the beam only clips the block's edge) the box
        // contact is the honest fallback.
        _surfaceNormal = Vector2.up;
        if (best != null)
        {
            bottomY = bestPointY;
            int rays = Physics2D.Raycast(origin, Vector2.down, _filter, _hits, topY - floorY);
            for (int i = 0; i < rays; i++)
            {
                Collider2D col = _hits[i].collider;
                if (col == null || col.GetComponentInParent<BlockController>() != best) continue;
                bottomY = Mathf.Min(bottomY, _hits[i].point.y);
                _surfaceNormal = _hits[i].normal; // tilts the tip glow/flare onto the face
                break; // hits arrive nearest-first; the first on the target is its surface
            }
        }
        else
        {
            bottomY = floorY + 1f;
        }
        return best;
    }

    private void Fire()
    {
        _fired = true;
        _fireAge = 0f;
        if (_beam != null) _beam.FireFlash = 1f;

        if (_target != null)
        {
            ImpactFx.BurstFromEveryCell(_target, _detonateEffect, _detonateScale);
            ImpactFx.ImpactPunch(0.05f, 0.12f, 0.16f);
            SfxPlayer.Play("zap_fire", 0.9f, 0.03f);
            ImpactFx.DestroyBlockWithShatter(_target, new Color(0.5f, 0.8f, 1f, 1f), sfx: null);
        }
        else
        {
            SfxPlayer.Play("zap_dud", 0.65f, 0.05f); // empty column - the shot reads as a dud
        }
    }

    public override void CancelSession() => Finish(destroySelf: !IsDestroying);

    private void Finish(bool destroySelf = true)
    {
        if (!BeginFinish()) return;

        if (_beam != null) Destroy(_beam.gameObject);
        // No piece locked during the shot; clearing the hold republishes spawn availability, so the
        // next bag piece spawns on its own (ActiveControlled is null after the active piece was
        // destroyed, so SpawnNextBlock proceeds).
        if (_spawner != null) _spawner.SetAutoSpawnSuspended(false);

        CompleteSessionLifecycle(destroySelf);
    }
}
