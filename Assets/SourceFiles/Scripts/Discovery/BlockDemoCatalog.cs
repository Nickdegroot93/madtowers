using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The data-driven demo table (BLOCKPREVIEWS.md): one entry per brick behaviour, keyed by the
/// variant asset's name (the same stable id ProgressStore persists). An entry = the scripted
/// scenario that plays in the diorama plus a fallback caption for the debut modal (the authored
/// Vault copy on the BlockData asset wins when present). Kept in CODE, like TutorialModifier's
/// step table, so the demo of a brick can never drift from a stale asset.
///
/// A variant with no entry (Normal, or a future brick before its demo is authored) simply gets
/// no debut modal - it is marked discovered silently on first spawn.
/// </summary>
public static class BlockDemoCatalog
{
    public sealed class Entry
    {
        public readonly System.Func<BlockDemoStage, IEnumerator> Scenario;
        public readonly string FallbackCaption;

        public Entry(System.Func<BlockDemoStage, IEnumerator> scenario, string fallbackCaption)
        {
            Scenario = scenario;
            FallbackCaption = fallbackCaption;
        }
    }

    private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>
    {
        ["Boulder"] = new Entry(BlockDemoScenarios.Boulder,
            "Four times the weight of a normal brick. It lands hard and strains whatever carries it."),
        ["Anchor"] = new Entry(BlockDemoScenarios.Anchor,
            "Freezes solid the instant it lands - even hanging over an edge. Build on it like ground."),
        ["Vine"] = new Entry(BlockDemoScenarios.Vine,
            "Welds itself to every brick it touches. A vine across a gap turns loose bricks into one."),
        ["Magma"] = new Entry(BlockDemoScenarios.Magma,
            "Melts where it lands, settling into every hollow beneath it before cooling to stone."),
        ["Bomb"] = new Entry(BlockDemoScenarios.Bomb,
            "The fuse starts on landing. When it blows, it takes every touching brick with it."),
        ["Ice"] = new Entry(BlockDemoScenarios.Ice,
            "Frozen slick. It slides off anything that isn't level - place it flat or lose it."),
        ["Vortex"] = new Entry(BlockDemoScenarios.Vortex,
            "Steering is reversed while it falls: push left and it goes right. Watch the swirl."),
        ["Locked"] = new Entry(BlockDemoScenarios.Locked,
            "Chained shut - it cannot rotate. The shape it spawns in is the shape you place."),
        ["Feather"] = new Entry(BlockDemoScenarios.Feather,
            "Almost weightless. Anything landing nearby can shove it right out of position."),
        ["Tremor"] = new Entry(BlockDemoScenarios.Tremor,
            "Shakes the whole tower when it lands. Sloppy placements will not survive the jolt."),
        ["Maw"] = new Entry(BlockDemoScenarios.Maw,
            "It devours any brick placed on top of it - and every meal costs you a life. Build around the mouth, never on it."),
        ["Sandstone"] = new Entry(BlockDemoScenarios.Sandstone,
            "It cracks under the weight it carries - watch the fractures grow with every brick. The third one is one too many."),
        ["Pyramid"] = new Entry(BlockDemoScenarios.Pyramid,
            "No flat top - whatever lands on its slopes slides away. It stacks proudly on anything; build beside it, never on it."),
        ["Curse"] = new Entry(BlockDemoScenarios.Curse,
            "Bury it. While its sigils burn in the open, every brick you place costs it one - at zero it takes a life and starts counting again."),
    };

    public static bool HasDemo(BlockData variant) =>
        variant != null && Entries.ContainsKey(ProgressStore.BlockId(variant));

    public static string Caption(BlockData variant) =>
        variant != null && Entries.TryGetValue(ProgressStore.BlockId(variant), out Entry entry)
            ? entry.FallbackCaption
            : string.Empty;

    /// <summary>A fresh scenario enumerator for the stage's loop; null when the variant has no demo.</summary>
    public static IEnumerator CreateScenario(BlockData variant, BlockDemoStage stage)
    {
        if (variant == null || stage == null) return null;
        return Entries.TryGetValue(ProgressStore.BlockId(variant), out Entry entry)
            ? entry.Scenario(stage)
            : null;
    }
}
