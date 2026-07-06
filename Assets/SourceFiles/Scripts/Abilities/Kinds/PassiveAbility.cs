using UnityEngine;

/// <summary>
/// Always-on ability. Two dials shape its lifecycle:
/// - Stacks: picking it again calls OnStackAdded (definitions decide what a stack
///   means - bigger chance, more blocks, etc.). Unique/maxStacks live on the base.
/// - Charges: 0 = permanent; N = the ability is consumed after its handlers report
///   "triggered" N times (a one-shot passive is simply charges = 1).
///
/// Event handlers return true to mean "my effect fired" - that's what consumes a
/// charge. TryInterceptLoss is special: it SHORT-CIRCUITS (first armed ability to
/// return true handles the loss; later ones stay armed), while the notification
/// hooks fan out to every owned passive. Ordering is acquisition order; the full
/// rules table lives in ABILITIES.md.
///
/// AbilityRuntime recomputes the fall-speed multiplier after acquisition, stacking,
/// life events and spawns - GetFallSpeedFactor is queried then, never per frame.
/// </summary>
public abstract class PassiveAbility : AbilityDefinition
{
    [Header("Lifecycle")]
    [Tooltip("0 = permanent. N = consumed after the ability's handlers report having triggered N times (1 = classic one-shot passive).")]
    [Min(0)]
    [SerializeField] private int charges;

    public int Charges => charges;

    /// <summary>Called once when first acquired (stacks == 1).</summary>
    public virtual void OnAcquired(AbilityContext context, int stacks) { }

    /// <summary>Called on each additional stack (stacks is the NEW total).</summary>
    public virtual void OnStackAdded(AbilityContext context, int stacks) { }

    /// <summary>Called when the ability leaves the inventory (charges exhausted).</summary>
    public virtual void OnRemoved(AbilityContext context) { }

    // ---- Notification hooks (fan-out; return true = triggered, consumes a charge) ----

    public virtual bool OnLifeLost(AbilityContext context) => false;

    public virtual bool OnBlockSpawned(AbilityContext context, BlockController block, BlockData data) => false;

    // ---- Intercepting hook (short-circuit; return true = loss handled, charge consumed) ----

    /// <summary>
    /// Higher values get first refusal for loss interception. Most abilities should leave this
    /// at 0 and resolve in acquisition order; use it only when presentation implies physical
    /// ordering, such as a catch line visibly above a destroy line.
    /// </summary>
    public virtual int LossInterceptPriority => 0;

    /// <summary>
    /// Raises the screen-bottom sweep line while this passive is armed. This keeps visual catch
    /// beams and their gameplay trigger in sync; destructive/default passives leave it at 0.
    /// </summary>
    public virtual float LossInterceptLineOffset => 0f;

    /// <summary>
    /// True while this passive renders a visible loss-line beam (Sacrifice/Hardline). While any
    /// armed passive reports true, LossZone triggers landed-block interception at the raised,
    /// always-on-screen LossZone.InterceptLineY instead of the (possibly off-screen) charge
    /// line, so the save happens at the laser the player can see. Invisible interceptors
    /// (e.g. Rebound) leave this false and keep the normal deep line.
    /// </summary>
    public virtual bool ShowsLossInterceptLine => false;

    /// <summary>
    /// A LANDED block is about to be lost off the bottom of the screen. Return true to
    /// handle it instead of charging a life - the handler MUST leave the block non-lost
    /// (freeze it or destroy it), or the cull sweep re-fires within 100 ms.
    /// </summary>
    public virtual bool TryInterceptLoss(AbilityContext context, BlockController block) => false;

    // ---- Stat contributions (pulled on recompute, never per frame) ----

    /// <summary>Multiplier this passive currently contributes to the spawn-time fall speed (1 = none).</summary>
    public virtual float GetFallSpeedFactor(AbilityContext context, int stacks) => 1f;
}
