using UnityEngine;

[System.Serializable]
public sealed class DifficultyController
{
    [SerializeField] private float _currentFallSpeed = 2.0f;
    [SerializeField] private float _maxFallSpeed = 5.0f;
    [SerializeField] private DifficultyScalingMode _scalingMode = DifficultyScalingMode.PerBlock;
    [SerializeField] private DifficultyAdjustmentMode _adjustmentMode = DifficultyAdjustmentMode.Additive;
    [SerializeField] private float _speedIncreasePerBlock = 0.1f;
    [SerializeField] private float _speedIncreaseIntervalSeconds = 60f;
    [SerializeField] private float _speedIncreasePerInterval = 0.1f;

    private float _speedTimer;

    public float BaseFallSpeed => _currentFallSpeed;

    public void ApplyConfig(GameModeConfig config)
    {
        if (config == null) return;

        _currentFallSpeed = config.InitialFallSpeed;
        _maxFallSpeed = config.MaxFallSpeed;
        _scalingMode = config.DifficultyScalingMode;
        _adjustmentMode = config.DifficultyAdjustmentMode;
        _speedIncreasePerBlock = config.SpeedIncreasePerBlock;
        _speedIncreaseIntervalSeconds = config.SpeedIncreaseIntervalSeconds;
        _speedIncreasePerInterval = config.SpeedIncreasePerInterval;
        _speedTimer = 0f;
    }

    public void Tick(float deltaTime)
    {
        if (_scalingMode != DifficultyScalingMode.OverTime) return;

        _speedTimer += deltaTime;
        while (_speedTimer >= _speedIncreaseIntervalSeconds)
        {
            _speedTimer -= _speedIncreaseIntervalSeconds;
            Increase(_speedIncreasePerInterval);
        }
    }

    public void RegisterScoredBlocks(int baseAmount)
    {
        if (_scalingMode != DifficultyScalingMode.PerBlock) return;

        Increase(_speedIncreasePerBlock * Mathf.Max(0, baseAmount));
    }

    private void Increase(float fallSpeedAmount)
    {
        if (_adjustmentMode == DifficultyAdjustmentMode.Percent)
        {
            _currentFallSpeed *= 1f + fallSpeedAmount;
        }
        else
        {
            _currentFallSpeed += fallSpeedAmount;
        }

        _currentFallSpeed = Mathf.Min(_currentFallSpeed, _maxFallSpeed);
    }
}
