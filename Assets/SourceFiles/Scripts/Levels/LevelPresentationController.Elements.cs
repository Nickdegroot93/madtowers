using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// World-element half of the backdrop: clouds (drift + gentle bob), hill silhouettes
/// with base fill, the faint sun, ground props and ambient particles. All play-mode
/// only, recycled around the camera. Sky/preset logic lives in
/// LevelPresentationController.cs.
/// </summary>
public partial class LevelPresentationController
{
    // ---- world elements (play mode only) -------------------------------------------------

    private void EnsureWorldElements()
    {
        if (_worldRoot != null || _preset == null || targetCamera == null) return;

        _worldRoot = new GameObject("BackdropElements").transform;
        _climbBaseY = targetCamera.transform.position.y;
        // Horizontal parallax measures sideways drift from the resting framing center, not from
        // wherever the opening pan starts the camera - so layers settle to neutral during play
        // and slide into place as the pan glides back to center.
        _panBaseX = TowerCameraController.FramingCenterX;

        IReadOnlyList<BackdropPreset.SpriteBackdropLayer> spriteLayers = _preset.SpriteBackdropLayers;
        int spriteLayerCount = spriteLayers != null ? spriteLayers.Count : 0;
        _spriteBackdropLayerTiles = new SpriteRenderer[spriteLayerCount][];
        _spriteBackdropAprons = new SpriteRenderer[spriteLayerCount];
        for (int i = 0; i < spriteLayerCount; i++)
        {
            BackdropPreset.SpriteBackdropLayer layer = spriteLayers[i];
            if (layer == null || layer.Sprite == null) continue;

            // A fill layer is a single full-screen panorama (no tiling); others tile sideways.
            int tileRadius = layer.FillView ? 0 : layer.HorizontalTileRadius;
            int tileCount = tileRadius * 2 + 1;
            _spriteBackdropLayerTiles[i] = new SpriteRenderer[tileCount];
            for (int tile = 0; tile < tileCount; tile++)
            {
                GameObject go = new GameObject($"SpriteBackdrop{i}_{tile - tileRadius}");
                go.transform.SetParent(_worldRoot, false);
                SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = layer.Sprite;
                sr.color = new Color(1f, 1f, 1f, layer.Alpha);
                sr.sortingOrder = SpriteBackdropSortingOrder + i;
                _spriteBackdropLayerTiles[i][tile] = sr;
            }

            // Solid ground apron below an opaque ground layer: guarantees no seam or plain gap
            // beneath it at any camera size, and sinks away with the layer as the tower climbs.
            if (!layer.FillView && layer.GroundFillColor.a > 0f)
            {
                GameObject apronGo = new GameObject($"SpriteBackdropApron{i}");
                apronGo.transform.SetParent(_worldRoot, false);
                SpriteRenderer apron = apronGo.AddComponent<SpriteRenderer>();
                apron.sprite = RuntimeSprites.Square();
                apron.color = layer.GroundFillColor;
                apron.sortingOrder = SpriteBackdropSortingOrder + i; // behind its layer's detail, with it
                _spriteBackdropAprons[i] = apron;
            }
        }

        // Clouds: spread through a band around the camera, recycled as it climbs.
        int cloudCount = _preset.CloudCount;
        _clouds = new SpriteRenderer[cloudCount];
        _cloudSpeeds = new float[cloudCount];
        _cloudBobPhases = new float[cloudCount];
        for (int i = 0; i < cloudCount; i++)
        {
            GameObject cloud = new GameObject($"Cloud{i}");
            cloud.transform.SetParent(_worldRoot, false);
            SpriteRenderer sr = cloud.AddComponent<SpriteRenderer>();
            sr.sprite = _preset.Clouds switch
            {
                BackdropPreset.CloudStyle.Blocky => RuntimeSprites.BlockyCloud(i),
                BackdropPreset.CloudStyle.Streak => RuntimeSprites.StreakCloud(i),
                _ => RuntimeSprites.Cloud(i),
            };
            sr.color = _preset.CloudColor;
            sr.sortingOrder = CloudSortingOrder;
            float scale = Random.Range(_preset.CloudScaleRange.x, _preset.CloudScaleRange.y);
            cloud.transform.localScale = new Vector3(scale, scale, 1f);
            cloud.transform.position = RandomCloudPosition(initialSpread: true);
            _clouds[i] = sr;
            _cloudSpeeds[i] = _preset.CloudDriftSpeed * Random.Range(0.6f, 1.4f) * (Random.value < 0.5f ? -1f : 1f);
            _cloudBobPhases[i] = Random.Range(0f, Mathf.PI * 2f);
        }

        // Hills: three parallax silhouettes (far -> near, hazier far color blended
        // automatically) parked at the floor; they leave the frame as you climb. A solid
        // base fill below them guarantees no cutoff line at any camera zoom.
        if (_preset.HillsEnabled)
        {
            _hills = new SpriteRenderer[3];
            for (int i = 0; i < 3; i++)
            {
                GameObject hill = new GameObject($"Hill{i}");
                hill.transform.SetParent(_worldRoot, false);
                SpriteRenderer sr = hill.AddComponent<SpriteRenderer>();
                sr.sprite = _preset.Hills == BackdropPreset.HillStyle.Mesa
                    ? RuntimeSprites.SteppedMesa(i)
                    : RuntimeSprites.HillSilhouette(i);
                sr.color = Color.Lerp(_preset.HillFarColor, _preset.HillNearColor, i / 2f);
                sr.sortingOrder = HillFarSortingOrder + i;
                _hills[i] = sr;
            }

            GameObject baseFill = new GameObject("HillBase");
            baseFill.transform.SetParent(_worldRoot, false);
            _hillBase = baseFill.AddComponent<SpriteRenderer>();
            _hillBase.sprite = RuntimeSprites.Square();
            _hillBase.color = _preset.HillNearColor;
            _hillBase.sortingOrder = HillFarSortingOrder - 1;
        }

        // Faint sun disc, revealed/passed as the tower climbs.
        if (_preset.SunEnabled)
        {
            GameObject sun = new GameObject("Sun");
            sun.transform.SetParent(_worldRoot, false);
            _sun = sun.AddComponent<SpriteRenderer>();
            _sun.sprite = RuntimeSprites.SoftDot();
            _sun.color = _preset.SunColor;
            _sun.sortingOrder = CloudSortingOrder - 5; // behind clouds, above the sky
            sun.transform.localScale = new Vector3(_preset.SunSize, _preset.SunSize, 1f);
        }

        // Ground props (cacti): hug the screen edges, alternating sides, but never closer
        // to the center than the floor footprint allows.
        GameModeConfig activeMode = LevelSelectionState.ResolveGameMode(null);
        _propMinFromCenter = (activeMode != null ? activeMode.FloorWidth : 9f) * 0.5f + 2.2f;

        int propCount = _preset.PropCount;
        _props = new SpriteRenderer[propCount];
        _propOffsets = new float[propCount];
        for (int i = 0; i < propCount; i++)
        {
            GameObject prop = new GameObject($"Prop{i}");
            prop.transform.SetParent(_worldRoot, false);
            SpriteRenderer sr = prop.AddComponent<SpriteRenderer>();
            sr.sprite = RuntimeSprites.Cactus(i);
            sr.color = _preset.PropColor;
            sr.sortingOrder = PropSortingOrder; // in front of all hill layers, behind the plateau
            float scale = Random.Range(_preset.PropScaleRange.x, _preset.PropScaleRange.y);
            prop.transform.localScale = new Vector3(scale, scale, 1f);

            _propOffsets[i] = Random.Range(0.9f, 2f); // inset from the screen edge
            _props[i] = sr;
        }

        int particleCount = _preset.ParticleCount;
        _particles = new Transform[particleCount];
        _particlePhases = new float[particleCount];
        for (int i = 0; i < particleCount; i++)
        {
            GameObject particle = new GameObject($"Ambient{i}");
            particle.transform.SetParent(_worldRoot, false);
            SpriteRenderer sr = particle.AddComponent<SpriteRenderer>();
            bool streak = _preset.ParticleStreakLength > 0f;
            sr.sprite = streak ? RuntimeSprites.Streak() : RuntimeSprites.SoftDot();
            sr.color = _preset.ParticleColor;
            // weather (rain) renders over every imported layer; ambient motes sit among them
            sr.sortingOrder = _preset.ParticlesInFront ? FrontParticleSortingOrder : ParticleSortingOrder;
            float size = _preset.ParticleSize * Random.Range(0.7f, 1.3f);
            particle.transform.localScale = streak
                ? new Vector3(size, _preset.ParticleStreakLength * Random.Range(0.85f, 1.15f), 1f)
                : new Vector3(size, size, 1f);
            if (streak)
            {
                // long axis follows the fall velocity so wind visibly slants the rain
                float slant = Mathf.Atan2(_preset.ParticleWindX,
                    Mathf.Max(0.01f, _preset.ParticleFallSpeed)) * Mathf.Rad2Deg;
                particle.transform.rotation = Quaternion.Euler(0f, 0f, slant);
            }
            particle.transform.position = RandomParticlePosition(anywhere: true);
            _particles[i] = particle.transform;
            _particlePhases[i] = Random.Range(0f, Mathf.PI * 2f);
        }

        CreateAmbienceElements();
    }

