using UnityEngine;

/// <summary>
/// Charges a life for every block that falls off the BOTTOM OF THE SCREEN, the moment it
/// leaves view. Camera-relative on purpose: at altitude a dropped block must not get a
/// free 100 m plunge into the old tower below ("maybe it wedges somewhere") or a ten-second
/// wait while it falls to the world floor - the penalty lands exactly when the player sees
/// the block disappear. Resting tower blocks below the camera are the normal state of a
/// tall game; only genuinely falling blocks count (see BlockController.IsLostBelow).
///
/// The object's fixed trigger collider below the floor stays as a backstop for any
/// rigidbody the sweep can't judge.
/// </summary>
public class LossZone : MonoBehaviour
{
    // The block must be FULLY below the screen edge plus a little slack - wobbling in and
    // out of the last visible pixels is not "gone".
    private const float CullMarginBelowScreen = 1f;

    /// <summary>The world-space line below which a block counts as lost, for the given
    /// camera - the single definition both the sweep and abilities consult (a doomed
    /// piece must not accept a consumable spent on it).</summary>
    public static float CullY(Camera camera)
    {
        return camera.transform.position.y - camera.orthographicSize - CullMarginBelowScreen;
    }

    // The sweep reads collider bounds for every tracked block; at late-game scale that is
    // hundreds of native calls, so it runs at 10 Hz instead of per frame. A block a full
    // margin below the screen cannot un-lose itself in 100 ms, and the timer uses scaled
    // time so it naturally freezes with the physics it observes.
    private const float SweepInterval = 0.1f;
    private float _nextSweepTime;

    private Camera _camera;

    private void Awake()
    {
        // The red translucent bar on this object is an editor-only guide showing where
        // the backstop trigger sits; players should never see it.
        SpriteRenderer guide = GetComponent<SpriteRenderer>();
        if (guide != null) guide.enabled = false;
    }

    private void Update()
    {
        if (GameManager.Instance == null || GameManager.Instance.isGameOver) return;
        if (GameManager.Instance.IsGamePaused) return; // no verdicts under the pause menu

        if (Time.time < _nextSweepTime) return;
        _nextSweepTime = Time.time + SweepInterval;

        if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
        if (_camera == null || !_camera.orthographic) return;

        float cullY = CullY(_camera);

        var blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block == null || !block.IsLostBelow(cullY)) continue;
            if (TryInterceptLoss(block)) continue;

            // DuringBlockLoss owns the per-block loss policy around the frozen call:
            // life charge (per CostsLifeWhenLost), live-total decrement, posthumous-score
            // suppression, and the try/finally that keeps a throw from stranding state.
            GameManager.Instance.DuringBlockLoss(block, block.HandleLostBelowScreen);

            // The final life may just have gone - leave the wreckage in peace.
            if (GameManager.Instance.isGameOver) return;
        }
    }

    // An armed ability (e.g. a one-shot Safety Net) may handle a loss instead of the
    // life charge. LANDED blocks only: saving the active piece would strand the
    // spawner's ActiveControlled gate (control can't be ended from outside) - the
    // active piece always takes the normal loss path.
    private bool TryInterceptLoss(BlockController block)
    {
        if (!block.HasLanded) return false;

        if (_abilities == null && GameManager.Instance != null)
        {
            _abilities = GameManager.Instance.GetComponent<AbilityRuntime>();
        }
        return _abilities != null && _abilities.TryInterceptLoss(block);
    }

    private AbilityRuntime _abilities;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (GameManager.Instance == null || GameManager.Instance.isGameOver) return;

        // If the collider belongs to a block, the Rigidbody2D may live on its parent.
        Rigidbody2D rb = other.attachedRigidbody;
        if (rb == null) return;

        BlockController block = rb.GetComponent<BlockController>();
        if (block != null)
        {
            if (TryInterceptLoss(block)) return;
            // Same per-block accounting as the screen-bottom cull (count + life policy).
            GameManager.Instance.DuringBlockLoss(block, block.HandleLostBelowScreen);
            return;
        }

        GameManager.Instance.GameOver();
        Destroy(rb.gameObject);
    }
}
