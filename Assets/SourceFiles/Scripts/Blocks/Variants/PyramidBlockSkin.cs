using UnityEngine;

/// <summary>One whole-piece stone/engine surface. All motion and shading are confined
/// to the collider-less PieceSkin; no per-cell randomisation or physics writes.</summary>
public sealed class PyramidBlockSkin : BlockVariantSkin
{
    protected override string MaterialResource => "CyberPyramid";
    private Material _material;
    private SpriteRenderer _surface;
    private float _phase;

    public void Apply()
    {
        if (_surface != null) return;
        var block = GetComponent<BlockController>();
        var parent = block != null ? block.PieceSkinTransform : transform.Find("PieceSkin");
        if (parent == null) return;
        var shader = Resources.Load<Shader>("CyberPyramid");
        if (shader == null) return;
        _material = new Material(shader);
        _material.SetTexture("_HazardSurface", Resources.Load<Texture2D>("HazardSurface"));
        var original = parent.GetComponent<SpriteRenderer>();
        HideChapterArt();
        var go = new GameObject("Cyber pyramid stone and engine");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0, .35f, 0);
        go.transform.localScale = new Vector3(3.3f, 3.5f, 1);
        _surface = go.AddComponent<SpriteRenderer>();
        _surface.sprite = RuntimeSprites.Square();
        _surface.sharedMaterial = _material;
        Cells.Add(_surface);
        if (original != null)
        {
            _surface.sortingLayerID = original.sortingLayerID;
            _surface.sortingOrder = original.sortingOrder;
            original.enabled = false;
        }
    }

    public void SetCharge(int charge)
    {
        if (_material == null) return;
        _material.SetFloat("_Charge", charge);
        _material.SetFloat("_BeatAt", _phase);
    }
    public void SetIgnition(float ignition) { if (_material != null) _material.SetFloat("_Ignition", ignition); }

    private void LateUpdate()
    {
        if (_material == null) return;
        _phase += Time.deltaTime;
        _material.SetFloat("_Phase", _phase);
    }

    public void Launch(Transform effectParent = null, bool playSound = true)
    {
        if (_surface == null) return;
        PyramidDepartureFx.Play(_surface, effectParent, playSound);
        _surface.enabled = false;
    }

    private void OnDestroy() { if (_material != null) Destroy(_material); }
}
