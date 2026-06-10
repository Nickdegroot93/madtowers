using UnityEngine;

[CreateAssetMenu(fileName = "CementTower", menuName = "Stacking/Power Ups/Cement Tower")]
public class CementTowerPowerUp : PowerUpDefinition
{
    public override void Apply(PowerUpContext context)
    {
        // Every block placed so far freezes exactly where it stands, however it stands.
        // The tower built from here on remains live physics as usual.
        var blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block == null || !block.HasLanded) continue;

            block.MakeSturdy();
        }
    }
}
