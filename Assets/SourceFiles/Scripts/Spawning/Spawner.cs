using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Spawner : MonoBehaviour
{
    [SerializeField] private GameModeConfig gameModeConfig;
    [SerializeField] private GameObject[] blockPrefabs;
    [SerializeField] private BlockData[] blockDataVariants;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private float spawnDelay = 0.5f;

    private BlockController _currentBlock;
    private readonly List<BlockDefinition> _definitionBag = new List<BlockDefinition>();
    private readonly List<int> _fallbackBag = new List<int>();
    private BlockDefinition _nextDefinition;
    public BlockController currentBlock => _currentBlock;

    public int nextBlockIndex { get; private set; } = -1;

    public string GetNextBlockName()
    {
        if (_nextDefinition != null)
        {
            return _nextDefinition.DisplayName;
        }

        if (nextBlockIndex >= 0 && blockPrefabs != null && nextBlockIndex < blockPrefabs.Length)
        {
            return blockPrefabs[nextBlockIndex].name;
        }
        return "None";
    }

    private void Start()
    {
        PrepareNextBlock();
        SpawnNextBlock();
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
                nextBlockIndex = -1;
            }
        }

        if (_nextDefinition == null && blockPrefabs != null && blockPrefabs.Length > 0)
        {
            if (_fallbackBag.Count == 0) RefillFallbackBag();
            if (_fallbackBag.Count == 0) return;

            int bagIndex = Random.Range(0, _fallbackBag.Count);
            nextBlockIndex = _fallbackBag[bagIndex];
            _fallbackBag.RemoveAt(bagIndex);
            _nextDefinition = null;
        }

        GameEvents.RaiseNextBlockChanged(GetNextBlockName());
    }

    private void SpawnNextBlock()
    {
        if (GameManager.Instance != null && GameManager.Instance.isGameOver)
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
            _currentBlock.ApplyConfig(gameModeConfig);

            BlockData data = GetBlockData(definition);
            if (data != null)
            {
                _currentBlock.ApplyData(data);
            }

            if (GameManager.Instance != null)
            {
                _currentBlock.fallSpeed = GameManager.Instance.currentFallSpeed;
                Rigidbody2D rb = blockObj.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.gravityScale = GameManager.Instance.currentGravityScale;
                }
            }
        }
    }

    private bool HasConfiguredBlocks()
    {
        return gameModeConfig != null &&
               gameModeConfig.BlockBag != null &&
               gameModeConfig.BlockBag.Count > 0;
    }

    private void RefillDefinitionBag()
    {
        _definitionBag.Clear();
        IReadOnlyList<BlockDefinition> configuredBlocks = gameModeConfig.BlockBag;
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

    private void RefillFallbackBag()
    {
        _fallbackBag.Clear();
        if (blockPrefabs == null) return;

        for (int i = 0; i < blockPrefabs.Length; i++)
        {
            if (blockPrefabs[i] != null) _fallbackBag.Add(i);
        }
    }

    private GameObject GetPreparedPrefab()
    {
        if (_nextDefinition != null) return _nextDefinition.Prefab;
        if (nextBlockIndex >= 0 && blockPrefabs != null && nextBlockIndex < blockPrefabs.Length)
        {
            return blockPrefabs[nextBlockIndex];
        }

        return null;
    }

    private BlockData GetBlockData(BlockDefinition definition)
    {
        if (definition != null && definition.DefaultData != null) return definition.DefaultData;

        IReadOnlyList<BlockData> configuredData = gameModeConfig != null
            ? gameModeConfig.FallbackBlockDataVariants
            : null;

        if (configuredData != null && configuredData.Count > 0)
        {
            return configuredData[Random.Range(0, configuredData.Count)];
        }

        if (blockDataVariants != null && blockDataVariants.Length > 0)
        {
            return blockDataVariants[Random.Range(0, blockDataVariants.Length)];
        }

        return null;
    }

    private void HandleBlockLocked()
    {
        if (_currentBlock != null)
        {
            _currentBlock.OnBlockLocked -= HandleBlockLocked;
        }
        
        StartCoroutine(SpawnWithDelay());
    }

    private IEnumerator SpawnWithDelay()
    {
        float delay = gameModeConfig != null ? gameModeConfig.SpawnDelay : spawnDelay;
        yield return new WaitForSeconds(delay);
        SpawnNextBlock();
    }
}
