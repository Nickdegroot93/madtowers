using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Spawner : MonoBehaviour
{
    [SerializeField] private GameModeConfig gameModeConfig;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private float spawnDelay = 0f;

    private struct VariantChance
    {
        public BlockData Variant;
        public float Chance;
    }

    private BlockController _currentBlock;
    private readonly List<BlockDefinition> _definitionBag = new List<BlockDefinition>();
    private readonly List<VariantChance> _variantChances = new List<VariantChance>();
    private BlockData _queuedVariantOverride;
    private BlockDefinition _nextDefinition;
    public BlockController currentBlock => _currentBlock;
    private GameModeConfig ActiveGameModeConfig => LevelSelectionState.ResolveGameMode(gameModeConfig);

    public string GetNextBlockName()
    {
        return _nextDefinition != null ? _nextDefinition.DisplayName : "None";
    }

    private void Start()
    {
        if (LevelSelectionState.IsSelectionPending) return;

        RegisterAmbientVariantChances();
        PrepareNextBlock();
        SpawnNextBlock();
    }

    // Level-authored variant rolls (e.g. "3% of bricks are giant on this level") use the same
    // registry as runtime power-ups, so both stack naturally.
    private void RegisterAmbientVariantChances()
    {
        GameModeConfig activeConfig = ActiveGameModeConfig;
        IReadOnlyList<AmbientBlockVariantChance> ambient = activeConfig != null
            ? activeConfig.AmbientBlockVariantChances
            : null;
        if (ambient == null) return;

        for (int i = 0; i < ambient.Count; i++)
        {
            AmbientBlockVariantChance entry = ambient[i];
            if (entry == null) continue;

            AddVariantChance(entry.Variant, entry.ChancePerBlock);
        }
    }

    private void PrepareNextBlock()
    {
        if (HasConfiguredBlocks())
        {
            if (_definitionBag.Count == 0) RefillDefinitionBag();

            if (_definitionBag.Count > 0)
            {
                int bagIndex = Random.Range(0, _definitionBag.Count);
                _nextDefinition = _definitionBag[bagIndex];
                _definitionBag.RemoveAt(bagIndex);
            }
        }

        GameEvents.RaiseNextBlockChanged(GetNextBlockName());
    }

    // Restarts the lock->spawn chain after an external gate (win verification) suppressed
    // it - the chain is event-driven, so a suppressed spawn never retries on its own.
    public void ResumeSpawning()
    {
        SpawnNextBlock();
    }

    /// <summary>
    /// Replaces the ACTIVE falling piece with another definition's piece at the same
    /// position, mid-fall (the Bullet consumable). The old piece is destroyed without
    /// locking (no score, no spawn trigger); the replacement rejoins the normal
    /// lock->spawn chain exactly like a spawned piece. Validates the replacement
    /// FULLY before touching the old piece - a misconfigured prefab must leave the
    /// game untouched (the lock->spawn chain has no retry; losing the active piece
    /// without a wired successor soft-locks the run).
    /// </summary>
    public bool ReplaceActivePiece(BlockDefinition definition)
    {
        if (definition == null || definition.Prefab == null) return false;

        BlockController active = BlockController.ActiveControlled;
        if (active == null || active != _currentBlock || active.HasLanded) return false;

        GameObject blockObj = Instantiate(definition.Prefab, active.transform.position, Quaternion.identity);
        BlockController replacement = blockObj.GetComponent<BlockController>();
        if (replacement == null)
        {
            Debug.LogError($"ReplaceActivePiece: '{definition.name}' prefab has no BlockController.", definition);
            Destroy(blockObj);
            return false;
        }

        active.OnBlockLocked -= HandleBlockLocked;
        Destroy(active.gameObject);
        _currentBlock = replacement;
        WireBlock(replacement, definition, definition.DefaultData);

        // BlockSpawned is deliberately NOT re-raised: the swap is the same logical
        // turn, and per-spawn passives (recovery windows, charge consumers) must not
        // pay twice for one piece. Revisit if a transform ability ever produces a
        // piece that should join combos (ComboDetector hooks locks via this event).
        return true;
    }

    private void SpawnNextBlock()
    {
        // Never two controlled pieces: a pending SpawnWithDelay coroutine and an external
        // ResumeSpawning can otherwise race (latent today - every config uses SpawnDelay 0).
        if (BlockController.ActiveControlled != null)
        {
            return;
        }

        if (GameManager.Instance != null && GameManager.Instance.isGameOver)
        {
            return;
        }

        // Hold-steady countdown after the win target is met: nothing spawns until the
        // tower has proven itself (LevelRuntimeController restarts spawning if it fails).
        if (LevelRuntimeController.IsVerifyingWin)
        {
            return;
        }

        GameObject prefab = GetPreparedPrefab();
        if (prefab == null)
        {
            Debug.LogError("No block prefabs assigned to Spawner!");
            return;
        }

        BlockDefinition definition = _nextDefinition;
        PrepareNextBlock();

        GameObject blockObj = Instantiate(prefab, spawnPoint.position, Quaternion.identity);

        _currentBlock = blockObj.GetComponent<BlockController>();
        if (_currentBlock != null)
        {
            BlockData data = RollVariantChances(GetBlockData(definition));
            WireBlock(_currentBlock, definition, data);
            GameEvents.RaiseBlockSpawned(_currentBlock, data);
        }
    }

    // Everything a new controlled piece needs to participate in the game - shared by
    // the normal spawn and ReplaceActivePiece so the two paths can never drift apart.
    private void WireBlock(BlockController block, BlockDefinition definition, BlockData data)
    {
        block.OnBlockLocked += HandleBlockLocked;
        block.ApplyConfig(ActiveGameModeConfig);
        if (data != null)
        {
            block.ApplyData(data);
        }

        // Difficulty scales the controlled descent speed only. Landed gravity stays constant
        // (BlockController normalizes it), so tower load never grows with block count.
        if (GameManager.Instance != null)
        {
            block.fallSpeed = GameManager.Instance.currentFallSpeed;
        }

        // The block's identity (shape + rolled variant) travels with it - combo
        // triggers match against this, never against GameObject names.
        block.gameObject.AddComponent<BlockIdentity>().Assign(definition, data);

        // Tell scoring which piece is now in play (covers mid-fall replacements like the
        // Bullet, which never re-raise BlockSpawned) so its lock counts - or doesn't.
        if (GameManager.Instance != null) GameManager.Instance.SetActivePiece(block, data);
    }

    private bool HasConfiguredBlocks()
    {
        GameModeConfig activeConfig = ActiveGameModeConfig;
        return activeConfig != null &&
               activeConfig.BlockBag != null &&
               activeConfig.BlockBag.Count > 0;
    }

    private void RefillDefinitionBag()
    {
        _definitionBag.Clear();
        GameModeConfig activeConfig = ActiveGameModeConfig;
        if (activeConfig == null) return;

        IReadOnlyList<BlockDefinition> configuredBlocks = activeConfig.BlockBag;
        for (int i = 0; i < configuredBlocks.Count; i++)
        {
            BlockDefinition definition = configuredBlocks[i];
            if (definition == null || definition.Prefab == null) continue;

            for (int copy = 0; copy < definition.BagCopies; copy++)
            {
                _definitionBag.Add(definition);
            }
        }
    }

    private GameObject GetPreparedPrefab()
    {
        return _nextDefinition != null ? _nextDefinition.Prefab : null;
    }

    private BlockData GetBlockData(BlockDefinition definition)
    {
        if (definition != null && definition.DefaultData != null) return definition.DefaultData;

        GameModeConfig activeConfig = ActiveGameModeConfig;
        IReadOnlyList<BlockData> configuredData = activeConfig != null
            ? activeConfig.FallbackBlockDataVariants
            : null;

        if (configuredData != null && configuredData.Count > 0)
        {
            return configuredData[Random.Range(0, configuredData.Count)];
        }

        return null;
    }

    /// <summary>
    /// One-shot override: the next brick becomes the given variant. Power-up choices open while
    /// the next piece is already spawned (frozen by the pause), so from the player's point of
    /// view THAT piece is "the next brick" - it gets the variant directly when possible.
    /// </summary>
    public void ApplyVariantToNextBlock(BlockData variant)
    {
        if (variant == null) return;

        if (_currentBlock != null && !_currentBlock.HasLanded)
        {
            _currentBlock.ApplyData(variant);

            // Keep the block's identity AND the accounting context in sync with the
            // swapped-in variant - exactly what WireBlock does for spawned/replaced
            // pieces. Without this, a variant with non-default count/life flags applied
            // to the in-air piece would be scored/lost against the ORIGINAL flags (the
            // identity component and GameManager's active-piece cache stay stale).
            if (_currentBlock.TryGetComponent(out BlockIdentity identity))
            {
                identity.Assign(identity.Definition, variant);
            }
            if (GameManager.Instance != null) GameManager.Instance.SetActivePiece(_currentBlock, variant);
            return;
        }

        _queuedVariantOverride = variant;
    }

    /// <summary>
    /// Registers a chance for future spawns to be replaced with the given variant - used by
    /// level-flavour rolls and recurring power-ups. Registering the same variant again stacks
    /// the chance.
    /// </summary>
    public void AddVariantChance(BlockData variant, float chance)
    {
        if (variant == null || chance <= 0f) return;

        for (int i = 0; i < _variantChances.Count; i++)
        {
            if (_variantChances[i].Variant != variant) continue;

            _variantChances[i] = new VariantChance
            {
                Variant = variant,
                Chance = Mathf.Clamp01(_variantChances[i].Chance + chance)
            };
            return;
        }

        _variantChances.Add(new VariantChance { Variant = variant, Chance = Mathf.Clamp01(chance) });
    }

    private BlockData RollVariantChances(BlockData baseData)
    {
        if (_queuedVariantOverride != null)
        {
            BlockData queued = _queuedVariantOverride;
            _queuedVariantOverride = null;
            return queued;
        }

        for (int i = 0; i < _variantChances.Count; i++)
        {
            if (Random.value < _variantChances[i].Chance)
            {
                return _variantChances[i].Variant;
            }
        }

        return baseData;
    }

    private void HandleBlockLocked()
    {
        if (_currentBlock != null)
        {
            _currentBlock.OnBlockLocked -= HandleBlockLocked;
        }
        
        GameModeConfig activeConfig = ActiveGameModeConfig;
        float delay = activeConfig != null ? activeConfig.SpawnDelay : spawnDelay;
        if (delay <= 0f)
        {
            SpawnNextBlock();
            return;
        }

        StartCoroutine(SpawnWithDelay(delay));
    }

    private IEnumerator SpawnWithDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SpawnNextBlock();
    }
}
