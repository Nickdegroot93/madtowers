using UnityEngine;

/// <summary>
/// Freeze, a CONSUMABLE: held in a slot and fired when the player chooses, not on pick.
/// Every block placed so far freezes exactly where it stands, however it stands; the tower
/// built from here on remains live physics as usual.
/// </summary>
[CreateAssetMenu(fileName = "Freeze", menuName = "Stacking/Abilities/Freeze")]
public class FreezePowerUp : ConsumableAbility
{
    public override bool CanActivate(AbilityContext context)
    {
        // Pointless with nothing standing; the slot dims until there is a tower.
        var blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] != null && blocks[i].HasLanded) return true;
        }
        return false;
    }

    // The ice overlay crawls in over this long; the physics lock lands a beat later so a settling
    // block keeps moving until the ice "grabs" it.
    private const float VisualSeconds = 1f;
    private const float PhysicsLockDelaySeconds = 0.25f;

    public override void Activate(AbilityContext context)
    {
        // TODO: placeholder SFX - swap "pop_01" for a ~1s ice/crackle clip once we have one.
        SfxPlayer.Play("pop_01", 0.7f, 0.05f);

        var blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block == null || !block.HasLanded) continue;

            block.Freeze(VisualSeconds, PhysicsLockDelaySeconds);
        }
    }
}
