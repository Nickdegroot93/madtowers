using UnityEngine;

/// <summary>
/// Ward's pending strike, riding on the hazard brick it is going to defuse.
///
/// Ward used to defuse a hazard the instant it spawned, which meant the player never saw a hazard
/// and never saw the ward work - the ability was invisible by construction. Now the hazard falls
/// LOOKING like a hazard for a beat, then the ward visibly strikes it down to a plain brick and
/// spends its charge (the armed rail burns the icon at that moment).
///
/// The beat is safe by construction: a hazard is inert while it falls - its behaviour is attached
/// by BlockData.OnLocked - so the only thing the strike must beat is the lock. It resolves on
/// whichever comes first:
///   - the timer (scaled time, so a pause freezes the beat like everything else),
///   - the piece's BeforeLock (a flick can land a brick in less than the delay),
///   - never, if the brick is destroyed first (culled off-screen, zapped, game over) - the strike
///     dies with it and no charge is spent. Ward holds the component itself as its claim, so a
///     vanished strike frees the ward with no teardown callback to miss.
/// One strike per Ward at a time, so two hazards in the air during an Overdraw/Fission sequence
/// cannot both claim the same charge.
/// </summary>
public class WardStrike : MonoBehaviour
{
    private BlockController _block;
    private WardPowerUp _ward;
    private AbilityContext _context;
    private GameObject _effect;
    private float _effectScale;
    private float _strikeTime;
    private bool _resolved;

    /// <summary>Arm a strike on <paramref name="block"/> and hand it back as the ward's claim. The
    /// caller has already checked the block CAN be defused (AbilityEffects.CanNeutralizeToPlain).</summary>
    public static WardStrike Arm(BlockController block, WardPowerUp ward, AbilityContext context,
        float delaySeconds, GameObject effect, float effectScale)
    {
        if (block == null || ward == null) return null;

        WardStrike strike = block.gameObject.AddComponent<WardStrike>();
        strike._block = block;
        strike._ward = ward;
        strike._context = context;
        strike._effect = effect;
        strike._effectScale = effectScale;
        strike._strikeTime = Time.time + Mathf.Max(0f, delaySeconds);
        block.BeforeLock += strike.HandleBeforeLock;
        return strike;
    }

    private void Update()
    {
        if (_resolved || Time.time < _strikeTime) return;
        Resolve(withPunch: true);
    }

    // A flick beat the timer: strike now, while the piece is still in-air state and the variant
    // swap still applies to THIS brick. No hit-stop on this path - the landing itself fires one in
    // the same frame, and two stacked freezes read as a stutter rather than as weight.
    private void HandleBeforeLock(BlockController block) => Resolve(withPunch: false);

    private void OnDestroy()
    {
        // The brick left play before the ward could strike: nothing defused, so nothing paid. Ward's
        // claim is this component, so its own destruction is what frees the ward - no callback here.
        if (_block != null) _block.BeforeLock -= HandleBeforeLock;
    }

    private void Resolve(bool withPunch)
    {
        if (_resolved) return;
        _resolved = true;
        if (_block != null) _block.BeforeLock -= HandleBeforeLock;

        bool defused = AbilityEffects.NeutralizeToPlain(_context, _block);

        // Free the claim BEFORE paying: spending the last charge destroys the ward's runtime clone,
        // and a stacked second charge must be able to claim the next hazard right away (this
        // component's own destruction is deferred to the end of the frame).
        if (_ward != null) _ward.ClearPendingStrike(this);

        if (defused)
        {
            ImpactFx.BurstFromEveryCell(_block, _effect, _effectScale); // null-safe; degrades to the punch
            if (withPunch) ImpactFx.ImpactPunch(0.03f, 0.06f, 0.12f);
            SfxPlayer.Play("ward_absorb", 0.75f, 0.04f);
            if (_context != null && _context.Runtime != null) _context.Runtime.SpendCharge(_ward);
        }

        Destroy(this);
    }
}
