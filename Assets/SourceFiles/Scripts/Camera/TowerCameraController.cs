using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class TowerCameraController : MonoBehaviour
{
    [SerializeField] private GameModeConfig gameModeConfig;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform[] verticalFollowers;
    [SerializeField] private float fallbackMinimumY = 0f;
    [Range(0.55f, 0.9f)]
    [SerializeField] private float fallbackTowerPeakScreenY = 0.7f;
    [Range(0.65f, 0.95f)]
    [SerializeField] private float fallbackSpawnPointScreenY = 0.86f;
    [SerializeField] private float fallbackSmoothTime = 0.35f;
    [SerializeField] private float fallbackMinimumCameraSize = 15f;
    [SerializeField] private float fallbackMaximumCameraSize = 24f;
    [SerializeField] private float fallbackHorizontalPadding = 1.5f;
    [Range(0.5f, 1f)]
    [SerializeField] private float fallbackHorizontalSafeArea = 0.78f;
    [SerializeField] private float fallbackZoomSmoothTime = 0.35f;

    private Camera _camera;
    private float _verticalVelocity;
    private float _zoomVelocity;
    private float _highestCameraY;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        if (_camera.orthographic)
        {
            _camera.orthographicSize = Mathf.Clamp(_camera.orthographicSize, MinimumCameraSize, MaximumCameraSize);
        }

        _highestCameraY = Mathf.Max(transform.position.y, MinimumCameraY);
        SetCameraY(_highestCameraY);
        UpdateSpawnPoint();
        UpdateVerticalFollowers();
    }

    private void LateUpdate()
    {
        float targetY = GetTargetCameraY();
        _highestCameraY = Mathf.Max(_highestCameraY, targetY);

        float smoothTime = Mathf.Max(0.01f, CameraSmoothTime);
        float nextY = Mathf.SmoothDamp(
            transform.position.y,
            _highestCameraY,
            ref _verticalVelocity,
            smoothTime);

        SetCameraY(nextY);
        UpdateZoom();
        UpdateSpawnPoint();
        UpdateVerticalFollowers();
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

    private float MinimumCameraY => gameModeConfig != null ? gameModeConfig.MinimumCameraY : fallbackMinimumY;
    private float TowerPeakScreenY => gameModeConfig != null ? gameModeConfig.TowerPeakScreenY : fallbackTowerPeakScreenY;
    private float SpawnPointScreenY => gameModeConfig != null ? gameModeConfig.SpawnPointScreenY : fallbackSpawnPointScreenY;
    private float CameraSmoothTime => gameModeConfig != null ? gameModeConfig.CameraSmoothTime : fallbackSmoothTime;
    private float MinimumCameraSize => gameModeConfig != null ? gameModeConfig.MinimumCameraSize : fallbackMinimumCameraSize;
    private float MaximumCameraSize => gameModeConfig != null ? gameModeConfig.MaximumCameraSize : fallbackMaximumCameraSize;
    private float HorizontalCameraPadding => gameModeConfig != null ? gameModeConfig.HorizontalCameraPadding : fallbackHorizontalPadding;
    private float HorizontalCameraSafeArea => gameModeConfig != null ? gameModeConfig.HorizontalCameraSafeArea : Mathf.Clamp(fallbackHorizontalSafeArea, 0.5f, 1f);
    private float CameraZoomSmoothTime => gameModeConfig != null ? gameModeConfig.CameraZoomSmoothTime : fallbackZoomSmoothTime;
}
