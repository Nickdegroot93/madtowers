using UnityEngine;

/// <summary>
/// The visual half of one Void Zone (VoidZoneModifier owns the rules): a rectangular tear in
/// the sky rendered with the VoidZone shader - dark eye, spiral arms, pulsing accretion rim.
/// Renders BEHIND blocks (order -3) so the falling piece visibly passes in front of it: the
/// void is a place, not a wall. Feed() spikes the shader's hunger while a block is being
/// devoured. Theme-independent fixed look (the Magma rule).
/// </summary>
public sealed class VoidZoneFx : MonoBehaviour
{
    private const int SortingOrder = -3; // above backdrop/lasers/beam, behind bricks (0)
    // The tear-open: the hole grows from a point to full size with its border flaring.
    // Public because the modifier arms a zone only AFTER this window - the danger must never
    // outrun what the player can see, whatever spawnAheadHeight is tuned to.
    public const float SpawnSeconds = 0.7f;

    private static Shader _shader;
    private Material _material;
    private float _hunger;
    private Vector3 _fullScale;
    private float _spawnElapsed;

    public static VoidZoneFx Create(Rect worldRect)
    {
        var go = new GameObject("VoidZoneFx");
        go.transform.position = worldRect.center;
        go.transform.localScale = Vector3.zero; // torn open by the spawn animation in Update

        var fx = go.AddComponent<VoidZoneFx>();
        fx._fullScale = new Vector3(worldRect.width, worldRect.height, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = RuntimeSprites.Square();
        if (_shader == null) _shader = Resources.Load<Shader>("VoidZone");
        fx._material = new Material(_shader);
        fx._material.SetFloat("_Aspect", worldRect.width / Mathf.Max(0.01f, worldRect.height));
        fx._material.SetFloat("_Seed", worldRect.center.x * 3.1f + worldRect.center.y * 7.7f);
        sr.sharedMaterial = fx._material;
        sr.sortingOrder = SortingOrder;
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
        // Tear open: fast growth easing into place, the border burning hot until it settles.
        if (_spawnElapsed < SpawnSeconds)
        {
            _spawnElapsed += Time.deltaTime;
            float u = Mathf.Clamp01(_spawnElapsed / SpawnSeconds);
            float ease = 1f - Mathf.Pow(1f - u, 3f);
            transform.localScale = _fullScale * ease;
            _hunger = Mathf.Max(_hunger, 1f - u);
        }

        if (_hunger > 0f)
        {
            _hunger = Mathf.Max(0f, _hunger - _hungerDecay * Time.deltaTime);
        }
        _material.SetFloat("_Hunger", _hunger);
    }

    private void OnDestroy()
    {
        if (_material != null) Destroy(_material);
    }
}
