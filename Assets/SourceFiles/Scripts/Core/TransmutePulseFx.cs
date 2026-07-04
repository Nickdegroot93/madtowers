using UnityEngine;

/// <summary>
/// The transmute moment made physical: the piece's VISUAL squashes wide, stretches tall and
/// settles elastically over ~0.35 s while the variant look swaps underneath - the brick visibly
/// "becomes" the new thing instead of flickering. Scales only visual child transforms (the
/// PieceSkin / variant overlays), never the body or colliders (PHYSICS.md cosmetic-only rule);
/// safe on the kinematic falling piece. Self-removes.
/// </summary>
public sealed class TransmutePulseFx : MonoBehaviour
{
    private const float Duration = 0.35f;

    private Transform[] _targets;
    private Vector3[] _baseScales;
    private float _age;

    public static void Play(BlockController block)
    {
        if (block == null) return;
        if (block.GetComponent<TransmutePulseFx>() != null) return; // one pulse at a time

        var renderers = block.GetComponentsInChildren<SpriteRenderer>(true);
        var targets = new System.Collections.Generic.List<Transform>();
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null || !renderers[i].enabled) continue;
            string n = renderers[i].gameObject.name;
            if (n.Contains("PlacementBeam") || n.Contains("VectorGuide")) continue;
            targets.Add(renderers[i].transform);
        }
        if (targets.Count == 0) return;

        TransmutePulseFx fx = block.gameObject.AddComponent<TransmutePulseFx>();
        fx._targets = targets.ToArray();
        fx._baseScales = new Vector3[targets.Count];
        for (int i = 0; i < targets.Count; i++) fx._baseScales[i] = targets[i].localScale;
    }

    private void Update()
    {
        _age += Time.deltaTime;
        float t = Mathf.Clamp01(_age / Duration);

        // Squash -> stretch -> elastic settle, volume-conserving.
        float w;
        if (t < 0.3f) w = Mathf.Lerp(0f, -0.16f, t / 0.3f);                         // squash
        else if (t < 0.55f) w = Mathf.Lerp(-0.16f, 0.12f, (t - 0.3f) / 0.25f);      // rebound tall
        else w = 0.12f * (FxKit.Elastic((t - 0.55f) / 0.45f, 1f, 5f, 14f) - 1f);    // settle

        for (int i = 0; i < _targets.Length; i++)
        {
            Transform target = _targets[i];
            if (target == null) continue;
            Vector3 b = _baseScales[i];
            target.localScale = new Vector3(b.x * (1f - w), b.y * (1f + w), b.z);
        }

        if (t >= 1f)
        {
            for (int i = 0; i < _targets.Length; i++)
                if (_targets[i] != null) _targets[i].localScale = _baseScales[i];
            Destroy(this);
        }
    }
}
