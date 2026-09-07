using UnityEngine;

/// <summary>Shape-bound cyber pyramid. Geometry and bag identity stay on Block_Pyramid;
/// every three subsequent counted placements power its unconditional departure.</summary>
[CreateAssetMenu(fileName = "Pyramid", menuName = "Stacking/Blocks/Cyber Pyramid")]
public sealed class PyramidBlockData : BlockData
{
    public override void OnApplied(BlockController block)
    {
        if (block == null) return;
        if (!block.TryGetComponent(out PyramidBlockSkin skin)) skin = block.gameObject.AddComponent<PyramidBlockSkin>();
        skin.Apply();
    }

    public override void OnLocked(BlockController block)
    {
        if (block == null || block.GetComponent<PyramidBlockBehaviour>() != null) return;
        if (GameManager.Instance != null && GameManager.Instance.isGameOver) return;
        if (LossZone.IsBelowCull(block.transform.position)) return;
        block.gameObject.AddComponent<PyramidBlockBehaviour>().Begin(block);
    }
}
