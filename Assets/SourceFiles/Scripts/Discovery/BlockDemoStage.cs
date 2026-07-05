using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The live "how this brick works" diorama (BLOCKPREVIEWS.md): a tiny self-contained sandbox
/// built far outside the play area, rendered by its own camera into a RenderTexture that a UI
/// RawImage displays (the debut modal and the Vault detail view share this component). The
/// sandbox holds PUPPETS - real block prefabs with the real procedural variant skins but no
/// BlockController - so the looks are always the shipped looks while nothing here can ever
/// touch the real simulation, the block ledger, or the loss sweep (see BlockDemoPuppet).
///
/// Lifecycle: Open() builds everything and starts the scripted loop; Close() destroys the root
/// and releases the texture. Exactly one stage exists at a time and it costs nothing when no
/// demo is on screen ("rendered on demand").
/// </summary>
public sealed class BlockDemoStage : MonoBehaviour
{
    /// <summary>Free GameObject layer reserved at runtime for demo objects, so the demo camera
    /// renders only the diorama no matter what the main scene is doing.</summary>
    public const int DemoLayer = 3;

    // Far outside any camera frustum, the LossZone backstop, and the tower's reachable space.
    // Each stage claims its own slot so two live dioramas' PHYSICS bodies can never meet.
    private static readonly Vector3 StageOrigin = new Vector3(1000f, 200f, 0f);
    private static int _originSlot;

    // Stage-local framing: the floor's top surface is y = 0, x = 0 is the centre.
    private const float ViewHalfHeight = 2.7f;  // orthographic size
    private const float ViewCenterY = 1.85f;    // show mostly the air above the floor
    private const float FloorHalfWidth = 4.2f;

    private Camera _camera;
    private RenderTexture _texture;
    private Transform _piecesRoot;
    private SpriteRenderer _fadeOverlay;
    private Coroutine _loop;
    private BlockData _variant;
    private ChapterDefinition _chapter;
    private bool _studio; // neutral poster look (no chapter theming, no terrain)
    private float _viewCenterY = ViewCenterY; // scenario-adjustable framing (SetView)

    /// <summary>The texture a RawImage should display. Valid between Open and Close.</summary>
    public Texture Texture => _texture;

    public BlockData Variant => _variant;
    public ChapterDefinition Chapter => _chapter;

    /// <summary>Build a stage looping the given variant's demo scenario. The chapter drives the
    /// diorama's theming (floor/backdrop colours + which piece art overlay-skins keep).</summary>
    public static BlockDemoStage Open(BlockData variant, ChapterDefinition chapter, int pixelWidth, int pixelHeight)
    {
        BlockDemoStage stage = Create(variant, chapter, pixelWidth, pixelHeight);
        stage._loop = stage.StartCoroutine(stage.RunLoop());
        return stage;
    }

    /// <summary>A stage holding a static poster POSE of the variant (no scenario loop) - the Vault
    /// grid's showcase renderer. Posters deliberately IGNORE chapter theming: a clean neutral
    /// studio shot (dark gradient, soft key light, floating brick over a soft shadow) so every
    /// thumbnail matches, with the CLASSIC brick skin standing in for "the chapter's colour" on
    /// overlay-skinned and plain bricks. Camera framing is tightened onto the brick.</summary>
    public static BlockDemoStage OpenPose(BlockData variant, ChapterDefinition chapter, int pixelSize)
    {
        BlockDemoStage stage = Create(variant, null, pixelSize, pixelSize, studio: true);
        // Tight hero framing: the resting brick fills the square.
        stage._camera.orthographicSize = 1.7f;
        stage._camera.transform.localPosition = new Vector3(0f, 1.15f, -10f);
        BlockDemoScenarios.PosterPose(stage);
        return stage;
    }

    private static BlockDemoStage Create(BlockData variant, ChapterDefinition chapter, int pixelWidth,
        int pixelHeight, bool studio = false)
    {
        var go = new GameObject("BlockDemoStage");
        _originSlot = (_originSlot + 1) % 64;
        go.transform.position = StageOrigin + new Vector3(_originSlot * 40f, 0f, 0f);
        go.layer = DemoLayer;
        BlockDemoStage stage = go.AddComponent<BlockDemoStage>();
        stage._variant = variant;
        stage._chapter = chapter;
        stage._studio = studio;
        stage.Build(Mathf.Clamp(pixelWidth, 64, 1024), Mathf.Clamp(pixelHeight, 64, 1024));
        return stage;
    }

