using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Locked look: a rusted iron gear bound by a chain and a screw-head locking pin per cell (procedural
/// Resources/Locked shader), seated in a fixed weathered slate casing; only the hardware
/// reacts. The chain runs across mid-height and connects cell-to-cell (via <c>_Col</c>) into one chain
/// binding the whole piece. Nothing spins idly (it's locked); instead it carries a faint involuntary
/// <c>_Strain</c> twitch, and when the player tries to rotate, <see cref="PlayRefuse"/> kicks a damped
/// spring: the gear lurches against the chain (which snaps taut) and springs back, with a spark at the pin
/// (<c>_Flash</c>). At the same instant the whole brick visual gives a tiny <b>flinch</b> in the pressed
/// direction and snaps back, so you see it physically try to rotate but fail.
///
/// The flinch is VISUAL-ONLY: it rotates the chapter-art sprite and the overlay cells about the piece
/// centre (never the Rigidbody, transform root, or colliders), only while the piece is falling, and resets
/// the moment it locks, so it never writes a landed body's pose and rides the kinematic, grid-owned
/// descent without ever feeding the solver. All on scaled time, so a pause
/// freezes it. See BLOCKVARIANTS.md.
/// </summary>
public sealed class LockedBlockSkin : BlockVariantSkin
{
    private static readonly int StrainId = Shader.PropertyToID("_Strain");
    private static readonly int FlashId = Shader.PropertyToID("_Flash");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");
    private static readonly int ColId = Shader.PropertyToID("_Col");
    private static readonly int GearAngleId = Shader.PropertyToID("_GearAngle");

    private const float Stiffness = 95f;          // spring constant pulling strain back to rest
    private const float Damping = 9.5f;           // under-damped, so it overshoots once then settles
    private const float RefuseKick = 13f;         // velocity impulse on a denied rotate (peak strain ~0.8)
    private const float IdleKick = 1.6f;          // tiny involuntary twitch (peak strain ~0.12)
    private const float MaxStep = 0.04f;          // clamp dt so a hitch can't blow the spring up
    private const float BrickFlinchDegrees = 3f;  // tiny whole-brick flinch at full strain (it "tries" to turn)

    private float _strain;
    private float _strainVel;
    private float _flash;
    private float _nextTwitch;
    private bool _locked;
    private float _lastAppliedStrain = float.NaN;
    private float _lastAppliedFlash = float.NaN;

    // The brick art (chapter sprite, or the per-cell sprites if the chapter art failed to load) we flinch.
    private readonly List<Transform> _brickVisuals = new List<Transform>();
    private readonly List<Vector3> _brickBasePos = new List<Vector3>();
    private Vector3 _pivot; // piece visual centre, in controller-local space

    /// <summary>Build the gear/chain look and capture the brick visuals to flinch. From LockedBlockData.OnApplied.</summary>
    public void Apply()
    {
        BuildCells();
        CaptureBrickVisuals();
    }

    /// <summary>Strain the gear against its chain and flinch the brick - the "no" cue. -1 left / +1 right.</summary>
    public void PlayRefuse(int direction)
    {
        if (_locked) return;
        enabled = true;
        _strainVel = Mathf.Sign(direction == 0 ? 1 : direction) * RefuseKick;
        _flash = 1f;
    }

    /// <summary>The piece locked: stop flinching the body and rest the brick visuals. From OnLocked.</summary>
    public void OnLocked()
    {
        _locked = true;
        ApplyFlinch(0f); // the gear may still idle-twitch, but the brick itself rests
        enabled = true;
    }

    protected override string MaterialResource => "Locked";
    protected override bool HidesChapterArt => true; // fixed slate and iron casing
    public override bool BlocksForeignOverlays => false; // material replacement must not change vine eligibility
    protected override string CellName => "LockedCell";

    protected override void ConfigureCell(int index, int col, int row, SpriteRenderer overlay, MaterialPropertyBlock mpb)
    {
        float seed = (index * 0.6180339f) % 1f;
        mpb.SetFloat(SeedId, seed);
        mpb.SetFloat(ColId, col);                  // continuous chain coordinate across cells
        mpb.SetFloat(GearAngleId, seed * 6.2831853f); // rest each cell's teeth at a different phase
        mpb.SetFloat(StrainId, 0f);
        mpb.SetFloat(FlashId, 0f);
    }

    private void CaptureBrickVisuals()
    {
        // The piece centre = average of the overlay cell positions (one per real cell).
        _pivot = Vector3.zero;
        if (BasePositions.Count > 0)
        {
            for (int i = 0; i < BasePositions.Count; i++) _pivot += BasePositions[i];
            _pivot /= BasePositions.Count;
        }

        // Everything visible that isn't one of our overlay cells (the chapter "PieceSkin" sprite, normally).
        SpriteRenderer[] all = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < all.Length; i++)
        {
            SpriteRenderer sr = all[i];
            if (sr == null || !sr.enabled || sr.sprite == null) continue;
            string n = sr.gameObject.name;
            if (n == CellName || n.Contains("PlacementBeam") || n.Contains("VectorGuide")) continue;
            _brickVisuals.Add(sr.transform);
            _brickBasePos.Add(sr.transform.localPosition);
        }
    }

    private void LateUpdate()
    {
        if (!IsBuilt) return;

        float dt = Mathf.Min(Time.deltaTime, MaxStep); // scaled - a pause freezes the strain (PHYSICS.md)

        // Faint involuntary twitch on a randomised cadence while falling, so the held piece feels under
        // tension. Stops once locked, so a tower of placed Locked blocks doesn't fidget.
        if (!_locked)
        {
            _nextTwitch -= dt;
            if (_nextTwitch <= 0f)
            {
                _strainVel += (Random.value < 0.5f ? -1f : 1f) * IdleKick;
                _nextTwitch = Random.Range(2f, 5f);
            }
        }

        // Damped spring back to rest: a denied rotate (or twitch) kicks the velocity, this resolves it.
        _strainVel += (-Stiffness * _strain - Damping * _strainVel) * dt;
        _strain = Mathf.Clamp(_strain + _strainVel * dt, -1f, 1f);
        if (_locked && Mathf.Abs(_strain) < 0.0005f && Mathf.Abs(_strainVel) < 0.0005f)
        {
            _strain = 0f;
            _strainVel = 0f;
        }
        SetCellsFloatIfChanged(StrainId, _strain, ref _lastAppliedStrain);

        // The whole-brick flinch tracks the strain, but only while falling (never after landing ownership).
        ApplyFlinch(_locked ? 0f : _strain * BrickFlinchDegrees);

        if (_flash > 0f)
        {
            _flash = Mathf.Max(0f, _flash - dt * 3.2f);
            SetCellsFloatIfChanged(FlashId, _flash, ref _lastAppliedFlash);
        }
        else
        {
            SetCellsFloatIfChanged(FlashId, 0f, ref _lastAppliedFlash);
        }

        if (_locked && _strain == 0f && _strainVel == 0f && _flash <= 0f)
        {
            enabled = false;
        }
    }

    private void SetCellsFloatIfChanged(int propertyId, float value, ref float lastValue)
    {
        if (!float.IsNaN(lastValue) && Mathf.Abs(value - lastValue) < 0.0005f) return;
        SetCellsFloat(propertyId, value);
        lastValue = value;
    }

    // Rotate the brick art and the overlay cells about the piece centre by 'deg' (visual only).
    private void ApplyFlinch(float deg)
    {
        float rad = deg * Mathf.Deg2Rad;
        float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
        Quaternion rot = Quaternion.Euler(0f, 0f, deg);

        for (int i = 0; i < _brickVisuals.Count; i++)
        {
            Transform t = _brickVisuals[i];
            if (t == null) continue;
            t.localPosition = RotateAboutPivot(_brickBasePos[i], c, s);
            t.localRotation = rot;
        }
        for (int i = 0; i < Cells.Count; i++)
        {
            SpriteRenderer cell = Cells[i];
            if (cell == null) continue;
            cell.transform.localPosition = RotateAboutPivot(BasePositions[i], c, s);
            cell.transform.localRotation = rot;
        }
    }

    private Vector3 RotateAboutPivot(Vector3 basePos, float c, float s)
    {
        Vector3 o = basePos - _pivot;
        return new Vector3(_pivot.x + c * o.x - s * o.y, _pivot.y + s * o.x + c * o.y, basePos.z);
    }
}
