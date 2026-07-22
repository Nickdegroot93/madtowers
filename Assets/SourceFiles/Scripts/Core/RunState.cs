using UnityEngine;

[System.Serializable]
public sealed class RunState
{
    // Hard ceiling for GAINED lives (SHOP.md §3.1): purchased supplies, authored level
    // lives and ExtraLife pickups can never stack a run past 3. Authored configs above
    // the cap (Custom Game's stepper goes to 20) are trusted as-is - the cap governs
    // additions, not starts.
    public const int MaxLives = 3;

    [SerializeField] private float _maxHeight;
    [SerializeField] private int _score;
    [SerializeField] private int _standingBlocks;
    [SerializeField] private int _totalPlacedBlocks;
    [SerializeField] private int _lives = 1;

    private float _floorOriginY;

    public int Score => _score;
    public int Lives => _lives;
    public int StandingBlocks => _standingBlocks;
    public int TotalPlacedBlocks => _totalPlacedBlocks;
    public float MaxHeightWorld => _maxHeight;
    public float FloorOriginY => _floorOriginY;
    public float TowerHeight => Mathf.Max(0f, _maxHeight - _floorOriginY);

    public void SetFloorOrigin(float floorOriginY)
    {
        _floorOriginY = floorOriginY;
        _maxHeight = floorOriginY;
    }

    public void SetLives(int lives) => _lives = Mathf.Max(0, lives);

    public void AddLife()
    {
        if (_lives >= MaxLives) return;
        _lives++;
    }

    public bool TrySpendLife()
    {
        if (_lives <= 0) return false;

        _lives--;
        return true;
    }

    public void AddScore(int amount) => _score += Mathf.Max(0, amount);

    public int AdjustStandingBlocks(int delta)
    {
        _standingBlocks = Mathf.Max(0, _standingBlocks + delta);
        return _standingBlocks;
    }

    public void IncrementPlacedBlocks() => _totalPlacedBlocks++;

    public bool TryUpdateMaxHeight(float height)
    {
        if (height <= _maxHeight) return false;

        _maxHeight = height;
        return true;
    }

    public RunResult ToResult() =>
        new RunResult(_score, _lives, _standingBlocks, _totalPlacedBlocks, TowerHeight,
            CoinLedger.RunCoins);
}
