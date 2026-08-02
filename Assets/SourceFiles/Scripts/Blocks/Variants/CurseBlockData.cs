using UnityEngine;

/// <summary>
/// The Curse: a haunted brick that must be BURIED. From the moment any of its top cells is open to
/// the sky a placement countdown runs, shown as sigils on the brick; every piece locked while it is
/// exposed burns one, and when the last sigil dies the curse fires - the player loses a LIFE
/// (through the hazard path, so LifeLossImmunity/Ward absorb it) and the count begins again.
/// Covering every top cell pacifies it; re-exposing it (its cover destroyed or knocked off)
/// restarts a FRESH countdown. The deliberate inverse of the Maw: the Maw punishes building ON it,
/// the Curse punishes NOT doing so (Nick 2026-08-02).
/// Look = CurseBlockSkin (sigil-eyed tomb stone + soul smoke); logic = CurseBlockBehaviour.
/// See BLOCKVARIANTS.md.
/// </summary>
[CreateAssetMenu(fileName = "CurseBlockData", menuName = "Stacking/Blocks/Curse Block Variant")]
public class CurseBlockData : BlockData
{
    [Header("Curse")]
    [Tooltip("Placements allowed while exposed before the curse fires (and between firings). Nick 2026-08-02: 4 to start, bracket in playtesting.")]
    [SerializeField] private int buryWithinPlacements = 4;

    public int BuryWithinPlacements => Mathf.Max(1, buryWithinPlacements);

    // Build the sealed-tomb look as soon as the variant is applied (after the chapter skin).
    // Get-or-add so a re-apply can't add a second skin. (Base OnApplied is empty; see BLOCKVARIANTS.md.)
    public override void OnApplied(BlockController block)
    {
        if (block == null) return;
        if (!block.TryGetComponent(out CurseBlockSkin skin))
            skin = block.gameObject.AddComponent<CurseBlockSkin>();
        skin.Apply();
    }

    // On landing the hex wakes: the eye opens and the countdown starts. A curse caught by the
    // game-over wreckage settle, or one that slid off and locked below the screen, must not
    // wake at all (mirrors MagmaMelt's guard - HandleLostBelowScreen locks lost pieces one
    // statement before destroying them, and a results-screen detonation is pure noise).
    public override void OnLocked(BlockController block)
    {
        if (block == null) return;
        bool runOver = GameManager.Instance != null && GameManager.Instance.isGameOver;
        if (runOver || LossZone.IsBelowCull(block.transform.position)) return;

        block.TryGetComponent(out CurseBlockSkin skin);
        if (skin != null) skin.Activate();
        block.gameObject.AddComponent<CurseBlockBehaviour>().Begin(skin, BuryWithinPlacements);
    }
}
