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
            _currentBlock.OnBlockLocked += HandleBlockLocked;
            _currentBlock.ApplyConfig(ActiveGameModeConfig);

            BlockData data = RollVariantChances(GetBlockData(definition));
            if (data != null)
            {
                _currentBlock.ApplyData(data);
            }

            // Difficulty scales the controlled descent speed only. Landed gravity stays constant
            // (BlockController normalizes it), so tower load never grows with block count.
            if (GameManager.Instance != null)
            {
                _currentBlock.fallSpeed = GameManager.Instance.currentFallSpeed;
            }

            // The block's identity (shape + rolled variant) travels with it - combo
            // triggers match against this, never against GameObject names.
            blockObj.AddComponent<BlockIdentity>().Assign(definition, data);
            GameEvents.RaiseBlockSpawned(_currentBlock, data);
        }
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
