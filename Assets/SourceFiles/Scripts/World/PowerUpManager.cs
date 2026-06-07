using UnityEngine;

public class PowerUpManager : MonoBehaviour
{
    [SerializeField] private GameModeConfig gameModeConfig;
    [SerializeField] private GameObject _powerUpPrefab;
    [SerializeField] private float _spawnInterval = 10f;
    [SerializeField] private float _spawnXRange = 4f;

    private readonly RuntimeObjectPool _pool = new RuntimeObjectPool();
    private float _lastSpawnHeight = 0f;

    private void Update()
    {
        if (GameManager.Instance == null) return;

        float currentMaxHeight = GameManager.Instance.maxHeight;
        
        // Ensure we spawn at 10, 20, etc. even if maxHeight jumps
        float interval = gameModeConfig != null ? gameModeConfig.PowerUpSpawnInterval : _spawnInterval;

        while (currentMaxHeight >= _lastSpawnHeight + interval)
        {
            _lastSpawnHeight += interval;
            SpawnPowerUp(_lastSpawnHeight);
        }
    }

    private void SpawnPowerUp(float height)
    {
        if (_powerUpPrefab == null) return;

        float spawnRange = gameModeConfig != null ? gameModeConfig.PowerUpSpawnXRange : _spawnXRange;
        float randomX = Mathf.Round(Random.Range(-spawnRange, spawnRange));
        Vector3 spawnPosition = new Vector3(randomX, height, 0f);
        
        GameObject powerUpObj = _pool.Get(_powerUpPrefab, spawnPosition, Quaternion.identity);
        PowerUp powerUp = powerUpObj.GetComponent<PowerUp>();
        
        if (powerUp != null)
        {
            powerUp.Collected -= HandlePowerUpCollected;
            powerUp.Collected += HandlePowerUpCollected;
            PowerUpType randomType = (PowerUpType)Random.Range(0, System.Enum.GetValues(typeof(PowerUpType)).Length);
            float slowMotionDuration = gameModeConfig != null ? gameModeConfig.SlowMotionDuration : 10f;
            powerUp.Initialize(randomType, slowMotionDuration);
        }
    }

    private void HandlePowerUpCollected(PowerUp powerUp)
    {
        if (powerUp == null) return;
        powerUp.Collected -= HandlePowerUpCollected;
        _pool.Release(_powerUpPrefab, powerUp.gameObject);
    }
}
