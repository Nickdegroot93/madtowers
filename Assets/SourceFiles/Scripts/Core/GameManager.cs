using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [SerializeField] private GameModeConfig gameModeConfig;
    [SerializeField] private float _currentFallSpeed = 2.0f;
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
    public float maxHeight => _maxHeight;
    public int score => _score;
    public int lives => _lives;
    public float currentFallSpeed => _currentFallSpeed;
    public float currentGravityScale => _currentGravityScale;

    private Coroutine _slowMotionCoroutine;
    private float _speedTimer;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            BlockController.ResetRuntimeState();
            PlayAreaController playAreaController = Object.FindAnyObjectByType<PlayAreaController>();
            if (playAreaController != null) playAreaController.ApplyConfig();
            ApplyConfig();
            PublishState();
        }
        else
        {
            Destroy(gameObject);
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
        if (gameModeConfig == null) return;

        _currentFallSpeed = gameModeConfig.InitialFallSpeed;
        _currentGravityScale = gameModeConfig.InitialGravityScale;
        _difficultyScalingMode = gameModeConfig.DifficultyScalingMode;
        _difficultyAdjustmentMode = gameModeConfig.DifficultyAdjustmentMode;
        _speedIncreasePerBlock = gameModeConfig.SpeedIncreasePerBlock;
        _gravityIncreasePerBlock = gameModeConfig.GravityIncreasePerBlock;
        _speedIncreaseIntervalSeconds = gameModeConfig.SpeedIncreaseIntervalSeconds;
        _speedIncreasePerInterval = gameModeConfig.SpeedIncreasePerInterval;
        _gravityIncreasePerInterval = gameModeConfig.GravityIncreasePerInterval;
        _lives = gameModeConfig.StartingLives;
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
        GameEvents.RaiseHeightChanged(_maxHeight);
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
        GameEvents.RaiseGameOver(_score, _maxHeight);
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
        Time.timeScale = gameModeConfig != null ? gameModeConfig.SlowMotionScale : 0.5f;
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
            return;
        }

        _currentFallSpeed += fallSpeedAmount;
        _currentGravityScale += gravityAmount;
    }

    public void UpdateMaxHeight(float height)
    {
        if (height > _maxHeight)
        {
            _maxHeight = height;
            GameEvents.RaiseHeightChanged(_maxHeight);
        }
    }
}
