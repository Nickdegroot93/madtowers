using System.Collections;
using UnityEngine;

/// <summary>
/// One demo per brick variant, built Nick's way: a TEMPLATE (an exact starting structure of
/// real bricks on a full-width floor) plus ONE dropped variant piece - and then the game's own
/// physics plays out the consequences. Motion is never animated: weight, balance, sliding,
/// toppling and falling are the real Box2D simulation on physical puppets with the variants'
/// real masses and friction materials. Only each variant's MOMENT is a small shim doing what
/// the real behaviour does (freeze, weld, blast, quake kicks, devour) minus game-state writes.
///
/// GRID DISCIPLINE (the placement contract): the floor is full width with its top at y = 0 and
/// COLUMNS are integers - a cell occupies [n, n+1], its centre is n+0.5. Structures sit on
/// exact columns like a real board; gaps are exact column counts. Every loop starts behind the
/// stage CURTAIN: the template spawns and physics-settles unseen, the scenario calls Reveal(),
/// and the video opens on a calm exact scene.
///
/// Pivot cheat-sheet (measured cell offsets from each prefab's pivot):
///   Pip (0,0) · Domino (0,0)(0,1) · O {-1,0}x{0,1} -> cols [a,a+2] => pivot a+1.5
///   I {-2..1,0} -> cols [a,a+4] => pivot a+2.5 · T/J/L bottom {-1..1,0} -> cols [a,a+3] =>
///   pivot a+1.5 (J stem over [a,a+1], L stem over [a+2,a+3]). Rest pivot y = 0.5 on the floor.
/// </summary>
public static class BlockDemoScenarios
{
    // ---- helpers -------------------------------------------------------------------------------

    private static T Dress<T>(GameObject puppet) where T : BlockVariantSkin
    {
        return puppet.AddComponent<T>();
    }

    private static Rigidbody2D Body(GameObject piece) =>
        piece != null ? piece.GetComponentInChildren<Rigidbody2D>() : null;

    /// <summary>Release a physical piece from above at the game's CONTROLLED descent speed (a
    /// fall governor caps it until first contact, like a real falling piece) - after touchdown,
    /// physics owns it completely.</summary>
    private static GameObject DropIn(BlockDemoStage stage, string shape, BlockData looks,
        Vector2 from, float rotationZ = 0f, float fallSpeed = 4f)
    {
        GameObject piece = stage.SpawnPhysical(shape, looks, from, rotationZ);
        Rigidbody2D body = Body(piece);
        if (body != null) body.linearVelocity = new Vector2(0f, -Mathf.Min(3f, fallSpeed));
        if (piece != null) piece.AddComponent<DemoFallGovernor>().MaxFallSpeed = fallSpeed;
        return piece;
    }

    /// <summary>The game's micro-align, demo edition: square the piece up and snap it onto the
    /// column grid (used at moments that lock a piece in place - a freeze, a melt).</summary>
    private static void SnapToGrid(GameObject piece)
    {
        if (piece == null) return;
        piece.transform.rotation = Quaternion.identity;
        Vector3 p = piece.transform.localPosition;
        p.x = Mathf.Round(p.x - 0.5f) + 0.5f;
        p.y = Mathf.Round(p.y - 0.5f) + 0.5f;
        piece.transform.localPosition = p;
        Rigidbody2D body = Body(piece);
        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
    }

    /// <summary>Snap square, then freeze the piece into terrain (Anchor's rule; also how the
    /// demo pins a landed maw, which never budges once fused).</summary>
    private static void FreezeSquare(GameObject piece)
    {
        SnapToGrid(piece);
        Rigidbody2D body = Body(piece);
        if (body != null) body.bodyType = RigidbodyType2D.Static;
    }

    /// <summary>Every physical piece within a stage-local circle (the bomb's blast query, the
    /// vine's weld search). A piece's identity is the puppet ROOT - the child of the stage's
    /// "Pieces" container (never transform.root, which is the stage itself).</summary>
    private static System.Collections.Generic.List<GameObject> PiecesNear(BlockDemoStage stage,
        Vector2 stageLocal, float radius, GameObject except)
    {
        var found = new System.Collections.Generic.List<GameObject>();
        Vector2 world = stage.transform.TransformPoint(stageLocal);
        foreach (Collider2D hit in Physics2D.OverlapCircleAll(world, radius, 1 << BlockDemoStage.DemoLayer))
        {
            Rigidbody2D body = hit.attachedRigidbody;
            if (body == null || body.bodyType == RigidbodyType2D.Static) continue; // floor / frozen
            Transform t = body.transform;
            while (t.parent != null && t.parent.name != "Pieces") t = t.parent;
            if (t.parent == null) continue; // not a puppet
            GameObject root = t.gameObject;
            if (root == except || found.Contains(root)) continue;
            found.Add(root);
        }
        return found;
    }

