using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class TowerCameraController : MonoBehaviour
{
    [SerializeField] private GameModeConfig gameModeConfig;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform[] verticalFollowers;
    [SerializeField] private float fallbackMinimumY = 0f;
    [Range(0.35f, 0.9f)]
    [SerializeField] private float fallbackTowerPeakScreenY = 0.5f;
    [Range(0.5f, 0.98f)]
    [SerializeField] private float fallbackSpawnPointScreenY = 0.9f;
    [SerializeField] private float fallbackSmoothTime = 0.35f;
    [SerializeField] private float fallbackMinimumCameraSize = 15f;
    [SerializeField] private float fallbackMaximumCameraSize = 24f;
    [SerializeField] private float fallbackHorizontalPadding = 1.5f;
    [Range(0.5f, 1f)]
    [SerializeField] private float fallbackHorizontalSafeArea = 0.78f;
    [SerializeField] private float fallbackZoomSmoothTime = 0.35f;

    private static TowerCameraController _instance;

    private Camera _camera;
    private float _verticalVelocity;
    private float _zoomVelocity;
    private float _highestCameraY;
    private float _baseY;
    private float _shakeTime;
    private float _shakeDuration;
    private float _shakeAmplitude;

    private void Awake()
    {
        _instance = this;
        _camera = GetComponent<Camera>();
        if (_camera.orthographic)
        {
            _camera.orthographicSize = Mathf.Clamp(_camera.orthographicSize, MinimumCameraSize, MaximumCameraSize);
        }

        _highestCameraY = Mathf.Max(transform.position.y, MinimumCameraY);
        _baseY = _highestCameraY;
        SetCameraY(_highestCameraY);
        UpdateSpawnPoint();
        UpdateVerticalFollowers();
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    // Purely visual impact shake (e.g. a flick-dropped piece landing). The shake is a
    // render-only offset added on top of the smoothed base position - the smoothing state
    // itself never sees it, and physics never reads the camera, so the tower is unaffected.
    public static void Impact(float amplitude = 0.16f, float duration = 0.22f)
    {
        if (_instance == null) return;
        _instance._shakeAmplitude = amplitude;
        _instance._shakeDuration = duration;
        _instance._shakeTime = duration;
    }

    private void LateUpdate()
    {
        float targetY = GetTargetCameraY();
        _highestCameraY = Mathf.Max(_highestCameraY, targetY);

        float smoothTime = Mathf.Max(0.01f, CameraSmoothTime);
        _baseY = Mathf.SmoothDamp(
            _baseY,
            _highestCameraY,
            ref _verticalVelocity,
            smoothTime);

        SetCameraY(_baseY + GetShakeOffset());
        UpdateZoom();
        UpdateSpawnPoint();
        UpdateVerticalFollowers();
    }

    // Damped vertical thump: a quick oscillation whose envelope falls off quadratically.
    private float GetShakeOffset()
    {
        if (_shakeTime <= 0f) return 0f;

        _shakeTime -= Time.deltaTime;
        if (_shakeTime <= 0f) return 0f;

        float remaining = _shakeTime / Mathf.Max(0.0001f, _shakeDuration); // 1 -> 0
        return Mathf.Sin((1f - remaining) * 30f) * _shakeAmplitude * remaining * remaining;
    }

    private void UpdateZoom()
    {
        if (_camera == null || !_camera.orthographic) return;

        float targetSize = GetTargetCameraSize();
        _camera.orthographicSize = Mathf.SmoothDamp(
            _camera.orthographicSize,
            targetSize,
            ref _zoomVelocity,
            CameraZoomSmoothTime);
    }

    private float GetTargetCameraSize()
    {
        if (!TryGetFocusedBlockBounds(out Bounds focusedBounds))
        {
            return MinimumCameraSize;
        }

        float cameraX = transform.position.x;
        float farthestHorizontalExtent = Mathf.Max(
            Mathf.Abs(focusedBounds.min.x - cameraX),
            Mathf.Abs(focusedBounds.max.x - cameraX));
        float aspect = Mathf.Max(0.01f, _camera.aspect);
        float safeAspect = aspect * HorizontalCameraSafeArea;
        float requiredHorizontalSize = (farthestHorizontalExtent + HorizontalCameraPadding) / safeAspect;
        return Mathf.Clamp(requiredHorizontalSize, MinimumCameraSize, MaximumCameraSize);
    }

    private bool TryGetFocusedBlockBounds(out Bounds focusedBounds)
    {
        focusedBounds = default;
        bool hasBounds = false;
        float focusHalfHeight = MinimumCameraSize;
        float minY = transform.position.y - focusHalfHeight;
        float maxY = transform.position.y + focusHalfHeight;
        IReadOnlyList<BlockController> blocks = BlockController.AllBlocks;

        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block == null) continue;
            if (!block.HasLanded) continue;
            if (!block.TryGetWorldBounds(out Bounds blockBounds)) continue;
            if (blockBounds.max.y < minY || blockBounds.min.y > maxY) continue;

            if (!hasBounds)
            {
                focusedBounds = blockBounds;
                hasBounds = true;
            }
            else
            {
                focusedBounds.Encapsulate(blockBounds);
            }
        }

        return hasBounds;
    }

    private float GetTargetCameraY()
    {
        float towerHeight = GameManager.Instance != null ? GameManager.Instance.maxHeight : 0f;
        float halfHeight = GetHalfHeight();
        float peakOffset = Mathf.Lerp(-halfHeight, halfHeight, TowerPeakScreenY);
        return Mathf.Max(MinimumCameraY, towerHeight - peakOffset);
    }

    private void UpdateSpawnPoint()
    {
        if (spawnPoint == null) return;

        float halfHeight = GetHalfHeight();
        float spawnOffset = Mathf.Lerp(-halfHeight, halfHeight, SpawnPointScreenY);
        Vector3 position = spawnPoint.position;
        position.y = transform.position.y + spawnOffset;
        spawnPoint.position = position;
    }

    private float GetHalfHeight()
    {
        return _camera != null && _camera.orthographic
            ? _camera.orthographicSize
            : 10f;
    }

    private void SetCameraY(float y)
    {
        Vector3 position = transform.position;
        position.y = y;
        transform.position = position;
    }

    private void UpdateVerticalFollowers()
    {
        if (verticalFollowers == null) return;

        for (int i = 0; i < verticalFollowers.Length; i++)
        {
            Transform follower = verticalFollowers[i];
            if (follower == null) continue;

            Vector3 position = follower.position;
            position.y = transform.position.y;
            follower.position = position;
        }
    }

    private GameModeConfig ActiveGameModeConfig => LevelSelectionState.ResolveGameMode(gameModeConfig);
    private float MinimumCameraY => ActiveGameModeConfig != null ? ActiveGameModeConfig.MinimumCameraY : fallbackMinimumY;
    private float TowerPeakScreenY => ActiveGameModeConfig != null ? ActiveGameModeConfig.TowerPeakScreenY : fallbackTowerPeakScreenY;
    private float SpawnPointScreenY => ActiveGameModeConfig != null ? ActiveGameModeConfig.SpawnPointScreenY : fallbackSpawnPointScreenY;
    private float CameraSmoothTime => ActiveGameModeConfig != null ? ActiveGameModeConfig.CameraSmoothTime : fallbackSmoothTime;
    private float MinimumCameraSize => ActiveGameModeConfig != null ? ActiveGameModeConfig.MinimumCameraSize : fallbackMinimumCameraSize;
    private float MaximumCameraSize => ActiveGameModeConfig != null ? ActiveGameModeConfig.MaximumCameraSize : fallbackMaximumCameraSize;
    private float HorizontalCameraPadding => ActiveGameModeConfig != null ? ActiveGameModeConfig.HorizontalCameraPadding : fallbackHorizontalPadding;
    private float HorizontalCameraSafeArea => ActiveGameModeConfig != null ? ActiveGameModeConfig.HorizontalCameraSafeArea : Mathf.Clamp(fallbackHorizontalSafeArea, 0.5f, 1f);
    private float CameraZoomSmoothTime => ActiveGameModeConfig != null ? ActiveGameModeConfig.CameraZoomSmoothTime : fallbackZoomSmoothTime;
}
