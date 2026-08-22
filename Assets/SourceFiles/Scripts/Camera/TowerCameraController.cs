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
    [SerializeField] private float fallbackZoomSmoothTime = 0.35f;

    [Header("Opening pan")]
    [Tooltip("Play a short left->right reveal before the first piece drops: the camera starts offset to the left and glides to center, showing off the scenery and the horizontal parallax.")]
    [SerializeField] private bool playIntroPan = true;
    [Tooltip("Seconds the opening pan takes. Block spawning is held until it finishes.")]
    [Min(0f)]
    [SerializeField] private float introPanDuration = 2f;
    [Tooltip("World units the camera starts to the LEFT of the framing center before gliding right to it.")]
    [Min(0f)]
    [SerializeField] private float introPanDistance = 8f;

    // Horizontal follow is its own motion (it tracks the piece being steered, not the tower
    // climb), so it gets its own responsiveness rather than borrowing the vertical CameraSmoothTime.
    // Snappier than the vertical climb so following a flick-to-edge doesn't feel like drag.
    private const float HorizontalFollowSmoothTime = 0.21f;

    private static TowerCameraController _instance;

    // The resting framing center X (ignores the opening pan's temporary offset). The backdrop
    // reads this so its horizontal parallax measures sideways drift from the gameplay center.
    private static float _framingCenterX;
    public static float FramingCenterX => _framingCenterX;

    /// <summary>The gameplay camera, for world-to-screen work (e.g. the tutorial's settle check).</summary>
    public static Camera Camera => _instance != null ? _instance._camera : null;

    private Camera _camera;
    private float _verticalVelocity;
    private float _zoomVelocity;
    private float _horizontalVelocity;
    private float _baseY;
    private float _baseX;
    private bool _hasInitializedFraming;
    private bool _introActive;
    private bool _introStarted;
    private float _introElapsed;
    private float _introStartX;
    // Trauma-based shake (JUICE.md): events ADD trauma [0,1]; amplitude = trauma² so small
    // knocks stay subtle while stacked/hard impacts compound naturally. Perlin noise drives
    // the offset (smooth, directionally unbiased); trauma decays linearly. Unscaled time so
    // hit-stop/pause never freezes the camera mid-offset.
    private const float TraumaDecayPerSecond = 1.4f;
    private const float ShakeMaxOffset = 0.45f;     // world units at trauma 1
    private const float ShakeMaxRollDegrees = 1.1f; // camera roll at trauma 1
    private const float ShakeNoiseFrequency = 20f;  // Perlin sample rate: ~20 direction changes/sec

    private float _trauma;
    private float _shakeNoiseTime;

    private void Awake()
    {
        _instance = this;
        _camera = GetComponent<Camera>();
        if (_camera.orthographic)
        {
            _camera.orthographicSize = Mathf.Clamp(_camera.orthographicSize, MinimumCameraSize, MaximumCameraSize);
        }

        _baseY = Mathf.Max(transform.position.y, MinimumCameraY);
        _baseX = transform.position.x;
        _framingCenterX = transform.position.x; // until the first real framing resolves
        // The camera is the sole authority on the opening-pan spawn hold: commit here (before any
        // Spawner.Start runs) so a level that pans never drops its first piece early, and one that
        // doesn't pan never leaves a stale hold from a previous level.
        _introActive = playIntroPan;
        if (_introActive) CameraIntroGate.Begin();
        else CameraIntroGate.Reset();
        SetCameraY(_baseY);
        UpdateSpawnPoint();
        UpdateVerticalFollowers();
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        if (_introActive) CameraIntroGate.End(); // never leak the hold if torn down mid-pan
    }

    // Purely visual impact shake. The shake is a render-only offset/roll added on top of the
    // smoothed base position - the smoothing state itself never sees it, and physics never
    // reads the camera, so the tower is unaffected.
    /// <summary>Add shake energy [0,1]. Peak world-unit offset ≈ trauma² × 0.45.</summary>
    public static void AddTrauma(float amount)
    {
        if (_instance == null || amount <= 0f) return;
        // Every screen shake in the game routes through here, so honouring the Screen Shake
        // setting at this one point disables them all (current and future). See GRAPHICS.md.
        if (!SettingsService.ScreenShake) return;
        _instance._trauma = Mathf.Clamp01(_instance._trauma + amount);
    }

    /// <summary>Legacy entry point: amplitude is the desired peak offset in world units.
    /// trauma = √(a/max) makes the trauma curve reproduce exactly that peak, so old call
    /// sites keep their tuned strength. Duration is now owned by the fixed trauma decay.</summary>
    public static void Impact(float amplitude = 0.16f, float duration = 0.22f)
    {
        AddTrauma(Mathf.Sqrt(Mathf.Max(0f, amplitude) / ShakeMaxOffset));
    }

    private void LateUpdate()
    {
        // Follows the LIVE standing top both ways - no "never descend" latch. In any mode
        // with lives in hand (the Flood starts with all 3), collapse-and-continue is a
        // normal state, and a camera
        // latched at the pre-collapse peak stared at empty air while pieces spawned into it
        // (Nick 2026-08-11). The target only moves when landed geometry changes (debris and
        // the falling piece are excluded), so the descent is the same smoothed glide as the
        // climb, never a chase after tumbling blocks.
        float targetY = GetTargetCameraY();

        float smoothTime = Mathf.Max(0.01f, CameraSmoothTime);
        _baseY = Mathf.SmoothDamp(
            _baseY,
            targetY,
            ref _verticalVelocity,
            smoothTime);

        // Horizontal pan + zoom only when there is content to frame. Until then (degenerate first
        // frames before the floor/config resolves) we hold the Awake values rather than latching a
        // placeholder - so the first real frame still snaps cleanly with no zoom pop.
        bool framed = GetTargetFraming(out float targetX, out float targetSize);
        if (framed) _framingCenterX = targetX; // resting center for the backdrop's parallax base (ignores the pan offset)

        if (_introActive)
        {
            // The timer advances every frame, framed or not, so the spawn gate is always released
            // even if framing never resolves - the pan visual only runs once content can be framed.
            TickIntroPan(framed, targetX, targetSize);
        }
        else if (framed)
        {
            if (!_hasInitializedFraming)
            {
                _hasInitializedFraming = true;
                _baseX = targetX;
                if (_camera != null && _camera.orthographic) _camera.orthographicSize = targetSize;
            }
            else
            {
                _baseX = Mathf.SmoothDamp(_baseX, targetX, ref _horizontalVelocity, HorizontalFollowSmoothTime);
                UpdateZoom(targetSize);
            }
        }

        TickShake(out Vector2 shakeOffset, out float shakeRoll);
        SetCameraPosition(_baseX + shakeOffset.x, _baseY + shakeOffset.y);
        transform.rotation = Quaternion.Euler(0f, 0f, shakeRoll);
        UpdateSpawnPoint();
        UpdateVerticalFollowers();
    }

    private void TickShake(out Vector2 offset, out float roll)
    {
        offset = Vector2.zero;
        roll = 0f;
        if (_trauma <= 0f) return;

        _shakeNoiseTime += Time.unscaledDeltaTime * ShakeNoiseFrequency;
        _trauma = Mathf.Max(0f, _trauma - TraumaDecayPerSecond * Time.unscaledDeltaTime);

        float amplitude = _trauma * _trauma;
        offset = new Vector2(
            (Mathf.PerlinNoise(_shakeNoiseTime, 0.3f) * 2f - 1f) * ShakeMaxOffset * amplitude,
            (Mathf.PerlinNoise(_shakeNoiseTime, 7.9f) * 2f - 1f) * ShakeMaxOffset * amplitude);
        roll = (Mathf.PerlinNoise(_shakeNoiseTime, 21.4f) * 2f - 1f) * ShakeMaxRollDegrees * amplitude;
    }

    // Opening reveal: hold the gameplay zoom, start offset to the LEFT of the framing center and
    // glide right to it over introPanDuration. The plateau slides into view and the new horizontal
    // parallax pushes the scenery into place. The first piece is gated until this finishes.
    private void TickIntroPan(bool framed, float targetX, float targetSize)
    {
        _introElapsed += Time.deltaTime;

        if (framed)
        {
            if (!_introStarted)
            {
                _introStarted = true;
                _hasInitializedFraming = true;
                if (_camera != null && _camera.orthographic) _camera.orthographicSize = targetSize;
                _introStartX = targetX - introPanDistance;
            }
            else
            {
                UpdateZoom(targetSize);
            }

            float t = introPanDuration > 0f ? Mathf.Clamp01(_introElapsed / introPanDuration) : 1f;
            float eased = t * t * (3f - 2f * t); // smoothstep ease-in-out
            _baseX = Mathf.Lerp(_introStartX, targetX, eased);
        }

        if (introPanDuration <= 0f || _introElapsed >= introPanDuration) EndIntroPan();
    }

    private void EndIntroPan()
    {
        _introActive = false;
        _horizontalVelocity = 0f; // hand off to the follow with no residual velocity
        CameraIntroGate.End();
    }

    private void UpdateZoom(float targetSize)
    {
        if (_camera == null || !_camera.orthographic) return;

        _camera.orthographicSize = Mathf.SmoothDamp(
            _camera.orthographicSize,
            targetSize,
            ref _zoomVelocity,
            CameraZoomSmoothTime);
    }

    // A follow camera: frame the horizontal span of the content (floor, the nearby tower, the
    // nearby sky islands, and the piece the player is steering) with a fixed column margin, then
    // BOTH pan and zoom to fit it. Normal play keeps the active piece over the tower, so the
    // span equals the tower and the camera sits still and tight; only when the player pushes a
    // piece out past the tower edge does the span grow on that side and the camera glide to
    // follow it - so reaching the drop lane stays possible without permanently zooming out.
    private bool GetTargetFraming(out float centerX, out float size)
    {
        centerX = transform.position.x;
        size = MinimumCameraSize;
        if (_camera == null || !_camera.orthographic) return false;

        if (!TryGetContentHorizontalBounds(out float minX, out float maxX)) return false;

        centerX = (minX + maxX) * 0.5f;
        float halfSpan = (maxX - minX) * 0.5f;
        float aspect = Mathf.Max(0.01f, _camera.aspect);
        // HorizontalCameraPadding is the visible margin (world units ≈ columns) left beyond the
        // content on each side - dividing by aspect alone (no safe-area inflation) keeps that
        // margin fixed rather than growing with the tower, which is what reads as "tight".
        float halfWidthWorld = halfSpan + HorizontalCameraPadding;
        size = Mathf.Clamp(halfWidthWorld / aspect, MinimumCameraSize, MaximumCameraSize);

        // When the content is wider than max zoom can show, centering on the midpoint could push
        // the piece the player is steering off-screen. Bias the centre so the active piece stays
        // fully framed - the far tower side is what gets cropped, never the actionable piece.
        float halfWidth = size * aspect;
        if (TryGetActivePieceHorizontalExtent(out float pieceMinX, out float pieceMaxX))
        {
            centerX = Mathf.Clamp(centerX, pieceMaxX - halfWidth, pieceMinX + halfWidth);
        }
        return true;
    }

    private bool TryGetContentHorizontalBounds(out float minX, out float maxX)
    {
        minX = 0f;
        maxX = 0f;
        bool hasBounds = false;

        // Floor is always part of the frame so the camera is sensible from the first frame
        // (before any block lands) and never zooms in tighter than the play area.
        GameModeConfig config = ActiveGameModeConfig;
        if (config != null)
        {
            HorizontalBounds.AddFloorSegments(config.FloorSegments, config.GridSpacing,
                ref minX, ref maxX, ref hasBounds);
        }

        float focusHalfHeight = MinimumCameraSize;
        float windowMinY = transform.position.y - focusHalfHeight;
        float windowMaxY = transform.position.y + focusHalfHeight;

        AddFocusedBlockHorizontalBounds(windowMinY, windowMaxY, ref minX, ref maxX, ref hasBounds);

        if (StaticSupportIslandManager.TryGetWorldHorizontalExtentInRange(windowMinY, windowMaxY,
                out float islandMinX, out float islandMaxX))
        {
            HorizontalBounds.Encapsulate(islandMinX, islandMaxX, ref minX, ref maxX, ref hasBounds);
        }

        AddActivePieceHorizontalBounds(ref minX, ref maxX, ref hasBounds);

        return hasBounds;
    }

    private void AddFocusedBlockHorizontalBounds(float minY, float maxY, ref float minX, ref float maxX, ref bool hasBounds)
    {
        IReadOnlyList<BlockController> blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block == null || !block.HasLanded) continue;
            // Debris never drives the frame: a block that has clearly fallen away from the
            // tower (sticky IsFallingAway) is on its way to the loss line - chasing it with a
            // zoom-out reads as the camera abandoning the tower, and it dragged the armed
            // ability lasers' world anchor down with the widened view (July 2026).
            if (block.IsFallingAway) continue;
            if (!block.TryGetWorldBounds(out Bounds blockBounds)) continue;
            if (blockBounds.max.y < minY || blockBounds.min.y > maxY) continue;
            HorizontalBounds.Encapsulate(blockBounds.min.x, blockBounds.max.x, ref minX, ref maxX, ref hasBounds);
        }
    }

    // The piece under player control is what makes the camera follow a push. No vertical window:
    // it is always the thing the player is acting on, wherever they have steered it.
    private void AddActivePieceHorizontalBounds(ref float minX, ref float maxX, ref bool hasBounds)
    {
        if (!TryGetActivePieceHorizontalExtent(out float pieceMinX, out float pieceMaxX)) return;
        HorizontalBounds.Encapsulate(pieceMinX, pieceMaxX, ref minX, ref maxX, ref hasBounds);
    }

    private bool TryGetActivePieceHorizontalExtent(out float minX, out float maxX)
    {
        minX = 0f;
        maxX = 0f;
        BlockController active = BlockController.ActiveControlled;
        if (active == null || active.HasLanded) return false;
        if (!active.TryGetWorldBounds(out Bounds bounds)) return false;
        minX = bounds.min.x;
        maxX = bounds.max.x;
        return true;
    }

    private float GetTargetCameraY()
    {
        // LiveTowerTopWorldY, not maxHeight: the record is monotonic (it feeds bests/XP) and
        // held the camera at the pre-collapse peak forever; the live top is what there is to
        // frame. MinimumCameraY still floors the frame, so an empty board reads as level start.
        float towerTopY = GameManager.Instance != null ? GameManager.Instance.LiveTowerTopWorldY : 0f;
        float halfHeight = GetHalfHeight();
        float peakOffset = Mathf.Lerp(-halfHeight, halfHeight, TowerPeakScreenY);
        return Mathf.Max(MinimumCameraY, towerTopY - peakOffset);
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

    private void SetCameraPosition(float x, float y)
    {
        Vector3 position = transform.position;
        position.x = x;
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
    private float CameraZoomSmoothTime => ActiveGameModeConfig != null ? ActiveGameModeConfig.CameraZoomSmoothTime : fallbackZoomSmoothTime;
}
