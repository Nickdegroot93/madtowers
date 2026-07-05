using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Archero-style ability choices: every N placed blocks (per GameModeConfig) the game
/// fully pauses and the player picks one of three rarity-weighted abilities. Added to
/// the GameManager's object at runtime; the UI is built in code like MainMenuRuntime.
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
    private readonly AbilityOfferRoller _roller = new AbilityOfferRoller();
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
        if (GameManager.Instance != null)
        {
            GameManager.Instance.PopPause(this);
            GameManager.Instance.ReleasePhase(this);
        }
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
        if (GameManager.Instance.CurrentPhase != GamePhase.Playing || GameManager.Instance.IsGamePaused) return;

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

        GameManager.Instance.PushPause(this);
        GameManager.Instance.RequestPhase(this, GamePhase.AbilityChoice);
        RuntimeUiKit.EnsureEventSystem();
        SfxPlayer.Play("ability_offer", 0.7f, 0.04f);
        BuildChoicePanel();
    }

    // Offers are SINGLE-RARITY: the profile (per-level override or the built-in
    // progress-scaled defaults) rolls the offer's rarity among rarities that actually
    // have available candidates, then the cards sample uniformly without replacement
    // within that rarity. A mixed common/legendary offer would be a non-choice.
    private void RollChoices(IReadOnlyList<AbilityDefinition> pool)
    {
        _rollBuffer.Clear();
        _rollBuffer.AddRange(_roller.Roll(pool, _runtime, ChoiceCount));
    }

    private void Pick(AbilityDefinition definition)
    {
        SfxPlayer.Play("ability_pick", 0.8f, 0.03f);
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
            GameManager.Instance.PopPause(this);
            GameManager.Instance.ReleasePhase(this);
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
        // Showing an offer IS seeing its abilities: all three cards unlock in the Vault whether
        // or not they get picked (rerolls pass through here again, so their cards count too).
        ProgressStore.MarkAbilitiesSeen(_rollBuffer);

        _panelRoot = RuntimeUiKit.CreateModal("Ability Choice", 6000);

        GameObject panel = RuntimeUiKit.CreateCenteredPanel(
            _panelRoot.transform, new Vector2(AbilityCardView.PanelWidth, 960f), drawBackground: false);
        // The shared panel builder leaves child heights uncontrolled; this layout is
        // height-budgeted (header + cards), so LayoutElement heights must be honored.
        var panelLayout = panel.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        panelLayout.childControlHeight = true;
        panelLayout.spacing = 10f; // tighten the gap between the header and the cards
        // Framed cards are fixed-height, so the panel has slack; without this the row stretches
        // and the cards float far below the header. Keep preferred heights, center the block.
        if (AbilityCardView.HasFrameSprite) panelLayout.childForceExpandHeight = false;

        Color offerAccent = _rollBuffer.Count > 0
            ? AbilityRarityInfo.GetColor(_rollBuffer[0].Rarity)
            : RuntimeUiKit.TitleColor;
        AbilityCardView.CreateHeader(panel.transform, offerAccent);

        GameObject cardRow = new GameObject("Cards");
        cardRow.transform.SetParent(panel.transform, false);

        bool framed = AbilityCardView.HasFrameSprite;
        LayoutElement rowElement = cardRow.AddComponent<LayoutElement>();
        if (framed)
        {
            // Framed cards are fixed-aspect (no growing to fit text). Reserve the height the
            // frame art needs at the row's REAL per-card width (FramedCardWidth already accounts
            // for the panel's side padding) so the reserved row height matches the laid-out cards.
            rowElement.minHeight = rowElement.preferredHeight =
                AbilityCardView.FramedCardWidth / AbilityCardView.FrameAspectWidthOverHeight;
        }
        else
        {
            // No fixed height: the row reports the height of its TALLEST card, and
            // childForceExpandHeight (below) stretches all three to match - so a long short-
            // description lengthens every card equally. minHeight is just a floor for short text.
            rowElement.minHeight = 460f;
        }

        HorizontalLayoutGroup rowLayout = cardRow.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = AbilityCardView.CardRowSpacing;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = !framed;   // framed cards own their height (AspectRatioFitter)
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = !framed;

        for (int i = 0; i < _rollBuffer.Count; i++)
        {
            AbilityCardView.Create(cardRow.transform, _rollBuffer[i], _runtime, Pick, ShowDetailPanel);
        }

        // Cards arrive one beat apart with a soft pop - the offer feels dealt, not dumped.
        for (int i = 0; i < cardRow.transform.childCount; i++)
        {
            UiEntranceFx.Play(cardRow.transform.GetChild(i).gameObject, delay: 0.06f + i * 0.08f);
        }

        // Reroll: if the player banked rerolls (RerollPowerUp), show a button under the cards
        // that redraws this offer in place and spends one. A centered, accent-outlined pill (the
        // card Details-button look) - NOT a full-width bar. Hidden once the pool is exhausted.
        if (_runtime != null && _runtime.RerollCharges > 0)
        {
            GameObject rerollRow = new GameObject("RerollRow");
            rerollRow.transform.SetParent(panel.transform, false);
            rerollRow.AddComponent<LayoutElement>().preferredHeight = 58f;
            HorizontalLayoutGroup rerollRowLayout = rerollRow.AddComponent<HorizontalLayoutGroup>();
            rerollRowLayout.childAlignment = TextAnchor.MiddleCenter;
            rerollRowLayout.childControlWidth = true;
            rerollRowLayout.childControlHeight = true;
            rerollRowLayout.childForceExpandWidth = false;   // let the button keep its own width
            rerollRowLayout.childForceExpandHeight = false;

            Button rerollButton = RuntimeUiKit.CreateButton(
                rerollRow.transform, $"Reroll  ({_runtime.RerollCharges})", 54f, RerollCurrentOffer);
            LayoutElement rerollLayout = rerollButton.GetComponent<LayoutElement>();
            rerollLayout.preferredWidth = 260f;
            rerollLayout.flexibleWidth = 0f;
            AbilityCardView.StyleDetailsButton(rerollButton, offerAccent);
        }
    }

    // Redraw the current offer in place, spending one banked reroll. Keeps the game paused and
    // touches none of the milestone/pending state - the same close->roll->build cycle the detail
    // view's Back button uses. If the re-roll comes up empty (every candidate now filtered out)
    // it spends nothing and keeps the current cards.
    private void RerollCurrentOffer()
    {
        if (_runtime == null || _runtime.RerollCharges <= 0) return;

        GameModeConfig config = GameManager.Instance != null ? GameManager.Instance.ActiveConfig : null;
        IReadOnlyList<AbilityDefinition> pool = config != null ? config.PowerUpChoicePool : null;
        if (pool == null || pool.Count == 0) return;

        var previous = new List<AbilityDefinition>(_rollBuffer);
        RollChoices(pool);
        if (_rollBuffer.Count == 0)
        {
            _rollBuffer.Clear();
            _rollBuffer.AddRange(previous); // nothing fresh to show - keep the cards, spend nothing
            return;
        }

        _runtime.TryConsumeReroll();
        CloseChoicePanel();
        BuildChoicePanel();
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
            GameObject iconSlot = new GameObject("IconSlot", typeof(RectTransform));
            iconSlot.transform.SetParent(panel.transform, false);
            iconSlot.AddComponent<LayoutElement>().preferredHeight = 160f;

            Image glyph = RuntimeUiKit.CreateIconTile(iconSlot.transform, 1f, 8f, out Image tile,
                AbilityRarityInfo.GetColor(definition.Rarity));
            RectTransform tileRect = tile.rectTransform;
            tileRect.anchorMin = tileRect.anchorMax = new Vector2(0.5f, 0.5f);
            tileRect.sizeDelta = new Vector2(160f, 160f);
            glyph.sprite = definition.Icon;
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
