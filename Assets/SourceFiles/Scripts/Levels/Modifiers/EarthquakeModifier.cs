using UnityEngine;

/// <summary>
/// Example level modifier: every interval, the whole tower gets a lateral jolt and must
/// survive it. Velocity-impulse only - never positions (see PHYSICS.md, Invariant I1).
/// </summary>
[CreateAssetMenu(fileName = "Earthquake", menuName = "Stacking/Levels/Modifiers/Earthquake")]
public class EarthquakeModifier : LevelModifier
{
    [Tooltip("Seconds between quakes.")]
    [Min(5f)]
    [SerializeField] private float interval = 25f;
    [Tooltip("Horizontal velocity jolt applied to every landed block, alternating direction per block for a shaking feel.")]
    [Range(0f, 3f)]
    [SerializeField] private float joltSpeed = 0.8f;
    [Tooltip("Quakes only start once this many blocks have been placed.")]
    [Min(0)]
    [SerializeField] private int graceBlocks = 8;

    private float _timer;
    private int _blocksPlaced;

    public override void OnBlockLocked(LevelModifierContext context, int totalBlocksPlaced)
    {
        _blocksPlaced = totalBlocksPlaced;
    }

    public override void OnUpdate(LevelModifierContext context, float deltaTime)
    {
        if (_blocksPlaced < graceBlocks) return;

        _timer += deltaTime;
        if (_timer < interval) return;

        _timer = 0f;
        Shake();
    }

    private void Shake()
    {
        var blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block == null || !block.HasLanded) continue;

            block.ApplyJolt(new Vector2((i % 2 == 0 ? 1f : -1f) * joltSpeed, 0f));
        }
    }
}
