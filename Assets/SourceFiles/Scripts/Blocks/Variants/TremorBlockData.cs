using UnityEngine;

/// <summary>
/// A brick that jolts the whole tower the moment it lands - survive the shake. It carries its own fixed,
/// theme-independent look - warm ochre fault-stone that constantly buzzes with held seismic energy and
/// discharges a shockwave on landing (TremorBlockSkin) - so it reads as "this one shakes" in any chapter,
/// even while still falling.
/// </summary>
[CreateAssetMenu(fileName = "TremorBlockData", menuName = "Stacking/Blocks/Tremor Block Variant")]
public class TremorBlockData : BlockData
{
    [Header("Quake")]
    [Tooltip("Peak velocity kick (u/s) at the epicenter. The whole tower shakes for a short burst; a " +
             "badly-placed block topples, a well-stacked one rides it out. This is the main 'annoying' dial.")]
    [Range(0f, 8f)]
    [SerializeField] private float shakeStrength = 1.5f;
    [Tooltip("How long the shake burst lasts.")]
    [Range(0.1f, 1.5f)]
    [SerializeField] private float shakeDuration = 0.5f;
    [Tooltip("World-unit reach of the epicenter falloff - blocks within this shake near full strength, " +
             "farther ones taper toward a floor (still felt across the tower).")]
    [Range(2f, 20f)]
    [SerializeField] private float shakeRadius = 8f;

    [Header("Landing FX")]
    [Tooltip("A one-shot ground-dust puff played at the brick's base on landing (e.g. CFXR2 Ground Hit).")]
    [SerializeField] private GameObject quakeDustEffect;
    [Tooltip("World-unit size of the dust puff.")]
    [Min(0.1f)]
    [SerializeField] private float dustScale = 1.2f;

    // Build the fixed fault-stone look as soon as the variant is applied (runs after the chapter skin).
    // Get-or-add so a re-apply can't add a second skin. (Base OnApplied is empty; see BLOCKVARIANTS.md.)
    public override void OnApplied(BlockController block)
    {
        if (block == null) return;
        if (!block.TryGetComponent(out TremorBlockSkin skin))
            skin = block.gameObject.AddComponent<TremorBlockSkin>();
        skin.Apply();
    }

    public override void OnLocked(BlockController block)
    {
        if (block == null) return;

        // The seismic discharge: ring + flash + camera kick on the brick, and a dust kick at its base.
        if (block.TryGetComponent(out TremorBlockSkin skin)) skin.PlayQuake();
        if (quakeDustEffect != null && block.TryGetWorldBounds(out Bounds bounds))
            Vfx.Spawn(quakeDustEffect, new Vector3(bounds.center.x, bounds.min.y, 0f), dustScale);

        // The quake itself: a sustained shake burst radiating from this brick (see TremorBlockBehaviour).
        block.gameObject.AddComponent<TremorBlockBehaviour>()
            .Arm(block.transform.position, shakeStrength, shakeDuration, shakeRadius);
    }
}
