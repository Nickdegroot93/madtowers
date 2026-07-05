using UnityEngine;

/// <summary>
/// Turns a real block prefab into a demo PUPPET: the exact cell layout, chapter piece art and
/// procedural variant skin of a live brick, WITHOUT the BlockController. Instantiating a real
/// BlockController is forbidden in the diorama - its Awake registers into the global block
/// ledger (LossZone sweeps it and charges lives), claims ActiveControlled, and the variant
/// behaviours mutate real game state (a demo Maw would eat a real life).
///
/// Two flavours:
/// - KINEMATIC (posters, control-story demos): body and colliders stripped; moved by script.
/// - PHYSICAL (the scenario videos): the prefab's real Rigidbody2D stays DYNAMIC with the
///   variant's real mass / friction material / gravity, colliders live - the demo plays out on
///   the same Box2D simulation as the game, so weight, sliding and toppling are REAL. Only the
///   variant "moments" (freeze, weld, blast, quake) are applied by small demo shims that do
///   what the real behaviours do, minus the game-state writes.
/// </summary>
public static class BlockDemoPuppet
{
    /// <summary>Instantiate the named shape ("O", "Domino", "T", ...) as a puppet dressed in the
    /// chapter's piece art and, when non-null, the variant's tint/material overrides. Variant
    /// SKINS are attached by the scenarios (each knows its typed skin + when its cues fire).
    /// <paramref name="neutralSkin"/> forces the CLASSIC brick art regardless of chapter - the
    /// studio posters' "one good brick colour that goes with everything".</summary>
    // Real-friction fallback so demo bricks grip like game bricks (the game's own fallback
    // material is private on BlockController; same values by observation: firm, no bounce).
    private static PhysicsMaterial2D _defaultMaterial;
    private static PhysicsMaterial2D DefaultMaterial =>
        _defaultMaterial ??= new PhysicsMaterial2D("DemoBlockMaterial") { friction = 0.7f, bounciness = 0f };

    public static GameObject Create(string shapeName, BlockData looks, ChapterDefinition chapter,
        bool neutralSkin = false, bool physical = false)
    {
        BlockDefinition definition = FindDefinition(shapeName);
        if (definition == null || definition.Prefab == null)
        {
            Debug.LogWarning($"[BlockDemo] No block definition '{shapeName}' - puppet skipped.");
            return null;
        }

        // Instantiate INACTIVE (under a disabled host) so BlockController.Awake never runs -
        // by the time the object activates, the controller is gone.
        var host = new GameObject("PuppetHost");
        host.SetActive(false);
        GameObject puppet = Object.Instantiate(definition.Prefab, host.transform);
        puppet.name = definition.Prefab.name; // keep the shape token for ChapterSkins

        foreach (BlockController controller in puppet.GetComponentsInChildren<BlockController>(true))
            Object.DestroyImmediate(controller);

        if (physical)
        {
            // The REAL simulation: the prefab's own body + colliders, dressed with the variant's
            // real physics stats. Isolation is by construction - no BlockController means no
            // ledger entry, no loss sweep, no ActiveControlled; and the diorama sits far outside
            // the play space, so these bodies only ever meet each other and the demo floor.
            Rigidbody2D body = puppet.GetComponentInChildren<Rigidbody2D>(true);
            if (body == null) body = puppet.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.mass = looks != null ? looks.Mass : 1f;
            body.gravityScale = looks != null ? looks.GravityScaleMultiplier : 1f;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            PhysicsMaterial2D material = looks != null && looks.PhysicsMaterial != null
                ? looks.PhysicsMaterial : DefaultMaterial;
            foreach (Collider2D collider in puppet.GetComponentsInChildren<Collider2D>(true))
            {
                collider.enabled = true;
                collider.sharedMaterial = material;
            }
            // The game's collider forgiveness (BlockController.ApplyColliderForgiveness): cells
            // are slightly narrower than their visuals with rounded corners, so a piece can drop
            // down a channel exactly its own width without wedging on the walls. Without this,
            // demo drops flush along a face grind, torque and tumble - which never happens in
            // the real game.
            const float footprintScale = 0.94f;
            const float cornerRadius = 0.08f;
            foreach (BoxCollider2D box in puppet.GetComponentsInChildren<BoxCollider2D>(true))
            {
                Vector2 size = box.size;
                box.size = new Vector2(
                    Mathf.Max(0.05f, size.x * footprintScale - 2f * cornerRadius),
                    Mathf.Max(0.05f, size.y - 2f * cornerRadius));
                box.edgeRadius = cornerRadius;
            }
            puppet.AddComponent<DemoContactRelay>();
        }
        else
        {
            foreach (Rigidbody2D body in puppet.GetComponentsInChildren<Rigidbody2D>(true))
                Object.DestroyImmediate(body);
            // Colliders stay: BlockVariantSkin.BuildCells derives the cell layout from them. They
            // never simulate (no body, far offset), but disable them anyway so no stray global
            // Physics2D query can ever see the diorama.
            foreach (Collider2D collider in puppet.GetComponentsInChildren<Collider2D>(true))
                collider.enabled = false;
        }

        ApplyChapterArt(puppet, chapter, neutralSkin);
        ApplyVariantLooks(puppet, looks);
        SetLayerRecursively(puppet.transform, BlockDemoStage.DemoLayer);

        puppet.transform.SetParent(null);
        Object.Destroy(host);
        puppet.SetActive(true);
        return puppet;
    }

