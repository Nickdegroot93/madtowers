using UnityEngine;

/// <summary>
/// Generic combo ability: when its trigger pattern lands, apply a status effect (a
/// reusable game state - Overdrive, immunity, slow-fall...). Most combo abilities that
/// "enter a state for N seconds" are just assets of this class with different trigger
/// and status references - no new code.
/// </summary>
[CreateAssetMenu(fileName = "StatusCombo", menuName = "Stacking/Abilities/Status On Combo")]
public class StatusComboAbility : ComboAbility
{
    [Tooltip("The game state this combo applies (duration/magnitude live on the status asset).")]
    [SerializeField] private StatusEffectDefinition status;

    public override void OnComboFired(AbilityContext context, ComboMatch match)
    {
        if (status == null || context.Status == null) return;

        context.Status.Apply(status);
        TowerCameraController.Impact(0.1f, 0.18f);
        SfxPlayer.Play("pop_01", 0.7f, 0.05f);
    }
}
