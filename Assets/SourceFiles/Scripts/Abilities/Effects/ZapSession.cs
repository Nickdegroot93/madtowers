using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Runtime-only driver for the Zap consumable (the Fission/Overdraw session pattern: a free
/// GameObject, static <see cref="IsActive"/>, created via <see cref="Begin"/>). The active piece has
/// already vanished; this owns the shot:
///  1. Withholds bag pieces (<see cref="Spawner.SetAutoSpawnSuspended"/>) so the field holds still.
///  2. Summons a vertical <see cref="ZapBeam"/> the player AIMS left/right (drag the pointer, or arrow
///     keys) while it charges from wide-and-loose to a thin needle over <see cref="ChargeDuration"/>s.
///  3. Each frame it casts straight down the aimed column and stops the beam on the FIRST dynamic
///     landed block it reaches (or the floor if the column is empty).
///  4. On full charge it detonates whatever block the beam is currently on (shared shatter path), or a
///     soft dud for an empty column, then resumes normal spawning.
///
/// Charge runs on SCALED time and is held while paused / during win verification, so a Zap can never
/// fire behind those screens (PHYSICS.md).
/// </summary>
public sealed class ZapSession : MonoBehaviour
{
    private const float ChargeDuration = 3f;
    private const float FireFlashTime = 0.3f;
    private const float TopMargin = 0.6f;     // beam enters from just above the screen top
    private const float InputGrace = 0.15f;   // ignore the pointer briefly so the activation tap can't yank aim
    private const float KeyAimSpeed = 7f;     // columns/sec for arrow-key aiming

    public static bool IsActive { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState() => IsActive = false;

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
    private bool _finishing;

    private ContactFilter2D _filter;
    private readonly RaycastHit2D[] _hits = new RaycastHit2D[24];

    public static void Begin(Spawner spawner, GameObject detonateEffect, float detonateScale, Color color, Color accent)
    {
        if (IsActive || spawner == null) return;

        GameObject go = new GameObject("ZapSession");
        go.AddComponent<ZapSession>().StartSession(spawner, detonateEffect, detonateScale, color, accent);
    }

    private void StartSession(Spawner spawner, GameObject detonateEffect, float detonateScale, Color color, Color accent)
    {
        IsActive = true;
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

        _beam = new GameObject("ZapBeam").AddComponent<ZapBeam>();
        _beam.Configure(color, accent);
        UpdateBeam();
    }

    private void Update()
    {
        if (_finishing) return;

        GameManager gm = GameManager.Instance;
        if (gm != null && gm.isGameOver) { Finish(); return; }

        _age += Time.deltaTime;

        if (!_fired)
        {
            bool frozen = gm != null && (gm.IsGamePaused || LevelRuntimeController.IsVerifyingWin);
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
        for (int i = 0; i < n; i++)
        {
            Collider2D col = _hits[i].collider;
            if (col == null) continue;
            Rigidbody2D body = col.attachedRigidbody;
            if (body == null || body.bodyType != RigidbodyType2D.Dynamic) continue;
            BlockController bc = col.GetComponentInParent<BlockController>();
            if (bc == null || !bc.HasLanded) continue;
            if (_hits[i].distance < bestDist) { bestDist = _hits[i].distance; best = bc; }
        }

        bottomY = best != null && best.TryGetWorldBounds(out Bounds b) ? b.max.y : floorY + 1f;
        return best;
    }

    private void Fire()
    {
        _fired = true;
        _fireAge = 0f;
        if (_beam != null) _beam.FireFlash = 1f;

        if (_target != null)
        {
            AbilityEffects.BurstFromEveryCell(_target, _detonateEffect, _detonateScale);
            AbilityEffects.ImpactPunch(0.05f, 0.12f, 0.16f);
            SfxPlayer.Play("impact_shatter_01", 0.9f, 0.05f);
            AbilityEffects.DestroyBlockWithShatter(_target, new Color(0.5f, 0.8f, 1f, 1f));
        }
        else
        {
            SfxPlayer.Play("impact_soft_01", 0.6f, 0.06f); // empty column - the shot reads as a dud
        }
    }

    private void OnDestroy()
    {
        if (!_finishing) Finish();
    }

    private void Finish()
    {
        if (_finishing) return;
        _finishing = true;

        if (_beam != null) Destroy(_beam.gameObject);
        if (_spawner != null)
        {
            _spawner.SetAutoSpawnSuspended(false);
            _spawner.ResumeSpawning(); // no piece locked during the shot, so kick the next one ourselves
        }

        IsActive = false;
        Destroy(gameObject);
    }
}