    private static BlockDefinition FindDefinition(string shapeName)
    {
        // Definitions are named "Block_O", "Block_Domino", ...; scenarios say just "O".
        string full = shapeName.StartsWith("Block_") ? shapeName : "Block_" + shapeName;
        foreach (BlockDefinition definition in ContentCatalog.AllBlocks())
        {
            if (definition != null &&
                string.Equals(definition.name, full, System.StringComparison.OrdinalIgnoreCase))
                return definition;
        }
        return null;
    }

    /// <summary>Re-stamp the demo layer onto everything under the puppet. Call after attaching a
    /// variant skin: BuildCells creates fresh overlay GameObjects on the default layer, which the
    /// demo camera would cull.</summary>
    public static void Relayer(GameObject puppet)
    {
        if (puppet != null) SetLayerRecursively(puppet.transform, BlockDemoStage.DemoLayer);
    }

    // The same treatment BlockController.ApplyBlockSkin gives a live piece: hide the plain cell
    // renderers and lay the chapter's one-sprite piece art over them (falls back to visible
    // cells when the chapter has no art for the shape).
    private static void ApplyChapterArt(GameObject puppet, ChapterDefinition chapter, bool neutralSkin)
    {
        string shape = ChapterSkins.ExtractShapeToken(puppet.name);
        if (string.IsNullOrEmpty(shape)) return;

        // Point the skin loader at the demo's chapter (or the neutral Classic set for studio
        // posters) without disturbing whatever the real game (or menu) currently has active.
        string previousFolder = ChapterSkins.Folder;
        if (neutralSkin) ChapterSkins.Folder = "Skins/Vault"; // the neutral studio-brick set
        else if (chapter != null) ChapterSkins.Apply(chapter);
        Sprite pieceSprite = ChapterSkins.LoadPiece(shape);
        ChapterSkins.Folder = previousFolder;
        if (pieceSprite == null) return;

        SpriteRenderer[] cellRenderers = puppet.GetComponentsInChildren<SpriteRenderer>();
        if (cellRenderers.Length == 0) return;

        Vector2 min = cellRenderers[0].transform.localPosition;
        Vector2 max = min;
        foreach (var sr in cellRenderers)
        {
            Vector2 p = sr.transform.localPosition;
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
            sr.enabled = false;
        }

        var skinGo = new GameObject("PieceSkin");
        skinGo.transform.SetParent(puppet.transform, false);
        skinGo.transform.localPosition = new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, 0f);
        SpriteRenderer skinRenderer = skinGo.AddComponent<SpriteRenderer>();
        skinRenderer.sprite = pieceSprite;
        skinRenderer.sortingLayerID = cellRenderers[0].sortingLayerID;
        skinRenderer.sortingOrder = cellRenderers[0].sortingOrder;
    }

    // The renderer half of BlockController.ApplyData: tint + sprite/material overrides. (Mass,
    // friction and the OnApplied hook are controller business a puppet doesn't have.)
    private static void ApplyVariantLooks(GameObject puppet, BlockData looks)
    {
        if (looks == null) return;
        foreach (SpriteRenderer sr in puppet.GetComponentsInChildren<SpriteRenderer>())
        {
            sr.color = looks.ColorTint;
            if (looks.SpriteOverride != null) sr.sprite = looks.SpriteOverride;
            if (looks.MaterialOverride != null) sr.sharedMaterial = looks.MaterialOverride;
        }
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }
}

/// <summary>Contact/rest reporting for a physical puppet - the scenario runner's "it landed"
/// signal, and the hook the variant-moment shims (freeze, weld, chomp) trigger from.</summary>
public sealed class DemoContactRelay : MonoBehaviour
{
    public bool HasTouched { get; private set; }
    public event System.Action<Collision2D> Touched;

    private void OnCollisionEnter2D(Collision2D collision)
    {
        HasTouched = true;
        Touched?.Invoke(collision);
    }
}

/// <summary>Caps a dropped piece's descent speed until first contact - the game's pieces fall
/// under CONTROL (a few units/second), not at freefall terminal speed, so a demo drop must
/// arrive with game-realistic momentum or every impact reads (and simulates) far too violent.
/// Removes itself the moment the piece touches anything; from then on physics is untouched.</summary>
public sealed class DemoFallGovernor : MonoBehaviour
{
    public float MaxFallSpeed = 4f;

    private Rigidbody2D _body;
    private DemoContactRelay _relay;

    private void Awake()
    {
        _body = GetComponentInChildren<Rigidbody2D>();
        _relay = GetComponent<DemoContactRelay>();
    }

    private void FixedUpdate()
    {
        if (_relay != null && _relay.HasTouched)
        {
            Destroy(this);
            return;
        }
        if (_body == null) return;
        Vector2 velocity = _body.linearVelocity;
        if (velocity.y < -MaxFallSpeed)
        {
            velocity.y = -MaxFallSpeed;
            _body.linearVelocity = velocity;
        }
    }
}
