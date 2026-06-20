using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

// Landed maintenance (I1-I3): settle detection, the stillness watchdog, the knife-edge
// sleep defer, velocity-only grid pull, sleeping, external jolts, and freezing in place.
public partial class BlockController
{
    private bool IsSettled()
    {
        return _rb.linearVelocity.magnitude <= settleLinearThreshold &&
               Mathf.Abs(_rb.angularVelocity) <= settleAngularThreshold;
    }

    // Going to sleep must never move the body. A block that physics holds slightly off-grid or
    // tilted has an off-grid equilibrium: snapping it at sleep time teleports it away from that
    // equilibrium, the solver wakes it and pushes it back, and the next sleep snaps it again -
    // a metronomic, infinite twitch. Grid registration comes from honest sources instead: pieces
    // land exactly on-grid, and the awake-time velocity pull eases flat blocks toward column/angle.
    private void SleepSettledBody()
    {
        _rb.linearVelocity = Vector2.zero;
        _rb.angularVelocity = 0f;
        _rb.Sleep();
    }

    // --- Knife-edge guard (bounded I3 refinement, see PHYSICS.md) -------------------------
    // A quiet block whose centre of mass hangs horizontally outside its supporting contacts
    // is mid-tip; force-sleeping it freezes a coin on its rim. Which side of the floor that
    // happened on was decided by sub-millimetre float noise, so identical-looking edge
    // placements survived on one side and fell on the other. Deferring sleep lets gravity
    // resolve the balance honestly. Strictly bounded: after KnifeEdgeGraceSeconds of
    // staying quiet anyway (leaning, wedged, vine-held) the block sleeps normally - I3's
    // no-twitch guarantee is delayed for marginal blocks, never lost.
    private const float KnifeEdgeGraceSeconds = 2f;
    private const float SupportSpanEpsilon = 0.01f;
    private static readonly ContactPoint2D[] SharedContactBuffer = new ContactPoint2D[16];
    private float _knifeEdgeDeferTime;

    private bool ShouldDeferSleepForKnifeEdge()
    {
        if (_knifeEdgeDeferTime >= KnifeEdgeGraceSeconds) return false;

        int count = _rb.GetContacts(SharedContactBuffer);
        Vector2 centerOfMass = _rb.worldCenterOfMass;
        bool hasSupport = false;
        float supportMinX = float.MaxValue;
        float supportMaxX = float.MinValue;
        for (int i = 0; i < count; i++)
        {
            ContactPoint2D contact = SharedContactBuffer[i];
            // Supporting contact: below the centre of mass and not a pure side graze.
            // (|normal.y| so the test is robust to contact normal orientation.)
            if (contact.point.y >= centerOfMass.y - 0.05f) continue;
            if (Mathf.Abs(contact.normal.y) < 0.5f) continue;
            hasSupport = true;
            supportMinX = Mathf.Min(supportMinX, contact.point.x);
            supportMaxX = Mathf.Max(supportMaxX, contact.point.x);
        }

        bool knifeEdged = hasSupport &&
            (centerOfMass.x < supportMinX - SupportSpanEpsilon ||
             centerOfMass.x > supportMaxX + SupportSpanEpsilon);
        if (!knifeEdged)
        {
            _knifeEdgeDeferTime = 0f;
            return false;
        }

        _knifeEdgeDeferTime += Time.fixedDeltaTime;
        return _knifeEdgeDeferTime < KnifeEdgeGraceSeconds;
    }

    private void HandleLandedMaintenance()
    {
        if ((!microAlignSettledBlocks && !sleepSettledBlocksOnLock) || _rb == null) return;
        if (_rb.bodyType != RigidbodyType2D.Dynamic || _rb.IsSleeping()) return;

        bool deferSleep = ShouldDeferSleepForKnifeEdge();

        UpdateStillnessWatchdog(deferSleep);
        if (_rb.IsSleeping()) return;

        // While deferred, the block stays fully live - no grid pull, no soft damping, no
        // settle timer - so nothing slows the tip that resolves the knife edge.
        if (IsSettled() && !deferSleep)
        {
            PullQuietBlockTowardGrid();
            SoftDampSettledBody();
            _landedMaintenanceSettleTimer += Time.fixedDeltaTime;
            if (_landedMaintenanceSettleTimer >= settleTime)
            {
                // Sleep freezes the block exactly where physics left it (see SleepSettledBody).
                if (sleepSettledBlocksOnLock)
                {
                    SleepSettledBody();
                }
                _landedMaintenanceSettleTimer = 0f;
            }
        }
        else
        {
            _landedMaintenanceSettleTimer = 0f;
        }
    }