    /// <summary>Detach the RenderTexture from this stage so it SURVIVES Close() - the poster
    /// pipeline captures a frame and keeps only the texture. Caller owns releasing it.</summary>
    public RenderTexture DetachTexture()
    {
        RenderTexture texture = _texture;
        if (_camera != null)
        {
            _camera.Render(); // one last guaranteed-fresh frame
            _camera.targetTexture = null;
        }
        _texture = null;
        return texture;
    }

    public void Close()
    {
        if (_loop != null) StopCoroutine(_loop);
        _loop = null;
        if (_camera != null) _camera.targetTexture = null;
        if (_texture != null)
        {
            _texture.Release();
            Destroy(_texture);
            _texture = null;
        }
        Destroy(gameObject);
    }

    // ---- construction ------------------------------------------------------------------------

    private void Build(int pixelWidth, int pixelHeight)
    {
        // Studio posters use a modern neutral: cool near-black, no chapter cast.
        Color accent = _chapter != null ? _chapter.MenuAccentColor : new Color(0.85f, 0.65f, 0.3f);
        Color deep = _studio
            ? new Color(0.082f, 0.088f, 0.104f, 1f)
            : Color.Lerp(accent, Color.black, 0.82f);

        _texture = new RenderTexture(pixelWidth, pixelHeight, 16, RenderTextureFormat.Default);
        _texture.name = "BlockDemoRT";

        var camGo = new GameObject("DemoCamera");
        camGo.transform.SetParent(transform, false);
        camGo.transform.localPosition = new Vector3(0f, ViewCenterY, -10f);
        _camera = camGo.AddComponent<Camera>();
        _camera.orthographic = true;
        _camera.orthographicSize = ViewHalfHeight;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = deep;
        _camera.cullingMask = 1 << DemoLayer;
        _camera.targetTexture = _texture;
        // The real bricks' emissive looks (Magma veins, Bomb fuse) need bloom to read the same
        // here as in the game.
        UniversalAdditionalCameraData urp = _camera.GetUniversalAdditionalCameraData();
        if (urp != null) urp.renderPostProcessing = true;

        if (_studio) BuildStudioEnvironment(deep);
        else BuildEnvironment(accent, deep);

        // The demo's REAL ground: a full-width static collider whose top is stage-local y = 0,
        // so physical puppets land on genuine Box2D contacts (Nick's rule: the floor is always
        // full width; scenarios vary the bricks, not the terrain).
        var floorGo = new GameObject("FloorCollider");
        floorGo.transform.SetParent(transform, false);
        floorGo.layer = DemoLayer;
        Rigidbody2D floorBody = floorGo.AddComponent<Rigidbody2D>();
        floorBody.bodyType = RigidbodyType2D.Static;
        BoxCollider2D floorBox = floorGo.AddComponent<BoxCollider2D>();
        floorBox.size = new Vector2(40f, 2f);
        floorBox.offset = new Vector2(0f, -1f);

        _piecesRoot = new GameObject("Pieces").transform;
        _piecesRoot.SetParent(transform, false);

        // Full-view overlay used for the soft cut between loops (BLOCKPREVIEWS.md: fade reads
        // calmer than a hard reset). Drawn above everything in the diorama.
        var fadeGo = new GameObject("LoopFade");
        fadeGo.transform.SetParent(transform, false);
        fadeGo.transform.localPosition = new Vector3(0f, ViewCenterY, -1f);
        fadeGo.transform.localScale = new Vector3(ViewHalfHeight * 4f, ViewHalfHeight * 4f, 1f);
        fadeGo.layer = DemoLayer;
        _fadeOverlay = fadeGo.AddComponent<SpriteRenderer>();
        _fadeOverlay.sprite = RuntimeSprites.Square();
        _fadeOverlay.color = new Color(deep.r, deep.g, deep.b, 0f);
        _fadeOverlay.sortingOrder = 500;
    }

    // The poster's product-shot look: a subtle cool gradient, one soft key light behind the
    // brick, and a soft contact shadow under it - no terrain, no chapter cast. Slick over themed.
    private void BuildStudioEnvironment(Color deep)
    {
        // Gentle vertical lift so the flat clear colour doesn't read as a dead void.
        SpriteRenderer sheen = NewSprite("Sheen", RuntimeSprites.SoftBlob(),
            new Vector3(0f, 3.4f, 3f), new Vector3(14f, 8f, 1f),
            new Color(1f, 1f, 1f, 0.045f), -30);
        sheen.transform.SetParent(transform, false);

        // The key light: a soft white-cyan glow directly behind the brick.
        SpriteRenderer key = NewSprite("KeyLight", RuntimeSprites.SoftBlob(),
            new Vector3(0f, 1.15f, 2f), new Vector3(5.6f, 5.6f, 1f),
            new Color(0.82f, 0.88f, 1f, 0.10f), -20);
        key.transform.SetParent(transform, false);

        // Contact shadow: a squashed dark blob where the brick "rests".
        SpriteRenderer shadow = NewSprite("ContactShadow", RuntimeSprites.SoftBlob(),
            new Vector3(0f, -0.12f, 1f), new Vector3(3.4f, 0.75f, 1f),
            new Color(0f, 0f, 0f, 0.5f), -10);
        shadow.transform.SetParent(transform, false);
    }

