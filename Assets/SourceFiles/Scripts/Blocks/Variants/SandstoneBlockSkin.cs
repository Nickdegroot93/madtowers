using UnityEngine;

/// <summary>
/// The Sandstone look: fixed, theme-independent warm sediment stone (procedural
/// Resources/Sandstone shader) replacing the chapter art. The personality is the CONTINUOUS
/// damage read-out: _Damage (ratcheted worst load) grows the crack network fluidly, _Load
/// (current pressure) drives sand trickling from the cracks, and near the limit the whole
/// brick shivers - the "one more and it bursts" warning the mechanic depends on.
/// Driven per physics tick by SandstoneBlockBehaviour.SetDamage. See BLOCKVARIANTS.md.
/// </summary>
public sealed class SandstoneBlockSkin : BlockVariantSkin
{
    private static readonly int DamageId = Shader.PropertyToID("_Damage");
    private static readonly int LoadId = Shader.PropertyToID("_Load");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");

    protected override string MaterialResource => "Sandstone";
    protected override string CellName => "SandstoneCell";

    private float _damage;
    private float _load;
    private bool _shivering;

    /// <summary>Build the sediment look. Called from SandstoneBlockData.OnApplied.</summary>
    public void Apply() => BuildCells();

    /// <summary>Push the live damage state: `damage01` is the ratcheted worst-load fraction
    /// (cracks never heal), `load01` the current pressure (drives the trickle).</summary>
    public void SetDamage(float damage01, float load01)
    {
        _damage = Mathf.Clamp01(damage01);
        _load = Mathf.Clamp01(load01);
    }

    protected override void ConfigureCell(int index, int col, int row, SpriteRenderer overlay,
        MaterialPropertyBlock mpb)
    {
        mpb.SetTexture("_MagmaCracks", Resources.Load<Texture2D>("MagmaCracks"));
    }

    private static Sprite _shard;
    public static void Shatter(Bounds bounds, Color tint)
    {
        if (_shard == null) _shard = Resources.Load<Sprite>("HazardShard");
        BlockShatterFx.Spawn(bounds, tint, 14, _shard);
    }

    private void LateUpdate()
    {
        if (!IsBuilt) return;
        SetCellsFloat(DamageId, _damage);
        SetCellsFloat(LoadId, _load);

        // The final warning: past ~85% of the limit the brick shivers - a tiny positional
        // buzz (visual cells only, never the physics body). Driven by the CURRENT
        // load, not the ratchet: shivering means "one more and it bursts", so it must stop
        // when the weight comes off (or the brick is frozen), while the cracks stay.
        float panic = Mathf.InverseLerp(0.85f, 1f, _load);
        if (panic <= 0f)
        {
            if (_shivering)
            {
                _shivering = false;
                for (int i = 0; i < Cells.Count; i++)
                    if (Cells[i] != null) Cells[i].transform.localPosition = BasePositions[i];
            }
            return;
        }
        _shivering = true;
        for (int i = 0; i < Cells.Count; i++)
        {
            if (Cells[i] == null) continue;
            float jx = (Mathf.PerlinNoise(Time.time * 18f, i * 3.7f) - 0.5f) * 0.05f * panic;
            float jy = (Mathf.PerlinNoise(i * 5.1f, Time.time * 18f) - 0.5f) * 0.05f * panic;
            Cells[i].transform.localPosition = BasePositions[i] + new Vector3(jx, jy, 0f);
        }
    }
}
