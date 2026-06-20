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
            sr.sprite = RuntimeSprites.SoftDot();
            sr.color = _preset.ParticleColor;
            sr.sortingOrder = ParticleSortingOrder;
            float size = _preset.ParticleSize * Random.Range(0.7f, 1.3f);
            particle.transform.localScale = new Vector3(size, size, 1f);
            particle.transform.position = RandomParticlePosition(anywhere: true);
            _particles[i] = particle.transform;
            _particlePhases[i] = Random.Range(0f, Mathf.PI * 2f);
        }

        CreateSpriteLayers();
    }

    private float CameraHalfHeight => targetCamera.orthographicSize;
    private float CameraHalfWidth => targetCamera.orthographicSize * targetCamera.aspect;

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

    private void CreateSpriteLayers()
    {
        BackdropPreset.SpriteLayer[] layers = _preset.SpriteLayers;
        if (layers == null || layers.Length == 0) return;

        for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
        {
            BackdropPreset.SpriteLayer layer = layers[layerIndex];
            Sprite[] sprites = layer.Sprites;
            if (sprites == null || sprites.Length == 0) continue;

            int count = layer.InstanceCount > 0 ? layer.InstanceCount : sprites.Length;
            float spread = CameraHalfWidth * layer.HorizontalSpread;

            for (int i = 0; i < count; i++)
            {
                Sprite sprite = sprites[i % sprites.Length];
                if (sprite == null) continue;

                GameObject layerObject = new GameObject($"{layer.Label}{i}");
                layerObject.transform.SetParent(_worldRoot, false);

                SpriteRenderer sr = layerObject.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.color = layer.Tint;
                sr.sortingOrder = layer.SortingOrder;

                float t = count <= 1 ? 0.5f : (float)i / (count - 1);
                float baseX = Mathf.Lerp(-spread, spread, t);
                Vector2 jitter = layer.PositionJitter;
                baseX += StableLayerRange(layer.Label, layerIndex, i, 0, -Mathf.Abs(jitter.x), Mathf.Abs(jitter.x));
                float baseY = layer.BaseYOffset + StableLayerRange(layer.Label, layerIndex, i, 1, -Mathf.Abs(jitter.y), Mathf.Abs(jitter.y));

                Vector2 scaleRange = layer.ScaleRange;
                float minScale = Mathf.Max(0.01f, Mathf.Min(scaleRange.x, scaleRange.y));
                float maxScale = Mathf.Max(minScale, Mathf.Max(scaleRange.x, scaleRange.y));

                RuntimeSpriteLayer runtime = new RuntimeSpriteLayer
                {
                    Config = layer,
                    Renderer = sr,
                    BaseX = baseX,
                    BaseY = baseY,
                    BaseScale = StableLayerRange(layer.Label, layerIndex, i, 2, minScale, maxScale)
                };
                _spriteLayerRuntimes.Add(runtime);
            }
        }
    }

    private static float StableLayerRange(string label, int layerIndex, int instanceIndex, int salt, float min, float max)
    {
        if (Mathf.Approximately(min, max)) return min;

        uint hash = 2166136261u;
        AddStableHash(ref hash, label);
        AddStableHash(ref hash, layerIndex);
        AddStableHash(ref hash, instanceIndex);
        AddStableHash(ref hash, salt);

        float t = (hash & 0x00ffffff) / 16777215f;
        return Mathf.Lerp(min, max, t);
    }

    private static void AddStableHash(ref uint hash, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            AddStableHash(ref hash, 0);
            return;
        }

        for (int i = 0; i < value.Length; i++)
        {
            hash ^= value[i];
            hash *= 16777619u;
        }
    }

    private static void AddStableHash(ref uint hash, int value)
    {
        unchecked
        {
            uint bits = (uint)value;
            for (int i = 0; i < 4; i++)
            {
                hash ^= bits & 0xffu;
                hash *= 16777619u;
                bits >>= 8;
            }
        }
    }

    private void UpdateSpriteLayers()
    {
        if (_spriteLayerRuntimes.Count == 0 || targetCamera == null) return;

        Vector3 cam = targetCamera.transform.position;
        float climbed = Climbed(cam);
        float floorY = GameManager.Instance != null
            ? GameManager.Instance.floorOriginY
            : _climbBaseY - CameraHalfHeight;

        for (int i = 0; i < _spriteLayerRuntimes.Count; i++)
        {
            RuntimeSpriteLayer runtime = _spriteLayerRuntimes[i];
            if (runtime.Renderer == null || runtime.Config == null) continue;

            float scale = runtime.BaseScale;
            if (runtime.Renderer.sprite != null && (runtime.Config.CoverCameraView || runtime.Config.FitToCameraWidth))
            {
                float spriteWidth = runtime.Renderer.sprite.bounds.size.x;
                float spriteHeight = runtime.Renderer.sprite.bounds.size.y;
                if (spriteWidth > 0f && spriteHeight > 0f)
                {
                    float widthScale = (CameraHalfWidth * 2f) / spriteWidth;
                    float fitScale = runtime.Config.CoverCameraView
                        ? Mathf.Max(widthScale, (CameraHalfHeight * 2f) / spriteHeight)
                        : widthScale;
                    scale *= fitScale * runtime.Config.FittedWidthMultiplier;
                }
            }

            float y = runtime.Config.AnchorToCamera
                ? cam.y + runtime.BaseY
                : floorY + runtime.BaseY + climbed * runtime.Config.VerticalParallax;

            Transform layerTransform = runtime.Renderer.transform;
            layerTransform.localScale = new Vector3(scale, scale, 1f);
            layerTransform.position = new Vector3(
                cam.x + runtime.BaseX,
                y,
                0f);
        }
    }

    private void UpdateBottomClarityVeil()
    {
        if (_preset == null || !_preset.BottomClarityVeilEnabled || targetCamera == null)
        {
            if (_bottomClarityVeil != null) _bottomClarityVeil.enabled = false;
            return;
        }

        EnsureBottomClarityVeil();
        if (_bottomClarityVeil == null || _bottomClarityVeil.sprite == null) return;

        _bottomClarityVeil.enabled = true;
        Vector3 cam = targetCamera.transform.position;
        float height = CameraHalfHeight * 2f * Mathf.Max(0.01f, _preset.BottomClarityVeilHeight);
        float width = CameraHalfWidth * 2.2f;
        Vector2 spriteSize = _bottomClarityVeil.sprite.bounds.size;
        if (spriteSize.x <= 0f || spriteSize.y <= 0f) return;

        _bottomClarityVeil.transform.position = new Vector3(
            cam.x,
            cam.y - CameraHalfHeight + height * 0.5f,
            0f);
        _bottomClarityVeil.transform.localScale = new Vector3(
            width / spriteSize.x,
            height / spriteSize.y,
            1f);
    }

    private void EnsureBottomClarityVeil()
    {
        if (_bottomClarityVeil != null) return;

        if (_bottomClarityVeilSprite == null)
        {
            _bottomClarityVeilSprite = CreateBottomClarityVeilSprite(_preset.BottomClarityVeilColor, _preset.BottomClarityVeilCurve);
        }

        GameObject veil = new GameObject("BottomClarityVeil");
        veil.transform.SetParent(_worldRoot, false);
        _bottomClarityVeil = veil.AddComponent<SpriteRenderer>();
        _bottomClarityVeil.sprite = _bottomClarityVeilSprite;
        _bottomClarityVeil.sortingOrder = ParticleSortingOrder + 1;
    }

    private static Sprite CreateBottomClarityVeilSprite(Color color, float curve)
    {
        const int Height = 128;
        Texture2D tex = new Texture2D(1, Height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        float maxAlpha = Mathf.Clamp01(color.a);
        Color pixel = color;
        for (int y = 0; y < Height; y++)
        {
            float t = (float)y / (Height - 1);
            pixel.a = maxAlpha * Mathf.Pow(1f - t, Mathf.Max(0.25f, curve));
            tex.SetPixel(0, y, pixel);
        }

        tex.Apply();
        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, 1, Height), new Vector2(0.5f, 0.5f), Height);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
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
            pos.x += Mathf.Sin(Time.time * 1.3f + _particlePhases[i]) * _preset.ParticleSwayAmount * Time.deltaTime;

            if (pos.y < cam.y - CameraHalfHeight * 1.15f)
            {
                pos = RandomParticlePosition(anywhere: false);
            }
            particle.position = pos;
        }
    }
}
