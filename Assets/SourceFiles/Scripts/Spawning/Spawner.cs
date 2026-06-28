using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Spawner : MonoBehaviour
{
    [SerializeField] private GameModeConfig gameModeConfig;
    [SerializeField] private Transform spawnPoint;
    [Tooltip("Editor fallback only: used when no GameModeConfig is resolved (the active mode's Spawn Delay always wins in normal play).")]
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
    // One-shot variant overrides for upcoming spawns, consumed front-first (a "next N bricks become
    // variant V" consumable queues the extras here; the in-air piece transforms directly).
    private readonly Queue<BlockData> _queuedVariantOverrides = new Queue<BlockData>();

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

    private void OnEnable()
    {
        GameEvents.SpawnAvailabilityChanged += HandleSpawnAvailabilityChanged;
    }

    private void OnDisable()
    {
        GameEvents.SpawnAvailabilityChanged -= HandleSpawnAvailabilityChanged;
    }

    private void HandleSpawnAvailabilityChanged(bool canSpawn)
    {
        if (canSpawn) SpawnNextBlock();
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

    /// <summary>Can the active falling piece trade places with the front of the look-ahead queue?</summary>
    public bool CanSwapActiveWithNextQueued()
    {
        BlockController active = BlockController.ActiveControlled;
        if (active == null || active != _currentBlock || active.HasLanded) return false;

        BlockDefinition outgoing = ActiveDefinition;
        if (outgoing == null || outgoing.Prefab == null) return false;

        if (_upcoming.Count == 0) return false;
        BlockDefinition incoming = _upcoming[0];
        if (incoming == null || incoming.Prefab == null) return false;
        if (incoming == outgoing) return false;
        if (incoming.Prefab.GetComponent<BlockController>() == null) return false;

        Camera camera = Camera.main;
        if (camera != null && camera.orthographic && active.transform.position.y < LossZone.CullY(camera)) return false;

        return true;
    }

    /// <summary>
    /// Swap the active falling shape with the next queued shape. The outgoing active shape becomes
    /// the queue front, and the incoming queued shape enters play at the active piece's position.
    /// </summary>
    public bool SwapActiveWithNextQueued()
    {
        if (!CanSwapActiveWithNextQueued()) return false;

        BlockController active = BlockController.ActiveControlled;
        BlockDefinition outgoing = ActiveDefinition;
        BlockDefinition incoming = _upcoming[0];
        Vector3 spawnPos = active.transform.position;

        GameObject blockObj = Instantiate(incoming.Prefab, spawnPos, Quaternion.identity);
        BlockController replacement = blockObj.GetComponent<BlockController>();
        if (replacement == null)
        {
            Debug.LogError($"SwapActiveWithNextQueued: '{incoming.name}' prefab has no BlockController.", incoming);
            Destroy(blockObj);
            return false;
        }

        active.OnBlockLocked -= HandleBlockLocked;
        Destroy(active.gameObject);
        _currentBlock = replacement;
        _upcoming[0] = outgoing;

        BlockData data = RollVariantChances(GetBlockData(incoming));
        WireBlock(replacement, incoming, data);
        AnnounceUpcoming();
        GameEvents.RaiseBlockSpawned(replacement, data);
        return true;
    }

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

    /// <summary>Draw a small hand of upcoming definitions, preferring distinct shapes.
    /// Overdraw uses this to turn one active piece into a three-shape choice without
    /// permanently duplicating the look-ahead queue. Duplicate rolls are returned to the
    /// queue front in their original order where possible.</summary>
    public List<BlockDefinition> TakeDistinctQueued(int count)
    {
        var choices = new List<BlockDefinition>(Mathf.Max(0, count));
        if (count <= 0) return choices;

        var duplicates = new List<BlockDefinition>();
        int attempts = Mathf.Max(count * 8, count);
        while (choices.Count < count && attempts-- > 0)
        {
            BlockDefinition next = TakeNextQueued();
            if (next == null) break;

            bool alreadyChosen = false;
            for (int i = 0; i < choices.Count; i++)
            {
                if (choices[i] == next)
                {
                    alreadyChosen = true;
                    break;
                }
            }

            if (alreadyChosen) duplicates.Add(next);
            else choices.Add(next);
        }

        while (choices.Count < count && duplicates.Count > 0)
        {
            int last = duplicates.Count - 1;
            choices.Add(duplicates[last]);
            duplicates.RemoveAt(last);
        }

        for (int i = duplicates.Count - 1; i >= 0; i--)
        {
            RequeueDefinition(duplicates[i]);
        }

        return choices;
    }

    /// <summary>Remove the live falling piece without locking or scoring. Used by
    /// active-state abilities that replace the turn with their own controlled sequence.</summary>
    public bool DestroyActivePieceWithoutLock()
    {
        BlockController active = BlockController.ActiveControlled;
        if (active == null || active != _currentBlock || active.HasLanded) return false;

        active.OnBlockLocked -= HandleBlockLocked;
        Destroy(active.gameObject);
        _currentBlock = null;
        if (GameManager.Instance != null) GameManager.Instance.SetActivePiece(null, null);
        return true;
    }

    // Compatibility shim for active-piece sessions that destroy the live piece without a lock.
    // Phase/gate releases resume through GameEvents.SpawnAvailabilityChanged.
    public void ResumeSpawning()
    {
        SpawnNextBlock();
    }

    /// <summary>
    /// Replaces the ACTIVE falling piece with another definition's piece at the same
    /// position, mid-fall (Shrink / transform consumables). The old piece is destroyed without
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

        // Default: spawn in-place (mid-fall transmute - Shrink/Pip). Hold passes a
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
    /// a normal spawn (same WireBlock path) so it cannot drift from it. Used by sessions to feed
    /// authored choice pieces; <paramref name="suspended"/> starts it hovering (descent deferred
    /// until the player commits a drop). By default it uses DefaultData and does not raise
    /// BlockSpawned (Fission shards are the same logical turn); pass <paramref name="asNewSpawn"/>
    /// for genuine new choice pieces such as Overdraw.
    /// </summary>
    public BlockController SpawnControlledPieceAt(
        BlockDefinition definition,
        Vector3 position,
        bool suspended,
        bool asNewSpawn = false)
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
        BlockData data = asNewSpawn ? RollVariantChances(GetBlockData(definition)) : definition.DefaultData;
        WireBlock(block, definition, data);
        if (suspended) block.SetDescentSuspended(true);
        if (asNewSpawn) GameEvents.RaiseBlockSpawned(block, data);
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

        if (GameManager.Instance != null && !GameManager.Instance.CanSpawnBlocks)
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
            // The bag spawn is the one place a queued variant override is consumed (the player's
            // next brick); everything else rolls only the ambient/chance table.
            BlockData data = ConsumeQueuedVariantOverride() ?? RollVariantChances(GetBlockData(definition));
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
        // transmutes, which never re-raise BlockSpawned) so its lock counts - or doesn't.
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

        _queuedVariantOverrides.Enqueue(variant);
    }

    /// <summary>
    /// Queues <paramref name="count"/> upcoming spawns to become the given variant (consumed
    /// front-first, before the random variant roll). Used for "next N bricks become variant V"
    /// after the in-air piece has already been transformed directly (see ApplyVariantToNextBlock).
    /// </summary>
    public void QueueVariantOverride(BlockData variant, int count)
    {
        if (variant == null) return;
        for (int i = 0; i < count; i++) _queuedVariantOverrides.Enqueue(variant);
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
    /// Multiplicatively REDUCES an already-registered variant's spawn chance (an ability that
    /// suppresses an annoying brick). Relative, so it scales with whatever the level's ambient
    /// rate is: <paramref name="fraction"/> 0.5 halves it; stacking shrinks it further toward -
    /// but never past - zero. No-op if the variant was never registered (the level can't spawn it),
    /// which is also why such abilities gate on requiresVariantsInLevel.
    /// </summary>
    public void ReduceVariantChance(BlockData variant, float fraction)
    {
        if (variant == null) return;
        fraction = Mathf.Clamp01(fraction);

        for (int i = 0; i < _variantChances.Count; i++)
        {
            if (_variantChances[i].Variant != variant) continue;

            _variantChances[i] = new VariantChance
            {
                Variant = variant,
                Chance = Mathf.Clamp01(_variantChances[i].Chance * (1f - fraction))
            };
            return;
        }
    }

    /// <summary>
    /// Multiplicatively reduces the spawn chance of EVERY registered variant flagged
    /// <see cref="BlockData.IsHazard"/> (Purifier). Data-driven off the hazard flag, so a new hostile
    /// brick is suppressed automatically - no hand-maintained list. No-op on variants the level can't spawn.
    /// </summary>
    public void ReduceHazardChances(float fraction)
    {
        fraction = Mathf.Clamp01(fraction);
        for (int i = 0; i < _variantChances.Count; i++)
        {
            VariantChance vc = _variantChances[i];
            if (vc.Variant == null || !vc.Variant.IsHazard) continue;
            _variantChances[i] = new VariantChance
            {
                Variant = vc.Variant,
                Chance = Mathf.Clamp01(vc.Chance * (1f - fraction))
            };
        }
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
        for (int i = 0; i < _variantChances.Count; i++)
        {
            if (Random.value < _variantChances[i].Chance)
            {
                return _variantChances[i].Variant;
            }
        }

        return baseData;
    }

    // A queued one-shot override ("next N bricks become variant V") applies ONLY to a genuine
    // bag spawn - the player's literal next brick. Session/bank/swap pieces (Overdraw draft, Hold
    // bank, Flip) must NOT consume it, or they'd steal the variant from the piece it was aimed at
    // and the count would desync. Returns the override variant, or null to fall through to the
    // normal chance roll.
    private BlockData ConsumeQueuedVariantOverride()
        => _queuedVariantOverrides.Count > 0 ? _queuedVariantOverrides.Dequeue() : null;

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
