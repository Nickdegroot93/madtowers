using UnityEngine;

/// <summary>Orientation a participating block must have, judged from its collider
/// bounds aspect (robust to 180-degree symmetry and small tilt - never read rotation).</summary>
public enum ComboOrientation
{
    Any,
    Vertical,
    Horizontal
}

/// <summary>Spatial relation between the just-locked block and the existing one.
/// One relation today; extend the enum + ComboDetector.Matches for new patterns.</summary>
public enum ComboRelation
{
    /// <summary>The new block rests directly on top of the other (horizontal overlap + touching).</summary>
    StackedDirectlyOn
}

/// <summary>
/// A block-pattern TRIGGER as an asset, deliberately separate from any effect: the
/// ComboDetector detects each trigger once, and every owned ComboAbility referencing it
/// fires from the same match. The same pattern can mean different things depending on
/// which abilities the player owns - that is the point.
///
/// Matching: both blocks must be drawn from the required BlockDefinition (reference
/// equality via BlockIdentity), both must have the required orientation, and the new
/// block must satisfy the relation against the existing one. Matches are validated at
/// lock, then REVALIDATED after the settle window before firing (no reward for a pair
/// that topples immediately). Blocks that participated in a match are consumed for that
/// trigger: a 3-stack fires once, a 4-stack twice.
/// </summary>
[CreateAssetMenu(fileName = "ComboTrigger", menuName = "Stacking/Abilities/Combo Trigger")]
public class ComboTriggerDefinition : ScriptableObject
{
    [SerializeField] private string displayName = "Combo";
    [Tooltip("Both participating blocks must be drawn from this shape (e.g. the I piece).")]
    [SerializeField] private BlockDefinition requiredDefinition;
    [SerializeField] private ComboOrientation orientation = ComboOrientation.Any;
    [SerializeField] private ComboRelation relation = ComboRelation.StackedDirectlyOn;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public BlockDefinition RequiredDefinition => requiredDefinition;
    public ComboOrientation Orientation => orientation;
    public ComboRelation Relation => relation;
}

/// <summary>What a fired trigger hands to its subscribed abilities. Do not retain the
/// block references - any block can be destroyed (zap, bomb, loss) at any time.</summary>
public readonly struct ComboMatch
{
    public readonly BlockController NewBlock;
    public readonly BlockController BaseBlock;
    public readonly Bounds CombinedBounds;
    /// <summary>Highest cell-center Y of the pair - e.g. a catch-line spawn height.</summary>
    public readonly float TopY;

    public ComboMatch(BlockController newBlock, BlockController baseBlock, Bounds combinedBounds, float topY)
    {
        NewBlock = newBlock;
        BaseBlock = baseBlock;
        CombinedBounds = combinedBounds;
        TopY = topY;
    }
}
