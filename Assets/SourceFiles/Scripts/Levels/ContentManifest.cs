using UnityEngine;

/// <summary>
/// Build-safe snapshot of every ability/block the Custom Game testing screen offers. The editor
/// discovers content live via AssetDatabase (see <see cref="ContentCatalog"/>); a player build has
/// no AssetDatabase, so a build preprocessor (ContentManifestBuilder, Editor-only) writes this
/// asset under Resources before each build and ContentCatalog reads it at runtime. It is regenerated
/// automatically at build time — there is no list to hand-maintain (the CUSTOMGAME.md rule holds).
/// </summary>
public class ContentManifest : ScriptableObject
{
    [SerializeField] private AbilityDefinition[] abilities = System.Array.Empty<AbilityDefinition>();
    [SerializeField] private BlockDefinition[] blocks = System.Array.Empty<BlockDefinition>();
    [SerializeField] private BlockData[] variants = System.Array.Empty<BlockData>();
    [SerializeField] private AbilityRarityProfile equalRarityProfile;

    public AbilityDefinition[] Abilities => abilities ?? System.Array.Empty<AbilityDefinition>();
    public BlockDefinition[] Blocks => blocks ?? System.Array.Empty<BlockDefinition>();
    public BlockData[] Variants => variants ?? System.Array.Empty<BlockData>();
    public AbilityRarityProfile EqualRarityProfile => equalRarityProfile;

#if UNITY_EDITOR
    /// <summary>Editor/build-time only: overwrite the snapshot from live AssetDatabase discovery.</summary>
    public void EditorPopulate(AbilityDefinition[] discoveredAbilities, BlockDefinition[] discoveredBlocks,
        BlockData[] discoveredVariants, AbilityRarityProfile profile)
    {
        abilities = discoveredAbilities ?? System.Array.Empty<AbilityDefinition>();
        blocks = discoveredBlocks ?? System.Array.Empty<BlockDefinition>();
        variants = discoveredVariants ?? System.Array.Empty<BlockData>();
        equalRarityProfile = profile;
    }
#endif
}