    private static IEnumerator GrowIn(Transform tr)
    {
        float t = 0f;
        const float life = 0.22f;
        Vector3 full = tr != null ? tr.localScale : Vector3.one;
        while (t < life && tr != null)
        {
            t += Time.deltaTime;
            tr.localScale = full * Mathf.SmoothStep(0.15f, 1f, Mathf.Clamp01(t / life));
            yield return null;
        }
        if (tr != null) tr.localScale = full;
    }

    // ---- the demos ------------------------------------------------------------------------------

    public static IEnumerator Boulder(BlockDemoStage stage)
    {
        // The boulder as a HELPER: a beam dangles two full columns off a tower's edge - on a
        // knife's edge, one brick on the wrong end from disaster. The boulder lands on the
        // supported end as BALLAST, and now a normal brick can land far out over the void and
        // the beam holds. Counterweight - that's what four times the mass buys you. Pure
        // simulation: without the boulder's torque the same landing dumps everything.
        stage.SetView(3.8f, 2.8f);
        stage.SpawnPhysical("O", null, new Vector2(1.5f, 0.5f), asleep: true); // tower, cols 0..2
        // The beam sits like a settled piece that slid half a cell: 1.5 columns dangling over
        // the void, just enough of its own weight over the tower to stand - but one brick on
        // the wrong end from disaster.
        stage.SpawnPhysical("I", null, new Vector2(1.0f, 2.5f), asleep: true); // spans -1.5..2.5
        yield return stage.Settle(0.5f);
        yield return stage.Reveal();
        yield return stage.Hold(0.35f);

        // The boulder lands over the tower - the anchor end of the seesaw.
        GameObject boulder = DropIn(stage, "O", stage.Variant, new Vector2(1.5f, 6.4f), fallSpeed: 4.5f);
        BoulderBlockSkin skin = Dress<BoulderBlockSkin>(boulder);
        skin.Apply();
        BlockDemoPuppet.Relayer(boulder);
        DemoContactRelay relay = boulder.GetComponent<DemoContactRelay>();
        bool slammed = false;
        relay.Touched += _ =>
        {
            if (slammed) return;
            slammed = true;
            skin.PlayLandImpact();
            stage.CameraKick(0.14f);
            stage.Dust(new Vector2(1.5f, 3.6f), 1.1f, 0.5f);
        };
        yield return stage.WaitForLand(boulder);
        yield return stage.Hold(0.8f);

        // The payoff: a brick lands entirely over the void - and the boulder holds the beam.
        GameObject prop = DropIn(stage, "O", null, new Vector2(-0.5f, 6.2f)); // cols -2..0
        yield return stage.WaitForLand(prop);
        stage.Dust(new Vector2(-0.5f, 3.0f), 0.6f, 0.3f);
        yield return stage.Hold(1.8f);
        _ = prop;
    }

    public static IEnumerator Anchor(BlockDemoStage stage)
    {
        // Template: a tower on cols -3..-1. The anchor I lands with only ONE cell on the tower
        // and three hanging in open air - physics would dump it instantly, but the anchor's
        // real rule fires first: freeze on contact (squared to the grid, like the game's
        // micro-align). Then a full brick lands far out on the frozen overhang - and stays.
        stage.SetView(4.0f, 2.9f);
        stage.SpawnPhysical("O", null, new Vector2(-1.5f, 0.5f), asleep: true);
        yield return stage.Settle(0.4f);
        yield return stage.Reveal();
        yield return stage.Hold(0.3f);

        GameObject anchor = DropIn(stage, "I", stage.Variant, new Vector2(0.5f, 5.8f)); // cols -2..2
        AnchorBlockSkin skin = Dress<AnchorBlockSkin>(anchor);
        skin.Apply();
        BlockDemoPuppet.Relayer(anchor);
        DemoContactRelay relay = anchor.GetComponent<DemoContactRelay>();
        bool frozen = false;
        relay.Touched += _ =>
        {
            if (frozen) return;
            frozen = true;
            FreezeSquare(anchor); // the real FreezeInPlace
            skin.PlayLockFlash();
        };
        yield return stage.WaitForLand(anchor);
        yield return stage.Hold(0.7f);

        // The proof brick drops onto the far end of the frozen beam (cols 0..2) - and holds,
        // a full column past anything the tower could ever support.
        GameObject prop = DropIn(stage, "O", null, new Vector2(1.5f, 5.8f));
        yield return stage.WaitForLand(prop);
        stage.Dust(new Vector2(1.0f, 3.0f), 0.6f, 0.3f);
        yield return stage.Hold(1.0f);
    }

