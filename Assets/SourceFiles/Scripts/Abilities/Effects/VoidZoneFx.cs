using UnityEngine;

/// <summary>
/// The visual half of one Void Zone (VoidZoneModifier owns the rules): a rectangular tear in
/// the sky rendered with the VoidZone shader: violet cut face, living currents and inward dust.
/// Renders BEHIND blocks (order -3) so the falling piece visibly passes in front of it: the
/// void is a place, not a wall. Feed() spikes the shader's hunger while a block is being
/// devoured. Theme-independent fixed look (the Magma rule).
/// </summary>
public sealed class VoidZoneFx : MonoBehaviour
{
    private const int SortingOrder = -3; // above backdrop/lasers/beam, behind bricks (0)
    // The tear-open: a tightening seam snaps apart into the full rule footprint.
    // Public because the modifier arms a zone only AFTER this window - the danger must never
    // outrun what the player can see, whatever spawnAheadHeight is tuned to.
    public const float SpawnSeconds = 0.7f;

    private static Shader _shader;
    private Material _material;
    private float _hunger;
    private Vector3 _fullScale;
    private float _spawnElapsed;
    private float _phase, _dustPhase;
    private SpriteRenderer[] _dust;
    private VoidScarFx _scar;

    public static VoidZoneFx Create(Rect worldRect)
    {
        var go = new GameObject("VoidZoneFx");
        go.transform.position = worldRect.center;
        go.transform.localScale = new Vector3(worldRect.width + .5f, worldRect.height + .5f, 1f); // torn open by the spawn animation in Update

        var fx = go.AddComponent<VoidZoneFx>();
        fx._fullScale = new Vector3(worldRect.width, worldRect.height, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = RuntimeSprites.Square();
        if (_shader == null) _shader = Resources.Load<Shader>("VoidZone");
        fx._material = new Material(_shader);
        fx._material.SetTexture("_NoiseTex", SurfaceSceneFx.Acquire(flood: false));
        fx._material.SetVector("_ZoneSize", new Vector4(worldRect.width, worldRect.height, 0, 0));
        fx._material.SetVector("_QuadSize", new Vector4(worldRect.width + .5f, worldRect.height + .5f, 0, 0));
        fx._material.SetFloat("_Open", 0f);
        fx._material.SetFloat("_Aspect", worldRect.width / Mathf.Max(0.01f, worldRect.height));
        fx._material.SetFloat("_Seed", worldRect.center.x * 3.1f + worldRect.center.y * 7.7f);
        sr.sharedMaterial = fx._material;
        sr.sortingOrder = SortingOrder;
        // Preallocate the closing remnant now: teardown never creates scene objects.
        fx._scar = VoidScarFx.Prepare(worldRect, fx._material, sr.sprite, SortingOrder);
        fx._dust = new SpriteRenderer[8];
        for (int i = 0; i < fx._dust.Length; i++)
        {
            var mote = new GameObject("Inward dust");
            mote.transform.SetParent(go.transform, false);
            var dust = mote.AddComponent<SpriteRenderer>();
            dust.sprite = RuntimeSprites.Square();
            dust.sortingOrder = SortingOrder + 1;
            fx._dust[i] = dust;
        }
        return fx;
    }

    /// <summary>Spike the feeding pulse for a moment - the hole visibly enjoys its meal.</summary>
    public void Feed(float seconds)
    {
        _hunger = Mathf.Max(_hunger, 1f);
        _hungerDecay = 1f / Mathf.Max(0.2f, seconds);
    }

    private float _hungerDecay = 1f;

    private void Update()
    {
        // Tighten, snap, settle within the original arming window.
        if (_spawnElapsed < SpawnSeconds)
        {
            _spawnElapsed += Time.deltaTime;
            float u = Mathf.Clamp01(_spawnElapsed / SpawnSeconds);
            // Tighten for .25 s, then unzip and settle; still fully open before the
            // ORIGINAL .7-second arming point. The modifier never reads the aperture.
            float snap = Mathf.Clamp01((u - .36f) / .50f);
            _material.SetFloat("_Open", 1f - Mathf.Pow(1f - snap, 3f));
            _material.SetFloat("_Anticipation", Mathf.Clamp01(u / .36f));
            _hunger = Mathf.Max(_hunger, 1f - u);
        }

        if (_hunger > 0f)
        {
            _hunger = Mathf.Max(0f, _hunger - _hungerDecay * Time.deltaTime);
        }
        _phase += Time.deltaTime;
        _dustPhase += Time.deltaTime * (.075f + _hunger * .24f);
        _material.SetFloat("_Phase", _phase);
        _material.SetFloat("_Hunger", _hunger);
        for (int i = 0; i < _dust.Length; i++)
        {
            float u = Mathf.Repeat(_dustPhase + i * .137f, 1f);
            float angle = i * 2.39996f + u * .7f;
            float pull = u * u;
            var dust = _dust[i];
            dust.transform.localPosition = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * (.69f * (1f - pull));
            float size = (.055f + (i % 3) * .02f) * (1f - pull);
            dust.transform.localScale = new Vector3(size / (_fullScale.x + .5f), size / (_fullScale.y + .5f), 1f);
            dust.transform.localRotation = Quaternion.Euler(0, 0, angle * Mathf.Rad2Deg);
            dust.color = new Color(.45f, .51f, .53f, Mathf.Sin(u * Mathf.PI) * (.14f + _hunger * .42f));
        }
    }

    private void OnDestroy()
    {
        // The prepared remnant owns disposal after a close. During a scene unload it
        // is destroyed with the scene, so no scar can leak into the next level.
        if (_scar != null) _scar.Close(_phase);
        // The scar owns the material and capture reference from Prepare onward. If it
        // was already destroyed during scene unload, both have already been released.
    }
}
