using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The devour: a landed block touched a Void Zone and is dragged into its eye. Uses the legal
/// doomed-block recipe (full HardlinePlatformFx parity): the body goes
/// KINEMATIC first - velocities zeroed, gravity off, constraints cleared, reach geometry
/// invalidated - its COLLIDERS are disabled (support drops the moment the void grabs it, and
/// the spiralling husk must not plow through the tower on its way to the eye) and every weld
/// joint touching it is destroyed (a vined block is torn free; see the maw exemption in the
/// sweep for the unbreakable welds). Only then are transform writes made, spiralling and
/// shrinking the block into the zone centre over ~half a second. Completion runs the standard
/// destruction flow (shatter -> RaiseBlockDestroyed -> Destroy: BLOCKS.md accounting +
/// neighbour wake) and charges the life via LoseLifeToHazard - cascades are allowed to drain
/// multiple lives by design (absolute law). After game over the husk is removed quietly: no
/// blast, no camera kick, no charge (leave the wreckage in peace).
/// </summary>
public sealed class VoidSuckFx : MonoBehaviour
{
    private static readonly Color VoidTint = new Color(0.35f, 0.2f, 0.6f, 1f);

    private BlockController _block;
    private GameManager _gameManager;
    private Vector3 _from;
    private Vector3 _to;
    private Quaternion _fromRotation;
    private Vector3 _fromScale;
    private float _duration;
    private float _elapsed;

    public static void Begin(BlockController block, Vector2 zoneCenter, float duration, GameManager gameManager)
    {
        if (block == null || block.GetComponent<VoidSuckFx>() != null) return;

        var fx = block.gameObject.AddComponent<VoidSuckFx>();
        fx._block = block;
        fx._gameManager = gameManager;
        fx._from = block.transform.position;
        fx._to = new Vector3(zoneCenter.x, zoneCenter.y, block.transform.position.z);
        fx._fromRotation = block.transform.rotation;
        fx._fromScale = block.transform.localScale;
        fx._duration = Mathf.Max(0.1f, duration);

        // Kinematic FIRST (the Hardline recipe): a doomed block leaves the simulation before
        // any transform is written, so the solver never sees a teleported dynamic body.
        var rb = block.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.gravityScale = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.constraints = RigidbodyConstraints2D.None;

            // Tear every weld free BEFORE the drag: a joint from/to a kinematically-moved
            // body would haul its partner across the board (vine glue; maws never reach here
            // - the sweep exempts them because their welds are unbreakable by design).
            foreach (Joint2D joint in block.GetComponents<Joint2D>()) Destroy(joint);
            IReadOnlyList<BlockController> all = BlockController.AllBlocks;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null || all[i] == block) continue;
                foreach (Joint2D joint in all[i].GetComponents<Joint2D>())
                {
                    if (joint.connectedBody == rb) Destroy(joint);
                }
            }
        }

        // Support drops HERE, not at destroy: the husk is scenery from the moment the void
        // grabs it, and a live collider sweeping to the eye would shove the tower en route.
        foreach (Collider2D col in block.GetComponentsInChildren<Collider2D>())
        {
            col.enabled = false;
        }
        BlockController.InvalidateReachGeometry();

        SfxPlayer.Play("void_suck", 0.8f);
    }

    private void Update()
    {
        // The run may have ended mid-suck: remove the husk quietly - no blast, no camera
        // kick, no posthumous life charge (the AirPocket "leave the wreckage in peace" rule).
        if (_gameManager != null && _gameManager.isGameOver)
        {
            if (_block != null) Destroy(_block.gameObject);
            return;
        }

        _elapsed += Time.deltaTime;
        float u = Mathf.Clamp01(_elapsed / _duration);
        // Accelerating pull: slow grip, then yanked in - u^2 eases the grab, the spin sells it.
        float pull = u * u;
        transform.position = Vector3.LerpUnclamped(_from, _to, pull);
        transform.rotation = _fromRotation * Quaternion.Euler(0f, 0f, 360f * u * u);
        transform.localScale = _fromScale * Mathf.Lerp(1f, 0.12f, pull);

        if (_elapsed >= _duration) Consume();
    }

    private void Consume()
    {
        if (_block != null && _block.TryGetWorldBounds(out Bounds bounds))
        {
            BlockShatterFx.Spawn(bounds, VoidTint, 8);
        }
        TowerCameraController.Impact(0.18f, 0.2f);
        if (_block != null)
        {
            GameEvents.RaiseBlockDestroyed(_block);
            Destroy(_block.gameObject);
        }
        if (_gameManager != null) _gameManager.LoseLifeToHazard();
    }
}
