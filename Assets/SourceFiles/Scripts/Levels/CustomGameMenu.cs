using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The Custom Game setup screen: replaces the hand-made test levels. Every knob is curated from
/// a chosen preset (GameModeConfig) and editable here; abilities and blocks are auto-discovered
/// (ContentCatalog), so new content appears with no code changes. On Start it builds a throwaway
/// runtime GameModeConfig + LevelDefinition and launches like any level. Editor-only content
/// (AssetDatabase) - see CUSTOMGAME.md. Settings persist for the session so iterating is quick.
/// </summary>
public static class CustomGameMenu
{
    private static GameObject _root;
    private static CustomGameSettings _settings;
    private static GameModeConfig[] _presets;
    private static int _presetIndex;
    private static Action _onBack;

    private static readonly Color SectionColor = new Color(0.62f, 0.7f, 0.78f, 1f);

    public static void Show(Action onBack)
    {
        _onBack = onBack;
        EnsureState();
        Build();
    }

    private static void EnsureState()
    {
        if (_presets == null || _presets.Length == 0)
        {
            _presets = Resources.LoadAll<GameModeConfig>("GameModes") ?? Array.Empty<GameModeConfig>();
            Array.Sort(_presets, (a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
            _presetIndex = Mathf.Max(0, Array.FindIndex(_presets, p => p != null && p.name.Contains("Classic")));
        }

        if (_settings != null) return;

        GameModeConfig basePreset = CurrentPreset();
        _settings = CustomGameSettings.FromConfig(basePreset);

        // Default content: the preset's block bag on; every ability on (best for testing).
        if (basePreset != null && basePreset.BlockBag != null)
        {
            foreach (BlockDefinition b in basePreset.BlockBag)
                if (b != null) _settings.EnabledBlocks.Add(b);
        }
        foreach (AbilityDefinition a in ContentCatalog.AllAbilities()) _settings.EnabledAbilities.Add(a);
    }

    private static GameModeConfig CurrentPreset() =>
        _presets != null && _presets.Length > 0 ? _presets[Mathf.Clamp(_presetIndex, 0, _presets.Length - 1)] : null;

    // Re-seed only the numeric rules from a preset; keep the player's block/ability picks.
    private static void ApplyPreset(int index)
    {
        _presetIndex = Mathf.Clamp(index, 0, _presets.Length - 1);
        var keptBlocks = new HashSet<BlockDefinition>(_settings.EnabledBlocks);
        var keptAbilities = new HashSet<AbilityDefinition>(_settings.EnabledAbilities);
        _settings = CustomGameSettings.FromConfig(CurrentPreset());
        _settings.EnabledBlocks.UnionWith(keptBlocks);
        _settings.EnabledAbilities.UnionWith(keptAbilities);
        Build();
    }

    private static void Build()
    {
        if (_root != null) UnityEngine.Object.Destroy(_root);
        _root = RuntimeUiKit.CreateOverlayCanvas("Custom Game", 5000);
        RuntimeUiKit.CreateBackdrop(_root.transform, new Color(0.04f, 0.06f, 0.08f, 0.98f));

        // Stretch the scroll panel to (nearly) the full screen so it fits any phone width - a
        // fixed-width centred panel overflowed once the canvas scaler scaled up on tall screens.
        GameObject scroll = RuntimeUiKit.CreateScrollColumn(_root.transform, new Vector2(100f, 100f), out Transform panel);

        // Stretch the scroll viewport to the screen (minus a small margin).
        RectTransform pr = (RectTransform)scroll.transform;
        pr.anchorMin = Vector2.zero;
        pr.anchorMax = Vector2.one;
        pr.offsetMin = new Vector2(24f, 24f);
        pr.offsetMax = new Vector2(-24f, -24f);

        // Nail the content column to the viewport width with ZERO horizontal inset. A fresh
        // RectTransform's default sizeDelta left the content wider than the panel and centred, so
        // its left edge fell outside the mask and clipped the start of every left-aligned label.
        RectTransform content = (RectTransform)panel;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = new Vector2(0f, content.offsetMin.y);
        content.offsetMax = new Vector2(0f, content.offsetMax.y);

        VerticalLayoutGroup vlg = panel.GetComponent<VerticalLayoutGroup>();
        if (vlg != null)
        {
            vlg.spacing = 8f;
            vlg.padding = new RectOffset(26, 26, 22, 22);
        }

        RuntimeUiKit.CreateLabel(panel, "Custom Game", 44, 72f, FontStyle.Bold, RuntimeUiKit.TitleColor);

        Button start = RuntimeUiKit.CreateButton(panel, "▶  Start", 84f, StartGame);
        Text startLabel = start.GetComponentInChildren<Text>();
        if (startLabel != null) startLabel.color = new Color(0.7f, 1f, 0.85f, 1f);

        if (_presets.Length > 0)
        {
            string[] names = Array.ConvertAll(_presets, p => p != null ? p.name.Replace("GameMode_", "") : "?");
            RuntimeUiKit.CreateCycleRow(panel, "Preset (base)", names, _presetIndex, ApplyPreset);
        }

        BuildGoalSection(panel);
        BuildRoundSection(panel);
        BuildPlayAreaSection(panel);
        BuildSpawningSection(panel);
        BuildBlocksSection(panel);
        BuildAbilitiesSection(panel);

        Spacer(panel);
        RuntimeUiKit.CreateButton(panel, "< Back", 64f, () =>
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _onBack?.Invoke();
        });
    }

    private static void BuildGoalSection(Transform panel)
    {
        Header(panel, "Goal");
        string[] goals = { "Endless", "Place Blocks", "Reach Height" };
        RuntimeUiKit.CreateCycleRow(panel, "Win by", goals, (int)_settings.TargetType,
            i => _settings.TargetType = (LevelTargetType)i);
        RuntimeUiKit.CreateStepperRow(panel, "Target (blocks / meters)", _settings.TargetValue, 1f, 999f, 5f, "0",
            v => _settings.TargetValue = v);
    }

    private static void BuildRoundSection(Transform panel)
    {
        Header(panel, "Round");
        RuntimeUiKit.CreateStepperRow(panel, "Starting lives", _settings.StartingLives, 0f, 20f, 1f, "0",
            v => _settings.StartingLives = Mathf.RoundToInt(v));
        RuntimeUiKit.CreateStepperRow(panel, "Initial fall speed", _settings.InitialFallSpeed, 0.5f, 12f, 0.5f, "0.0",
            v => _settings.InitialFallSpeed = v);
        RuntimeUiKit.CreateStepperRow(panel, "Max fall speed", _settings.MaxFallSpeed, 1f, 20f, 0.5f, "0.0",
            v => _settings.MaxFallSpeed = v);

        string[] modes = { "None", "Per Block", "Over Time" };
        RuntimeUiKit.CreateCycleRow(panel, "Difficulty ramp", modes, (int)_settings.DifficultyScalingMode,
            i => _settings.DifficultyScalingMode = (DifficultyScalingMode)i);
        RuntimeUiKit.CreateStepperRow(panel, "  + speed / block", _settings.SpeedIncreasePerBlock, 0f, 2f, 0.05f, "0.00",
            v => _settings.SpeedIncreasePerBlock = v);
        RuntimeUiKit.CreateStepperRow(panel, "  time step (sec)", _settings.SpeedIncreaseIntervalSeconds, 5f, 300f, 5f, "0",
            v => _settings.SpeedIncreaseIntervalSeconds = v);
        RuntimeUiKit.CreateStepperRow(panel, "  + speed / step", _settings.SpeedIncreasePerInterval, 0f, 2f, 0.05f, "0.00",
            v => _settings.SpeedIncreasePerInterval = v);
    }

    private static void BuildPlayAreaSection(Transform panel)
    {
        Header(panel, "Play Area");
        RuntimeUiKit.CreateStepperRow(panel, "Floor width (columns)", _settings.FloorColumns, 1f, 21f, 1f, "0",
            v => _settings.FloorColumns = Mathf.RoundToInt(v));
    }

    private static void BuildSpawningSection(Transform panel)
    {
        Header(panel, "Spawning");
        RuntimeUiKit.CreateStepperRow(panel, "Spawn delay (sec)", _settings.SpawnDelay, 0f, 3f, 0.1f, "0.0",
            v => _settings.SpawnDelay = v);
        RuntimeUiKit.CreateStepperRow(panel, "Power-up every N blocks (0=off)", _settings.PowerUpChoiceEveryBlocks,
            0f, 50f, 1f, "0", v => _settings.PowerUpChoiceEveryBlocks = Mathf.RoundToInt(v));
        RuntimeUiKit.CreateToggleRow(panel, "Static support islands", _settings.StaticIslandsEnabled,
            v => _settings.StaticIslandsEnabled = v);
        RuntimeUiKit.CreateStepperRow(panel, "  island spawn chance", _settings.StaticIslandSpawnChance, 0f, 1f, 0.05f, "0.00",
            v => _settings.StaticIslandSpawnChance = v);
    }

    private static void BuildBlocksSection(Transform panel)
    {
        Header(panel, "Blocks");
        List<BlockDefinition> blocks = ContentCatalog.AllBlocks();
        if (blocks.Count == 0)
        {
            RuntimeUiKit.CreateLabel(panel, EditorOnlyNote(), 22, 60f, FontStyle.Italic, SectionColor);
            return;
        }
        foreach (BlockDefinition block in blocks)
        {
            BlockDefinition captured = block;
            RuntimeUiKit.CreateToggleRow(panel, block.DisplayName, _settings.EnabledBlocks.Contains(block),
                on => { if (on) _settings.EnabledBlocks.Add(captured); else _settings.EnabledBlocks.Remove(captured); });
        }
    }

    private static void BuildAbilitiesSection(Transform panel)
    {
        Header(panel, "Abilities");
        List<AbilityDefinition> abilities = ContentCatalog.AllAbilities();
        if (abilities.Count == 0)
        {
            RuntimeUiKit.CreateLabel(panel, EditorOnlyNote(), 22, 60f, FontStyle.Italic, SectionColor);
            return;
        }

        AbilityRarity? group = null;
        foreach (AbilityDefinition ability in abilities)
        {
            if (group == null || ability.Rarity != group.Value)
            {
                group = ability.Rarity;
                BuildRarityHeader(panel, group.Value, abilities);
            }
            AbilityDefinition captured = ability;
            RuntimeUiKit.CreateToggleRow(panel, ability.DisplayName, _settings.EnabledAbilities.Contains(ability),
                on => { if (on) _settings.EnabledAbilities.Add(captured); else _settings.EnabledAbilities.Remove(captured); });
        }
    }

    // Rarity sub-header with All / None shortcuts for that rarity (rebuilds to refresh checkboxes).
    private static void BuildRarityHeader(Transform panel, AbilityRarity rarity, List<AbilityDefinition> all)
    {
        GameObject row = new GameObject("RarityHeader", typeof(RectTransform));
        row.transform.SetParent(panel, false);
        row.AddComponent<LayoutElement>().preferredHeight = 50f;
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = true;

        Text label = RuntimeUiKit.CreateLabel(row.transform, rarity.ToString(), 26, 50f, FontStyle.Bold,
            RuntimeUiKit.TitleColor, TextAnchor.MiddleLeft);
        label.GetComponent<LayoutElement>().flexibleWidth = 1f;

        SmallButton(row.transform, "All", () => SetRarity(all, rarity, true));
        SmallButton(row.transform, "None", () => SetRarity(all, rarity, false));
    }

    private static void SetRarity(List<AbilityDefinition> all, AbilityRarity rarity, bool on)
    {
        foreach (AbilityDefinition a in all)
        {
            if (a.Rarity != rarity) continue;
            if (on) _settings.EnabledAbilities.Add(a); else _settings.EnabledAbilities.Remove(a);
        }
        Build();
    }

    private static void StartGame()
    {
        GameModeConfig preset = CurrentPreset();
        GameModeConfig runtime = preset != null
            ? UnityEngine.Object.Instantiate(preset)
            : ScriptableObject.CreateInstance<GameModeConfig>();
        runtime.name = "CustomGameConfig";
        runtime.ApplyCustomGameOverrides(_settings);

        LevelDefinition level = LevelDefinition.CreateRuntime("Custom Game", runtime,
            _settings.TargetType, _settings.TargetValue, ContentCatalog.EqualRarityProfile());

        LevelSelectionState.SelectLevel(level);
        Time.timeScale = 1f;
        if (_root != null) UnityEngine.Object.Destroy(_root);
        _root = null;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    // ---- small helpers -------------------------------------------------------------------

    private static void Header(Transform panel, string text)
    {
        Spacer(panel);
        RuntimeUiKit.CreateLabel(panel, text.ToUpperInvariant(), 24, 40f, FontStyle.Bold, SectionColor,
            TextAnchor.MiddleLeft);
    }

    private static void Spacer(Transform panel) =>
        RuntimeUiKit.CreateLabel(panel, "", 8, 12f, FontStyle.Normal, Color.clear);

    private static string EditorOnlyNote() =>
        "Ability/block toggles are available in the Unity editor only.";

    private static void SmallButton(Transform parent, string text, UnityEngine.Events.UnityAction onClick)
    {
        Button button = RuntimeUiKit.CreateButton(parent, text, 50f, onClick);
        LayoutElement layout = button.GetComponent<LayoutElement>();
        layout.preferredWidth = 110f;
        layout.flexibleWidth = 0f;
        Text label = button.GetComponentInChildren<Text>();
        if (label != null) label.fontSize = 24;
    }
}