    public static IEnumerator Vine(BlockDemoStage stage)
    {
        // Template: two towers with an exact two-column gap. The vine bridges them, welds ON
        // CONTACT (real joints), and a shove proves the cluster now moves as one.
        GameObject left = stage.SpawnPhysical("O", null, new Vector2(-1.5f, 0.5f), asleep: true);  // cols -3..-1
        GameObject right = stage.SpawnPhysical("O", null, new Vector2(2.5f, 0.5f), asleep: true);  // cols 1..3
        yield return stage.Settle(0.4f);
        yield return stage.Reveal();
        yield return stage.Hold(0.3f);

        GameObject vine = DropIn(stage, "I", stage.Variant, new Vector2(0.5f, 5.6f)); // cols -2..2
        VineBlockSkin skin = Dress<VineBlockSkin>(vine);
        skin.Apply();
        BlockDemoPuppet.Relayer(vine);
        yield return stage.WaitForLand(vine);

        Rigidbody2D vineBody = Body(vine);
        foreach (GameObject neighbour in PiecesNear(stage, new Vector2(0.5f, 2.5f), 2.6f, vine))
        {
            Rigidbody2D other = Body(neighbour);
            if (other == null || vineBody == null) continue;
            FixedJoint2D joint = vineBody.gameObject.AddComponent<FixedJoint2D>();
            joint.connectedBody = other;
            VineBlockSkin weld = neighbour.GetComponent<VineBlockSkin>() ?? Dress<VineBlockSkin>(neighbour);
            weld.GrowFrom(Vector2.up);
            BlockDemoPuppet.Relayer(neighbour);
        }
        yield return stage.Hold(0.9f);

        // The proof: one shove on the vine hops the ENTIRE welded cluster together.
        if (vineBody != null) vineBody.AddForce(new Vector2(-2.4f, 3.4f), ForceMode2D.Impulse);
        yield return stage.Hold(1.6f);
        _ = left; _ = right;
    }

    public static IEnumerator Magma(BlockDemoStage stage)
    {
        // Template: a pocket exactly ONE column wide (col -1..0), two deep, between two towers.
        // The magma T bridges it and yields two outer Pips plus one central vertical
        // Domino: connected cells with equal fall distance stay rigidly joined.
        stage.SetView(3.8f, 2.6f);
        stage.SpawnPhysical("O", null, new Vector2(-1.5f, 0.5f), asleep: true); // cols -3..-1
        stage.SpawnPhysical("O", null, new Vector2(1.5f, 0.5f), asleep: true);  // cols 0..2
        yield return stage.Settle(0.4f);
        yield return stage.Reveal();
        yield return stage.Hold(0.3f);

        GameObject magma = DropIn(stage, "T", stage.Variant, new Vector2(-0.5f, 5.6f)); // bottom cols -2..1
        MagmaBlockSkin skin = Dress<MagmaBlockSkin>(magma);
        skin.Apply();
        BlockDemoPuppet.Relayer(magma);
        yield return stage.WaitForLand(magma);
        SnapToGrid(magma); // micro-align before the melt so the cells line up with the pocket
        yield return stage.Hold(0.9f); // the crust breathes (skin wobble)

        // The middle and stem share one fall distance, so they become a vertical Domino.
        // The two disconnected shoulders remain Pips on the towers.
        Vector2[] order = { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(-1f, 0f), new Vector2(1f, 0f) };
        Transform magmaTr = magma.transform;
        var cells = new System.Collections.Generic.List<Vector2>();
        foreach (Vector2 offset in order)
            cells.Add(stage.transform.InverseTransformPoint(magmaTr.TransformPoint(offset)));
        stage.Dust(new Vector2(-0.5f, 2.6f), 1.1f, 0.45f);
        Object.Destroy(magma);
        for (int i = 0; i < cells.Count; i++)
        {
            if (i == 1) continue; // the stem stays joined to the middle cell: both fall two cells
            GameObject stone = stage.SpawnPhysical(i == 0 ? "Domino" : "Pip", null, cells[i]);
            if (stone != null)
            {
                stone.GetComponent<Rigidbody2D>().mass = i == 0 ? 2f : 1f;
                MagmaBlobVisual visual = stone.AddComponent<MagmaBlobVisual>();
                visual.InitMeltCell(Color.white, null, .6f);
                BlockDemoPuppet.Relayer(stone);
                stage.StartCoroutine(CoolMagmaFragment(stage, stone, visual));
            }
            yield return stage.Hold(0.15f);
        }
        yield return stage.Hold(2.0f); // physics settles the stones into the pocket
    }

    private static IEnumerator CoolMagmaFragment(BlockDemoStage stage, GameObject stone, MagmaBlobVisual visual)
    {
        yield return stage.WaitForLand(stone);
        if (visual != null) visual.Solidify();
    }

