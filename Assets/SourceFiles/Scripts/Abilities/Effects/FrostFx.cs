using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the freeze overlay on a single block: ramps the Frost shader's <c>_Freeze</c> from 0 to 1
/// over a duration so the ice crawls in along the shader's noise front (see Frost.shader). One per
/// frozen block, created by <see cref="BlockController.Freeze"/>; disables itself once fully iced.
///
/// Uses a MaterialPropertyBlock for the per-block values (_Freeze, _Seed) so every overlay keeps
/// SHARING the one Frost.mat asset - so tuning that material in the Inspector (in play mode) updates
/// all frozen blocks live. (Instancing the material per block would break that link.)
/// </summary>
public sealed class FrostFx : MonoBehaviour
{
    private static readonly int FreezeId = Shader.PropertyToID("_Freeze");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");
    private static readonly int PatternId = Shader.PropertyToID("_Pattern");
    private static readonly int TurnId = Shader.PropertyToID("_Turn");
    private static readonly int DetailStrengthId = Shader.PropertyToID("_DetailStrength");
    private static int _variantCursor;

    private SpriteRenderer[] _renderers;
    private MaterialPropertyBlock _mpb;
    private float[] _seeds;
    private float[] _patterns;
    private float[] _turns;
    private float[] _detailStrengths;
    private float _seconds;
    private float _age;
    private float _seed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        _variantCursor = 0;
    }

    public void Play(List<SpriteRenderer> overlays, float seconds, float seed)
    {
        _renderers = overlays.ToArray();
        _seeds = new float[_renderers.Length];
        _patterns = new float[_renderers.Length];
        _turns = new float[_renderers.Length];
        _detailStrengths = new float[_renderers.Length];
        int sequenceOffset = _variantCursor;
        _variantCursor = (_variantCursor + _renderers.Length) % 20;
        for (int i = 0; i < _seeds.Length; i++)
        {
            _seeds[i] = seed + i * 17.31f;
            int variant = (sequenceOffset + i) % 20; // five fracture stamps x four rotations
            int pattern = variant % 5;
            _patterns[i] = pattern;
            _turns[i] = variant / 5;
            _detailStrengths[i] = ResolveDetailStrength(variant, pattern);
        }

        _seconds = Mathf.Max(0.01f, seconds);
        _seed = seed;
        _age = 0f;
        _mpb = new MaterialPropertyBlock();
        ApplyFreeze(0f);
        enabled = true;
    }

    private void Update()
    {
        _age += Time.deltaTime;
        float freeze = Mathf.Clamp01(_age / _seconds);
        ApplyFreeze(freeze);
        if (freeze >= 1f) enabled = false; // holds final shader properties
    }

    private void ApplyFreeze(float freeze)
    {
        for (int i = 0; i < _renderers.Length; i++)
        {
            SpriteRenderer r = _renderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetFloat(FreezeId, freeze);
            _mpb.SetFloat(SeedId, _seeds != null && i < _seeds.Length ? _seeds[i] : _seed);
            _mpb.SetFloat(PatternId, _patterns != null && i < _patterns.Length ? _patterns[i] : 0f);
            _mpb.SetFloat(TurnId, _turns != null && i < _turns.Length ? _turns[i] : 0f);
            _mpb.SetFloat(DetailStrengthId,
                _detailStrengths != null && i < _detailStrengths.Length ? _detailStrengths[i] : 1f);
            r.SetPropertyBlock(_mpb);
        }
    }

    private static float ResolveDetailStrength(int variant, int pattern)
    {
        // A frozen tower reads better when a few panes are just cloudy ice, not every cell carrying
        // a glyph-like crack. The cycle still stays deterministic: 20 panes cover the small tile set.
        if (variant == 0 || variant == 7 || variant == 14) return 0f;
        if (pattern == 0) return 0.22f;
        if (pattern == 1) return 0.58f;
        if (pattern == 2) return 0.78f;
        if (pattern == 3) return 0.36f;
        return 0.62f;
    }
}