    private float CameraHalfHeight => targetCamera.orthographicSize;
    private float CameraHalfWidth => targetCamera.orthographicSize * targetCamera.aspect;

    // Overscan for fill-view panoramas: a hair larger than the view so no edge ever shows.
    private const float FillViewOverscan = 1.04f;
    // Fill-view panoramas darken slightly toward this tint at full altitude, preserving the
    // "the air thins as you climb" feel the old parallaxing sky layer gave before it cut off.
    private static readonly Color FillViewHighTint = new Color(0.72f, 0.74f, 0.8f, 1f);
    // How far a ground apron runs below its layer's bottom; deep enough to clear the screen
    // bottom at the start, finite so it sinks out of view with the layer as the tower climbs.
    private const float ApronDepth = 50f;

    private void UpdateSpriteBackdropLayers()
    {
        if (_spriteBackdropLayerTiles == null || _spriteBackdropLayerTiles.Length == 0 || targetCamera == null) return;

        IReadOnlyList<BackdropPreset.SpriteBackdropLayer> layers = _preset.SpriteBackdropLayers;
        if (layers == null) return;

        Vector3 cam = targetCamera.transform.position;
        float floorY = GameManager.Instance != null
            ? GameManager.Instance.floorOriginY
            : cam.y - CameraHalfHeight;
        float climbed = Climbed(cam);
        float panX = cam.x - _panBaseX; // sideways drift from the neutral framing center
        float altitude01 = Altitude01();

        for (int i = 0; i < _spriteBackdropLayerTiles.Length && i < layers.Count; i++)
        {
            SpriteRenderer[] tiles = _spriteBackdropLayerTiles[i];
            BackdropPreset.SpriteBackdropLayer layer = layers[i];
            if (tiles == null || tiles.Length == 0 || layer == null) continue;

            SpriteRenderer sample = tiles[0];
            if (sample == null || sample.sprite == null) continue;

            Vector2 size = sample.sprite.bounds.size;
            if (size.x <= 0f || size.y <= 0f) continue;

            if (layer.FillView)
            {
                UpdateFillViewLayer(sample, layer, size, cam, altitude01);
                continue;
            }

            float targetHeight = layer.WorldHeight > 0f ? layer.WorldHeight : CameraHalfHeight * 2.15f;
            float scale = targetHeight / size.y;
            float scaledHeight = size.y * scale;
            float tileSpacing = Mathf.Max(0.1f, size.x * scale - layer.HorizontalTileOverlap);
            // Sideways parallax: anchor drifts LESS than the camera (factor < 1) so near layers
            // track the pan and far layers lag, reading as depth. factor 1 == glued (no parallax).
            float anchorX = _panBaseX + panX * layer.HorizontalParallax + layer.WorldOffsetX;
            float y = floorY + layer.FloorOffsetY + scaledHeight * 0.5f + climbed * layer.VerticalParallax;
            // Hover bob (flying craft etc.): smooth sine, phase-offset per layer so
            // multiple hovering layers never move in lockstep.
            if (layer.HoverAmount > 0f)
            {
                y += Mathf.Sin(Time.time * (Mathf.PI * 2f / layer.HoverPeriodSeconds) + i * 1.7f) * layer.HoverAmount;
            }
            int center = tiles.Length / 2;
            // Endless sideways drift (clouds, mist): each tile's offset wraps within the row's
            // total width, so a tile leaving one end reappears at the other and coverage around
            // the anchor never thins out however long the scroll runs.
            float drift = layer.DriftSpeedX * Time.time;
            float rowWidth = tiles.Length * tileSpacing;

            for (int tile = 0; tile < tiles.Length; tile++)
            {
                SpriteRenderer sr = tiles[tile];
                if (sr == null) continue;

                float offsetX = (tile - center) * tileSpacing;
                if (layer.DriftSpeedX != 0f)
                {
                    offsetX = Mathf.Repeat(offsetX + drift + rowWidth * 0.5f, rowWidth) - rowWidth * 0.5f;
                }
                sr.transform.localScale = new Vector3(scale, scale, 1f);
                sr.transform.position = new Vector3(anchorX + offsetX, y, 0f);
                // Alpha applied per frame like the fill layers, so preset edits show up live.
                if (!Mathf.Approximately(sr.color.a, layer.Alpha))
                {
                    sr.color = new Color(1f, 1f, 1f, layer.Alpha);
                }
            }

            UpdateLayerApron(i, cam, y - scaledHeight * 0.5f);
        }
    }

