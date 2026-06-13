using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [SerializeField] private GameModeConfig gameModeConfig;
    [SerializeField] private float _currentFallSpeed = 2.0f;
    [SerializeField] private float _maxFallSpeed = 5.0f;
    [SerializeField] private DifficultyScalingMode _difficultyScalingMode = DifficultyScalingMode.PerBlock;
    [SerializeField] private DifficultyAdjustmentMode _difficultyAdjustmentMode = DifficultyAdjustmentMode.Additive;
    [SerializeField] private float _speedIncreasePerBlock = 0.1f;
    [SerializeField] private float _speedIncreaseIntervalSeconds = 60f;
    [SerializeField] private float _speedIncreasePerInterval = 0.1f;
    [SerializeField] private float _maxHeight = 0f;
    [SerializeField] private int _score = 0;
    [SerializeField] private int _standingBlocks = 0;
    [SerializeField] private int _lives = 1;

    public bool isGameOver { get; private set; }
    public bool IsGamePaused { get; private set; }
    public float maxHeight => _maxHeight;
    /// <summary>Tower height in meters above the floor (what the HUD shows). maxHeight stays world-space for the camera/spawners.</summary>
    public float towerHeight => Mathf.Max(0f, _maxHeight - _heightOriginY);
    /// <summary>World Y of the floor surface.</summary>
    public float floorOriginY => _heightOriginY;
    public int score => _score;
    /// <summary>Live count of real placed blocks still standing - the HUD total and the
    /// PlaceBlocks win metric. Goes down when a counting block is destroyed or falls off.</summary>
    public int placedBlocks => _standingBlocks;
    public int lives => _lives;
    // The difficulty ramp owns _currentFallSpeed (and the cap applies to it); ability
    // effects compose as a multiplier IN THE GETTER, never by mutating the ramp value -
    // a mutate-then-restore multiplier is unrecoverable once the ramp writes again.
    // The Spawner stamps this onto each piece at spawn, so changes apply next piece.
    public float currentFallSpeed => _currentFallSpeed * _abilityFallSpeedMultiplier;
    public GameModeConfig ActiveConfig => ActiveGameModeConfig;

    private Coroutine _slowMotionCoroutine;
    private float _speedTimer;
    private float _heightOriginY;
    private float _gameplayTimeScale = 1f;
    private float _abilityFallSpeedMultiplier = 1f;
    private StatusEffects _statusEffects;
    // The piece currently in play + its flags, reported by the Spawner as it's wired
    // (both fresh spawns and mid-fall variant swaps). The lock-time AddScore call from
    // BlockController is param-less and fires after ActiveControlled has already cleared,
    // so this is how scoring learns which piece is locking and whether it counts.
    private BlockController _activeBlock;
    private BlockData _activeBlockData;
    // Loss context, scoped by DuringBlockLoss around the frozen HandleLostBelowScreen call:
    // GameOver() reads whether the lost piece costs a life, and AddScore() suppresses the
    // posthumous lock-score of a piece that fell off (it was lost, not placed).
    private bool _losingBlockCostsLife = true;
    private bool _inBlockLoss;
    private GameModeConfig ActiveGameModeConfig => LevelSelectionState.ResolveGameMode(gameModeConfig);

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            BlockController.ResetRuntimeState();
            TowerHeightLimit.Reset(); // ceilings never leak between levels
            // Resolve the active theme once; skin must apply before any skinned visual
            // loads (the floor's ground skin is applied just below; block skins at spawn).
            ThemeDefinition activeTheme = Campaign.FindThemeOf(LevelSelectionState.SelectedLevel);
            ThemeSkins.Apply(activeTheme);
            MusicPlayer.PlayForTheme(activeTheme);
            PlayAreaController playAreaController = Object.FindAnyObjectByType<PlayAreaController>();
            if (playAreaController != null)
            {
                playAreaController.ApplyConfig();
                // Tower height is measured from the floor surface, not world zero - otherwise a
                // floor below y=0 makes the HUD read 0.0m until the tower crosses world zero.
                if (playAreaController.TryGetFloorTopWorldY(out float floorTopY))
                {
                    _heightOriginY = floorTopY;
                    _maxHeight = floorTopY;
                }
            }
            ApplyConfig();
            PublishState();

            // Ability stack: status effects and the runtime must exist before anything
            // that resolves them via GetComponent in Awake (detector, HUD, controller).
            // Never ?? on a UnityEngine.Object: the editor's fake-null wrapper passes a
            // reference-null check and would silently skip the AddComponent.
            if (!TryGetComponent(out _statusEffects))
            {
                _statusEffects = gameObject.AddComponent<StatusEffects>();
            }
            if (GetComponent<AbilityRuntime>() == null)
            {
                gameObject.AddComponent<AbilityRuntime>();
            }
            if (GetComponent<ComboDetector>() == null)
            {
                gameObject.AddComponent<ComboDetector>();
            }
            if (GetComponent<AbilityHud>() == null)
            {
                gameObject.AddComponent<AbilityHud>();
            }
            if (GetComponent<AbilityChoiceController>() == null)
            {
                gameObject.AddComponent<AbilityChoiceController>();
            }
            if (GetComponent<PauseMenuController>() == null)
            {
                gameObject.AddComponent<PauseMenuController>();
            }
            if (GetComponent<LevelRuntimeController>() == null)
            {
                gameObject.AddComponent<LevelRuntimeController>();
            }
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>The Spawner reports each piece as it's wired up (both normal spawns and
    /// mid-fall replacements/variant swaps), so the param-less lock-score call from
    /// BlockController can tell which piece is locking and whether it counts.</summary>
    public void SetActivePiece(BlockController block, BlockData data)
    {
        _activeBlock = block;
        _activeBlockData = data;
    }

    /// <summary>Full pause used by the choice/completion screens.</summary>
    public void SetGamePaused(bool paused)
    {
        if (IsGamePaused == paused) return;

        IsGamePaused = paused;
        RefreshTimeScale();
    }

    // Single authority over Time.timeScale: pause always wins, slow motion applies underneath.
    // (Letting pause and slow motion each save/restore the timescale froze the game permanently
    // when a slow-motion ended that had started during a pause.)
    private void RefreshTimeScale()
    {
        Time.timeScale = IsGamePaused ? 0f : _gameplayTimeScale;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void ApplyConfig()
    {
        GameModeConfig activeConfig = ActiveGameModeConfig;
        if (activeConfig == null) return;

        _currentFallSpeed = activeConfig.InitialFallSpeed;
        _maxFallSpeed = activeConfig.MaxFallSpeed;
        _difficultyScalingMode = activeConfig.DifficultyScalingMode;
        _difficultyAdjustmentMode = activeConfig.DifficultyAdjustmentMode;
        _speedIncreasePerBlock = activeConfig.SpeedIncreasePerBlock;
        _speedIncreaseIntervalSeconds = activeConfig.SpeedIncreaseIntervalSeconds;
        _speedIncreasePerInterval = activeConfig.SpeedIncreasePerInterval;
        _lives = activeConfig.StartingLives;
    }

    private void Update()
    {
        if (isGameOver || _difficultyScalingMode != DifficultyScalingMode.OverTime) return;

        _speedTimer += Time.deltaTime;
        while (_speedTimer >= _speedIncreaseIntervalSeconds)
        {
            _speedTimer -= _speedIncreaseIntervalSeconds;
            IncreaseDifficulty(_speedIncreasePerInterval);
        }
    }

    private void PublishState()
    {
        GameEvents.RaiseScoreChanged(_score);
        GameEvents.RaiseStandingBlocksChanged(_standingBlocks);
        GameEvents.RaiseLivesChanged(_lives);
        GameEvents.RaiseHeightChanged(towerHeight);
    }

    public void GameOver()
    {
        if (isGameOver) return;

        // A block flagged "free to lose" (e.g. the Bullet projectile) never costs a life
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

        if (_lives > 0)
        {
            _lives--;
            GameEvents.RaiseLivesChanged(_lives);
            GameEvents.RaiseLifeLost();
            Debug.Log($"Life lost! Remaining: {_lives}");
            return;
        }

        isGameOver = true;

        // A run can end mid slow-motion: without this the wreckage plays out at 0.5x and
        // then visibly snaps to full speed when the effect's timer expires under the panel.
        if (_slowMotionCoroutine != null)
        {
            StopCoroutine(_slowMotionCoroutine);
            _slowMotionCoroutine = null;
        }
        _gameplayTimeScale = 1f;
        RefreshTimeScale();

        GameEvents.RaiseGameOver(_score, towerHeight);
        Debug.Log("Game Over");
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        BlockController.ResetRuntimeState();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void AddLife()
    {
        _lives++;
        GameEvents.RaiseLivesChanged(_lives);
        Debug.Log($"Life added! Total: {_lives}");
    }

    public void ApplySlowMotion(float duration)
    {
        if (_slowMotionCoroutine != null)
        {
            StopCoroutine(_slowMotionCoroutine);
        }
        _slowMotionCoroutine = StartCoroutine(SlowMotionRoutine(duration));
    }

    private System.Collections.IEnumerator SlowMotionRoutine(float duration)
    {
        GameModeConfig activeConfig = ActiveGameModeConfig;
        _gameplayTimeScale = activeConfig != null ? activeConfig.SlowMotionScale : 0.5f;
        RefreshTimeScale();

        // The duration is seconds of PLAYED time at the slowed rate: a realtime wait burned
        // the whole effect while the game sat paused (pause menu, power-up picker), consuming
        // the power-up with zero slowed gameplay. Tick only while actually playing.
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (!IsGamePaused) elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        _gameplayTimeScale = 1f;
        RefreshTimeScale();
        _slowMotionCoroutine = null;
    }

    /// <summary>Composed multiplier from abilities/status effects; pushed by AbilityRuntime
    /// on inventory/lives/status changes, never per frame.</summary>
    public void SetAbilityFallSpeedMultiplier(float multiplier)
    {
        _abilityFallSpeedMultiplier = Mathf.Clamp(multiplier, 0.1f, 3f);
    }

    public void AddScore(int amount = 1)
    {
        if (isGameOver) return;

        // A piece lost off the bottom locks "posthumously" (frozen BlockController calls
        // this on the way out) - that is a loss, not a placement, so it never scores.
        if (_inBlockLoss) return;

        // Pieces that aren't real blocks (the Bullet projectile) don't score or count.
        if (_activeBlockData != null && !_activeBlockData.CountsAsPlacedBlock) return;

        // Overdrive-style states amplify EVERY score grant while active. Score is the
        // progression currency (win targets, picker milestones, wave counts) - that
        // amplification accelerating them all is the designed effect.
        int baseAmount = amount;
        if (_statusEffects != null)
        {
            amount += _statusEffects.ExtraScorePerBlock;
        }

        _score += amount;
        if (_difficultyScalingMode == DifficultyScalingMode.PerBlock)
        {
            // Difficulty ramps on the UNAMPLIFIED amount: Overdrive accelerates the
            // player's progression, never the game's speed against them.
            IncreaseDifficulty(_speedIncreasePerBlock * baseAmount);
        }
        GameEvents.RaiseScoreChanged(_score);

        // The live standing count tracks PHYSICAL blocks (one per placed piece), so it is
        // never amplified - Overdrive is a progression bonus, not extra real blocks. Record
        // the count on the block itself so its eventual -1 fires exactly once when it leaves.
        AdjustStandingBlocks(1);
        if (_activeBlock != null && _activeBlock.TryGetComponent(out BlockIdentity identity))
        {
            identity.MarkCountedAsPlaced();
        }
    }

    // The live count of real placed blocks present (HUD total + PlaceBlocks win). Clamped
    // at zero so a stray double-remove can never drive the displayed total negative.
    private void AdjustStandingBlocks(int delta)
    {
        int next = Mathf.Max(0, _standingBlocks + delta);
        if (next == _standingBlocks) return;
        _standingBlocks = next;
        GameEvents.RaiseStandingBlocksChanged(_standingBlocks);
    }

    /// <summary>A placed block has left the board by destruction (a Bullet hit now; the
    /// puzzle laser later). Drops it from the live total exactly once if its placement was
    /// counted (idempotent - a double-call is a no-op). The caller still destroys it.</summary>
    public void RemovePlacedBlock(BlockController block)
    {
        if (block != null && block.TryGetComponent(out BlockIdentity identity) && identity.TryConsumeCounted())
        {
            AdjustStandingBlocks(-1);
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
        _inBlockLoss = true;
        _losingBlockCostsLife = BlockData.CostsLife(block);

        // A landed, counted block leaving costs the board one block; an active piece pushed
        // off was never counted (TryConsumeCounted returns false), so nothing is subtracted.
        if (block != null && block.TryGetComponent(out BlockIdentity identity) && identity.TryConsumeCounted())
        {
            AdjustStandingBlocks(-1);
        }

        try { lossAction?.Invoke(); }
        finally
        {
            _inBlockLoss = false;
            _losingBlockCostsLife = true;
        }
    }

    private void IncreaseDifficulty(float fallSpeedAmount)
    {
        if (_difficultyAdjustmentMode == DifficultyAdjustmentMode.Percent)
        {
            _currentFallSpeed *= 1f + fallSpeedAmount;
        }
        else
        {
            _currentFallSpeed += fallSpeedAmount;
        }

        _currentFallSpeed = Mathf.Min(_currentFallSpeed, _maxFallSpeed);
    }

    public void UpdateMaxHeight(float height)
    {
        if (height > _maxHeight)
        {
            _maxHeight = height;
            GameEvents.RaiseHeightChanged(towerHeight);
        }
    }
}
