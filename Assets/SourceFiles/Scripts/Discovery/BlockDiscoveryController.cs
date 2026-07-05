using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The once-ever brick debut: watches every spawn for a variant the player has NEVER seen,
/// waits the split second until the piece is visibly in view, then freezes the action and shows
/// the debut modal (live demo + description + Continue). The freeze is the TUTORIAL's
/// world-alive kind, not a timeScale pause: the debuting brick hovers mid-air with its skin
/// animating behind the dimmed backdrop, and the demo diorama runs - while the Discovery phase
/// alone holds spawning, the timed-goal clock and any pending ability offer. Discovery is
/// persisted at modal-open (quit-safe), which is also what unlocks the brick's Vault entry.
///
/// Installed by GameSystemsInstaller next to AbilityChoiceController, whose defer-until-Playing
/// presentation pattern this copies.
/// </summary>
public sealed class BlockDiscoveryController : MonoBehaviour
{
    // "In sight": the piece's centre has dropped into the top three quarters of the view (the
    // spawn point sits above the visible top), and at least MinAge has passed so the freeze
    // never lands on the same frame as the spawn pop. PresentAnywayAge covers a camera framing
    // that never satisfies the viewport test.
    private const float InViewViewportY = 0.75f;
    private const float MinAgeSeconds = 0.35f;
    private const float PresentAnywayAgeSeconds = 2.5f;

    private sealed class PendingDebut
    {
        public BlockController Piece;
        public BlockData Variant;
        public float QueuedAt;
    }

    private static BlockDiscoveryController _instance;

    private readonly Queue<PendingDebut> _pending = new Queue<PendingDebut>();
    private readonly HashSet<string> _queuedIds = new HashSet<string>();

    private bool _presenting;
    private BlockController _hoveredPiece;
    private BlockDemoStage _stage;
    private GameObject _modal;
    private PieceGestures _savedGestures;

    private void Awake()
    {
        _instance = this;
    }

    private void OnEnable()
    {
        GameEvents.BlockSpawned += HandleBlockSpawned;
        GameEvents.GameOver += HandleGameOver;
    }

    private void OnDisable()
    {
        GameEvents.BlockSpawned -= HandleBlockSpawned;
        GameEvents.GameOver -= HandleGameOver;
        if (_presenting) Dismiss(); // scene teardown mid-modal must still restore the globals
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    /// <summary>Mid-run variant applications that bypass BlockSpawned (a consumable transmuting
    /// the falling piece) route through here so the transmuted brick still gets its debut.</summary>
    public static void NotifyVariantApplied(BlockController piece, BlockData variant)
    {
        if (_instance != null) _instance.HandleBlockSpawned(piece, variant);
    }

    private void HandleBlockSpawned(BlockController piece, BlockData variant)
    {
        string id = ProgressStore.BlockId(variant);

        // No demo (the plain brick, or a future variant before its scenario is authored):
        // unlock the Vault entry silently and move on - never a modal.
        if (!BlockDemoCatalog.HasDemo(variant))
        {
            ProgressStore.MarkBlockDiscovered(variant);
            return;
        }

        if (ProgressStore.HasDiscoveredBlock(variant) || !_queuedIds.Add(id)) return;

        _pending.Enqueue(new PendingDebut { Piece = piece, Variant = variant, QueuedAt = Time.time });
    }

    private void Update()
    {
        if (_presenting || _pending.Count == 0) return;

        GameManager gm = GameManager.Instance;
        if (gm == null) return;
        if (gm.isGameOver)
        {
            // The run ended before the debut could show; the variant stays undiscovered and the
            // debut fires on its next-ever spawn instead.
            _pending.Clear();
            _queuedIds.Clear();
            return;
        }

        // The ability offer's defer idiom: wait out other overlays; the debut keeps.
        if (gm.CurrentPhase != GamePhase.Playing || gm.IsGamePaused) return;
        // Someone else (the gesture tutorial) owns input right now - never fight it.
        if (TouchGestureInput.Suspended) return;

        PendingDebut next = _pending.Peek();
        if (ProgressStore.HasDiscoveredBlock(next.Variant))
        {
            _pending.Dequeue(); // e.g. discovered via a queued sibling while waiting
            return;
        }

        if (!IsReadyToPresent(next)) return;

        _pending.Dequeue();
        Present(next);
    }

    private static bool IsReadyToPresent(PendingDebut debut)
    {
        float age = Time.time - debut.QueuedAt;
        if (age < MinAgeSeconds) return false;
        if (age >= PresentAnywayAgeSeconds) return true;

        // The piece died (shattered/eaten) or already landed: nothing left to wait for - the
        // player HAS seen the brick, so the debut presents over whatever is on screen.
        if (debut.Piece == null || debut.Piece.HasLanded) return true;

        Camera camera = TowerCameraController.Camera;
        if (camera == null) camera = Camera.main;
        if (camera == null) return true;

        return camera.WorldToViewportPoint(debut.Piece.transform.position).y <= InViewViewportY;
    }

    private void Present(PendingDebut debut)
    {
        _presenting = true;

        // Persist FIRST (the tutorial's quit-safety rule): if the app dies mid-modal the debut
        // never replays - the player did see the brick.
        ProgressStore.MarkBlockDiscovered(debut.Variant);

        GameManager.Instance.RequestPhase(this, GamePhase.Discovery);

        // The world-alive freeze: hover the debuting piece (it keeps animating mid-air behind
        // the backdrop), gate every gesture, and suspend touch - TutorialModifier's exact trio.
        _hoveredPiece = debut.Piece != null && !debut.Piece.HasLanded ? debut.Piece : null;
        if (_hoveredPiece != null) _hoveredPiece.SetDescentSuspended(true);
        TouchGestureInput.Suspended = true;
        _savedGestures = BlockController.AllowedGestures;
        BlockController.AllowedGestures = PieceGestures.None;

        RuntimeUiKit.EnsureEventSystem();
        _stage = BlockDemoStage.Open(debut.Variant, GameMenuStyle.ActiveChapter,
            BlockDebutModal.DemoPixelWidth, BlockDebutModal.DemoPixelHeight);
        _modal = BlockDebutModal.Show(debut.Variant, _stage.Texture, Dismiss);
        SfxPlayer.Play("ability_offer", 0.55f, 0.03f);
    }

    private void Dismiss()
    {
        if (!_presenting) return;
        _presenting = false;

        if (_modal != null) Destroy(_modal);
        _modal = null;
        if (_stage != null) _stage.Close();
        _stage = null;

        BlockController.AllowedGestures = _savedGestures;
        TouchGestureInput.Suspended = false;
        if (_hoveredPiece != null && !_hoveredPiece.HasLanded)
        {
            _hoveredPiece.SetDescentSuspended(false);
            _hoveredPiece.SetNormalFallSpeedFactor(
                GameManager.Instance != null ? GameManager.Instance.AbilityFallSpeedFactor : 1f);
        }
        _hoveredPiece = null;

        if (GameManager.Instance != null) GameManager.Instance.ReleasePhase(this);
    }

    // A game over racing the modal: tear down NOW and give every global back (the tutorial's
    // HandleGameOver rule) - the game-over screen must never sit under a stale debut.
    private void HandleGameOver(int score, float maxHeight)
    {
        if (_presenting) Dismiss();
        _pending.Clear();
        _queuedIds.Clear();
    }
}
