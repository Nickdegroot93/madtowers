using UnityEngine;

/// <summary>
/// Charges a life for every block that drops past the death line near the BOTTOM OF THE SCREEN.
/// Camera-relative on purpose: at altitude a dropped block must not get a free 100 m plunge into
/// the old tower below ("maybe it wedges somewhere") or a ten-second wait while it falls to the
/// world floor - the penalty lands as the player watches the block sink past the line, which is
/// always on screen no matter how high the camera has climbed. Resting tower blocks below the
/// line are the normal state of a tall game; only genuinely falling blocks count (see
/// BlockController.IsLostBelow).
///
/// The object's fixed trigger collider below the floor stays as a backstop for any
/// rigidbody the sweep can't judge.
/// </summary>
public class LossZone : MonoBehaviour
{
    // The death line sits a little ABOVE the bottom screen edge (not below it), so the beam is
    // visible at every camera height and a block is charged as it sinks past the bottom of the
    // view - not after dropping fully out of sight. Camera-relative, so it rises with the camera.
    // Trade-off: at altitude the standing tower reaches the bottom edge, so roughly this many
    // world units of it sit below the line; keep it modest. (Tunable - was 1 unit BELOW the edge,
    // which put the line and the death itself off-screen once the camera climbed.)
    private const float LossLineAboveScreenBottom = 2f;

    /// <summary>The world-space line below which a block counts as lost, for the given camera -
    /// the single definition the sweep, the death beam and abilities all consult (a doomed piece
    /// must not accept a consumable spent on it). Sits just inside the bottom edge of the view.</summary>
    public static float CullY(Camera camera)
    {
        return camera.transform.position.y - camera.orthographicSize + LossLineAboveScreenBottom;
    }

    /// <summary>True if a world position sits below the bottom-screen kill line right now - i.e. a
    /// piece that locked/slid off down there is on its way out and the cull sweep owns it. On-land
    /// variant effects (e.g. Magma melt) consult this so they don't fire on a doomed piece. A
    /// non-orthographic / absent camera counts as "not below" (no kill line to be under).</summary>
    public static bool IsBelowCull(Vector3 worldPosition)
    {
        Camera camera = Camera.main;
        return camera != null && camera.orthographic && worldPosition.y < CullY(camera);
    }

    /// <summary>
    /// The highest line that can currently charge a bottom-screen loss. Early in a run
    /// the fixed trigger is usually the visible "hit" line; once the camera climbs, the
    /// camera-relative cull can become higher and takes over.
    /// </summary>
    public static float CurrentLossLineY(Camera camera)
    {
        bool hasLine = false;
        float lineY = 0f;

        if (camera != null && camera.orthographic)
        {
            lineY = CullY(camera);
            hasLine = true;
        }

        Collider2D activeTrigger = _active != null ? _active._triggerCollider : null;
        if (activeTrigger != null && activeTrigger.enabled)
        {
            float triggerTopY = activeTrigger.bounds.max.y;
            lineY = hasLine ? Mathf.Max(lineY, triggerTopY) : triggerTopY;
            hasLine = true;
        }

        return hasLine ? lineY : 0f;
    }

    // The sweep reads collider bounds for every tracked block; at late-game scale that is
    // hundreds of native calls, so it runs at 10 Hz instead of per frame. A block a full
    // margin below the screen cannot un-lose itself in 100 ms, and the timer uses scaled
    // time so it naturally freezes with the physics it observes.
    private const float SweepInterval = 0.1f;
    private static LossZone _active;

    private float _nextSweepTime;

    private Camera _camera;
    private Collider2D _triggerCollider;

    private void Awake()
    {
        _triggerCollider = GetComponent<Collider2D>();

        // The red translucent bar on this object is an editor-only guide showing where
        // the backstop trigger sits; players should never see it.
        SpriteRenderer guide = GetComponent<SpriteRenderer>();
        if (guide != null) guide.enabled = false;
    }

    private void OnEnable()
    {
        _active = this;
    }

    private void OnDisable()
    {
        if (_active == this) _active = null;
    }

    private void Update()
    {
        if (GameManager.Instance == null || GameManager.Instance.isGameOver) return;
        if (GameManager.Instance.IsGamePaused) return; // no verdicts under the pause menu

        if (Time.time < _nextSweepTime) return;
        _nextSweepTime = Time.time + SweepInterval;

        if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
        if (_camera == null || !_camera.orthographic) return;

        EnsureAbilities();

        float cullY = CullY(_camera);
        float landedInterceptCullY = cullY;
        if (_abilities != null)
        {
            landedInterceptCullY += Mathf.Max(0f, _abilities.LossInterceptLineOffset);
        }

        var blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block == null) continue;

            float blockCullY = block.HasLanded ? landedInterceptCullY : cullY;
            if (!block.IsLostBelow(blockCullY)) continue;

            ResolveLostBlock(block);

            // The final life may just have gone - leave the wreckage in peace.
            if (GameManager.Instance.isGameOver) return;
        }
    }

    // Resolve a block that has crossed the loss line, AT MOST ONCE. The camera sweep and
    // the backstop trigger can both catch the same falling piece (a straight drop off-screen
    // hits both), so the first to resolve it consumes the block's one-shot loss guard;
    // whichever arrives second skips - otherwise one block costs two lives, or a block an
    // armed ability already saved gets charged anyway. Resolution = let an ability intercept,
    // else charge the normal per-block loss (DuringBlockLoss owns the life/count/score policy).
    private void ResolveLostBlock(BlockController block)
    {
        if (block.TryGetComponent(out BlockIdentity identity) && !identity.TryConsumeLoss()) return;
        if (TryInterceptLoss(block)) return;
        GameManager.Instance.DuringBlockLoss(block, block.HandleLostBelowScreen);
    }

    // An armed ability (e.g. Sacrifice) may handle a loss instead of the
    // life charge. LANDED blocks only: saving the active piece would strand the
    // spawner's ActiveControlled gate (control can't be ended from outside) - the
    // active piece always takes the normal loss path.
    private bool TryInterceptLoss(BlockController block)
    {
        if (!block.HasLanded) return false;

        EnsureAbilities();
        return _abilities != null && _abilities.TryInterceptLoss(block);
    }

    // Lazily cache the AbilityRuntime. Both the sweep and the backstop trigger consult it, and
    // the trigger can fire before the first sweep, so the lookup lives in one place.
    private void EnsureAbilities()
    {
        if (_abilities == null && GameManager.Instance != null)
        {
            _abilities = GameManager.Instance.GetComponent<AbilityRuntime>();
        }
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
            ResolveLostBlock(block); // same once-guarded path as the screen-bottom cull
            return;
        }

        GameManager.Instance.GameOver();
        Destroy(rb.gameObject);
    }
}