    // A full-screen panorama (the back-most sky/atmosphere). Scaled UNIFORMLY to cover the camera
    // view plus overscan and centered on it - its edges can never enter the frame, so it never cuts
    // off at the top however high the tower climbs. Uniform (not per-axis) so the artwork keeps its
    // aspect: a round sun stays round. The overflowing side/edge is simply cropped off-screen.
    private void UpdateFillViewLayer(SpriteRenderer sr, BackdropPreset.SpriteBackdropLayer layer,
        Vector2 size, Vector3 cam, float altitude01)
    {
        float scale = Mathf.Max(
            (CameraHalfWidth * 2f) / size.x,
            (CameraHalfHeight * 2f) / size.y) * FillViewOverscan;
        sr.transform.localScale = new Vector3(scale, scale, 1f);
        sr.transform.position = new Vector3(cam.x, cam.y, 0f);

        Color tint = Color.Lerp(Color.white, FillViewHighTint, altitude01);
        tint.a = layer.Alpha;
        sr.color = tint;
    }

    // Solid apron pinned to a layer's opaque bottom, running deep down so the ground always
    // reaches the screen bottom with no seam, and sinking with the layer as the tower climbs.
    private void UpdateLayerApron(int index, Vector3 cam, float layerBottomY)
    {
        if (_spriteBackdropAprons == null || index >= _spriteBackdropAprons.Length) return;
        SpriteRenderer apron = _spriteBackdropAprons[index];
        if (apron == null) return;

        float width = CameraHalfWidth * 2.6f; // always spans the view, whatever the pan
        apron.transform.localScale = new Vector3(width, ApronDepth, 1f);
        // Top edge at the layer's bottom (small overlap to hide the seam), extending downward.
        apron.transform.position = new Vector3(cam.x, layerBottomY + 0.06f - ApronDepth * 0.5f, 0f);
    }