    private void BuildEnvironment(Color accent, Color deep)
    {
        // Soft atmosphere: a broad accent glow low behind the action so the bricks pop.
        var glow = NewSprite("Glow", RuntimeSprites.SoftBlob(),
            new Vector3(0f, 0.6f, 2f), new Vector3(9f, 5f, 1f),
            new Color(accent.r, accent.g, accent.b, 0.14f), -20);
        glow.transform.SetParent(transform, false);

        // Floor: the chapter's real ground art when it loads, else themed flat colour. The top
        // surface sits at stage-local y = 0.
        string previousFolder = ChapterSkins.Folder;
        if (_chapter != null) ChapterSkins.Apply(_chapter);
        Sprite cap = ChapterSkins.LoadGroundCap();
        Sprite fill = ChapterSkins.LoadGroundFill();
        ChapterSkins.Folder = previousFolder;

        if (cap != null && fill != null)
        {
            SpriteRenderer capSr = NewSprite("FloorCap", cap, new Vector3(0f, -0.25f, 1f), Vector3.one,
                Color.white, -10);
            capSr.drawMode = SpriteDrawMode.Tiled;
            capSr.size = new Vector2(FloorHalfWidth * 2f, 0.5f);
            capSr.transform.SetParent(transform, false);

            SpriteRenderer fillSr = NewSprite("FloorFill", fill, new Vector3(0f, -1.75f, 1f), Vector3.one,
                Color.white, -11);
            fillSr.drawMode = SpriteDrawMode.Tiled;
            fillSr.size = new Vector2(FloorHalfWidth * 2f, 2.5f);
            fillSr.transform.SetParent(transform, false);
        }
        else
        {
            SpriteRenderer slab = NewSprite("Floor", RuntimeSprites.Square(),
                new Vector3(0f, -1.5f, 1f), new Vector3(FloorHalfWidth * 2f, 3f, 1f),
                Color.Lerp(accent, Color.black, 0.6f), -11);
            slab.transform.SetParent(transform, false);
            SpriteRenderer lip = NewSprite("FloorLip", RuntimeSprites.Square(),
                new Vector3(0f, -0.05f, 0.9f), new Vector3(FloorHalfWidth * 2f, 0.1f, 1f),
                Color.Lerp(accent, Color.white, 0.15f), -10);
            lip.transform.SetParent(transform, false);
        }
    }

    private static SpriteRenderer NewSprite(string name, Sprite sprite, Vector3 localPos,
        Vector3 localScale, Color color, int order)
    {
        var go = new GameObject(name);
        go.layer = DemoLayer;
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    // ---- the loop ------------------------------------------------------------------------------

    private IEnumerator RunLoop()
    {
        while (true)
        {
            IEnumerator scenario = BlockDemoCatalog.CreateScenario(_variant, this);
            if (scenario == null) yield break;

            // The curtain: each iteration starts OPAQUE so the template builds and physics-
            // settles unseen; the scenario calls Reveal() once its scene is static. The video
            // always opens on a calm, exact structure - never mid-settle chaos.
            SetFadeAlpha(1f);
            yield return scenario;
            yield return Hold(0.8f);
            yield return Fade(1f, 0.25f);
            ClearPieces();
        }
    }

    /// <summary>Scenario call: drop the curtain and start the show (after template settle).</summary>
    public IEnumerator Reveal() => Fade(0f, 0.3f);

    private void SetFadeAlpha(float alpha)
    {
        if (_fadeOverlay == null) return;
        Color c = _fadeOverlay.color;
        c.a = alpha;
        _fadeOverlay.color = c;
    }

    // Skins keep creating child objects while they animate (overlay cells, Maw's tongue, one-shot
    // puffs) and those default to layer 0, which the demo camera culls. Sweep the diorama every
    // frame - a few dozen transforms, negligible, and it makes the layer bulletproof.
    private void LateUpdate()
    {
        if (_piecesRoot == null) return;
        foreach (Transform child in _piecesRoot.GetComponentsInChildren<Transform>(true))
        {
            if (child.gameObject.layer != DemoLayer) child.gameObject.layer = DemoLayer;
        }
    }

    private void ClearPieces()
    {
        for (int i = _piecesRoot.childCount - 1; i >= 0; i--)
            Destroy(_piecesRoot.GetChild(i).gameObject);
    }

    private IEnumerator Fade(float targetAlpha, float duration)
    {
        float start = _fadeOverlay.color.a;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            Color c = _fadeOverlay.color;
            c.a = Mathf.Lerp(start, targetAlpha, Mathf.Clamp01(t / duration));
            _fadeOverlay.color = c;
            yield return null;
        }
    }

