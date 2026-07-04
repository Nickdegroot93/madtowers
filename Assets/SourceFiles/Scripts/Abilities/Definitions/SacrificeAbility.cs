using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One-shot passive (set Charges = 1 on the asset): the first landed block that would
/// fall off the screen is destroyed instead of charging a life, and the tower pays by
/// losing its current topmost landed block. With charges exhausted the ability disappears.
/// </summary>
[CreateAssetMenu(fileName = "Sacrifice", menuName = "Stacking/Abilities/Sacrifice")]
public class SacrificeAbility : PassiveAbility
{
    [Header("Presentation")]
    [Tooltip("Visible bottom-of-screen warning line while this one-shot passive is armed.")]
    [SerializeField] private Color laserColor = new Color(0.35f, 0.65f, 1f, 1f);
    [Tooltip("Impact effect played from every cell of destroyed blocks.")]
    [SerializeField] private GameObject impactEffect;
    [Tooltip("Size multiplier for the impact effect.")]
    [SerializeField] private float effectScale = 1f;

    private SacrificeLaserLine _laserLine;

    public override void OnAcquired(AbilityContext context, int stacks)
    {
        EnsureLaserLine();
    }

    public override void OnRemoved(AbilityContext context)
    {
        if (_laserLine != null)
        {
            Object.Destroy(_laserLine.gameObject);
            _laserLine = null;
        }
    }

    public override bool TryInterceptLoss(AbilityContext context, BlockController block)
    {
        // Contract: a handled block must end non-lost - destroyed counts. This hook is
        // called only for landed blocks by LossZone, so active pieces keep the normal loss
        // path and the spawner never gets stranded.
        SacrificeLaserLine.FlashAtLossLine(laserColor);

        BlockController payment = FindTopmostTowerBlockExcept(block);
        Detonate(block);
        if (payment != null) Detonate(payment);
        ImpactFx.ImpactPunch(0.075f, 0.16f, 0.18f);
        return true;
    }

    private void EnsureLaserLine()
    {
        if (_laserLine != null) return;

        GameObject go = new GameObject("SacrificeLaserLine");
        _laserLine = go.AddComponent<SacrificeLaserLine>();
        _laserLine.Configure(laserColor);
        SfxPlayer.Play("laser_line_on", 0.65f, 0.04f);
    }

    private BlockController FindTopmostTowerBlockExcept(BlockController excluded)
    {
        BlockController topmost = null;
        float topY = float.NegativeInfinity;
        IReadOnlyList<BlockController> blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController candidate = blocks[i];
            if (candidate == null || candidate == excluded || !candidate.HasLanded) continue;

            float candidateY = candidate.GetHighestCellY();
            if (candidateY <= topY) continue;

            topY = candidateY;
            topmost = candidate;
        }
        return topmost;
    }

    private void Detonate(BlockController block)
    {
        if (block == null) return;

        ImpactFx.BurstFromEveryCell(block, impactEffect, effectScale);
        if (block.TryGetWorldBounds(out Bounds bounds))
        {
            BlockShatterFx.Spawn(bounds, laserColor);
        }
        SfxPlayer.Play("shatter_sacrifice", 0.85f, 0.05f);
        GameEvents.RaiseBlockDestroyed(block);
        Object.Destroy(block.gameObject);
    }
}
