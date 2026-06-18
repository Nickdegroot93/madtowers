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

    private struct DefinitionChance
    {
        public BlockDefinition Definition;
        public float Chance;
    }

    private BlockController _currentBlock;
    private readonly List<BlockDefinition> _definitionBag = new List<BlockDefinition>();
    private readonly List<DefinitionChance> _definitionChances = new List<DefinitionChance>();
    // Out-of-bag bricks an ability has introduced to THIS run (Pip / Domino). They drop via
    // the definition-chance roll, never via the authored bag; CanSpawnDefinition treats them
    // as spawnable so the roll - and any later chance-boost ability - can target them.
    private readonly HashSet<BlockDefinition> _injectedDefinitions = new HashSet<BlockDefinition>();
    private readonly List<VariantChance> _variantChances = new List<VariantChance>();
    private BlockData _queuedVariantOverride;

    // Set by the Fission session: while true the lock->spawn chain stops auto-spawning bag
    // pieces so the session can feed its own 1x1 shards. Run-local; resets with a fresh Spawner.
    private bool _suppressAutoSpawn;

    // Stable look-ahead queue: holds exactly _visibleQueueDepth rolled shapes, front =
    // next to spawn. A queued shape is NEVER re-rolled, so what the HUD previews is exactly
    // what spawns. Foresight widens the depth (SetVisibleQueueDepth); default is one.
    public const int MaxVisibleQueueDepth = 2;
    private readonly List<BlockDefinition> _upcoming = new List<BlockDefinition>(MaxVisibleQueueDepth);
    private readonly List<string> _upcomingNames = new List<string>(MaxVisibleQueueDepth);
    private int _visibleQueueDepth = 1;

    public BlockController currentBlock => _currentBlock;
    private GameModeConfig ActiveGameModeConfig => LevelSelectionState.ResolveGameMode(gameModeConfig);

    /// <summary>The upcoming shapes' display names, front first (live reused buffer; read
    /// it synchronously). One entry by default, more once Foresight widens visibility.</summary>
    public IReadOnlyList<string> GetUpcomingBlockNames() => _upcomingNames;

    /// <summary>Widen (or restore) how many upcoming shapes are prepared and previewed.
    /// Tops the queue up immediately so new previews appear without waiting for a spawn.</summary>
    public void SetVisibleQueueDepth(int depth)
    {
        int clamped = Mathf.Clamp(depth, 1, MaxVisibleQueueDepth);
        if (clamped == _visibleQueueDepth) return;

        _visibleQueueDepth = clamped;
        RefillQueue();
    }

    private void Start()
    {
        if (LevelSelectionState.IsSelectionPending) return;

        RegisterAmbientVariantChances();
        RefillQueue();
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

    // Rolls a SINGLE upcoming shape: a forced definition-chance injection (Spike/Cube
    // Supply) if one fires, else a draw from the shuffle bag. Null only when the mode has
    // no configured blocks.
    private BlockDefinition RollOneDefinition()
    {
        if (!HasConfiguredBlocks()) return null;

        if (TryRollDefinitionChance(out BlockDefinition boosted)) return boosted;

        if (_definitionBag.Count == 0) RefillDefinitionBag();
        if (_definitionBag.Count == 0) return null;

        int bagIndex = Random.Range(0, _definitionBag.Count);
        BlockDefinition drawn = _definitionBag[bagIndex];
        _definitionBag.RemoveAt(bagIndex);
        return drawn;
    }

    // Tops the look-ahead queue up to the visible depth (existing entries stay put - the
    // queue is stable) and announces the new preview. O(depth), depth <= MaxVisibleQueueDepth.
    private void RefillQueue()
    {
        while (_upcoming.Count < _visibleQueueDepth)
        {
            BlockDefinition rolled = RollOneDefinition();
            if (rolled == null) break; // no configured blocks - leave the queue as-is
            _upcoming.Add(rolled);
        }

        AnnounceUpcoming();
    }

    private void AnnounceUpcoming()
    {
        _upcomingNames.Clear();
        for (int i = 0; i < _upcoming.Count; i++)
        {
            _upcomingNames.Add(_upcoming[i] != null ? _upcoming[i].DisplayName : "None");
        }

        GameEvents.RaiseNextBlockChanged(_upcomingNames);
    }

    /// <summary>Put a shape back at the FRONT of the look-ahead queue so it spawns next
    /// (the Rebound ability "teleports" a saved block back to the top of the queue). Only
    /// the shape returns - the variant is re-rolled at spawn like any queued piece. The
    /// queue is transiently one longer than the visible depth; it drains on the next spawn.</summary>
    public void RequeueDefinition(BlockDefinition definition)
    {
        if (definition == null || definition.Prefab == null) return;

        _upcoming.Insert(0, definition);
        AnnounceUpcoming();
    }

    /// <summary>The shape of the piece currently in play, or null (used by the Hold cache).</summary>
    public BlockDefinition ActiveDefinition =>
        _currentBlock != null && _currentBlock.TryGetComponent(out BlockIdentity id) ? id.Definition : null;

    /// <summary>Where a fresh piece spawns (the top). The Hold cache drops a banked-in piece here.</summary>
    public Vector3 SpawnPosition => spawnPoint != null ? spawnPoint.position : Vector3.zero;

    /// <summary>Pop the next queued shape off the front and refill (the Hold cache's bank case:
    /// the banked piece leaves the field and this becomes the new active piece).</summary>
    public BlockDefinition TakeNextQueued()
    {
        if (_upcoming.Count == 0) RefillQueue();
        if (_upcoming.Count == 0) return null;

        BlockDefinition next = _upcoming[0];
        _upcoming.RemoveAt(0);
        RefillQueue();
        return next;
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
    public bool ReplaceActivePiece(BlockDefinition definition, Vector3? atPosition = null, bool asNewSpawn = false)
    {
        if (definition == null || definition.Prefab == null) return false;

        BlockController active = BlockController.ActiveControlled;
        if (active == null || active != _currentBlock || active.HasLanded) return false;

        // Default: spawn in-place (mid-fall transmute - Bullet/Shrink/Pip). Hold passes a
        // position to lift the swapped piece slightly, or to drop the banked-in piece at the top.
        Vector3 spawnPos = atPosition ?? active.transform.position;
        GameObject blockObj = Instantiate(definition.Prefab, spawnPos, Quaternion.identity);
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

        // asNewSpawn = a genuinely new piece entering play (the Hold cache's BANK: the old piece
        // left the board, this is the next one). It rolls variants and raises BlockSpawned so it
        // joins combos / slow windows / on-spawn passives exactly like a normal spawn. Without it
        // (transmute + Hold SWAP) the piece keeps the same turn: DefaultData, no BlockSpawned, so
        // per-spawn passives never pay twice. The per-piece lockout keys off BlockLocked, not this,
        // so a banked-in piece raising BlockSpawned can't reopen the re-hold loophole.
        BlockData data = asNewSpawn ? RollVariantChances(GetBlockData(definition)) : definition.DefaultData;
        WireBlock(replacement, definition, data);
        if (asNewSpawn) GameEvents.RaiseBlockSpawned(replacement, data);
        return true;
    }

    /// <summary>
    /// While true, the lock->spawn chain does NOT auto-spawn the next bag piece. The Fission
    /// session owns spawning for its duration (it feeds 1x1 shards itself); it clears this on
    /// the final shard's lock so the very next SpawnNextBlock resumes normal play.
    /// </summary>
    public void SetAutoSpawnSuspended(bool suspended) => _suppressAutoSpawn = suspended;

    /// <summary>
    /// Spawn a fresh controlled piece of the given definition at a position, wired exactly like
    /// a normal spawn (same WireBlock path) so it cannot drift from it. Used by the Fission
    /// session to feed each 1x1 shard; <paramref name="suspended"/> starts it hovering (descent
    /// deferred until the player commits a drop). Uses DefaultData (no variant roll) and does NOT
    /// raise BlockSpawned - a shard is the same logical turn, like a transmute, not a new draw.
    /// </summary>
    public BlockController SpawnControlledPieceAt(BlockDefinition definition, Vector3 position, bool suspended)
    {
        if (definition == null || definition.Prefab == null) return null;

        GameObject blockObj = Instantiate(definition.Prefab, position, Quaternion.identity);
        BlockController block = blockObj.GetComponent<BlockController>();
        if (block == null)
        {
            Debug.LogError($"SpawnControlledPieceAt: '{definition.name}' prefab has no BlockController.", definition);
            Destroy(blockObj);
            return null;
        }

        _currentBlock = block;
        WireBlock(block, definition, definition.DefaultData);
        if (suspended) block.SetDescentSuspended(true);
        return block;
    }

    private void SpawnNextBlock()
    {
        // The Fission session feeds its own shards; never inject a bag piece mid-session.
        if (_suppressAutoSpawn)
        {
            return;
        }

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

        if (_upcoming.Count == 0) RefillQueue();
        BlockDefinition definition = _upcoming.Count > 0 ? _upcoming[0] : null;
        GameObject prefab = definition != null ? definition.Prefab : null;
        if (prefab == null)
        {
            Debug.LogError("No block prefabs assigned to Spawner!");
            return;
        }

        // Consume the front, then top the queue back up so the preview advances: the shown
        // next-next becomes the next, unchanged (stable), and a fresh tail is rolled.
        _upcoming.RemoveAt(0);
        RefillQueue();

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
        // (BlockController normalizes it), so tower load never grows with block count. The
        // ability factor (Air Brake, recovery / slo-mo) is stamped SEPARATELY so it applies
        // to normal descent only - fast drops use the un-factored base speed.
        if (GameManager.Instance != null)
        {
            block.fallSpeed = GameManager.Instance.BaseFallSpeed;
            block.SetNormalFallSpeedFactor(GameManager.Instance.AbilityFallSpeedFactor);
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

    public bool CanSpawnDefinition(BlockDefinition definition)
    {
        if (definition == null || definition.Prefab == null) return false;
        if (_injectedDefinitions.Contains(definition)) return true; // ability-introduced bricks

        GameModeConfig activeConfig = ActiveGameModeConfig;
        IReadOnlyList<BlockDefinition> configuredBlocks = activeConfig != null
            ? activeConfig.BlockBag
            : null;
        if (configuredBlocks == null) return false;

        for (int i = 0; i < configuredBlocks.Count; i++)
        {
            if (configuredBlocks[i] == definition) return true;
        }
        return false;
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

    /// <summary>
    /// Registers a run-local chance to inject an extra shape before drawing from the
    /// normal bag. This never mutates BlockDefinition.bagCopies (asset data) and never
    /// removes cards from the authored bag; it simply makes the chosen shape appear a
    /// little more often for the rest of this run.
    /// </summary>
    public void AddDefinitionChance(BlockDefinition definition, float chance)
    {
        if (!CanSpawnDefinition(definition) || chance <= 0f) return;

        for (int i = 0; i < _definitionChances.Count; i++)
        {
            if (_definitionChances[i].Definition != definition) continue;

            _definitionChances[i] = new DefinitionChance
            {
                Definition = definition,
                Chance = Mathf.Clamp01(_definitionChances[i].Chance + chance)
            };
            return;
        }

        _definitionChances.Add(new DefinitionChance { Definition = definition, Chance = Mathf.Clamp01(chance) });
    }

    /// <summary>
    /// Introduce an OUT-OF-BAG brick to this run with a per-spawn drop chance (the Pip /
    /// Domino abilities). Marks it spawnable first so CanSpawnDefinition recognises it -
    /// which also lets a later chance-boost ability targeting the same brick become
    /// available - then registers the chance through the normal accumulating registry.
    /// Run-local; resets with the scene-fresh Spawner. Never touches the authored bag.
    /// </summary>
    public void AddInjectedDefinition(BlockDefinition definition, float chance)
    {
        if (definition == null || definition.Prefab == null) return;

        // Mark spawnable FIRST (this is what unlocks a later booster ability and lets the
        // roll target the brick), THEN register the drop chance - AddDefinitionChance gates
        // on CanSpawnDefinition, which only passes because of the line above. A zero chance
        // still introduces the brick (it just doesn't drop on its own until a booster adds
        // chance), so the two effects are deliberately decoupled.
        _injectedDefinitions.Add(definition);
        if (chance > 0f) AddDefinitionChance(definition, chance);
    }

    private bool TryRollDefinitionChance(out BlockDefinition definition)
    {
        float totalChance = 0f;
        for (int i = 0; i < _definitionChances.Count; i++)
        {
            DefinitionChance entry = _definitionChances[i];
            if (!CanSpawnDefinition(entry.Definition)) continue;

            totalChance += entry.Chance;
        }

        if (Random.value >= Mathf.Clamp01(totalChance))
        {
            definition = null;
            return false;
        }

        float pick = Random.Range(0f, totalChance);
        for (int i = 0; i < _definitionChances.Count; i++)
        {
            DefinitionChance entry = _definitionChances[i];
            if (!CanSpawnDefinition(entry.Definition)) continue;

            pick -= entry.Chance;
            if (pick > 0f) continue;

            definition = entry.Definition;
            return true;
        }

        definition = null;
        return false;
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

        GameEvents.RaiseBlockLocked(); // one per piece-turn; resets the Hold cache's per-piece lockout

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
