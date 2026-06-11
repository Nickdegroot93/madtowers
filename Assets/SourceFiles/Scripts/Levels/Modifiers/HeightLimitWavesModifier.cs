using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "Laser limit" level type (Tricky Towers' puzzle mode): blocks arrive in waves, and the
/// whole tower must stay below a glowing limit line. When a wave's blocks are all placed,
/// the line rises and the next, bigger wave begins. A landed block crossing the line is
/// zapped (destroyed) and costs a life via the normal GameOver/lives flow.
///
/// Pure modifier - no engine changes. Pair it on a LevelDefinition with
/// targetType PlaceBlocks and targetValue = the sum of all wave block counts, so the
/// existing goal system provides the win the moment the last wave is cleared.
/// Only LANDED blocks are checked: the falling piece passes the line freely (it has to -
/// it spawns above it).
/// </summary>
[CreateAssetMenu(fileName = "HeightLimitWaves", menuName = "Stacking/Levels/Modifiers/Height Limit Waves")]
public class HeightLimitWavesModifier : LevelModifier
{
    [System.Serializable]
    public sealed class Wave
    {
        [Min(1)] public int blockCount = 6;
        [Tooltip("Limit line height in meters above the floor while this wave is being placed.")]
        [Min(1f)] public float lineHeightAboveFloor = 5f;
    }

    [Tooltip("Each wave: how many blocks to place, and where the line sits. Cleared in order; line rises between waves.")]
    [SerializeField] private Wave[] waves =
    {
        new Wave { blockCount = 6,  lineHeightAboveFloor = 5f },
        new Wave { blockCount = 10, lineHeightAboveFloor = 10f },
        new Wave { blockCount = 15, lineHeightAboveFloor = 17f },
        new Wave { blockCount = 21, lineHeightAboveFloor = 26f },
    };

    [Tooltip("Seconds the line takes to glide to the next wave's height.")]
    [SerializeField] private float lineRiseSeconds = 1.2f;

    [Header("Laser Style (per level; themes can override the art)")]
    [SerializeField] private Color lineColor = new Color(1f, 0.27f, 0.2f, 1f);
    [Tooltip("World thickness of the default code-built line (ignored when a theme supplies laser.png).")]
    [SerializeField] private float lineThickness = 0.12f;
    [Range(0f, 1f)]
    [SerializeField] private float lineBaseAlpha = 0.55f;
    [Range(0f, 1f)]
    [SerializeField] private float linePulseAmount = 0.18f;
    [SerializeField] private float linePulseSpeed = 6f;

    private const float ZapCooldownSeconds = 0.75f; // a collapse can't chain-drain lives in one beat
    private const float LineLength = 90f;

    private LevelModifierContext _context;
    private SpriteRenderer _line;
    private int _waveIndex;
    private float _floorY;
    private float _lineY;
    private float _lineTargetY;
    private float _zapCooldown;
    private float _flash;
    private bool _finished;

    public int TotalBlockCount
    {
        get
        {
            int total = 0;
            for (int i = 0; i < waves.Length; i++) total += waves[i].blockCount;
            return total;
        }
    }

    public override void OnLevelStart(LevelModifierContext context)
    {
        _context = context;
        _floorY = context.GameManager != null ? context.GameManager.floorOriginY : 0f;
        _waveIndex = 0;
        _finished = waves.Length == 0;

        // The win comes from the level's PlaceBlocks goal; catch a mismatched wiring early.
        if (context.Level != null &&
            (context.Level.TargetType != LevelTargetType.PlaceBlocks ||
             (int)context.Level.TargetValue != TotalBlockCount))
        {
            Debug.LogWarning(
                $"[HeightLimitWaves] '{context.Level.DisplayName}' should use targetType " +
                $"PlaceBlocks with targetValue {TotalBlockCount} (sum of wave block counts) " +
                $"so the level completes when the last wave clears.", this);
        }

        _lineY = _lineTargetY = CurrentLineWorldY();
        CreateLineVisual();
    }

    public override void OnBlockLocked(LevelModifierContext context, int totalBlocksPlaced)
    {
        if (_finished) return;

        // Advance through every wave whose cumulative block count is reached.
        int cumulative = 0;
        int reachedWave = waves.Length;
        for (int i = 0; i < waves.Length; i++)
        {
            cumulative += waves[i].blockCount;
            if (totalBlocksPlaced < cumulative) { reachedWave = i; break; }
        }

        if (reachedWave >= waves.Length)
        {
            // All waves cleared: the limit disappears; the PlaceBlocks target completes the
            // level, and "Keep Building" continues as a free endless run.
            _finished = true;
            if (_line != null) _line.gameObject.SetActive(false);
            return;
        }

        if (reachedWave != _waveIndex)
        {
            _waveIndex = reachedWave;
            _lineTargetY = CurrentLineWorldY();
        }
    }

    public override void OnUpdate(LevelModifierContext context, float deltaTime)
    {
        if (_line == null || _finished) return;

        // Glide toward the current wave's height, pulse, and track the camera horizontally.
        float riseSpeed = Mathf.Abs(_lineTargetY - _lineY) / Mathf.Max(0.05f, lineRiseSeconds);
        _lineY = Mathf.MoveTowards(_lineY, _lineTargetY, Mathf.Max(riseSpeed, 2f) * deltaTime);

        Camera cam = Camera.main;
        float x = cam != null ? cam.transform.position.x : 0f;
        _line.transform.position = new Vector3(x, _lineY, 0f);

        _flash = Mathf.Max(0f, _flash - deltaTime * 2.5f);
        Color c = lineColor;
        c.a = Mathf.Clamp01(lineBaseAlpha + linePulseAmount * Mathf.Sin(Time.time * linePulseSpeed) + _flash);
        _line.color = c;

        _zapCooldown -= deltaTime;
        if (_zapCooldown <= 0f) CheckViolations();
    }

    // A landed block whose top crosses the line is zapped: destroyed + one life lost (the
    // normal GameOver flow ends the run when lives are out). One zap per cooldown window so
    // the collapse caused by a zap can't instantly drain every life.
    private void CheckViolations()
    {
        IReadOnlyList<BlockController> blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block == null || !block.HasLanded) continue;
            if (!block.TryGetWorldBounds(out Bounds bounds)) continue;
            if (bounds.max.y <= _lineY + 0.02f) continue;

            BlockShatterFx.Spawn(bounds, lineColor);
            Object.Destroy(block.gameObject);
            _zapCooldown = ZapCooldownSeconds;
            _flash = 0.6f;
            TowerCameraController.Impact(0.15f, 0.2f);
            _context?.GameManager?.GameOver();
            return;
        }
    }

    private float CurrentLineWorldY()
    {
        if (waves.Length == 0) return _floorY;
        return _floorY + waves[Mathf.Clamp(_waveIndex, 0, waves.Length - 1)].lineHeightAboveFloor;
    }

    // The line art follows the active theme's skin folder when it provides laser.png
    // (same convention as piece_*.png and ground.png); otherwise a code-built soft bar.
    // A themed sprite keeps its authored height - only its length is stretched.
    private void CreateLineVisual()
    {
        Sprite themed = ThemeSkins.LoadLaser();

        GameObject go = new GameObject("HeightLimitLine");
        _line = go.AddComponent<SpriteRenderer>();
        _line.sprite = themed != null ? themed : RuntimeSprites.SoftHorizontalBar(lineThickness);
        _line.sortingOrder = 50; // in front of blocks (0), behind UI
        _line.transform.position = new Vector3(0f, _lineY, 0f);
        _line.transform.localScale = new Vector3(LineLength / _line.sprite.bounds.size.x, 1f, 1f);
    }
}
