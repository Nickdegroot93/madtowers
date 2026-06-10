using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [SerializeField] private GameModeConfig gameModeConfig;
    [SerializeField] private float _currentFallSpeed = 2.0f;
    [SerializeField] private float _maxFallSpeed = 5.0f;
    [SerializeField] private float _currentGravityScale = 1.0f;
    [SerializeField] private DifficultyScalingMode _difficultyScalingMode = DifficultyScalingMode.PerBlock;
    [SerializeField] private DifficultyAdjustmentMode _difficultyAdjustmentMode = DifficultyAdjustmentMode.Additive;
    [SerializeField] private float _speedIncreasePerBlock = 0.1f;
    [SerializeField] private float _gravityIncreasePerBlock = 0.05f;
    [SerializeField] private float _speedIncreaseIntervalSeconds = 60f;
    [SerializeField] private float _speedIncreasePerInterval = 0.1f;
    [SerializeField] private float _gravityIncreasePerInterval = 0.05f;
    [SerializeField] private float _maxHeight = 0f;
    [SerializeField] private int _score = 0;
    [SerializeField] private int _lives = 1;

    public bool isGameOver { get; private set; }
    public bool IsGamePaused { get; private set; }
    public float maxHeight => _maxHeight;
    /// <summary>Tower height in meters above the floor (what the HUD shows). maxHeight stays world-space for the camera/spawners.</summary>
    public float towerHeight => Mathf.Max(0f, _maxHeight - _heightOriginY);
    public int score => _score;
    public int lives => _lives;
    public float currentFallSpeed => _currentFallSpeed;
    public float currentGravityScale => _currentGravityScale;
    public GameModeConfig ActiveConfig => ActiveGameModeConfig;

    private Coroutine _slowMotionCoroutine;
    private float _speedTimer;
    private float _heightOriginY;
    private float _timeScaleBeforePause = 1f;
    private GameModeConfig ActiveGameModeConfig => LevelSelectionState.ResolveGameMode(gameModeConfig);

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            BlockController.ResetRuntimeState();
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

    /// <summary>Full pause used by the power-up choice screen. Restores the previous time scale on resume.</summary>
    public void SetGamePaused(bool paused)
    {
        if (IsGamePaused == paused) return;

        IsGamePaused = paused;
        if (paused)
        {
            _timeScaleBeforePause = Time.timeScale;
            Time.timeScale = 0f;
        }
        else
        {
            Time.timeScale = _timeScaleBeforePause;
        }
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
        _currentGravityScale = activeConfig.InitialGravityScale;
        _difficultyScalingMode = activeConfig.DifficultyScalingMode;
        _difficultyAdjustmentMode = activeConfig.DifficultyAdjustmentMode;
        _speedIncreasePerBlock = activeConfig.SpeedIncreasePerBlock;
        _gravityIncreasePerBlock = activeConfig.GravityIncreasePerBlock;
        _speedIncreaseIntervalSeconds = activeConfig.SpeedIncreaseIntervalSeconds;
        _speedIncreasePerInterval = activeConfig.SpeedIncreasePerInterval;
        _gravityIncreasePerInterval = activeConfig.GravityIncreasePerInterval;
        _lives = activeConfig.StartingLives;
    }

    private void Update()
    {
        if (isGameOver || _difficultyScalingMode != DifficultyScalingMode.OverTime) return;

        _speedTimer += Time.deltaTime;
        while (_speedTimer >= _speedIncreaseIntervalSeconds)
        {
            _speedTimer -= _speedIncreaseIntervalSeconds;
            IncreaseDifficulty(_speedIncreasePerInterval, _gravityIncreasePerInterval);
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
        float originalTimeScale = Time.timeScale;
        GameModeConfig activeConfig = ActiveGameModeConfig;
        Time.timeScale = activeConfig != null ? activeConfig.SlowMotionScale : 0.5f;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = originalTimeScale;
        _slowMotionCoroutine = null;
    }

    public void AddScore(int amount = 1)
    {
        if (isGameOver) return;
        _score += amount;
        if (_difficultyScalingMode == DifficultyScalingMode.PerBlock)
        {
            IncreaseDifficulty(_speedIncreasePerBlock * amount, _gravityIncreasePerBlock * amount);
        }
        GameEvents.RaiseScoreChanged(_score);
    }

    private void IncreaseDifficulty(float fallSpeedAmount, float gravityAmount)
    {
        if (_difficultyAdjustmentMode == DifficultyAdjustmentMode.Percent)
        {
            _currentFallSpeed *= 1f + fallSpeedAmount;
            _currentGravityScale *= 1f + gravityAmount;
        }
        else
        {
            _currentFallSpeed += fallSpeedAmount;
            _currentGravityScale += gravityAmount;
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