    // The velocity-based settle check above can be defeated by a marginal contact configuration:
    // a block pivoting on a corner alternates between two contact states and the solver kicks it
    // every cycle, so its instantaneous velocity never stays quiet. But such a limit cycle has
    // zero NET movement, which is what this watchdog measures. Anything that is not actually
    // going anywhere is put to sleep, making persistent twitching structurally impossible.
    private void UpdateStillnessWatchdog(bool deferSleep)
    {
        if (!sleepSettledBlocksOnLock) return;

        float positionDrift = Vector2.Distance(_rb.position, _stillnessAnchorPosition);
        float rotationDrift = Mathf.Abs(Mathf.DeltaAngle(_rb.rotation, _stillnessAnchorRotation));
        if (positionDrift > stillnessPositionTolerance || rotationDrift > stillnessRotationToleranceDegrees)
        {
            _stillnessAnchorPosition = _rb.position;
            _stillnessAnchorRotation = _rb.rotation;
            _stillnessTimer = 0f;
            return;
        }

        _stillnessTimer += Time.fixedDeltaTime;
        // The timer keeps accruing while a knife-edge defers sleep, so the moment the
        // grace expires the watchdog acts immediately - the I3 guarantee is delayed for
        // marginal blocks, never lost.
        if (_stillnessTimer >= stillnessTime && !deferSleep)
        {
            SleepSettledBody();
        }
    }

    private void SoftDampSettledBody()
    {
        float damping = Mathf.Clamp01(softSettleDampingFactor);
        _rb.linearVelocity *= damping;
        _rb.angularVelocity *= damping;
    }

    private void PullQuietBlockTowardGrid()
    {
        if (!microAlignSettledBlocks) return;

        // Tolerance contract: only ease blocks that are already essentially in place. A piece
        // that tipped, tilted, or slid beyond the caps can never reach its snapped grid pose, so
        // pulling it every frame would turn it into a permanent agitator for the whole tower.
        float snappedRotation = SnapValue(_rb.rotation, RotationStep);
        float rotationCorrection = Mathf.DeltaAngle(_rb.rotation, snappedRotation);
        float absRotationCorrection = Mathf.Abs(rotationCorrection);
        if (absRotationCorrection > Mathf.Max(0f, microAlignMaxRotationDegrees)) return;

        PullQuietBlockRotationTowardGrid(rotationCorrection);
        if (absRotationCorrection > QuietPullMaxTiltDegrees) return;

        _cellGeometry.Refresh();
        float primaryX = _cellGeometry.GetPrimaryWorldX(transform.position.x);
        float correction = SnapValue(primaryX, gridSpacing) - primaryX;
        if (Mathf.Abs(correction) <= 0.001f * gridSpacing) return;
        if (Mathf.Abs(correction) > Mathf.Max(0f, microAlignMaxColumnFraction) * gridSpacing) return;

        // Correct via a small velocity bias, never by writing the transform. Position writes
        // fought the contact solver (each step created fresh penetration that popped the
        // neighbours awake) and broke rigidbody interpolation, which made whole towers shimmer.
        // A sub-settle-threshold velocity keeps the solver in charge and never resets the
        // settle timer; whatever drift remains is closed by the bounded snap at sleep time.
        float maxPullSpeed = Mathf.Max(0f, quietGridPullMaxSpeedFraction) * gridSpacing;
        float pullSpeed = Mathf.Clamp(
            correction * Mathf.Clamp01(quietGridPullFactor) / Time.fixedDeltaTime,
            -maxPullSpeed, maxPullSpeed);

        Vector2 velocity = _rb.linearVelocity;
        velocity.x = pullSpeed;
        _rb.linearVelocity = velocity;
    }

    private void PullQuietBlockRotationTowardGrid(float correctionDegrees)
    {
        if (Mathf.Abs(correctionDegrees) <= 0.01f) return;

        // Rotate via angular velocity, never by writing rb.rotation. Keep the pull below the
        // settled threshold so the correction itself does not prevent the normal sleep path.
        float maxPullSpeed = Mathf.Max(0f, settleAngularThreshold * 0.5f);
        float pullSpeed = Mathf.Clamp(
            correctionDegrees * Mathf.Clamp01(quietGridPullFactor) / Time.fixedDeltaTime,
            -maxPullSpeed, maxPullSpeed);

        _rb.angularVelocity = pullSpeed;
    }