    public IEnumerator Hold(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            yield return null;
        }
    }

    // ---- helpers the scenarios build with -------------------------------------------------------

    /// <summary>Per-scenario framing: taller builds (Feather's tipping tower, Bomb's stack) zoom
    /// the camera out and lift its centre. Persists for the stage's lifetime - a scenario calls
    /// this once at its start.</summary>
    public void SetView(float orthoSize, float centerY)
    {
        _viewCenterY = centerY;
        if (_camera != null)
        {
            _camera.orthographicSize = orthoSize;
            _camera.transform.localPosition = new Vector3(0f, centerY, -10f);
        }
        if (_fadeOverlay != null)
        {
            _fadeOverlay.transform.localPosition = new Vector3(0f, centerY, -1f);
            _fadeOverlay.transform.localScale = new Vector3(orthoSize * 4f, orthoSize * 4f, 1f);
        }
    }

    /// <summary>Mid-scenario cut: fade to the backdrop and clear every piece - and STAY dark, so
    /// the next half's template can build and settle unseen; Reveal() lifts the curtain again.
    /// Lets a loop show an A/B contrast (Feather: "a normal brick topples this - a feather
    /// doesn't").</summary>
    public IEnumerator FadeCut()
    {
        yield return Fade(1f, 0.22f);
        ClearPieces();
    }

    /// <summary>Spawn a KINEMATIC puppet at a stage-local position (posters and control-story
    /// demos - physics scenarios use SpawnPhysical). Shape by BlockDefinition asset name ("O",
    /// "T", ...); null looks = the plain chapter brick.</summary>
    public GameObject Spawn(string shapeName, BlockData looks, Vector2 localPos, float rotationZ = 0f)
    {
        GameObject puppet = BlockDemoPuppet.Create(shapeName, looks, _chapter, _studio);
        if (puppet == null) return null;
        puppet.transform.SetParent(_piecesRoot, false);
        puppet.transform.localPosition = new Vector3(localPos.x, localPos.y, 0f);
        puppet.transform.localRotation = Quaternion.Euler(0f, 0f, rotationZ);
        return puppet;
    }

    /// <summary>A PHYSICAL puppet - real dynamic body, the variant's real mass/friction, live
    /// colliders. Template structure pieces spawn asleep at their exact pose (they wake the
    /// moment anything disturbs them); dropped pieces start awake and simply fall.</summary>
    public GameObject SpawnPhysical(string shapeName, BlockData looks, Vector2 localPos,
        float rotationZ = 0f, bool asleep = false)
    {
        GameObject puppet = BlockDemoPuppet.Create(shapeName, looks, _chapter, _studio, physical: true);
        if (puppet == null) return null;
        puppet.transform.SetParent(_piecesRoot, false);
        puppet.transform.localPosition = new Vector3(localPos.x, localPos.y, 0f);
        puppet.transform.localRotation = Quaternion.Euler(0f, 0f, rotationZ);
        Rigidbody2D body = puppet.GetComponentInChildren<Rigidbody2D>();
        if (body != null && asleep) body.Sleep();
        return puppet;
    }

    /// <summary>Wait until the piece has touched something AND (nearly) stopped moving - the
    /// physics-honest "it landed". Timeout keeps a surprising simulation from stalling the loop.</summary>
    public IEnumerator WaitForLand(GameObject piece, float timeout = 4f)
    {
        if (piece == null) yield break;
        DemoContactRelay relay = piece.GetComponent<DemoContactRelay>();
        Rigidbody2D body = piece.GetComponentInChildren<Rigidbody2D>();
        float t = 0f;
        while (t < timeout && piece != null)
        {
            t += Time.deltaTime;
            bool touched = relay == null || relay.HasTouched;
            bool slow = body == null || body.linearVelocity.magnitude < 0.35f;
            if (touched && slow) yield break;
            yield return null;
        }
    }

    /// <summary>Give the whole diorama a settle beat (used after building a template so sleeping
    /// bodies that DO have something to say - a marginal balance - can say it before the drop).</summary>
    public IEnumerator Settle(float seconds = 0.5f) => Hold(seconds);

    /// <summary>Accelerating fall to a stage-local Y, with a small settle squash on arrival -
    /// reads like the game's descent without any physics.</summary>
    public IEnumerator Drop(GameObject piece, float toY, float speed = 4.5f, bool squash = true)
    {
        if (piece == null) yield break;
        Transform tr = piece.transform;
        float v = Mathf.Max(1.2f, speed * 0.4f);
        while (tr != null && tr.localPosition.y > toY)
        {
            v = Mathf.Min(speed * 1.8f, v + 14f * Time.deltaTime);
            Vector3 p = tr.localPosition;
            p.y = Mathf.Max(toY, p.y - v * Time.deltaTime);
            tr.localPosition = p;
            yield return null;
        }
        if (tr != null && squash) yield return Squash(tr);
    }

    private IEnumerator Squash(Transform tr)
    {
        Vector3 baseScale = tr.localScale;
        float t = 0f;
        const float duration = 0.16f;
        while (t < duration && tr != null)
        {
            t += Time.deltaTime;
            float k = Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI); // up then back
            tr.localScale = new Vector3(baseScale.x * (1f + 0.06f * k), baseScale.y * (1f - 0.09f * k), 1f);
            yield return null;
        }
        if (tr != null) tr.localScale = baseScale;
    }

    /// <summary>Shatter a puppet with the game's real shard burst and remove it.</summary>
    public void Shatter(GameObject piece, Color tint)
    {
        if (piece == null) return;
        Bounds area = new Bounds(piece.transform.position, Vector3.one);
        var renderers = piece.GetComponentsInChildren<SpriteRenderer>();
        foreach (var sr in renderers) area.Encapsulate(sr.bounds);
        BlockShatterFx.Spawn(area, tint);
        SetShardLayer();
        Destroy(piece);
    }

    // BlockShatterFx spawns shards at the scene root on the default layer; sweep them onto the
    // demo layer so the demo camera (and only it) shows them. Cheap: shards are few and the
    // stage is the only thing near StageOrigin.
    private void SetShardLayer()
    {
        foreach (BlockShatterFx fx in FindObjectsByType<BlockShatterFx>(FindObjectsSortMode.None))
        {
            if (Vector3.Distance(fx.transform.position, transform.position) > 50f) continue;
            foreach (Transform child in fx.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = DemoLayer;
        }
    }

    /// <summary>A soft dust puff at a stage-local position (landings, impacts).</summary>
    public void Dust(Vector2 localPos, float size = 0.8f, float alpha = 0.4f)
    {
        StartCoroutine(DustPuff(localPos, size, alpha));
    }

    private IEnumerator DustPuff(Vector2 localPos, float size, float alpha)
    {
        var sr = NewSprite("Dust", RuntimeSprites.SoftBlob(), new Vector3(localPos.x, localPos.y, 0.5f),
            new Vector3(size, size * 0.6f, 1f), new Color(0.9f, 0.88f, 0.84f, alpha), 40);
        sr.transform.SetParent(_piecesRoot, false);
        sr.transform.localPosition = new Vector3(localPos.x, localPos.y, 0.5f);
        float t = 0f;
        const float life = 0.45f;
        while (t < life && sr != null)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);
            sr.transform.localScale = new Vector3(size * (1f + k * 1.6f), size * 0.6f * (1f + k * 1.2f), 1f);
            Color c = sr.color;
            c.a = alpha * (1f - k * k);
            sr.color = c;
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }

    /// <summary>A brief camera nudge for heavy moments (Boulder slam, Bomb blast).</summary>
    public void CameraKick(float strength = 0.12f)
    {
        StartCoroutine(Kick(strength));
    }

    private IEnumerator Kick(float strength)
    {
        Transform cam = _camera != null ? _camera.transform : null;
        if (cam == null) yield break;
        Vector3 basePos = new Vector3(0f, _viewCenterY, -10f);
        float t = 0f;
        const float life = 0.22f;
        while (t < life && cam != null)
        {
            t += Time.deltaTime;
            float k = 1f - Mathf.Clamp01(t / life);
            cam.localPosition = basePos + (Vector3)(Random.insideUnitCircle * strength * k * k);
            yield return null;
        }
        if (cam != null) cam.localPosition = basePos;
    }
}
