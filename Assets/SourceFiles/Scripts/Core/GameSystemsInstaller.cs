using UnityEngine;

/// <summary>
/// Installs the ability/UI system stack onto the GameManager's GameObject. Keeping the roster here
/// (instead of a per-system if-add ladder inlined in GameManager.Awake) means a new subsystem is one
/// line in one list, and GameManager stays an orchestrator rather than knowing every system by name.
///
/// Order matters and is preserved: StatusEffects and AbilityRuntime must exist BEFORE the systems
/// that resolve them via GetComponent in their own Awake (ComboDetector, AbilityHud,
/// AbilityChoiceController, ...). Each component is added only if absent, so re-entry is safe.
/// </summary>
public static class GameSystemsInstaller
{
    public static void Install(GameObject host)
    {
        if (host == null) return;

        Ensure<StatusEffects>(host);          // status state + the runtime are resolved by the rest,
        Ensure<AbilityRuntime>(host);         // so they go first
        Ensure<RunSuppliesApplier>(host);     // consumes the pre-run loadout (needs AbilityRuntime for Stocked boosts)
        Ensure<ComboDetector>(host);
        Ensure<StatusFieldController>(host);
        Ensure<HoldCache>(host);
        Ensure<AbilityHud>(host);
        Ensure<ArmedAbilityHud>(host);    // left-edge readout of one-shot passives still holding a charge
        Ensure<HoldButton>(host);
        Ensure<AbilityChoiceController>(host);
        Ensure<BlockDiscoveryController>(host); // brick debut modals + Vault discovery marking
        Ensure<PauseMenuController>(host);
        Ensure<LevelRuntimeController>(host);
        Ensure<CoinLedger>(host);         // JUICE.md Phase 3: run-coin accounting + banking
        Ensure<PlacementScout>(host);     // silent perfect-stack/golden detector (mints coins)
        Ensure<GoldenBlockDirector>(host);// the scheduled golden brick (economy metronome)
        Ensure<CoinHud>(host);            // the coin flight + counter pill (the ONLY celebration)
        Ensure<WaveHud>(host);            // puzzle-wave countdown pill (top-right, under lives)
    }

    // == null (not ??) on purpose: the editor's fake-null wrapper passes a reference-null check and
    // would silently skip the AddComponent.
    private static void Ensure<T>(GameObject host) where T : Component
    {
        if (host.GetComponent<T>() == null) host.AddComponent<T>();
    }
}
