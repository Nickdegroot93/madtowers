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

        float cullY = _camera.transform.position.y - _camera.orthographicSize - CullMarginBelowScreen;

        var blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block == null || !block.IsLostBelow(cullY)) continue;

            block.HandleLostBelowScreen(); // Destroy is deferred, so the list stays stable here

            // The final life may just have gone - leave the wreckage in peace.
            if (GameManager.Instance.isGameOver) return;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (GameManager.Instance == null || GameManager.Instance.isGameOver) return;

        // If the collider belongs to a block, the Rigidbody2D may live on its parent.
        Rigidbody2D rb = other.attachedRigidbody;
        if (rb == null) return;

        BlockController block = rb.GetComponent<BlockController>();
        if (block != null)
        {
            block.HandleLostBelowScreen(); // same accounting as the screen-bottom cull
            return;
        }

        GameManager.Instance.GameOver();
        Destroy(rb.gameObject);
    }
}
