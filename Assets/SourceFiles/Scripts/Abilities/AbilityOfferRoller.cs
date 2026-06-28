using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Headless offer-selection policy for the ability picker. UI code owns when to show the offer;
/// this class owns which ability definitions are eligible and how rarity-weighted offers roll.
/// </summary>
public sealed class AbilityOfferRoller
{
    private readonly List<AbilityDefinition> _results = new List<AbilityDefinition>();

    public IReadOnlyList<AbilityDefinition> Roll(
        IReadOnlyList<AbilityDefinition> pool,
        AbilityRuntime runtime,
        int choiceCount)
    {
        _results.Clear();
        if (pool == null || runtime == null || choiceCount <= 0) return _results;

        AbilityContext context = runtime.Context;
        if (context == null) return _results;

        int rarityCount = Enum.GetValues(typeof(AbilityRarity)).Length;
        List<AbilityDefinition>[] byRarity = new List<AbilityDefinition>[rarityCount];
        for (int r = 0; r < rarityCount; r++) byRarity[r] = new List<AbilityDefinition>();

        for (int i = 0; i < pool.Count; i++)
        {
            AbilityDefinition ability = pool[i];
            if (ability == null) continue;
            if (!ability.IsAvailable(context, runtime.GetOwnedStacks(ability))) continue;
            byRarity[(int)ability.Rarity].Add(ability);
        }

        int chosen = ChooseRarity(byRarity, context);
        if (chosen < 0) return _results;

        List<AbilityDefinition> candidates = byRarity[chosen];
        while (_results.Count < choiceCount && candidates.Count > 0)
        {
            int pick = UnityEngine.Random.Range(0, candidates.Count);
            _results.Add(candidates[pick]);
            candidates.RemoveAt(pick);
        }

        return _results;
    }

    private static int ChooseRarity(List<AbilityDefinition>[] byRarity, AbilityContext context)
    {
        RarityWeightStage stage = AbilityRarityProfile.Resolve(
            context.Level != null ? context.Level.AbilityRarityProfile : null,
            GetRunProgress(context));

        float totalWeight = 0f;
        for (int r = 0; r < byRarity.Length; r++)
        {
            if (byRarity[r].Count > 0) totalWeight += stage.GetWeight((AbilityRarity)r);
        }

        if (totalWeight > 0f)
        {
            float roll = UnityEngine.Random.Range(0f, totalWeight);
            for (int r = 0; r < byRarity.Length; r++)
            {
                if (byRarity[r].Count == 0) continue;
                roll -= stage.GetWeight((AbilityRarity)r);
                if (roll < 0f) return r;
            }

            // Random.Range's float upper bound is inclusive, so exactly-totalWeight can miss.
            for (int r = byRarity.Length - 1; r >= 0; r--)
            {
                if (byRarity[r].Count > 0 && stage.GetWeight((AbilityRarity)r) > 0f) return r;
            }

            return -1;
        }

        // All remaining candidates sit in zero-weight rarities. An earned offer should not starve:
        // fall back to a uniform pick among rarities that still have candidates.
        int options = 0;
        for (int r = 0; r < byRarity.Length; r++) if (byRarity[r].Count > 0) options++;
        if (options == 0) return -1;

        int pickIndex = UnityEngine.Random.Range(0, options);
        for (int r = 0; r < byRarity.Length; r++)
        {
            if (byRarity[r].Count == 0) continue;
            if (pickIndex-- == 0) return r;
        }

        return -1;
    }

    private static float GetRunProgress(AbilityContext context)
    {
        if (context.Level == null || context.GameManager == null) return 0f;
        return context.Level.WinCondition.RunProgress01(context.GameManager);
    }
}
