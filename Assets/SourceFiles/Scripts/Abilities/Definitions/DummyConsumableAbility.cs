using UnityEngine;

/// <summary>UI/testing dummy: a consumable that does nothing on activation (it still
/// occupies a slot and exercises the swap dialog).</summary>
[CreateAssetMenu(fileName = "DummyConsumable", menuName = "Stacking/Abilities/Dummy Consumable")]
public class DummyConsumableAbility : ConsumableAbility
{
    public override void Activate(AbilityContext context)
    {
        SfxPlayer.Play("pop_01", 0.6f, 0.05f);
    }
}
