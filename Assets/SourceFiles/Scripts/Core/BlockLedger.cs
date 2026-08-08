using System;
using UnityEngine;

public sealed class BlockLedger : IDisposable
{
    private readonly RunState _state;
    private readonly DifficultyController _difficulty;
    private readonly Func<StatusEffects> _statusProvider;
    private readonly Func<bool> _isGameOver;
    private bool _inBlockLoss;

    public BlockLedger(
        RunState state,
        DifficultyController difficulty,
        Func<StatusEffects> statusProvider,
        Func<bool> isGameOver)
    {
        _state = state;
        _difficulty = difficulty;
        _statusProvider = statusProvider;
        _isGameOver = isGameOver;
    }

    public BlockController LastPlacedBlock { get; private set; }

    public void Subscribe()
    {
        GameEvents.BlockLanded += HandleBlockLanded;
        GameEvents.BlockDestroyed += HandleBlockDestroyed;
    }

    public void Dispose()
    {
        GameEvents.BlockLanded -= HandleBlockLanded;
        GameEvents.BlockDestroyed -= HandleBlockDestroyed;
    }

    public void BeginBlockLoss(BlockController block)
    {
        _inBlockLoss = true;
        RemovePlacedBlock(block);
    }

    public void EndBlockLoss() => _inBlockLoss = false;

    public void RemovePlacedBlock(BlockController block)
    {
        if (LastPlacedBlock == block) LastPlacedBlock = null;

        if (block != null && block.TryGetComponent(out BlockIdentity identity) && identity.TryConsumeCounted())
        {
            int count = _state.AdjustStandingBlocks(-1);
            GameEvents.RaiseStandingBlocksChanged(count);
        }
    }

    private void HandleBlockLanded(BlockController block, float highestCellY)
    {
        if (_state.TryUpdateMaxHeight(highestCellY))
        {
            GameEvents.RaiseHeightChanged(_state.TowerHeight);
        }

        if (_isGameOver() || _inBlockLoss) return;

        BlockIdentity identity = null;
        BlockData data = null;
        if (block != null && block.TryGetComponent<BlockIdentity>(out identity))
        {
            data = identity.Variant;
        }
        if (data != null && !data.CountsAsPlacedBlock) return;
        // Debris of another block's placement (magma's melt pips): physically real, but it
        // was not a placement, so it must not move the score, the live count or a wave
        // quota. Returning here also means TryConsumeCounted stays false, so its eventual
        // destruction correctly decrements nothing.
        if (identity != null && identity.SuppressPlacedCount) return;

        int baseAmount = 1;
        int amount = baseAmount;
        StatusEffects status = _statusProvider();
        if (status != null)
        {
            amount += status.ExtraScorePerBlock;
        }

        _state.AddScore(amount);
        _difficulty.RegisterScoredBlocks(baseAmount);
        GameEvents.RaiseScoreChanged(_state.Score);

        int count = _state.AdjustStandingBlocks(1);
        GameEvents.RaiseStandingBlocksChanged(count);

        if (identity != null)
        {
            identity.MarkCountedAsPlaced();
            LastPlacedBlock = block;
        }

        _state.IncrementPlacedBlocks();
        GameEvents.RaiseBlockPlaced(_state.TotalPlacedBlocks);
    }

    private void HandleBlockDestroyed(BlockController block) => RemovePlacedBlock(block);
}
