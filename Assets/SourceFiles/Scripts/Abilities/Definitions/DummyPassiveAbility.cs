using UnityEngine;

/// <summary>UI/testing dummy: a passive that does nothing. Charges still drive the
/// type badge (0 = PASSIVE, 1 = ONE-TIME), so card variants are testable per rarity.</summary>
[CreateAssetMenu(fileName = "DummyPassive", menuName = "Stacking/Abilities/Dummy Passive")]
public class DummyPassiveAbility : PassiveAbility
{
}