    public static IEnumerator Bomb(BlockDemoStage stage)
    {
        // Template: a three-brick tower (cols -2..0) with a pillar flush against it (cols 0..2).
        // The bomb drops down the pillar's column - a clean one-column channel, exactly like a
        // real placement - lands touching the tower's MIDDLE brick, and the blast removes what
        // it touches. The top brick genuinely loses its support and crashes down onto the base.
        stage.SetView(4.2f, 3.1f);
        GameObject baseBrick = stage.SpawnPhysical("O", null, new Vector2(-0.5f, 0.5f), asleep: true);
        GameObject middle = stage.SpawnPhysical("O", null, new Vector2(-0.5f, 2.5f), asleep: true);
        stage.SpawnPhysical("O", null, new Vector2(-0.5f, 4.5f), asleep: true);
        GameObject pillar = stage.SpawnPhysical("O", null, new Vector2(1.5f, 0.5f), asleep: true);
        yield return stage.Settle(0.5f);
        yield return stage.Reveal();
        yield return stage.Hold(0.3f);

        GameObject bomb = DropIn(stage, "O", stage.Variant, new Vector2(1.5f, 7.2f));
        BombBlockSkin skin = Dress<BombBlockSkin>(bomb);
        skin.Apply();
        BlockDemoPuppet.Relayer(bomb);
        yield return stage.WaitForLand(bomb);

        // The REAL fuse countdown look: heating seams, heartbeat, pre-flash.
        skin.Ignite();
        const float fuse = 1.8f;
        float t = 0f;
        while (t < fuse)
        {
            t += Time.deltaTime;
            skin.SetFuse(t / fuse);
            yield return null;
        }

        Vector3 centerWorld = bomb != null ? bomb.transform.position : stage.transform.position;
        Vector2 centerLocal = stage.transform.InverseTransformPoint(centerWorld);
        if (stage.Variant is BombBlockData bombData && bombData.ExplosionEffect != null)
        {
            GameObject blast = Vfx.Spawn(bombData.ExplosionEffect, centerWorld, bombData.ExplosionScale);
            if (blast != null) BlockDemoPuppet.Relayer(blast);
        }
        stage.CameraKick(0.2f);
        foreach (GameObject victim in PiecesNear(stage, centerLocal, 2.1f, bomb))
        {
            if (victim == middle || victim == pillar)
                stage.Shatter(victim, new Color(0.6f, 0.5f, 0.4f, 1f));
        }
        stage.Shatter(bomb, new Color(0.13f, 0.13f, 0.15f, 1f));
        yield return stage.Hold(2.2f); // the top brick falls for real
        _ = baseBrick;
    }

    public static IEnumerator Ice(BlockDemoStage stage)
    {
        // Template: a tall tower on cols -2..0. The classic HOOK move: a vertical L descends
        // beside the tower with its one protruding cube at the top, catching the tower's top
        // edge - a placement that genuinely works with normal friction. This one is ICE: the
        // real slippery material has no grip, the hook skids off the corner, and the piece
        // drops down the wall. Pure simulation.
        stage.SetView(4.0f, 2.9f);
        stage.SpawnPhysical("O", null, new Vector2(-1.5f, 0.5f), asleep: true); // cols -3..-1
        stage.SpawnPhysical("O", null, new Vector2(-1.5f, 2.5f), asleep: true); // top y = 4
        yield return stage.Settle(0.4f);
        yield return stage.Reveal();
        yield return stage.Hold(0.3f);

        // L rotated +90: vertical body in cols -1..0, its foot cube pointing top-LEFT over the
        // tower's edge column (-2..-1) - the hook.
        GameObject ice = DropIn(stage, "L", stage.Variant, new Vector2(-0.5f, 5.6f), 90f);
        IceBlockSkin skin = Dress<IceBlockSkin>(ice);
        skin.Apply();
        BlockDemoPuppet.Relayer(ice);

        // No scripted slide: the foot catches the ledge, and the ice material decides the rest.
        yield return stage.Hold(3.2f);
        yield return stage.Hold(0.6f);
    }

    public static IEnumerator Feather(BlockDemoStage stage)
    {
        // A/B on the identical template: a cantilevered beam that a NORMAL brick's weight tips
        // over - and the FEATHER's quarter-weight doesn't. Same drop, same spot; the only
        // difference is the variant's real mass. Both outcomes are pure simulation.
        stage.SetView(3.9f, 2.8f);

        // A: the normal brick wrecks it.
        stage.SpawnPhysical("O", null, new Vector2(0.5f, 0.5f), asleep: true);   // pillar cols -1..1
        GameObject beamA = stage.SpawnPhysical("I", null, new Vector2(1.0f, 2.5f), asleep: true);
        yield return stage.Settle(0.5f);
        yield return stage.Reveal();
        yield return stage.Hold(0.3f);
        DropIn(stage, "O", null, new Vector2(2.4f, 6.2f));
        yield return stage.Hold(2.4f); // physics: lands on the overhang, beam tips, everything falls

        yield return stage.FadeCut();

        // B: identical rig, same landing spot - but the feather barely weighs anything.
        stage.SpawnPhysical("O", null, new Vector2(0.5f, 0.5f), asleep: true);
        GameObject beamB = stage.SpawnPhysical("I", null, new Vector2(1.0f, 2.5f), asleep: true);
        yield return stage.Settle(0.4f);
        yield return stage.Reveal();
        yield return stage.Hold(0.3f);
        GameObject feather = DropIn(stage, "O", stage.Variant, new Vector2(2.4f, 6.2f), fallSpeed: 3f);
        FeatherBlockSkin skin = Dress<FeatherBlockSkin>(feather);
        skin.Apply();
        BlockDemoPuppet.Relayer(feather);
        DemoContactRelay relay = feather.GetComponent<DemoContactRelay>();
        bool fluttered = false;
        relay.Touched += _ => { if (!fluttered) { fluttered = true; skin.PlayLandFlutter(); } };
        yield return stage.Hold(2.4f); // physics: the beam holds
        _ = beamA; _ = beamB;
    }

