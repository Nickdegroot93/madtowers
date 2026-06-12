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
    [SerializeField] private int _lives = 1;

    public bool isGameOver { get; private set; }
    public bool IsGamePaused { get; private set; }
    public float maxHeight => _maxHeight;
    /// <summary>Tower height in meters above the floor (what the HUD shows). maxHeight stays world-space for the camera/spawners.</summary>
    public float towerHeight => Mathf.Max(0f, _maxHeight - _heightOriginY);
    /// <summary>World Y of the floor surface.</summary>
    public float floorOriginY => _heightOriginY;
    public int score => _score;
    public int lives => _lives;
    public float currentFallSpeed => _currentFallSpeed;
    public GameModeConfig ActiveConfig => ActiveGameModeConfig;

    private Coroutine _slowMotionCoroutine;
    private float _speedTimer;
    private float _heightOriginY;
    private float _gameplayTimeScale = 1f;
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

            if (GetComponent<PowerUpChoiceController>() == null)
            {
                gameObject.AddComponent<PowerUpChoiceController>();
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
        GameEvents.RaiseLivesChanged(_lives);
        GameEvents.RaiseHeightChanged(towerHeight);
    }

    public void GameOver()
    {
        if (isGameOver) return;
        
        if (_lives > 0)
        {
            _lives--;
            GameEvents.RaiseLivesChanged(_lives);
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

    public void AddScore(int amount = 1)
    {
        if (isGameOver) return;
        _score += amount;
        if (_difficultyScalingMode == DifficultyScalingMode.PerBlock)
        {
            IncreaseDifficulty(_speedIncreasePerBlock * amount);
        }
        GameEvents.RaiseScoreChanged(_score);
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
