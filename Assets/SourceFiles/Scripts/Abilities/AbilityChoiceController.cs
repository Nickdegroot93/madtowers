using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Archero-style ability choices: every N placed blocks (per GameModeConfig) the game
/// fully pauses and the player picks one of three rarity-weighted abilities. Added to
/// the GameManager's object at runtime; the UI is built in code like LevelSelectRuntimeMenu.
///
/// Offers are RECORDED on the score event but PRESENTED from Update only when nothing
/// more important is happening (win verification, pauses, game over) - see Update.
/// Milestone detection is crossing-based, not modulo: abilities may grant bonus score
/// (Overdrive-style states), and a +2 jump must not hop over an earned offer.
///
/// Pick routing by kind: Instant applies immediately; Consumable goes to a slot (or the
/// swap dialog when both are full - resolved before the game unpauses); Passive/Combo
/// are acquired into the AbilityRuntime inventory.
/// </summary>
public class AbilityChoiceController : MonoBehaviour
{
    private const int ChoiceCount = 3;

    private readonly List<AbilityDefinition> _rollBuffer = new List<AbilityDefinition>();
    private GameObject _panelRoot;
    private AbilityRuntime _runtime;
    private int _lastHandledScore;
    private bool _offerPending;

    private void Awake()
    {
        _runtime = GetComponent<AbilityRuntime>();
    }

    private void OnEnable()
    {
        GameEvents.ScoreChanged += HandleScoreChanged;
    }

    private void OnDisable()
    {
        GameEvents.ScoreChanged -= HandleScoreChanged;
        CloseChoicePanel();
    }

    private void HandleScoreChanged(int score)
    {
        if (GameManager.Instance == null || GameManager.Instance.isGameOver) return;
        if (_panelRoot != null || score <= 0 || score <= _lastHandledScore) return;

        GameModeConfig config = GameManager.Instance.ActiveConfig;
        if (config == null || config.PowerUpChoiceEveryBlocks <= 0) return;

        // Crossing-based: did this score change pass a milestone? (score can jump by
        // more than 1 under a ScorePerBlockBonus state - modulo would skip the offer.)
        int interval = config.PowerUpChoiceEveryBlocks;
        bool crossedMilestone = score / interval > _lastHandledScore / interval;
        _lastHandledScore = score;
        if (!crossedMilestone) return;

        IReadOnlyList<AbilityDefinition> pool = config.PowerUpChoicePool;
        if (pool == null || pool.Count == 0) return;

        _offerPending = true;
    }

    private void Update()
    {
        if (!_offerPending || _panelRoot != null) return;
        if (GameManager.Instance == null) return;

        if (GameManager.Instance.isGameOver)
        {
            _offerPending = false; // the run ended before the reward could be presented
            return;
        }

        // Wait out the win-verification countdown and any other full-screen pause
        // (level-complete panel, pause menu) - the offer keeps, it doesn't vanish.
        if (LevelRuntimeController.IsVerifyingWin || GameManager.Instance.IsGamePaused) return;

        GameModeConfig config = GameManager.Instance.ActiveConfig;
        IReadOnlyList<AbilityDefinition> pool = config != null ? config.PowerUpChoicePool : null;
        if (pool == null || pool.Count == 0)
        {
            _offerPending = false;
            return;
        }

        _offerPending = false;
        RollChoices(pool);
        if (_rollBuffer.Count == 0) return; // every candidate filtered out: offer quietly skipped

        GameManager.Instance.SetGamePaused(true);
        RuntimeUiKit.EnsureEventSystem();
        BuildChoicePanel();
    }