    private Vector3 RandomCloudPosition(bool initialSpread)
    {
        Vector3 cam = targetCamera.transform.position;
        float x = cam.x + Random.Range(-CameraHalfWidth, CameraHalfWidth) * 1.2f;
        float y = initialSpread
            ? cam.y + Random.Range(-CameraHalfHeight, CameraHalfHeight * 2f)
            : cam.y + Random.Range(CameraHalfHeight * 1.1f, CameraHalfHeight * 1.8f);
        return new Vector3(x, y, 0f);
    }

    private void UpdateClouds()
    {
        if (_clouds == null) return;

        Vector3 cam = targetCamera.transform.position;
        float wrapX = CameraHalfWidth * 1.5f;
        const float BobAmplitude = 0.18f;
        const float BobFrequency = 0.22f;
        for (int i = 0; i < _clouds.Length; i++)
        {
            Transform cloud = _clouds[i].transform;
            Vector3 pos = cloud.position;
            pos.x += _cloudSpeeds[i] * Time.deltaTime;
            // Gentle vertical bob (delta of a sine, so it composes with recycling).
            float phase = _cloudBobPhases[i];
            pos.y += (Mathf.Sin((Time.time) * BobFrequency + phase)
                      - Mathf.Sin((Time.time - Time.deltaTime) * BobFrequency + phase)) * BobAmplitude;

            if (pos.x > cam.x + wrapX) pos.x = cam.x - wrapX;
            else if (pos.x < cam.x - wrapX) pos.x = cam.x + wrapX;

            // Fell far below the view (camera climbed past it): respawn above.
            if (pos.y < cam.y - CameraHalfHeight * 1.6f)
            {
                pos = RandomCloudPosition(initialSpread: false);
            }
            cloud.position = pos;
        }
    }