    public static IEnumerator Tremor(BlockDemoStage stage)
    {
        // Template: a sketchy-but-standing stack - a T leaning a full column past its tower's
        // edge (statically stable, barely). The tremor lands flush beside the tower; its REAL
        // rule is radial velocity kicks, which shove the sloppy piece AWAY - out over its
        // overhang - and the simulation topples it. The clean base rides the quake out.
        stage.SetView(4.6f, 2.9f);
        GameObject tower = stage.SpawnPhysical("O", null, new Vector2(0.5f, 0.5f), asleep: true);  // cols -1..1
        GameObject beam = stage.SpawnPhysical("T", null, new Vector2(-0.5f, 2.5f), asleep: true);  // cols -2..1, overhang left
        yield return stage.Settle(0.7f);
        yield return stage.Reveal();
        yield return stage.Hold(0.35f);

        GameObject tremor = DropIn(stage, "T", stage.Variant, new Vector2(2.5f, 5.9f)); // cols 1..4, flush right
        TremorBlockSkin skin = Dress<TremorBlockSkin>(tremor);
        skin.Apply();
        BlockDemoPuppet.Relayer(tremor);
        yield return stage.WaitForLand(tremor);

        // The discharge: the real behaviour's radial velocity kicks, scaled by distance.
        skin.PlayQuake();
        stage.CameraKick(0.14f);
        stage.Dust(new Vector2(2.5f, 0.1f), 1.0f, 0.45f);
        Vector2 quakeLocal = stage.transform.InverseTransformPoint(tremor.transform.position);
        foreach (GameObject piece in PiecesNear(stage, quakeLocal, 8f, tremor))
        {
            Rigidbody2D body = Body(piece);
            if (body == null) continue;
            Vector2 pieceLocal = stage.transform.InverseTransformPoint(body.worldCenterOfMass);
            Vector2 dir = (pieceLocal - quakeLocal).normalized;
            float falloff = 1f / (1f + Vector2.Distance(pieceLocal, quakeLocal) * 0.2f);
            // Shear amplifies with height (the whip a real quake gives a tower's top): stacked
            // pieces outslide their base instead of drifting with it, which is what actually
            // topples a sloppy overhang.
            float heightShear = 1f + 0.35f * Mathf.Max(0f, pieceLocal.y);
            body.WakeUp();
            body.linearVelocity += (dir + Vector2.up * 0.5f).normalized * (4.2f * falloff * heightShear);
            body.angularVelocity += Random.Range(-50f, 50f) * falloff;
        }
        yield return stage.Hold(2.6f); // physics: the overhang walks off and collapses
        _ = tower; _ = beam;
    }

    public static IEnumerator Sandstone(BlockDemoStage stage)
    {
        // The load scale: the sandstone lands on open ground and a deliberate tower goes up
        // on it - a flush O, then a wide I laid as a bridge, and a final O. Each landing
        // grows the crack network (the shim drives the skin's ratcheting read-out the way
        // the real load reader does; the real crack SFX ticks per stage). The third brick is
        // one too many: a shiver, then it bursts to sand and the whole tower it carried
        // drops by real physics. No scenery - the tower IS the story (Nick's call).
        stage.SetView(4.2f, 3.0f);
        yield return stage.Reveal();
        yield return stage.Hold(0.3f);

        GameObject sand = DropIn(stage, "O", stage.Variant, new Vector2(0.5f, 6.6f)); // cols -1..1
        SandstoneBlockSkin skin = Dress<SandstoneBlockSkin>(sand);
        skin.Apply();
        BlockDemoPuppet.Relayer(sand);
        yield return stage.WaitForLand(sand);
        yield return stage.Hold(0.7f);

        // Weights are shapes whose hues contrast the sandstone's tan (an O is the same
        // yellow and the two would blur together - Nick's call). Brick one: a cyan I laid
        // flush across it. The first cracks appear.
        yield return DropWeightAndCrack(stage, skin, "I", new Vector2(0.5f, 7.0f), 0f, 0.39f, 1f);
        yield return stage.Hold(0.9f);

        // Brick two: a green S on the bridge (its cells sit right of the pivot - drop half a
        // column left so the piece CENTres over the tower). The network spreads, sand trickles.
        yield return DropWeightAndCrack(stage, skin, "S", new Vector2(-0.5f, 7.6f), 0.39f, 0.78f, 0.92f);
        yield return stage.Hold(1.0f);

        // Brick three: a red Z - one too many. It shivers under the strain, then bursts,
        // and the tower it carried comes down. (Z centred over the S's top pair so the
        // collapse keeps it on the pile instead of flinging it out of frame.)
        GameObject last = DropIn(stage, "Z", null, new Vector2(0f, 8.6f));
        yield return stage.WaitForLand(last);
        yield return DriveDamage(skin, 0.78f, 0.95f, 0.35f);
        SfxPlayer.Play("sandstone_crack", 0.6f, 0.06f, 0.84f);
        yield return stage.Hold(0.7f); // the shiver: "it's about to go"
        stage.Shatter(sand, new Color(0.82f, 0.68f, 0.42f, 1f)); // Crumble()'s SandTint
        SfxPlayer.Play("sandstone_burst", 1f);
        stage.CameraKick(0.12f);
        stage.Dust(new Vector2(0.5f, 1.2f), 1.1f, 0.5f);
        yield return stage.Hold(2.2f);
    }