    // Weighted sample without replacement over the AVAILABLE pool: unique-and-owned,
    // stack-capped, level-banned and condition-failing abilities never reach the roll.
    private void RollChoices(IReadOnlyList<AbilityDefinition> pool)
    {
        _rollBuffer.Clear();
        AbilityContext context = _runtime != null ? _runtime.Context : null;

        List<AbilityDefinition> candidates = new List<AbilityDefinition>(pool.Count);
        for (int i = 0; i < pool.Count; i++)
        {
            AbilityDefinition ability = pool[i];
            if (ability == null) continue;
            if (context != null && _runtime != null &&
                !ability.IsAvailable(context, _runtime.GetOwnedStacks(ability))) continue;
            candidates.Add(ability);
        }

        while (_rollBuffer.Count < ChoiceCount && candidates.Count > 0)
        {
            int totalWeight = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                totalWeight += AbilityRarityInfo.GetRollWeight(candidates[i].Rarity);
            }

            int roll = Random.Range(0, totalWeight);
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= AbilityRarityInfo.GetRollWeight(candidates[i].Rarity);
                if (roll < 0)
                {
                    _rollBuffer.Add(candidates[i]);
                    candidates.RemoveAt(i);
                    break;
                }
            }
        }
    }

    private void Pick(AbilityDefinition definition)
    {
        switch (definition)
        {
            case InstantAbility instant:
                // Clone-apply-discard: even instants follow the state rule, so an
                // author adding instance fields can never leak state into the asset.
                InstantAbility clone = Instantiate(instant);
                clone.Apply(_runtime.Context);
                Destroy(clone);
                break;

            case ConsumableAbility consumable:
                if (!_runtime.TryAddConsumable(consumable))
                {
                    ShowSwapDialog(consumable); // stays paused until resolved
                    return;
                }
                break;

            default: // PassiveAbility, ComboAbility
                _runtime.AcquirePassive(definition);
                break;
        }

        CloseAndResume();
    }

    private void CloseAndResume()
    {
        CloseChoicePanel();
        if (GameManager.Instance != null)
        {
            GameManager.Instance.SetGamePaused(false);
        }
    }

    private void CloseChoicePanel()
    {
        if (_panelRoot == null) return;

        Destroy(_panelRoot);
        _panelRoot = null;
    }

    // ---- Runtime UI ---------------------------------------------------------------------------

    private void BuildChoicePanel()
    {
        _panelRoot = RuntimeUiKit.CreateModal("Ability Choice", 6000);

        GameObject panel = RuntimeUiKit.CreateCenteredPanel(
            _panelRoot.transform, new Vector2(1000f, 760f), drawBackground: false);

        RuntimeUiKit.CreateLabel(panel.transform, "Choose a Power-Up", 56, 90f, FontStyle.Bold,
            RuntimeUiKit.TitleColor);

        GameObject cardRow = new GameObject("Cards");
        cardRow.transform.SetParent(panel.transform, false);
        LayoutElement rowElement = cardRow.AddComponent<LayoutElement>();
        rowElement.preferredHeight = 560f;

        HorizontalLayoutGroup rowLayout = cardRow.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 24f;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = true;

        for (int i = 0; i < _rollBuffer.Count; i++)
        {
            CreateCard(cardRow.transform, _rollBuffer[i]);
        }
    }

    private void CreateCard(Transform parent, AbilityDefinition definition)
    {
        Color rarityColor = AbilityRarityInfo.GetColor(definition.Rarity);

        GameObject cardObject = new GameObject(definition.DisplayName);
        cardObject.transform.SetParent(parent, false);

        Image frame = cardObject.AddComponent<Image>();
        frame.color = Color.Lerp(new Color(0.09f, 0.12f, 0.14f, 1f), rarityColor, 0.18f);

        Button button = cardObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.colorMultiplier = 1f;
        button.colors = colors;
        button.targetGraphic = frame;

        VerticalLayoutGroup cardLayout = cardObject.AddComponent<VerticalLayoutGroup>();
        cardLayout.padding = new RectOffset(20, 20, 26, 26);
        cardLayout.spacing = 16f;
        cardLayout.childAlignment = TextAnchor.UpperCenter;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = false;
        cardLayout.childForceExpandWidth = true;
        cardLayout.childForceExpandHeight = false;

        AbilityType type = definition.Type;
        RuntimeUiKit.CreateLabel(cardObject.transform, AbilityTypeInfo.GetLabel(type),
            22, 28f, FontStyle.Bold, AbilityTypeInfo.GetColor(type), TextAnchor.UpperCenter);
        RuntimeUiKit.CreateLabel(cardObject.transform, definition.Rarity.ToString().ToUpperInvariant(),
            26, 36f, FontStyle.Bold, rarityColor, TextAnchor.UpperCenter);

        if (definition.Icon != null)
        {
            GameObject iconObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconObject.transform.SetParent(cardObject.transform, false);
            Image icon = iconObject.GetComponent<Image>();
            icon.sprite = definition.Icon;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            LayoutElement iconLayout = iconObject.AddComponent<LayoutElement>();
            iconLayout.preferredHeight = 120f;
        }

        RuntimeUiKit.CreateLabel(cardObject.transform, definition.DisplayName,
            38, 110f, FontStyle.Bold, Color.white, TextAnchor.UpperCenter);

        int stacks = _runtime != null ? _runtime.GetOwnedStacks(definition) : 0;
        if (stacks > 0)
        {
            RuntimeUiKit.CreateLabel(cardObject.transform, $"Owned ×{stacks}",
                24, 32f, FontStyle.Bold, new Color(0.6f, 0.9f, 0.65f, 1f), TextAnchor.UpperCenter);
        }

        RuntimeUiKit.CreateLabel(cardObject.transform, definition.ShortDescription,
            27, 200f, FontStyle.Normal, RuntimeUiKit.BodyTextColor, TextAnchor.UpperCenter);

        // Nested button: UGUI raycasts stop at the inner target, so tapping Details
        // never also picks the card.
        RuntimeUiKit.CreateButton(cardObject.transform, "Details", 48f, () => ShowDetailPanel(definition));

        AbilityDefinition picked = definition;
        button.onClick.AddListener(() => Pick(picked));
    }

    // The "See details" view: full presentation block (type, rarity, icon, title, LONG
    // description) with Choose/Back. The roll buffer is untouched, so Back rebuilds the
    // same three cards - no reroll. Future home of the explainer video.
    private void ShowDetailPanel(AbilityDefinition definition)
    {
        CloseChoicePanel();

        _panelRoot = RuntimeUiKit.CreateModal("Ability Details", 6000);
        GameObject panel = RuntimeUiKit.CreateCenteredPanel(_panelRoot.transform, new Vector2(680f, 720f));

        AbilityType type = definition.Type;
        RuntimeUiKit.CreateLabel(panel.transform,
            $"{AbilityTypeInfo.GetLabel(type)}  ·  {definition.Rarity.ToString().ToUpperInvariant()}",
            24, 34f, FontStyle.Bold, AbilityTypeInfo.GetColor(type));

        if (definition.Icon != null)
        {
            GameObject iconObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconObject.transform.SetParent(panel.transform, false);
            Image icon = iconObject.GetComponent<Image>();
            icon.sprite = definition.Icon;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            LayoutElement iconLayout = iconObject.AddComponent<LayoutElement>();
            iconLayout.preferredHeight = 150f;
        }

        RuntimeUiKit.CreateLabel(panel.transform, definition.DisplayName, 44, 64f,
            FontStyle.Bold, RuntimeUiKit.TitleColor);
        RuntimeUiKit.CreateLabel(panel.transform, definition.LongDescription, 27, 280f,
            FontStyle.Normal, RuntimeUiKit.BodyTextColor, TextAnchor.UpperCenter);

        RuntimeUiKit.CreateButton(panel.transform, $"Choose {definition.DisplayName}", 80f, () => Pick(definition));
        RuntimeUiKit.CreateButton(panel.transform, "Back", 70f, () =>
        {
            CloseChoicePanel();
            BuildChoicePanel();
        });
    }

    // Both slots are full: the player chooses what the new consumable replaces (or
    // discards it). The game STAYS paused until this resolves - the swap is part of the
    // same offer, not a second decision the tower keeps falling under.
    private void ShowSwapDialog(ConsumableAbility incoming)
    {
        CloseChoicePanel();

        _panelRoot = RuntimeUiKit.CreateModal("Ability Swap", 6000);

        GameObject panel = RuntimeUiKit.CreateCenteredPanel(_panelRoot.transform, new Vector2(640f, 520f));

        RuntimeUiKit.CreateLabel(panel.transform, "Slots are full", 44, 70f, FontStyle.Bold,
            RuntimeUiKit.TitleColor);
        RuntimeUiKit.CreateLabel(panel.transform, $"Swap in {incoming.DisplayName}?", 30, 50f,
            FontStyle.Normal, RuntimeUiKit.BodyTextColor);
        RuntimeUiKit.CreateLabel(panel.transform, incoming.ShortDescription, 24, 60f,
            FontStyle.Italic, RuntimeUiKit.BodyTextColor);

        for (int i = 0; i < AbilityRuntime.ConsumableSlotCount; i++)
        {
            int slot = i;
            ConsumableAbility current = _runtime.GetSlotSource(i);
            string label = current != null ? $"Replace {current.DisplayName}" : $"Use slot {i + 1}";
            RuntimeUiKit.CreateButton(panel.transform, label, 80f, () =>
            {
                _runtime.ReplaceConsumable(slot, incoming);
                CloseAndResume();
            });
        }

        RuntimeUiKit.CreateButton(panel.transform, $"Discard {incoming.DisplayName}", 80f, CloseAndResume);
    }
}
