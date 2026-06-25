using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime-only driver for the Fission consumable. The active piece has already shattered (the
/// ability played the burst); this owns the aftermath: one real 1x1 shard hovers at the spawn
/// line as the controllable "active drop" piece (descent suspended until the player commits),
/// while the remaining shards float above it as a small queue of ghost sprites. Each time the
/// active shard locks, the next shard is fed in and the front ghost glides into the drop slot -
/// no teleport snaps. When the last shard lands, normal spawning resumes.
///
/// It never pauses Time.timeScale (that would freeze the controllable shard). The "kinda paused"
/// feel comes from the Spawner withholding bag pieces (SetAutoSpawnSuspended) and the waiting
/// shards hovering instead of falling. Shards are real Block_Pip bricks, so each counts and risks
/// a life exactly like a placed block (BLOCKS.md) - a tetromino becomes four counting placements.
/// </summary>
public sealed class FissionSession : MonoBehaviour
{
    private sealed class Ghost
    {
        public Transform Root;
        public SpriteRenderer[] Renderers;
        public Color[] BaseColors;
        public float Phase;
        public bool FlyingOut;
        public float FlyAge;
        public Vector3 FlyFrom;
    }

    private const float GhostScale = 0.62f;
    private const float GhostAlpha = 0.72f;
    private const float HudClearanceCells = 0.65f;   // queue row sits this far BELOW the HUD bottom
    private const float QueueToDropCells = 1.2f;      // drop slot sits this far below the queue row
    private const float QueueSpacingCells = 0.82f;   // horizontal gap between queued shards
    private const float HoverAmplitude = 0.06f;      // floating up/down bob
    private const float HoverSpeed = 3.1f;
    private const float ReflowLerp = 13f;            // how briskly the row recenters when one leaves
    private const float FlyInSeconds = 0.2f;         // front ghost gliding into the drop slot
    private const int SortingOrderLift = 220;

    private readonly List<Ghost> _ghosts = new List<Ghost>();
    private Spawner _spawner;
    private BlockDefinition _pip;
    private int _shardsRemaining;   // shards still to be SPAWNED as the active piece (= live ghosts)
    private bool _finishing;

    // Resolved each frame from the live HUD bottom so the presentation sits clear of the top menu
    // (BLOCKS / NEXT / HEIGHT cards) on any aspect, and tracks the camera if it shifts.
    private float _queueWorldY;
    private float _dropWorldY;

    // The active drop slot: play-area centre X (the spawn column) at the resolved drop height.
    private Vector3 DropPos => new Vector3(
        _spawner != null ? _spawner.SpawnPosition.x : 0f,
        _dropWorldY,
        _spawner != null ? _spawner.SpawnPosition.z : 0f);

    // Place the queue row just below the HUD and the drop slot below that. Falls back to the spawn
    // line if the HUD bar isn't built/available, so the session still works.
    private void ResolveAnchors()
    {
        float hudBottom;
        Camera cam = Camera.main;
        if (UIManager.Instance != null && cam != null &&
            UIManager.Instance.TryGetTopHudBottomWorldY(cam, out float wy))
        {
            hudBottom = wy;
        }
        else
        {
            hudBottom = _spawner != null ? _spawner.SpawnPosition.y : 0f;
        }

        _queueWorldY = hudBottom - HudClearanceCells;
        _dropWorldY = _queueWorldY - QueueToDropCells;
    }

    public static bool IsActive { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState() => IsActive = false;

    /// <summary>Begin a Fission session: <paramref name="cellCount"/> shards (one per cell of the
    /// piece that was just shattered). The caller has already verified there is a live active piece
    /// and that cellCount >= 2.</summary>
    public static void Begin(Spawner spawner, BlockDefinition pip, int cellCount)
    {
        if (IsActive || spawner == null || pip == null || cellCount < 2) return;

        GameObject go = new GameObject("FissionSession");
        go.AddComponent<FissionSession>().StartSession(spawner, pip, cellCount);
    }

    private void StartSession(Spawner spawner, BlockDefinition pip, int cellCount)
    {
        IsActive = true;
        ActivePieceSession.Enter();
        _spawner = spawner;
        _pip = pip;
        ResolveAnchors();

        // Withhold bag pieces for the whole session - the session feeds its own shards. Set BEFORE
        // the first shard can lock so a lock never injects a normal piece mid-session.
        _spawner.SetAutoSpawnSuspended(true);

        // Shard #1 reuses the proven transmute primitive (cleanly destroys the original piece and
        // wires the replacement), then hovers.
        if (!_spawner.ReplaceActivePiece(_pip, DropPos))
        {
            Finish();
            return;
        }
        BlockController active = BlockController.ActiveControlled;
        if (active != null) active.SetDescentSuspended(true);

        // The remaining shards are the floating queue. Clone the look from the live shard so the
        // ghosts match the current theme's Pip skin exactly.
        int ghostCount = cellCount - 1;
        for (int i = 0; i < ghostCount; i++)
        {
            Ghost ghost = active != null ? CreateGhost(active, Random.Range(0f, Mathf.PI * 2f)) : null;
            if (ghost != null) _ghosts.Add(ghost);
        }
        LayoutGhostsImmediate();
        // Gameplay count is the true number of shards still to drop (one per remaining cell),
        // independent of how many ghost visuals built - the ghosts are presentation only.
        _shardsRemaining = ghostCount;

        GameEvents.BlockLocked += HandleBlockLocked;
    }

    private void OnDisable() => GameEvents.BlockLocked -= HandleBlockLocked;

    // Safety net: if the session GameObject is torn down WITHOUT a normal Finish (a scene reload /
    // level restart mid-session), the static IsActive would otherwise stay true and permanently
    // gate consumables + Pocket Cache in the next run. Mirrors ExtractTargetingSession's guard.
    private void OnDestroy()
    {
        if (!_finishing) Finish();
    }

    // The active shard just landed. Feed the next one (the front ghost glides into the slot), or,
    // if this was the last, drop the spawn lock so the Spawner's own lock->spawn chain (which calls
    // SpawnNextBlock right after this event) resumes normal play.
    private void HandleBlockLocked()
    {
        if (_finishing) return;

        if (_shardsRemaining > 0)
        {
            BlockController shard = _spawner.SpawnControlledPieceAt(_pip, DropPos, suspended: true);
            if (shard == null)
            {
                // Spawn refused (misconfig) - bail out cleanly rather than strand the run.
                Finish();
                return;
            }

            ConsumeFrontGhost();
            _shardsRemaining--;
            SfxPlayer.Play("swoosh_01", 0.6f, 0.06f);
        }
        else
        {
            _spawner.SetAutoSpawnSuspended(false);
            Finish();
        }
    }

    private void Update()
    {
        if (_finishing) return;

        // Game over can destroy the active shard without a lock event - tear down so we never
        // strand the spawn lock or leak ghosts (Finish clears the spawn lock, null-safe).
        if (GameManager.Instance != null && GameManager.Instance.isGameOver)
        {
            Finish();
            return;
        }

        ResolveAnchors();
        AnimateGhosts(Time.unscaledDeltaTime);
    }

    // ---- Ghost visuals -----------------------------------------------------------------------

    private void AnimateGhosts(float dt)
    {
        float time = Time.unscaledTime;

        // Live queue members lerp toward their (recentred) slot in forward order, plus a bob.
        int liveCount = 0;
        for (int i = 0; i < _ghosts.Count; i++) if (!_ghosts[i].FlyingOut) liveCount++;

        int idx = 0;
        for (int i = 0; i < _ghosts.Count; i++)
        {
            Ghost ghost = _ghosts[i];
            if (ghost.Root == null || ghost.FlyingOut) continue;

            Vector3 target = SlotPosition(idx, liveCount);
            target.y += Mathf.Sin(time * HoverSpeed + ghost.Phase) * HoverAmplitude;
            ghost.Root.position = Vector3.Lerp(ghost.Root.position, target, 1f - Mathf.Exp(-ReflowLerp * dt));
            idx++;
        }

        // Flying-out members glide into the drop slot, fade, then destroy (reverse loop = safe removal).
        for (int i = _ghosts.Count - 1; i >= 0; i--)
        {
            Ghost ghost = _ghosts[i];
            if (ghost.Root == null) { _ghosts.RemoveAt(i); continue; }
            if (!ghost.FlyingOut) continue;

            ghost.FlyAge += dt;
            float t = Mathf.Clamp01(ghost.FlyAge / FlyInSeconds);
            float e = Smooth01(t);
            ghost.Root.position = Vector3.Lerp(ghost.FlyFrom, DropPos, e);
            ghost.Root.localScale = Vector3.one * Mathf.Lerp(GhostScale, 1f, e);
            SetGhostAlpha(ghost, (1f - e) * GhostAlpha);
            if (t >= 1f)
            {
                Destroy(ghost.Root.gameObject);
                _ghosts.RemoveAt(i);
            }
        }
    }

    // Front (index 0 among live ghosts) flies into the drop slot to "become" the new active shard.
    private void ConsumeFrontGhost()
    {
        for (int i = 0; i < _ghosts.Count; i++)
        {
            Ghost ghost = _ghosts[i];
            if (ghost.FlyingOut) continue;
            ghost.FlyingOut = true;
            ghost.FlyAge = 0f;
            ghost.FlyFrom = ghost.Root != null ? ghost.Root.position : DropPos;
            return;
        }
    }

    private void LayoutGhostsImmediate()
    {
        int liveCount = 0;
        for (int i = 0; i < _ghosts.Count; i++) if (!_ghosts[i].FlyingOut) liveCount++;

        int idx = 0;
        for (int i = 0; i < _ghosts.Count; i++)
        {
            Ghost ghost = _ghosts[i];
            if (ghost.FlyingOut || ghost.Root == null) continue;
            ghost.Root.position = SlotPosition(idx, liveCount);
            idx++;
        }
    }

    private Vector3 SlotPosition(int index, int liveCount)
    {
        float totalWidth = Mathf.Max(0, liveCount - 1) * QueueSpacingCells;
        float centerX = _spawner != null ? _spawner.SpawnPosition.x : 0f;
        float x = centerX - totalWidth * 0.5f + index * QueueSpacingCells;
        return new Vector3(x, _queueWorldY, _spawner != null ? _spawner.SpawnPosition.z : 0f);
    }

    private Ghost CreateGhost(BlockController source, float phase)
    {
        SpriteRenderer[] sourceRenderers = source.GetComponentsInChildren<SpriteRenderer>();
        var clones = new List<SpriteRenderer>();
        var baseColors = new List<Color>();
        var propertyBlock = new MaterialPropertyBlock();

        GameObject root = new GameObject("FissionGhost");
        root.transform.position = source.transform.position;
        root.transform.rotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        for (int i = 0; i < sourceRenderers.Length; i++)
        {
            SpriteRenderer src = sourceRenderers[i];
            if (src == null || !src.enabled || src.sprite == null) continue;
            if (src.gameObject.name == "PlacementBeam") continue;

            GameObject child = new GameObject(src.gameObject.name);
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = source.transform.InverseTransformPoint(src.transform.position);
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = src.transform.lossyScale;

            SpriteRenderer clone = child.AddComponent<SpriteRenderer>();
            clone.sprite = src.sprite;
            clone.sharedMaterial = src.sharedMaterial;
            clone.drawMode = src.drawMode;
            clone.size = src.size;
            clone.flipX = src.flipX;
            clone.flipY = src.flipY;
            clone.sortingLayerID = src.sortingLayerID;
            clone.sortingOrder = src.sortingOrder + SortingOrderLift;
            propertyBlock.Clear();
            src.GetPropertyBlock(propertyBlock);
            clone.SetPropertyBlock(propertyBlock);

            Color baseColor = src.color;
            baseColor.a = GhostAlpha;
            clone.color = baseColor;
            clones.Add(clone);
            baseColors.Add(baseColor);
        }

        if (clones.Count == 0)
        {
            Destroy(root);
            return null;
        }

        root.transform.localScale = Vector3.one * GhostScale;
        return new Ghost
        {
            Root = root.transform,
            Renderers = clones.ToArray(),
            BaseColors = baseColors.ToArray(),
            Phase = phase
        };
    }

    private static void SetGhostAlpha(Ghost ghost, float alpha)
    {
        for (int i = 0; i < ghost.Renderers.Length; i++)
        {
            SpriteRenderer renderer = ghost.Renderers[i];
            if (renderer == null) continue;
            Color color = ghost.BaseColors[i];
            color.a = alpha;
            renderer.color = color;
        }
    }

    private void Finish()
    {
        if (_finishing) return;
        _finishing = true;

        for (int i = 0; i < _ghosts.Count; i++)
        {
            if (_ghosts[i].Root != null) Destroy(_ghosts[i].Root.gameObject);
        }
        _ghosts.Clear();

        if (_spawner != null) _spawner.SetAutoSpawnSuspended(false);
        IsActive = false;
        ActivePieceSession.Exit();
        Destroy(gameObject);
    }

    private static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
