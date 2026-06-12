using UnityEngine;

/// <summary>
/// Flash Freeze, now a CONSUMABLE: held in a slot and fired when the player chooses,
/// not on pick. Every block placed so far freezes exactly where it stands, however it
/// stands; the tower built from here on remains live physics as usual.
/// </summary>
[CreateAssetMenu(fileName = "CementTower", menuName = "Stacking/Abilities/Cement Tower")]
public class CementTowerPowerUp : ConsumableAbility
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

    public override void Activate(AbilityContext context)
    {
        var blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block == null || !block.HasLanded) continue;

            block.FreezeInPlace();
        }
    }
}