    private void UpdateHills()
    {
        if (_hills == null || GameManager.Instance == null) return;

        // Anchored at the floor with slight upward parallax: distant hills track the
        // camera a touch, so they linger longer before sinking out of view.
        float floorY = GameManager.Instance.floorOriginY;
        Vector3 cam = targetCamera.transform.position;
        float climbed = Climbed(cam);
        float width = CameraHalfWidth * 2.6f;

        float nearY = floorY;
        float nearScale = 1f;
        for (int i = 0; i < _hills.Length; i++)
        {
            SpriteRenderer hill = _hills[i];
            float parallax = 0.2f - 0.07f * i;             // far hills cling to the view longer
            float centerOffsetY = 0.4f - 0.8f * i;         // far crests peek above the near ones
            Vector2 size = hill.sprite.bounds.size;
            float scale = width / size.x;
            hill.transform.localScale = new Vector3(scale, scale, 1f);
            float y = floorY + centerOffsetY + climbed * parallax;
            hill.transform.position = new Vector3(cam.x, y, 0f);
            if (i == _hills.Length - 1) { nearY = y; nearScale = scale; }
        }

        // Solid fill starting just below the nearest hill's lowest valley (which scales
        // with zoom) and running deep down: no cutoff line at any camera zoom.
        if (_hillBase != null)
        {
            const float FillDepth = 60f;
            float lowestValley = nearY - 1.27f * nearScale; // crest minimum of the silhouette
            _hillBase.transform.localScale = new Vector3(width, FillDepth, 1f);
            _hillBase.transform.position = new Vector3(cam.x, lowestValley - FillDepth * 0.5f + 0.1f, 0f);
        }
    }

    // The sun sits at a fixed screen X; vertically it lives near sunHeightMeters but
    // drifts at 90% of camera speed, so it floats through the view over a long climb band.
    private void UpdateSun()
    {
        if (_sun == null || GameManager.Instance == null) return;

        Vector3 cam = targetCamera.transform.position;
        float floorY = GameManager.Instance.floorOriginY;
        float x = cam.x + (_preset.SunScreenX - 0.5f) * 2f * CameraHalfWidth * 0.85f;
        float y = floorY + _preset.SunHeightMeters + Climbed(cam) * 0.9f;
        _sun.transform.position = new Vector3(x, y, 0f);
    }

    private float Climbed(Vector3 cameraPosition)
    {
        return Mathf.Max(0f, cameraPosition.y - _climbBaseY);
    }

    private void UpdateProps()
    {
        if (_props == null || _props.Length == 0 || GameManager.Instance == null) return;

        float floorY = GameManager.Instance.floorOriginY;
        Vector3 cam = targetCamera.transform.position;
        float climbed = Climbed(cam);
        for (int i = 0; i < _props.Length; i++)
        {
            SpriteRenderer prop = _props[i];
            float halfHeight = prop.sprite.bounds.size.y * prop.transform.localScale.y * 0.5f;
            // Near the screen edge (reads as "coming in from off-screen", and slides
            // outward as the camera zooms), but never over the floor.
            float side = i % 2 == 0 ? 1f : -1f;
            float fromCenter = Mathf.Max(_propMinFromCenter, CameraHalfWidth - _propOffsets[i]);
            prop.transform.position = new Vector3(
                cam.x + side * fromCenter,
                floorY + halfHeight - 0.15f + climbed * 0.05f, // base on the ground, slight parallax
                0f);
        }
    }

    private Vector3 RandomParticlePosition(bool anywhere)
    {
        Vector3 cam = targetCamera.transform.position;
        float x = cam.x + Random.Range(-CameraHalfWidth, CameraHalfWidth) * 1.1f;
        float y = anywhere
            ? cam.y + Random.Range(-CameraHalfHeight, CameraHalfHeight)
            : cam.y + CameraHalfHeight * Random.Range(1.05f, 1.3f);
        // spawn upwind by half the expected drift so wind-blown particles still cover the view
        if (_preset.ParticleWindX != 0f && _preset.ParticleFallSpeed > 0.01f)
        {
            x -= _preset.ParticleWindX * (CameraHalfHeight * 2.3f / _preset.ParticleFallSpeed) * 0.5f;
        }
        return new Vector3(x, y, 0f);
    }

    private void UpdateParticles()
    {
        if (_particles == null || _particles.Length == 0) return;

        Vector3 cam = targetCamera.transform.position;
        for (int i = 0; i < _particles.Length; i++)
        {
            Transform particle = _particles[i];
            Vector3 pos = particle.position;
            pos.y -= _preset.ParticleFallSpeed * Time.deltaTime;
            pos.x += Mathf.Sin(Time.time * 1.3f + _particlePhases[i]) * _preset.ParticleSwayAmount * Time.deltaTime
                     + _preset.ParticleWindX * Time.deltaTime;

            if (pos.y < cam.y - CameraHalfHeight * 1.15f)
            {
                pos = RandomParticlePosition(anywhere: false);
            }
            particle.position = pos;
        }
    }
}