    // External disturbance (earthquakes, wind, ...) as a velocity impulse - the only legal way
    // for outside systems to push a landed block (PHYSICS.md I1: never positions). Anchored
    // (Static) blocks ignore jolts by nature of their body type.
    public void ApplyJolt(Vector2 velocityChange)
    {
        if (_rb == null || _rb.bodyType != RigidbodyType2D.Dynamic) return;

        _rb.WakeUp();
        _rb.linearVelocity += velocityChange;
    }

    // Freezes this block permanently exactly where it currently is - used by anchor brick
    // variants and the Freeze power-up. A Static body costs nothing in the solver and
    // acts as a player-made platform; it can never drift, wake, or be knocked over.
    public void FreezeInPlace()
    {
        if (_rb == null || _rb.bodyType == RigidbodyType2D.Static) return;

        _rb.linearVelocity = Vector2.zero;
        _rb.angularVelocity = 0f;
        _rb.bodyType = RigidbodyType2D.Static;
    }

    // The Freeze power-up's entry point: kick off the crawling-ice overlay NOW, but delay the
    // actual physics lock by physicsDelaySeconds so a settling/teetering block keeps moving for
    // a beat and then locks as the ice grabs it (it reads as the freeze stopping the motion).
    // FreezeInPlace is idempotent, so a block already frozen just no-ops.
    public void Freeze(float visualSeconds, float physicsDelaySeconds)
    {
        if (_rb == null) return;

        BuildFrostOverlay(visualSeconds);
        if (_rb.bodyType == RigidbodyType2D.Static) return;

        if (physicsDelaySeconds <= 0f) FreezeInPlace();
        else Invoke(nameof(FreezeInPlace), physicsDelaySeconds);
    }

    private static bool _frostLoaded;
    private static Material _frostTemplate;
    private bool _hasFrostOverlay;

    // One frost pane per physical cell, not one outline around the whole tetromino. The panes sample
    // their colour from the current chapter's piece art, then the Frost shader turns that into cloudy
    // ice with bevels, scratches and internal cracks. Uses the tweakable Resources/Frost.mat; falls
    // back to building from the shader. If neither exists, the physics lock still happens.
    private void BuildFrostOverlay(float seconds)
    {
        if (!_frostLoaded)
        {
            _frostLoaded = true;
            _frostTemplate = Resources.Load<Material>("Frost");
            if (_frostTemplate == null)
            {
                Shader shader = Resources.Load<Shader>("Frost");
                if (shader != null) _frostTemplate = new Material(shader);
            }
        }
        if (_frostTemplate == null) return;

        var overlays = new List<SpriteRenderer>();
        SpriteRenderer pieceRenderer = FindPieceSkinRenderer();
        SpriteRenderer sortSource = pieceRenderer != null ? pieceRenderer : GetComponentInChildren<SpriteRenderer>();
        int sortingLayerId = sortSource != null ? sortSource.sortingLayerID : 0;
        int sortingOrder = sortSource != null ? sortSource.sortingOrder : 0;

        BoxCollider2D[] colliders = GetComponentsInChildren<BoxCollider2D>();
        for (int i = 0; i < colliders.Length; i++)
        {
            BoxCollider2D box = colliders[i];
            if (box == null || box.isTrigger) continue;

            Vector3 worldCenter = box.transform.TransformPoint(box.offset);
            Vector3 localCenter = transform.InverseTransformPoint(worldCenter);
            SpriteRenderer cellRenderer = box.GetComponent<SpriteRenderer>();
            float cellSize = ResolveFrostCellSize(cellRenderer);

            if (!_hasFrostOverlay)
            {
                GameObject go = new GameObject("FrostOverlay");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(localCenter.x, localCenter.y, 0f);
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = new Vector3(cellSize, cellSize, 1f);

                SpriteRenderer overlay = go.AddComponent<SpriteRenderer>();
                overlay.sprite = RuntimeSprites.Square();
                overlay.sharedMaterial = _frostTemplate;
                overlay.sortingLayerID = sortingLayerId;
                overlay.sortingOrder = sortingOrder + 2; // above the chapter skin and any old cell renderers
                overlay.color = ResolveFrostCellTint(pieceRenderer, worldCenter,
                    cellRenderer != null ? cellRenderer.color : Color.white);
                overlays.Add(overlay);
            }
        }

        if (!_hasFrostOverlay && overlays.Count == 0)
        {
            // Fallback for a malformed prefab: frost whatever visible sprite it has instead of
            // silently losing the visual.
            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer sr = renderers[i];
                if (sr == null || !sr.enabled || sr.sprite == null) continue;
                if (sr.gameObject.name == "FrostOverlay") continue;

                GameObject go = new GameObject("FrostOverlay");
                go.transform.SetParent(sr.transform, false);
                SpriteRenderer overlay = go.AddComponent<SpriteRenderer>();
                overlay.sprite = sr.sprite;
                overlay.sharedMaterial = _frostTemplate;
                overlay.sortingLayerID = sr.sortingLayerID;
                overlay.sortingOrder = sr.sortingOrder + 2;
                overlay.color = sr.color;
                overlays.Add(overlay);
            }
        }

