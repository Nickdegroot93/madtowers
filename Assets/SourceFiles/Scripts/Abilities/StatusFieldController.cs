using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns timed game states (StatusEffectDefinition) into on-screen feedback. Fully data-driven:
/// the STATE carries its own look via StatusEffectDefinition.ScreenEffect (a prefab, e.g. the Hovl
/// fullscreen overlay that self-parents to the camera and sizes to the view). Any active state
/// that has a ScreenEffect is shown while active and torn down when it ends - so surfacing a new
/// state, or pointing a second ability at an existing one, needs NO code here: just author the
/// status asset and drop a prefab on it. Lives on the GameManager object next to StatusEffects.
/// </summary>
public sealed class StatusFieldController : MonoBehaviour
{
    private const float FadeOutSeconds = 1.2f;

    private StatusEffects _status;
    private readonly List<StatusEffectDefinition> _active = new List<StatusEffectDefinition>();
    private readonly Dictionary<StatusEffectDefinition, GameObject> _shown = new Dictionary<StatusEffectDefinition, GameObject>();
    private readonly List<StatusEffectDefinition> _ended = new List<StatusEffectDefinition>();

    private void Awake()
    {
        _status = GetComponent<StatusEffects>();
    }

    private void Update()
    {
        if (_status == null) return;
        bool over = GameManager.Instance != null && GameManager.Instance.isGameOver;

        _status.GetActiveDefinitions(_active);

        // Show a field for any newly-active state that carries one.
        if (!over)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                StatusEffectDefinition def = _active[i];
                if (def == null || def.ScreenEffect == null || _shown.ContainsKey(def)) continue;
                _shown[def] = ShowField(def.ScreenEffect);
            }
        }

        // Tear down fields whose state ended (or on game over).
        _ended.Clear();
        foreach (KeyValuePair<StatusEffectDefinition, GameObject> kv in _shown)
        {
            if (over || !_active.Contains(kv.Key)) _ended.Add(kv.Key);
        }
        for (int i = 0; i < _ended.Count; i++)
        {
            HideField(_shown[_ended[i]]);
            _shown.Remove(_ended[i]);
        }
    }

    private GameObject ShowField(GameObject prefab)
    {
        SfxPlayer.Play("status_engage", 0.65f, 0.04f);

        GameObject effect = Instantiate(prefab); // HS_ScreenEffect parents it to Camera.main and sizes it
        // A prefab can bundle several systems (e.g. a buff + its smoke); they ship as one-shots,
        // so loop every system to sustain the look for the whole window.
        foreach (ParticleSystem ps in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            ps.Play();
        }
        return effect;
    }

    private void HideField(GameObject effect)
    {
        if (effect == null) return;

        foreach (ParticleSystem ps in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting); // let live particles fade out
        }
        Destroy(effect, FadeOutSeconds);
    }
}
