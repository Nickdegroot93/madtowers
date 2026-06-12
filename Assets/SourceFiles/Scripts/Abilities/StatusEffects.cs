using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime for timed game states (StatusEffectDefinition). Lives on the GameManager's
/// object. Timers tick on scaled time, so pauses freeze every active state for free.
/// Consumers: GameManager consults LifeLossImmunity and ScorePerBlockBonus directly;
/// AbilityRuntime folds GetFallSpeedFactor() into the spawn-speed multiplier (the
/// Changed event tells it when to recompute). Abilities with Custom states query
/// IsActive(definition) themselves.
/// </summary>
public class StatusEffects : MonoBehaviour
{
    private sealed class ActiveEffect
    {
        public StatusEffectDefinition Definition;
        public float Remaining;
        public float Magnitude;
    }

    private readonly List<ActiveEffect> _active = new List<ActiveEffect>();

    /// <summary>Raised when any state is applied, re-applied or expires.</summary>
    public event System.Action Changed;

    public void Apply(StatusEffectDefinition definition)
    {
        Apply(definition, -1f, float.NaN);
    }

    /// <summary>Apply a state; negative duration / NaN magnitude mean "use the asset's defaults".</summary>
    public void Apply(StatusEffectDefinition definition, float durationOverride, float magnitudeOverride)
    {
        if (definition == null) return;

        float duration = durationOverride > 0f ? durationOverride : definition.DefaultDuration;
        float magnitude = float.IsNaN(magnitudeOverride) ? definition.Magnitude : magnitudeOverride;

        ActiveEffect existing = Find(definition);
        if (existing == null)
        {
            _active.Add(new ActiveEffect { Definition = definition, Remaining = duration, Magnitude = magnitude });
        }
        else
        {
            switch (definition.StackPolicy)
            {
                case StatusStackPolicy.ExtendDuration:
                    existing.Remaining += duration;
                    break;
                case StatusStackPolicy.StackMagnitude:
                    existing.Magnitude += magnitude;
                    existing.Remaining = Mathf.Max(existing.Remaining, duration);
                    break;
                default: // RefreshDuration
                    existing.Remaining = Mathf.Max(existing.Remaining, duration);
                    break;
            }
        }

        Changed?.Invoke();
    }

    public bool IsActive(StatusEffectDefinition definition) => Find(definition) != null;

    public bool IsActive(StatusEffectKind kind)
    {
        for (int i = 0; i < _active.Count; i++)
        {
            if (_active[i].Definition.Kind == kind) return true;
        }
        return false;
    }

    public float GetRemaining(StatusEffectDefinition definition)
    {
        ActiveEffect effect = Find(definition);
        return effect != null ? effect.Remaining : 0f;
    }

    /// <summary>Product of all active fall-speed multipliers (1 when none).</summary>
    public float GetFallSpeedFactor()
    {
        float factor = 1f;
        for (int i = 0; i < _active.Count; i++)
        {
            if (_active[i].Definition.Kind == StatusEffectKind.FallSpeedMultiplier)
            {
                factor *= _active[i].Magnitude;
            }
        }
        return factor;
    }

    /// <summary>Sum of all active per-grant score bonuses (0 when none).</summary>
    public int ExtraScorePerBlock
    {
        get
        {
            float bonus = 0f;
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].Definition.Kind == StatusEffectKind.ScorePerBlockBonus)
                {
                    bonus += _active[i].Magnitude;
                }
            }
            return Mathf.RoundToInt(bonus);
        }
    }

    private void Update()
    {
        if (_active.Count == 0) return;

        bool expired = false;
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            _active[i].Remaining -= Time.deltaTime; // scaled: pauses freeze states for free
            if (_active[i].Remaining <= 0f)
            {
                _active.RemoveAt(i);
                expired = true;
            }
        }

        if (expired) Changed?.Invoke();
    }

    private ActiveEffect Find(StatusEffectDefinition definition)
    {
        for (int i = 0; i < _active.Count; i++)
        {
            if (_active[i].Definition == definition) return _active[i];
        }
        return null;
    }
}