        if (_hasFrostOverlay) return;
        if (overlays.Count == 0) return;

        _hasFrostOverlay = true;
        FrostFx fx = gameObject.AddComponent<FrostFx>();
        fx.Play(overlays, seconds, Random.value * 50f); // per-block seed varies the crawl pattern
    }

    private SpriteRenderer FindPieceSkinRenderer()
    {
        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer sr = renderers[i];
            if (sr != null && sr.gameObject.name == "PieceSkin") return sr;
        }
        return null;
    }

    private float ResolveFrostCellSize(SpriteRenderer cellRenderer)
    {
        if (cellRenderer != null && cellRenderer.sprite != null)
        {
            Bounds spriteBounds = cellRenderer.sprite.bounds;
            Vector3 scale = cellRenderer.transform.localScale;
            float width = Mathf.Abs(spriteBounds.size.x * scale.x);
            float height = Mathf.Abs(spriteBounds.size.y * scale.y);
            float size = Mathf.Max(width, height);
            if (size > 0.01f) return size;
        }

        return Mathf.Max(0.01f, gridSpacing);
    }

    private Color ResolveFrostCellTint(SpriteRenderer pieceRenderer, Vector3 worldCenter, Color fallback)
    {
        Color tint = fallback;
        if (pieceRenderer != null && pieceRenderer.sprite != null && pieceRenderer.sprite.texture != null)
        {
            Sprite sprite = pieceRenderer.sprite;
            Texture2D texture = sprite.texture;
            if (!texture.isReadable) return tint;

            try
            {
                Vector3 localCenter = pieceRenderer.transform.InverseTransformPoint(worldCenter);
                Color sampled = SampleBestPieceColor(sprite, texture, localCenter);
                if (sampled.a > 0.05f)
                {
                    Color rendererColor = pieceRenderer.color;
                    tint = new Color(
                        sampled.r * rendererColor.r,
                        sampled.g * rendererColor.g,
                        sampled.b * rendererColor.b,
                        1f);
                }
            }
            catch (UnityException)
            {
                // Non-readable art still freezes; it just uses the renderer tint fallback.
            }
        }

        tint.a = 1f;
        return tint;
    }

    private Color SampleBestPieceColor(Sprite sprite, Texture2D texture, Vector3 localCenter)
    {
        Color best = Color.clear;
        float bestScore = -1f;
        Rect textureRect = sprite.rect;
        Bounds bounds = sprite.bounds;

        for (int i = 0; i < 5; i++)
        {
            Vector2 offset = Vector2.zero;
            if (i == 1) offset = new Vector2(-0.22f, 0.16f);
            else if (i == 2) offset = new Vector2(0.22f, 0.16f);
            else if (i == 3) offset = new Vector2(-0.18f, -0.18f);
            else if (i == 4) offset = new Vector2(0.18f, -0.18f);

            Vector2 local = new Vector2(localCenter.x + offset.x, localCenter.y + offset.y);
            float u = Mathf.InverseLerp(bounds.min.x, bounds.max.x, local.x);
            float v = Mathf.InverseLerp(bounds.min.y, bounds.max.y, local.y);
            Color sample = texture.GetPixelBilinear(
                (textureRect.x + Mathf.Clamp01(u) * textureRect.width) / texture.width,
                (textureRect.y + Mathf.Clamp01(v) * textureRect.height) / texture.height);
            if (sample.a <= 0.05f) continue;

            float max = Mathf.Max(sample.r, Mathf.Max(sample.g, sample.b));
            float min = Mathf.Min(sample.r, Mathf.Min(sample.g, sample.b));
            float saturation = max - min;
            float luminance = sample.r * 0.299f + sample.g * 0.587f + sample.b * 0.114f;
            float score = sample.a * (saturation * 0.75f + luminance * 0.25f);
            if (score <= bestScore) continue;

            best = sample;
            bestScore = score;
        }

        return best;
    }
}
