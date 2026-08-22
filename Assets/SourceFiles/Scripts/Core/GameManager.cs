using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [SerializeField] private GameModeConfig gameModeConfig;
    [SerializeField] private RunState _runState = new RunState();
    [SerializeField] private DifficultyController _difficulty = new DifficultyController();

    public bool isGameOver { get; private set; }
    /// <summary>Free starting lives granted by the level's game type (the Flood grants the
    /// cap): a floor over the config's authored StartingLives, applied in ApplyConfig.</summary>
    private int _grantedRunLives;
    public bool IsGamePaused { get; private set; }
    public GamePhase CurrentPhase { get; private set; } = GamePhase.Playing;
    public bool CanSpawnBlocks => CurrentPhase == GamePhase.Playing && !IsGamePaused && _spawnHoldOwners.Count == 0;
    public float maxHeight => _runState.MaxHeightWorld;
    /// <summary>PEAK tower height in meters above the floor - the monotonic record that feeds
    /// bests, results and XP. Never goes down; for what is standing right now (HUD, camera,
    /// hold-steady checks) use <see cref="liveTowerHeight"/>.</summary>
    public float towerHeight => _runState.TowerHeight;
    /// <summary>World Y of the top of the highest block actually STANDING right now (the floor
    /// while nothing stands). Unlike maxHeight this goes DOWN when the tower sheds blocks, so
    /// the camera can follow a collapse back to the real tower - with lives gone from some
    /// modes (the Flood), collapse-and-continue is a normal state the monotonic record was
    /// never meant to serve. A block clearly falling off the tower stops counting while it
    /// falls (IsFallingClearOfTower - NOT the sticky IsFallingAway latch, which stays set on
    /// recoverable jolts long after the block re-seats). Recomputed at most every 0.15s: the
    /// walk touches every landed block's cell geometry, and every consumer (camera SmoothDamp
    /// 0.35s, the 5 Hz publish, hold-steady tolerance checks) fully hides that staleness.</summary>
    public float LiveTowerTopWorldY
    {
        get
        {
            if (Time.unscaledTime - _liveTopCacheAt >= LiveTopRefreshSeconds)
            {
                _liveTopCacheAt = Time.unscaledTime;
                _liveTopCache = ComputeLiveTowerTopWorldY();
            }
            return _liveTopCache;
        }
    }
    /// <summary>Live standing-tower height in meters above the floor (what the HUD height
    /// counter shows). The live counterpart of <see cref="towerHeight"/>.</summary>
    public float liveTowerHeight => Mathf.Max(0f, LiveTowerTopWorldY - floorOriginY);
    /// <summary>World Y of the floor surface.</summary>
    public float floorOriginY => _runState.FloorOriginY;
    public int score => _runState.Score;
    /// <summary>Live count of real placed blocks still standing - the HUD total and the
    /// PlaceBlocks win metric. Goes down when a counting block is destroyed or falls off.</summary>
    public int placedBlocks => _runState.StandingBlocks;
    public int lives => _runState.Lives;
    // The difficulty ramp owns its base fall speed (and the cap applies to it); ability
    // effects compose as a multiplier IN THE GETTER, never by mutating the ramp value -
    // a mutate-then-restore multiplier is unrecoverable once the ramp writes again.
    // The Spawner stamps this onto each piece at spawn, and SetAbilityFallSpeedMultiplier
    // re-stamps the piece already in the air, so a change is felt on the current brick too.
    public float currentFallSpeed => _difficulty.BaseFallSpeed * _abilityFallSpeedMultiplier;
    /// <summary>The difficulty-ramped descent speed WITHOUT ability factors - what fast
    /// drops / flicks use, so an ability slow never fights a player who chose to go fast.</summary>
    public float BaseFallSpeed => _difficulty.BaseFallSpeed;
    /// <summary>The owned abilities' combined fall-speed multiplier (Air Brake, recovery /
    /// slo-mo windows). Applied to NORMAL descent only - fast drops ignore it.</summary>
    public float AbilityFallSpeedFactor => _abilityFallSpeedMultiplier;
    public GameModeConfig ActiveConfig => ActiveGameModeConfig;
    public BlockController LastPlacedBlock => _ledger != null ? _ledger.LastPlacedBlock : null;
    public RunResult CurrentRunResult => _runState.ToResult();
    /// <summary>The number of the chapter that owns the active level, or 0 if none is resolved
    /// (custom/endless). Used to chapter-gate ability offers (AbilityDefinition.minChapterNumber).</summary>
    public int CurrentChapterNumber => _currentChapterNumber;

    private int _currentChapterNumber;
    private float _abilityFallSpeedMultiplier = 1f;
    private StatusEffects _statusEffects;
    private BlockLedger _ledger;
    private readonly HashSet<object> _pauseOwners = new HashSet<object>();
    private readonly HashSet<object> _spawnHoldOwners = new HashSet<object>();
    // Phase is owner-keyed, mirroring pause: a modal/transient owner REQUESTS an overlay phase and
    // RELEASES it when it closes. The effective phase is the highest-priority outstanding request
    // (or Playing when none is held). See RequestPhase.
    private readonly Dictionary<object, GamePhase> _phaseRequests = new Dictionary<object, GamePhase>();
    private bool _gameOverLatched;
    private static readonly object LegacyPauseOwner = new object();
    private bool _spawnAvailabilityPublishingEnabled;
    private bool _spawnAvailabilityInitialized;
    private bool _lastSpawnAvailability;
    // Loss context, scoped by DuringBlockLoss around the frozen HandleLostBelowScreen call:
    // GameOver() reads whether the lost piece costs a life; BlockLedger suppresses the
    // posthumous placement score of a piece that fell off (it was lost, not placed).
    private bool _losingBlockCostsLife = true;
    // Live standing-top cache (see LiveTowerTopWorldY) + the 5 Hz publish that lets the HUD
    // height counter come DOWN after a collapse (HeightChanged historically fired only on new
    // peaks, from BlockLedger). 5 Hz because the walk touches every landed block and a lost
    // half-meter showing 0.2s late is invisible next to the camera's own glide.
    private const float LiveHeightPollInterval = 0.2f;
    private float _liveHeightPollTimer;
    private float _lastPublishedLiveHeight;
    // Unscaled-time throttle (0.15s) rather than a per-frame cache: the camera reads this
    // every rendered frame, including at timeScale=0 on pause/game-over screens, and a
    // per-frame walk over 100+ blocks' cell geometry cost ~0.1-0.25ms on mid-tier Android
    // for a value that only meaningfully changes on land/collapse (review 2026-08-11).
    private const float LiveTopRefreshSeconds = 0.15f;
    private float _liveTopCache;
    private float _liveTopCacheAt = float.NegativeInfinity;
    private GameModeConfig ActiveGameModeConfig => LevelSelectionState.ResolveGameMode(gameModeConfig);

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            BlockController.ResetRuntimeState();
            TowerHeightLimit.Reset(); // ceilings never leak between levels
            WaveRevealGate.Reset();   // nor a wave-transition spawn hold
            // Tutorial-scoped globals: their RuntimeInitializeOnLoadMethod resets only run once
            // per app/domain load, so a run that dies without a clean modifier teardown must not
            // leak an input lock or a lit nudge spotlight into the next run.
            TouchGestureInput.Suspended = false;
            UIManager.SetNudgeGuideBoost(0f);
            // A game type can grant free starting lives (the Flood: the full 3, so the run
            // has a real cost for dumped bricks without selling anything). Resolved once;
            // the level can't change mid-run. Applied in ApplyConfig below.
            _grantedRunLives = LevelSelectionState.SelectedLevel != null
                ? LevelSelectionState.SelectedLevel.GrantedRunLives : 0;
            // Resolve the active chapter once; skin must apply before any skinned visual
            // loads (the floor's ground skin is applied just below; block skins at spawn).
            ChapterDefinition activeChapter = Campaign.FindChapterOf(LevelSelectionState.SelectedLevel);
            _currentChapterNumber = activeChapter != null ? activeChapter.ChapterNumber : 0;
            ChapterSkins.Apply(activeChapter);
            MusicPlayer.PlayForChapter(activeChapter);
            PlayAreaController playAreaController = Object.FindAnyObjectByType<PlayAreaController>();
            if (playAreaController != null)
            {
                playAreaController.ApplyConfig();
                // Tower height is measured from the floor surface, not world zero - otherwise a
                // floor below y=0 makes the HUD read 0.0m until the tower crosses world zero.
                if (playAreaController.TryGetFloorTopWorldY(out float floorTopY))
                {
                    _runState.SetFloorOrigin(floorTopY);
                }
            }
            ApplyConfig();
            CameraIntroGate.SyncToGameManager(this);
            WaveRevealGate.SyncToGameManager(this);
            PublishState();

            // Ability/UI system stack: the installer owns the roster and the deterministic add
            // order (StatusEffects + AbilityRuntime must exist before the systems that resolve
            // them via GetComponent in their own Awake). Capture the status component after.
            GameSystemsInstaller.Install(gameObject);
            _statusEffects = GetComponent<StatusEffects>();
            _ledger = new BlockLedger(_runState, _difficulty, () => _statusEffects, () => isGameOver);
            _ledger.Subscribe();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        _spawnAvailabilityPublishingEnabled = true;
        RepublishSpawnAvailability();
    }

    // Owner-keyed phase, symmetric with PushPause/PopPause: an owner REQUESTS an overlay phase
    // (Intro, WinVerifying, AbilityChoice, Paused, Completed) while its screen/sequence is live and
    // RELEASES it when done. The effective CurrentPhase is the highest-priority outstanding request,
    // or Playing when none is held - so overlapping owners can never strand each other (the failure
    // a single "SetPhase slot" had: a pause opened over an ability choice used to lose the choice's
    // phase on resume), and an illegal transition is structurally impossible - you can only add or
    // drop your OWN request. GameOver is a one-way latch (see GameOver) that outranks every request.
    public void RequestPhase(object owner, GamePhase phase)
    {
        if (owner == null) return;
        if (_phaseRequests.TryGetValue(owner, out GamePhase existing) && existing == phase) return;

        _phaseRequests[owner] = phase;
        RecomputePhase();
    }

    public void ReleasePhase(object owner)
    {
        if (owner == null) return;
        if (!_phaseRequests.Remove(owner)) return;

        RecomputePhase();
    }

    private void RecomputePhase()
    {
        GamePhase effective = GamePhase.Playing;
        if (_gameOverLatched)
        {
            effective = GamePhase.GameOver;
        }
        else
        {
            foreach (GamePhase requested in _phaseRequests.Values)
            {
                if (PhasePriority(requested) > PhasePriority(effective)) effective = requested;
            }
        }

        if (effective == CurrentPhase) return;

        GamePhase previous = CurrentPhase;
        CurrentPhase = effective;
        GameEvents.RaisePhaseChanged(previous, CurrentPhase);
        RefreshSpawnAvailability();
    }

    // Higher wins when more than one owner holds a phase: a level completing or a pause opened
    // during an ability choice / win countdown must show the heavier screen, not the lighter one.
    private static int PhasePriority(GamePhase phase)
    {
        switch (phase)
        {
            case GamePhase.GameOver: return 7;
            case GamePhase.Completed: return 6;
            case GamePhase.Paused: return 5;   // a system pause must outrank a debut modal
            case GamePhase.Discovery: return 4;
            case GamePhase.AbilityChoice: return 3;
            case GamePhase.WinVerifying: return 2;
            case GamePhase.Intro: return 1;
            default: return 0; // Playing
        }
    }

    public void PushPause(object owner)
    {
        if (owner == null) owner = LegacyPauseOwner;
        if (!_pauseOwners.Add(owner)) return;

        RefreshPauseState();
    }

    public void PopPause(object owner)
    {
        if (owner == null) owner = LegacyPauseOwner;
        if (!_pauseOwners.Remove(owner)) return;

        RefreshPauseState();
    }

    public void SetSpawnSuspended(object owner, bool suspended)
    {
        if (owner == null) owner = this;

        bool changed = suspended
            ? _spawnHoldOwners.Add(owner)
            : _spawnHoldOwners.Remove(owner);
        if (changed) RefreshSpawnAvailability();
    }

    public void RepublishSpawnAvailability()
    {
        RefreshSpawnAvailability(force: true);
    }

    /// <summary>Compatibility wrapper for older callers. New modal owners should use PushPause/PopPause.</summary>
    public void SetGamePaused(bool paused)
    {
        if (paused) PushPause(LegacyPauseOwner);
        else PopPause(LegacyPauseOwner);
    }

    private void RefreshPauseState()
    {
        bool paused = _pauseOwners.Count > 0;
        if (IsGamePaused == paused) return;

        IsGamePaused = paused;
        RefreshTimeScale();
        RefreshSpawnAvailability();
    }

    // Single authority over Time.timeScale: it is only ever 1 (playing) or 0 (paused). Slow-time
    // abilities deliberately do NOT touch the clock - they slow only a block's NORMAL descent (via
    // the FallSpeedMultiplier status -> normal-fall-speed factor), so fast drops, physics, and the
    // rest of the simulation always run at full speed.
    private void RefreshTimeScale()
    {
        Time.timeScale = IsGamePaused ? 0f : 1f;
    }

    private void RefreshSpawnAvailability(bool force = false)
    {
        bool canSpawn = CanSpawnBlocks;
        if (!_spawnAvailabilityPublishingEnabled)
        {
            _spawnAvailabilityInitialized = true;
            _lastSpawnAvailability = canSpawn;
            return;
        }

        if (!force && _spawnAvailabilityInitialized && _lastSpawnAvailability == canSpawn) return;

        _spawnAvailabilityInitialized = true;
        _lastSpawnAvailability = canSpawn;
        GameEvents.RaiseSpawnAvailabilityChanged(canSpawn);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            _ledger?.Dispose();
            _ledger = null;
            Instance = null;
        }
    }

    private void ApplyConfig()
    {
        GameModeConfig activeConfig = ActiveGameModeConfig;
        if (activeConfig == null) return;

        _difficulty.ApplyConfig(activeConfig);
        // Type-granted lives (the Flood's free 3) floor the config's authored lives; both
        // sit under purchases, which top up afterwards (RunSuppliesApplier, capped at 3).
        _runState.SetLives(Mathf.Max(activeConfig.StartingLives, _grantedRunLives));
    }

    private void Update()
    {
        if (isGameOver) return;

        _difficulty.Tick(Time.deltaTime);
        PollLiveHeight(Time.deltaTime);
    }

    // Publishes the LIVE height whenever it moves - in either direction. BlockLedger still
    // raises instantly on a new peak (same value at that moment: the peak IS the block that
    // just locked); this poll is what brings the counter back down after a collapse.
    private void PollLiveHeight(float deltaTime)
    {
        _liveHeightPollTimer -= deltaTime;
        if (_liveHeightPollTimer > 0f) return;
        _liveHeightPollTimer = LiveHeightPollInterval;

        float live = liveTowerHeight;
        if (Mathf.Approximately(live, _lastPublishedLiveHeight)) return;
        _lastPublishedLiveHeight = live;
        GameEvents.RaiseHeightChanged(live);
    }

    private float ComputeLiveTowerTopWorldY()
    {
        float highest = floorOriginY;
        IReadOnlyList<BlockController> blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block == null || !block.HasLanded || block.IsFallingClearOfTower) continue;
            highest = Mathf.Max(highest, block.GetHighestCellY());
        }
        return highest;
    }

    private void PublishState()
    {
        GameEvents.RaiseScoreChanged(_runState.Score);
        GameEvents.RaiseStandingBlocksChanged(_runState.StandingBlocks);
        GameEvents.RaiseLivesChanged(_runState.Lives);
        GameEvents.RaiseHeightChanged(liveTowerHeight);
    }

    public void GameOver()
    {
        if (isGameOver) return;

        // A block flagged "free to lose" (e.g. a projectile-style piece) never costs a life
        // when it falls off - it isn't a real block. Set per-loss by ReportBlockLost.
        if (!_losingBlockCostsLife)
        {
            return;
        }

        // A LifeLossImmunity status (ability-granted "game state") absorbs every life
        // charge while active - a whole-tower collapse during the window costs nothing
        // beyond whatever opened it. Checked before the final-death branch too: immune
        // means immune.
        if (_statusEffects != null && _statusEffects.IsActive(StatusEffectKind.LifeLossImmunity))
        {
            return;
        }

        if (_runState.TrySpendLife())
        {
            GameEvents.RaiseLivesChanged(_runState.Lives);
            GameEvents.RaiseLifeLost();
            Debug.Log($"Life lost! Remaining: {_runState.Lives}");
            return;
        }

        FinishRun("Game Over");
    }

    /// <summary>Terminal failure that bypasses per-block life/immunity rules: used by level goals
    /// such as timeouts where the challenge itself was failed, not a block-loss charge.</summary>
    public void EndRunNow(string reason) => FinishRun(reason);

    private void FinishRun(string reason)
    {
        if (isGameOver) return;

        isGameOver = true;
        _gameOverLatched = true; // terminal: outranks every phase request until a scene reload
        RecomputePhase();

        GameEvents.RaiseGameOver(_runState.Score, towerHeight);
        Debug.Log(string.IsNullOrWhiteSpace(reason) ? "Game Over" : reason);
    }

    private bool _restartPending;

    public void RestartGame()
    {
        // Try Again is a NEW run: it must win its own start_run grant before the reload
        // (BACKEND.md §6.1 - each loss nets exactly one attempt, retries included). The
        // gate answers instantly for Custom Game / online-disabled; while a server answer
        // is pending, further clicks are ignored. A denial (out of attempts, offline)
        // lands back on the menu, where the level modal owns the messaging.
        if (_restartPending) return;
        _restartPending = true;
        RunGate.BeginRun(LevelSelectionState.SelectedLevel, boosted: false, loadoutJson: null, result =>
        {
            _restartPending = false;
            if (this == null) return; // scene tore down while the grant was in flight
            if (!result.Allowed)
            {
                // "busy" = another grant already in flight (e.g. a menu PLAY racing this
                // retry) - swallow it; that grant's landing decides what happens next.
                if (result.DeniedReason != "busy") MainMenuRuntime.ReturnToMenu();
                return;
            }
            Time.timeScale = 1f;
            _pauseOwners.Clear();
            _phaseRequests.Clear();
            _gameOverLatched = false;
            IsGamePaused = false;
            BlockController.ResetRuntimeState();
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        });
    }

    public void AddLife()
    {
        _runState.AddLife();
        GameEvents.RaiseLivesChanged(_runState.Lives);
        Debug.Log($"Life added! Total: {_runState.Lives}");
    }

    /// <summary>The Slow Descent supply (SHOP.md §3.2): one flat scale on the whole authored
    /// speed curve, applied by RunSuppliesApplier at run start - independent of the ability
    /// fall-speed multiplier, which AbilityRuntime recomputes freely all run.</summary>
    public void ApplyRunSupplySpeedScale(float multiplier)
    {
        _difficulty.ScaleSpeeds(multiplier);
    }

    /// <summary>Composed multiplier from abilities/status effects; pushed by AbilityRuntime
    /// on inventory/lives/status changes, never per frame.</summary>
    public void SetAbilityFallSpeedMultiplier(float multiplier)
    {
        _abilityFallSpeedMultiplier = Mathf.Clamp(multiplier, 0.1f, 3f);

        // The factor is LIVE, not just a spawn stamp: re-stamp the brick already falling so a slow
        // used mid-flight is felt on THIS piece. Tapping Slo-Mo and watching nothing happen until
        // the next brick spawned was the whole complaint. Scripted rides (the tutorial's pre-roll)
        // pin their piece and are left alone.
        BlockController active = BlockController.LiveActivePiece;
        if (active != null && !active.NormalFallSpeedPinned)
        {
            active.SetNormalFallSpeedFactor(_abilityFallSpeedMultiplier);
        }
    }

    /// <summary>Runs the (frozen) per-block loss inside the loss policy: GameOver() learns
    /// whether this block costs a life, the posthumous lock-score is suppressed, and a
    /// counted block is dropped from the live total - exactly once. The try/finally keeps
    /// a throw in the frozen call from stranding the flags (which would silently disable
    /// all future scoring and life charges). The only entry point - callers never touch
    /// the loss flags directly, so the global side-channel can't be mis-scoped.</summary>
    public void DuringBlockLoss(BlockController block, System.Action lossAction)
    {
        _ledger?.BeginBlockLoss(block);
        _losingBlockCostsLife = BlockData.CostsLife(block);

        try { lossAction?.Invoke(); }
        finally
        {
            _ledger?.EndBlockLoss();
            _losingBlockCostsLife = true;
        }
    }

    /// <summary>Charge one life for a hazard that is NOT a fall-off loss (e.g. the Maw devouring a block).
    /// Routes through GameOver so it respects LifeLossImmunity and runs the identical end-of-run sequence
    /// on the last life; a maw bite always costs a life regardless of the eaten block's own life flag.</summary>
    public void LoseLifeToHazard()
    {
        _losingBlockCostsLife = true;
        GameOver();
    }

}
