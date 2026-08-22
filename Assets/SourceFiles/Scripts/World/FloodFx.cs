using UnityEngine;

/// <summary>
/// The visual half of the Rising Flood (RisingFloodModifier owns the rules): one huge
/// translucent quad shaded by the Flood shader, drawn IN FRONT of the bricks so the
/// submerged tower reads submerged. The waterline is the only game-relevant edge; this
/// class maps a world-space surface Y onto the shader's rest waterline and drives the
/// shader's time and danger inputs. Purely cosmetic (PHYSICS.md): no colliders, no bodies,
/// blocks under water keep simulating exactly as before - the flood is a rule, not a fluid.
/// </summary>
public sealed class FloodFx : MonoBehaviour
{
    // In front of bricks (0) and their variant-skin overlays (+2..), far behind nothing that
    // matters - screen-space HUD canvases composite above all world sprites regardless.
    private const int SortingOrder = 30;
    private const float QuadWidth = 300f;  // covers any zoom-out; camera x drift is a non-issue
    private const float QuadHeight = 24f;  // visible depth before the deep colour bottoms out
    private const float SurfaceFrac = 0.9f; // shader rest waterline; headroom above it is foam/spray

    private static readonly int PhaseId = Shader.PropertyToID("_Phase");
    private static readonly int AgitPhaseId = Shader.PropertyToID("_AgitPhase");
    private static readonly int DangerId = Shader.PropertyToID("_Danger");

    // Danger SLEWS toward the modifier's raw value, never steps: the raw margin jumps a whole
    // block height every landing (danger is 1 - margin/4m, so one brick = a 0.25 step), and a
    // stepped danger visibly reseated the waves each slam (Nick 2026-08-11: "the water
    // twitches"). Full range in ~0.7s - imperceptible against the flood's slow rise, gone as
    // a per-landing pop.
    private const float DangerSlewPerSecond = 1.5f;
    // Shader's agitation factor (1 + danger * this) - mirrored in Flood.shader's `agit`. The
    // agitated phase must integrate the SAME rate the amplitude scaling uses, or the waves'
    // speed and steepness would disagree about how angry the water is.
    private const float AgitationGain = 1.1f;

    // The danger BED: quiet lapping water that is silent for the whole run except the
    // last DangerBandMeters, where its volume rides the SAME smoothed danger the shader
    // agitation uses - the flood's mood is the countdown, in audio as in visuals (Nick
    // 2026-08-22: "some kind of a danger sound, last 4 meters"). A dedicated source, NOT
    // SfxPlayer.PlayLoop: that slot is shared (the win-verification countdown would
    // steal it and StopLoop would kill the water).
    private const float BedMaxVolume = 0.55f;

    private Material _material;
    private AudioSource _bed;
    private float _phase;
    private float _agitPhase; // advances at the agitated RATE - see Update
    private float _danger;        // smoothed value the shader sees
    private float _targetDanger;  // raw value from the modifier's margin sweep

    public static FloodFx Create(Color shallow, Color deep, Color foam, float centerX)
    {
        var go = new GameObject("FloodFx");
        var fx = go.AddComponent<FloodFx>();

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = RuntimeSprites.Square();
        Shader shader = Resources.Load<Shader>("Flood");
        fx._material = new Material(shader);
        fx._material.SetColor("_ShallowColor", shallow);
        fx._material.SetColor("_DeepColor", deep);
        fx._material.SetColor("_FoamColor", foam);
        fx._material.SetFloat("_TilesX", QuadWidth);
        fx._material.SetFloat("_Seed", Random.value);
        sr.sharedMaterial = fx._material;
        sr.sortingOrder = SortingOrder;

        go.transform.localScale = new Vector3(QuadWidth, QuadHeight, 1f);
        go.transform.position = new Vector3(centerX, -1000f, 0f); // parked until the first SetSurfaceY

        AudioClip lapping = Resources.Load<AudioClip>("Audio/Sfx/flood_danger");
        if (lapping != null)
        {
            fx._bed = go.AddComponent<AudioSource>();
            fx._bed.clip = lapping;
            fx._bed.loop = true;
            fx._bed.playOnAwake = false;
            fx._bed.spatialBlend = 0f;   // 2D, like every SfxPlayer source
            fx._bed.volume = 0f;
        }
        return fx;
    }

    /// <summary>Place the rest waterline at this world Y (the modifier's authoritative surface).</summary>
    public void SetSurfaceY(float worldY)
    {
        Vector3 p = transform.position;
        // uv.y = SurfaceFrac sits (SurfaceFrac - 0.5) * height above the quad's centre.
        p.y = worldY - (SurfaceFrac - 0.5f) * QuadHeight;
        transform.position = p;
    }

    /// <summary>0 = calm chase, 1 = about to swallow the tower. Agitates waves + foam.
    /// Stepped inputs are fine - the visual slews (see DangerSlewPerSecond).</summary>
    public void SetDanger(float danger) => _targetDanger = Mathf.Clamp01(danger);

    private void Update()
    {
        // Scaled time: a pause freezes the flood, visually and mechanically (PHYSICS.md).
        float dt = Time.deltaTime;
        _danger = Mathf.MoveTowards(_danger, _targetDanger, DangerSlewPerSecond * dt);
        _phase += dt;
        // Agitation changes the RATE the phase advances at, never multiplies total elapsed
        // time: the shader's old `t * agit` phases jumped by t * Δagit whenever danger moved
        // (a discontinuity that grew with run time - the "glitchy when I spam blocks" twitch).
        // Integrated here, a danger change only speeds the waves up or down, seamlessly.
        _agitPhase += dt * (1f + _danger * AgitationGain);
        _material.SetFloat(PhaseId, _phase);
        _material.SetFloat(AgitPhaseId, _agitPhase);
        _material.SetFloat(DangerId, _danger);

        if (_bed != null)
        {
            // Audio ignores timeScale, so the bed gates itself: silent whenever the world
            // isn't live play (pause sheet, drafts, win verification, game over) - the
            // flood is frozen then and frozen water doesn't lap. Volume is _danger
            // directly: that value is ALREADY slewed above, and a second smoothing stage
            // made the bed audibly lag the wave agitation it rides (review 2026-08-22).
            // The source plays once and stays playing at volume 0 - Pause/Play churn on
            // the band edge resumed mid-waveform as a click (the AirPocketFx precedent).
            GameManager gm = GameManager.Instance;
            bool live = gm != null && !gm.isGameOver && !gm.IsGamePaused
                        && gm.CurrentPhase == GamePhase.Playing;
            _bed.volume = live ? _danger * BedMaxVolume * SettingsService.EffectiveSfx : 0f;
            if (!_bed.isPlaying) _bed.Play();
        }
    }

    private void OnDestroy()
    {
        if (_material != null) Destroy(_material);
    }
}