    /// <summary>One brick onto the sandstone tower, then ramp the skin's crack read-out to the
    /// level the real load reader would settle at, with the real crack tick.</summary>
    private static IEnumerator DropWeightAndCrack(BlockDemoStage stage, SandstoneBlockSkin skin,
        string shape, Vector2 from, float damageFrom, float damageTo, float crackPitch)
    {
        GameObject weight = DropIn(stage, shape, null, from);
        yield return stage.WaitForLand(weight);
        SfxPlayer.Play("sandstone_crack", 0.6f, 0.06f, crackPitch);
        yield return DriveDamage(skin, damageFrom, damageTo, 0.5f);
    }

    /// <summary>The demo's stand-in for the smoothed load reader: ease the skin's damage and
    /// current-load read-outs from the previous level up to the new one (cracks only ever
    /// grow - the ratchet).</summary>
    private static IEnumerator DriveDamage(SandstoneBlockSkin skin, float from, float to,
        float seconds)
    {
        if (skin == null) yield break;
        float t = 0f;
        while (t < seconds && skin != null)
        {
            t += Time.deltaTime;
            float d = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds)));
            skin.SetDamage(d, d);
            yield return null;
        }
        if (skin != null) skin.SetDamage(to, to);
    }

    public static IEnumerator Pyramid(BlockDemoStage stage)
    {
        // The no-flat-top lesson, told twice by real physics: the pyramid lands like a
        // monument, an O dropped dead on the peak tips and rolls away, and a wide I laid
        // across the apex see-saws off. No shims at all - the slope IS the behaviour.
        stage.SetView(4.2f, 3.0f);
        yield return stage.Reveal();
        yield return stage.Hold(0.3f);

        // 3-wide monument. Cells sit at integer offsets from the pivot, and demo columns
        // have their centres at n+0.5 - so the ON-GRID drop is x=0.5 (cells -0.5/0.5/1.5,
        // apex over ~0.57, right where the O and I are aimed).
        GameObject pyramid = DropIn(stage, "Pyramid", stage.Variant, new Vector2(0.5f, 6.6f));
        yield return stage.WaitForLand(pyramid);
        yield return stage.Hold(0.8f);

        // Attempt one: an O dead-centre on the peak - it tips and rolls off.
        GameObject o = DropIn(stage, "O", null, new Vector2(0.5f, 7.6f));
        yield return stage.WaitForLand(o);
        yield return stage.Hold(2.0f); // physics: the topple and slide

        // Attempt two: a wide I bridged across the apex - a see-saw with one ending.
        GameObject beam = DropIn(stage, "I", null, new Vector2(0.5f, 8.4f));
        yield return stage.WaitForLand(beam);
        yield return stage.Hold(2.6f); // physics: the see-saw slides away

        // The monument stands alone.
        yield return stage.Hold(1.2f);
    }

    public static IEnumerator Maw(BlockDemoStage stage)
    {
        // A flat I maw: every top cell is exposed, so the strip wakes as a wall of mouths -
        // exactly what a landed I maw looks like in game. A real brick lands on it and is
        // devoured; then a SECOND maw lands on top and is spared - maws never eat maws, so
        // stacking them is safe.
        yield return stage.Reveal();
        GameObject maw = DropIn(stage, "I", stage.Variant, new Vector2(0.5f, 5.2f)); // cols -2..2
        MawBlockSkin skin = Dress<MawBlockSkin>(maw);
        skin.Apply();
        BlockDemoPuppet.Relayer(maw);
        yield return stage.WaitForLand(maw);
        FreezeSquare(maw); // a fused maw never budges
        skin.Activate();
        yield return stage.Hold(0.9f);

        // Prey: a full brick lands on the mouths - devoured, and it cost a life.
        GameObject prey = DropIn(stage, "O", null, new Vector2(0.5f, 5.6f)); // cols -1..1
        yield return stage.WaitForLand(prey);
        yield return stage.Hold(0.25f);
        skin.PlayChomp();
        yield return stage.Hold(0.12f);
        stage.Shatter(prey, new Color(0.45f, 0.2f, 0.5f, 1f));
        stage.CameraKick(0.1f);
        stage.StartCoroutine(LifeCostPulse(stage, new Vector2(0.5f, 1.8f)));
        yield return stage.Hold(0.9f);

        // The safe move: another MAW lands on the mouths - and is NOT eaten. Build maw on maw.
        GameObject stacked = DropIn(stage, "T", stage.Variant, new Vector2(0.5f, 5.8f)); // cols -1..2
        MawBlockSkin stackedSkin = Dress<MawBlockSkin>(stacked);
        stackedSkin.Apply();
        BlockDemoPuppet.Relayer(stacked);
        yield return stage.WaitForLand(stacked);
        FreezeSquare(stacked);
        stackedSkin.Activate();
        yield return stage.Hold(1.2f);
    }

    public static IEnumerator Curse(BlockDemoStage stage)
    {
        // A landed curse counting down: each placement while it sits exposed burns one sigil; at
        // zero it FIRES and takes a life, then re-arms. Then the counter-move: a brick placed ON
        // TOP entombs it - smoke snuffed, sigils dimmed, safe. (Skin props are driven directly -
        // puppet rule, no real behaviour on the demo stage.)
        yield return stage.Reveal();
        GameObject curse = DropIn(stage, "O", stage.Variant, new Vector2(-1.5f, 5.4f)); // cols -3..-1
        CurseBlockSkin skin = Dress<CurseBlockSkin>(curse);
        skin.Apply();
        BlockDemoPuppet.Relayer(curse);
        yield return stage.WaitForLand(curse);
        FreezeSquare(curse);
        skin.Activate();
        skin.SetDemoExposure(true);
        skin.SetCountdown(2, 4);   // join the story near the end: two sigils left
        yield return stage.Hold(0.8f);

        // A brick lands BESIDE it - not on it - and a sigil still burns: distance doesn't help.
        GameObject side1 = DropIn(stage, "O", null, new Vector2(1.5f, 5.8f)); // cols 0..2
        yield return stage.WaitForLand(side1);
        skin.PlaySigilBurn(1, 4);
        yield return stage.Hold(0.7f);

        // Another lands - the last sigil dies and the curse fires: a life is taken, and it re-arms.
        GameObject side2 = DropIn(stage, "O", null, new Vector2(1.5f, 6.4f));
        yield return stage.WaitForLand(side2);
        skin.PlayFire(4, 4);
        stage.CameraKick(0.09f);
        stage.StartCoroutine(LifeCostPulse(stage, new Vector2(-1.5f, 2f)));
        yield return stage.Hold(1f);

        // The counter-move: bury it. A brick ON TOP snuffs the smoke and closes the eye - safe.
        GameObject cap = DropIn(stage, "O", null, new Vector2(-1.5f, 6.2f));
        yield return stage.WaitForLand(cap);
        skin.SetDemoExposure(false);
        yield return stage.Hold(1.3f);
    }

    // ---- control-story demos (scripted steering; these teach an INPUT rule, so the descent is
    // staged like the tutorial rather than simulated) ---------------------------------------------

    public static IEnumerator Vortex(BlockDemoStage stage)
    {
        yield return stage.Reveal();
        GameObject piece = stage.Spawn("T", stage.Variant, new Vector2(1.2f, 4.6f));
        VortexBlockSkin skin = Dress<VortexBlockSkin>(piece);
        skin.Apply();
        BlockDemoPuppet.Relayer(piece);

        // The tell: an input arrow points RIGHT while the brick obeys in reverse.
        var arrowGo = new GameObject("InputArrow");
        arrowGo.transform.SetParent(piece.transform.parent, false);
        arrowGo.layer = BlockDemoStage.DemoLayer;
        var arrow = arrowGo.AddComponent<SpriteRenderer>();
        arrow.sprite = MenuSprites.Chevron(Color.white);
        arrow.color = new Color(1f, 1f, 1f, 0f);
        arrow.sortingOrder = 60;
        arrowGo.transform.localScale = Vector3.one * 0.9f;

        yield return stage.Hold(0.3f);
        Transform tr = piece.transform;
        for (int push = 0; push < 3 && tr != null; push++)
        {
            float t = 0f;
            while (t < 0.5f && tr != null)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / 0.5f);
                arrow.color = new Color(1f, 1f, 1f, Mathf.Sin(k * Mathf.PI) * 0.95f);
                arrowGo.transform.localPosition = tr.localPosition + new Vector3(1.4f + k * 0.25f, 0f, 0f);
                tr.localPosition += new Vector3(-1.0f * Time.deltaTime / 0.5f, -0.85f * Time.deltaTime, 0f);
                yield return null;
            }
            yield return stage.Hold(0.12f);
        }
        if (tr != null) yield return stage.Drop(piece, 0.5f, 4f);
        Object.Destroy(arrowGo);
        yield return stage.Hold(0.5f);
    }

    public static IEnumerator Locked(BlockDemoStage stage)
    {
        yield return stage.Reveal();
        GameObject piece = stage.Spawn("T", stage.Variant, new Vector2(0f, 4.6f));
        LockedBlockSkin skin = Dress<LockedBlockSkin>(piece);
        skin.Apply();
        BlockDemoPuppet.Relayer(piece);
        yield return stage.Hold(0.4f);

        Transform tr = piece.transform;
        for (int attempt = 0; attempt < 2 && tr != null; attempt++)
        {
            float t = 0f;
            while (t < 0.7f && tr != null)
            {
                t += Time.deltaTime;
                tr.localPosition += new Vector3(0f, -1.1f * Time.deltaTime, 0f);
                yield return null;
            }
            skin.PlayRefuse(attempt == 0 ? 1 : -1);
            yield return stage.Hold(0.45f);
        }
        if (tr != null) yield return stage.Drop(piece, 0.5f, 4f);
        yield return stage.Hold(0.6f);
    }

    // ---- posters -------------------------------------------------------------------------------

    /// <summary>The Vault grid's static showcase pose: the brick at rest, dressed in its real
    /// skin, framed as a hero shot (no scenario). Every variant poses as the T for a uniform
    /// collection. The skin attaches by the BLOCKVARIANTS.md naming convention
    /// (&lt;Name&gt;BlockSkin), so a NEW brick's poster needs no code here at all - only bricks
    /// whose poster wants an extra cue (the Maw's waking grin) get a case.</summary>
    public static void PosterPose(BlockDemoStage stage)
    {
        string id = ProgressStore.BlockId(stage.Variant);
        // Shape-bound bricks (the Pyramid) pose as their own silhouette, not the uniform T
        // (their data IS their shape; a pyramid-skinned T would be a lie). Data-driven so
        // the next shape-bound brick poses correctly with no edit here.
        GameObject piece = ContentCatalog.IsShapeBound(stage.Variant)
            ? stage.Spawn(id, stage.Variant, new Vector2(0f, 0.5f))
            : stage.Spawn("T", stage.Variant, new Vector2(0f, 0.5f));
        BlockVariantSkin skin = AttachSkinByConvention(piece, id);
        if (skin is MawBlockSkin maw) maw.Activate();                    // the grins ARE the poster
        if (skin is SandstoneBlockSkin sand) sand.SetDamage(0.55f, 0f);  // the cracks ARE the poster
    }

    /// <summary>Get the variant's real look onto a puppet with zero per-variant code: resolve
    /// &lt;id&gt;BlockSkin by the naming convention, add it, call its public Apply(). Returns null
    /// for skinless variants (their tint/material overrides already applied at spawn).</summary>
    private static BlockVariantSkin AttachSkinByConvention(GameObject piece, string id)
    {
        if (piece == null || string.IsNullOrEmpty(id)) return null;
        System.Type skinType = System.Type.GetType(id + "BlockSkin");
        if (skinType == null || !typeof(BlockVariantSkin).IsAssignableFrom(skinType)) return null;
        var skin = (BlockVariantSkin)piece.AddComponent(skinType);
        skinType.GetMethod("Apply")?.Invoke(skin, null);
        BlockDemoPuppet.Relayer(piece);
        return skin;
    }

    // A rising, fading red pulse over the maw - the "that cost a life" cue without HUD text.
    private static IEnumerator LifeCostPulse(BlockDemoStage stage, Vector2 at)
    {
        var go = new GameObject("LifeCost");
        go.transform.SetParent(stage.transform, false);
        go.transform.localPosition = new Vector3(at.x, at.y, -0.5f);
        go.layer = BlockDemoStage.DemoLayer;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = RuntimeSprites.SoftBlob();
        sr.color = new Color(0.95f, 0.2f, 0.25f, 0.0f);
        sr.sortingOrder = 80;
        float t = 0f;
        const float life = 0.9f;
        while (t < life && sr != null)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);
            go.transform.localPosition = new Vector3(at.x, at.y + k * 0.9f, -0.5f);
            go.transform.localScale = Vector3.one * (0.7f + k * 0.6f);
            sr.color = new Color(0.95f, 0.2f, 0.25f, Mathf.Sin(k * Mathf.PI) * 0.55f);
            yield return null;
        }
        if (sr != null) Object.Destroy(go);
    }
}
