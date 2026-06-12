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

    // Offers are SINGLE-RARITY: the profile (per-level override or the built-in
    // progress-scaled defaults) rolls the offer's rarity among rarities that actually
    // have available candidates, then the cards sample uniformly without replacement
    // within that rarity. A mixed common/legendary offer would be a non-choice.
    private void RollChoices(IReadOnlyList<AbilityDefinition> pool)
    {
        _rollBuffer.Clear();
        AbilityContext context = _runtime != null ? _runtime.Context : null;
        if (context == null) return;

        // Bucket the AVAILABLE pool by rarity (unique-and-owned, stack-capped, banned
        // and condition-failing abilities never reach the roll). Bucket count derives
        // from the enum - a future fifth rarity must not silently under-allocate here.
        int rarityCount = System.Enum.GetValues(typeof(AbilityRarity)).Length;
        List<AbilityDefinition>[] byRarity = new List<AbilityDefinition>[rarityCount];
        for (int r = 0; r < rarityCount; r++) byRarity[r] = new List<AbilityDefinition>();
        for (int i = 0; i < pool.Count; i++)
        {
            AbilityDefinition ability = pool[i];
            if (ability == null) continue;
            if (!ability.IsAvailable(context, _runtime.GetOwnedStacks(ability))) continue;
            byRarity[(int)ability.Rarity].Add(ability);
        }

        // Roll the offer's rarity: profile weights at the current run progress, with
        // empty rarities excluded so the offer never comes up blank while others exist.
        RarityWeightStage stage = AbilityRarityProfile.Resolve(
            context.Level != null ? context.Level.AbilityRarityProfile : null, GetRunProgress(context));

        float totalWeight = 0f;
        for (int r = 0; r < byRarity.Length; r++)
        {
            if (byRarity[r].Count > 0) totalWeight += stage.GetWeight((AbilityRarity)r);
        }

        int chosen = -1;
        if (totalWeight > 0f)
        {
            float roll = Random.Range(0f, totalWeight);
            for (int r = 0; r < byRarity.Length; r++)
            {
                if (byRarity[r].Count == 0) continue;
                roll -= stage.GetWeight((AbilityRarity)r);
                if (roll < 0f) { chosen = r; break; }
            }
            // Random.Range's float upper bound is INCLUSIVE: a roll of exactly
            // totalWeight exits the loop unchosen - take the last weighted rarity.
            if (chosen < 0)
            {
                for (int r = byRarity.Length - 1; r >= 0; r--)
                {
                    if (byRarity[r].Count > 0 && stage.GetWeight((AbilityRarity)r) > 0f) { chosen = r; break; }
                }
            }
        }
        else
        {
            // All remaining candidates sit in zero-weight rarities (e.g. a
            // legendaries-only profile with every legendary already owned). An earned
            // offer must not starve for the rest of the run: fall back to a uniform
            // pick among rarities that still have candidates.
            int options = 0;
            for (int r = 0; r < byRarity.Length; r++) if (byRarity[r].Count > 0) options++;
            if (options == 0) return;
            int pickIndex = Random.Range(0, options);
            for (int r = 0; r < byRarity.Length; r++)
            {
                if (byRarity[r].Count == 0) continue;
                if (pickIndex-- == 0) { chosen = r; break; }
            }
        }
        if (chosen < 0) return;

        // Uniform sample without replacement within the chosen rarity; fewer than three
        // candidates simply shows fewer cards.
        List<AbilityDefinition> candidates = byRarity[chosen];
        while (_rollBuffer.Count < ChoiceCount && candidates.Count > 0)
        {
            int pick = Random.Range(0, candidates.Count);
            _rollBuffer.Add(candidates[pick]);
            candidates.RemoveAt(pick);
        }
    }

    // Fraction of the level target reached (0 on endless / no level): drives the
    // rarity escalation - offers near the goal are spicier than offers at block 20.
    // Reads the SAME context the profile was resolved from, never a second source.
    private float GetRunProgress(AbilityContext context)
    {
        if (context.Level == null || context.GameManager == null) return 0f;

        switch (context.Level.TargetType)
        {
            case LevelTargetType.PlaceBlocks:
                return Mathf.Clamp01(context.GameManager.score / context.Level.TargetValue);
            case LevelTargetType.ReachHeight:
                return Mathf.Clamp01(context.GameManager.towerHeight / context.Level.TargetValue);
            default:
                return 0f;
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
            _panelRoot.transform, new Vector2(1000f, 880f), drawBackground: false);
        // The shared panel builder leaves child heights uncontrolled; this layout is
        // height-budgeted (header + cards), so LayoutElement heights must be honored.
        panel.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().childControlHeight = true;

        Color offerAccent = _rollBuffer.Count > 0
            ? AbilityRarityInfo.GetColor(_rollBuffer[0].Rarity)
            : RuntimeUiKit.TitleColor;
        CreateHeader(panel.transform, offerAccent);

        GameObject cardRow = new GameObject("Cards");
        cardRow.transform.SetParent(panel.transform, false);
        LayoutElement rowElement = cardRow.AddComponent<LayoutElement>();
        rowElement.preferredHeight = 640f;

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

    // "CHOOSE AN ABILITY" with the mockups' flourish: soft side bars + small diamonds,
    // all tinted to the offer's rarity (single-rarity offers make this meaningful).
    private void CreateHeader(Transform parent, Color accent)
    {
        GameObject header = new GameObject("Header", typeof(RectTransform));
        header.transform.SetParent(parent, false);
        LayoutElement headerElement = header.AddComponent<LayoutElement>();
        headerElement.preferredHeight = 110f;

        HorizontalLayoutGroup row = header.AddComponent<HorizontalLayoutGroup>();
        row.childAlignment = TextAnchor.MiddleCenter;
        row.spacing = 14f;
        row.childControlWidth = false;
        row.childControlHeight = false;

        CreateHeaderFlourish(header.transform, accent, leftSide: true);
        Text title = RuntimeUiKit.CreateLabel(header.transform, "CHOOSE AN ABILITY", 48, 64f,
            FontStyle.Bold, RuntimeUiKit.TitleColor);
        title.font = RuntimeUiKit.TitleFont;
        ((RectTransform)title.transform).sizeDelta = new Vector2(520f, 64f);
        CreateHeaderFlourish(header.transform, accent, leftSide: false);

        Text subtitle = RuntimeUiKit.CreateLabel(parent, "Select one ability to empower your run",
            24, 34f, FontStyle.Normal, Color.Lerp(accent, Color.white, 0.35f));
        subtitle.font = RuntimeUiKit.TitleFont;
    }

    private static void CreateHeaderFlourish(Transform parent, Color accent, bool leftSide)
    {
        GameObject flourish = new GameObject(leftSide ? "FlourishL" : "FlourishR", typeof(RectTransform));
        RectTransform rect = (RectTransform)flourish.transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(120f, 20f);

        GameObject barObject = new GameObject("Bar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform bar = (RectTransform)barObject.transform;
        bar.SetParent(rect, false);
        bar.anchorMin = new Vector2(0f, 0.5f);
        bar.anchorMax = new Vector2(1f, 0.5f);
        bar.offsetMin = new Vector2(0f, -2f);
        bar.offsetMax = new Vector2(0f, 2f);
        Image barImage = barObject.GetComponent<Image>();
        barImage.sprite = RuntimeSprites.SoftHorizontalBar(0.1f);
        barImage.color = new Color(accent.r, accent.g, accent.b, 0.55f);
        barImage.raycastTarget = false;

        GameObject diamondObject = new GameObject("Diamond", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform diamond = (RectTransform)diamondObject.transform;
        diamond.SetParent(rect, false);
        diamond.anchorMin = diamond.anchorMax = new Vector2(leftSide ? 1f : 0f, 0.5f);
        diamond.sizeDelta = new Vector2(16f, 16f);
        Image diamondImage = diamondObject.GetComponent<Image>();
        diamondImage.sprite = RuntimeSprites.Diamond();
        diamondImage.color = Color.Lerp(accent, Color.white, 0.2f);
        diamondImage.raycastTarget = false;
    }

    private static readonly Color CardPlateColor = new Color(0.055f, 0.045f, 0.105f, 0.96f);

    private void CreateCard(Transform parent, AbilityDefinition definition)
    {
        Color rarityColor = AbilityRarityInfo.GetColor(definition.Rarity);

        // Two-layer chrome: a fixed dark cut-corner plate, plus a rarity-tinted glowing
        // frame stretched over it (outside the vertical layout's control).
        GameObject cardObject = new GameObject(definition.DisplayName);
        cardObject.transform.SetParent(parent, false);

        Image plate = cardObject.AddComponent<Image>();
        plate.sprite = RuntimeSprites.CardPlate();
        plate.type = Image.Type.Sliced;
        plate.color = CardPlateColor;
        cardObject.AddComponent<RectMask2D>(); // clips the legendary shine sweep

        // Header region: a rarity-tinted gradient AREA whose straight bottom edge is
        // the header boundary (the mockups have no divider line). Height = card top
        // padding + the header container, so its edge lands exactly where the badge
        // pill straddles. Drawn under the frame so the border line stays on top.
        GameObject bandObject = new GameObject("HeaderBand", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform bandRect = (RectTransform)bandObject.transform;
        bandRect.SetParent(cardObject.transform, false);
        bandRect.anchorMin = new Vector2(0f, 1f);
        bandRect.anchorMax = new Vector2(1f, 1f);
        bandRect.pivot = new Vector2(0.5f, 1f);
        bandRect.offsetMin = new Vector2(3f, -CardHeaderBandHeight);
        bandRect.offsetMax = new Vector2(-3f, -3f);
        Image band = bandObject.GetComponent<Image>();
        band.sprite = RuntimeSprites.CardHeaderBand();
        band.type = Image.Type.Sliced;
        band.color = new Color(rarityColor.r, rarityColor.g, rarityColor.b, 0.55f);
        band.raycastTarget = false;
        bandObject.AddComponent<LayoutElement>().ignoreLayout = true;

        GameObject frameObject = new GameObject("Frame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform frameRect = (RectTransform)frameObject.transform;
        frameRect.SetParent(cardObject.transform, false);
        frameRect.anchorMin = Vector2.zero;
        frameRect.anchorMax = Vector2.one;
        frameRect.offsetMin = Vector2.zero;
        frameRect.offsetMax = Vector2.zero;
        Image frame = frameObject.GetComponent<Image>();
        frame.sprite = RuntimeSprites.CardFrame();
        frame.type = Image.Type.Sliced;
        frame.color = rarityColor;
        frame.raycastTarget = false;
        frameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        // Equal card widths no matter the content: identical preferred + flexible
        // weights mean the row splits its width evenly instead of by text length.
        LayoutElement cardElement = cardObject.AddComponent<LayoutElement>();
        cardElement.preferredWidth = 10f;
        cardElement.flexibleWidth = 1f;

        Button button = cardObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.colorMultiplier = 1f;
        button.colors = colors;
        button.targetGraphic = plate;

        VerticalLayoutGroup cardLayout = cardObject.AddComponent<VerticalLayoutGroup>();
        cardLayout.padding = new RectOffset(28, 28, 16, 16);
        cardLayout.spacing = 10f;
        cardLayout.childAlignment = TextAnchor.UpperCenter;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = true; // LayoutElement heights are authoritative
        cardLayout.childForceExpandWidth = true;
        cardLayout.childForceExpandHeight = false;

        // Header section: title over a divider line, the type badge pill straddling it.
        CreateCardHeader(cardObject.transform, definition.DisplayName.ToUpperInvariant(),
            definition.Type, rarityColor);

        // Top group: the owned-stack note rides directly under the header.
        int stacks = _runtime != null ? _runtime.GetOwnedStacks(definition) : 0;
        if (stacks > 0)
        {
            RuntimeUiKit.CreateLabel(cardObject.transform, $"Owned ×{stacks}",
                22, 28f, FontStyle.Bold, new Color(0.6f, 0.9f, 0.65f, 1f), TextAnchor.MiddleCenter);
        }

        // Middle group: the artwork, CENTERED in the leftover space - equal flexible
        // spacers above and below it push the header group up and the text group down.
        CreateFlexibleSpacer(cardObject.transform);

        GameObject iconObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        iconObject.transform.SetParent(cardObject.transform, false);
        Image icon = iconObject.GetComponent<Image>();
        icon.sprite = definition.Icon != null ? definition.Icon : RuntimeSprites.AbilityGlyph();
        icon.color = definition.Icon != null ? Color.white : Color.Lerp(rarityColor, Color.white, 0.25f);
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        LayoutElement iconLayout = iconObject.AddComponent<LayoutElement>();
        iconLayout.preferredHeight = 165f;

        CreateFlexibleSpacer(cardObject.transform);

        // Bottom group: description sits just above the button.
        Text shortText = RuntimeUiKit.CreateLabel(cardObject.transform, definition.ShortDescription,
            21, 100f, FontStyle.Normal, new Color(0.76f, 0.8f, 0.88f, 0.95f), TextAnchor.LowerCenter);
        shortText.lineSpacing = 1.05f;

        // Nested button: UGUI raycasts stop at the inner target, so tapping Details
        // never also picks the card.
        Button details = RuntimeUiKit.CreateButton(cardObject.transform, "DETAILS", 52f,
            () => ShowDetailPanel(definition));
        StyleDetailsButton(details, rarityColor);

        if (definition.Rarity == AbilityRarity.Legendary)
        {
            cardObject.AddComponent<AbilityCardShine>();
        }

        AbilityDefinition picked = definition;
        button.onClick.AddListener(() => Pick(picked));
    }

    private static void CreateFlexibleSpacer(Transform parent)
    {
        GameObject spacer = new GameObject("Spacer", typeof(RectTransform));
        spacer.transform.SetParent(parent, false);
        LayoutElement element = spacer.AddComponent<LayoutElement>();
        element.flexibleHeight = 1f;
    }

    // Card top padding + this header's height = where the header band's bottom edge
    // lands; the badge pill straddles exactly that boundary.
    private const float CardHeaderHeight = 84f;
    private const float CardHeaderBandHeight = 16f + CardHeaderHeight; // + card top padding

    // The card's header section: the title lives inside the rarity-tinted band region
    // (drawn by CreateCard), and the type badge pill sits ON the band's bottom edge -
    // centered, straddling it. The boundary IS the band edge; there is no divider line.
    private static void CreateCardHeader(Transform parent, string titleText, AbilityType type, Color rarityColor)
    {
        GameObject header = new GameObject("CardHeader", typeof(RectTransform));
        header.transform.SetParent(parent, false);
        header.AddComponent<LayoutElement>().preferredHeight = CardHeaderHeight;

        // Title fills the band area; best-fit keeps it to one line when it can, wraps
        // to two only when a long name leaves no choice.
        GameObject titleObject = new GameObject("Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        RectTransform titleRect = (RectTransform)titleObject.transform;
        titleRect.SetParent(header.transform, false);
        titleRect.anchorMin = new Vector2(0f, 0f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.offsetMin = new Vector2(4f, 22f);
        titleRect.offsetMax = new Vector2(-4f, -4f);
        Text title = titleObject.GetComponent<Text>();
        title.font = RuntimeUiKit.TitleFont;
        title.text = titleText;
        title.fontStyle = FontStyle.Bold;
        title.alignment = TextAnchor.MiddleCenter;
        title.color = RuntimeUiKit.TitleColor;
        title.resizeTextForBestFit = true;
        title.resizeTextMinSize = 15;
        title.resizeTextMaxSize = 28;
        title.raycastTarget = false;

        // The badge pill, straddling the band's bottom edge (container bottom).
        GameObject pillObject = new GameObject("Badge", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform pill = (RectTransform)pillObject.transform;
        pill.SetParent(header.transform, false);
        pill.anchorMin = pill.anchorMax = new Vector2(0.5f, 0f);
        pill.anchoredPosition = new Vector2(0f, 0f);
        pill.sizeDelta = new Vector2(84f, 32f);
        Image pillImage = pillObject.GetComponent<Image>();
        pillImage.sprite = RuntimeSprites.RoundedPanel();
        pillImage.type = Image.Type.Sliced;
        pillImage.color = new Color(0.05f, 0.045f, 0.1f, 1f); // opaque: hides the line behind it
        pillImage.raycastTarget = false;

        RuntimeUiKit.AddOutline(pillObject.transform, new Color(rarityColor.r, rarityColor.g, rarityColor.b, 0.9f));

        GameObject glyphObject = new GameObject("Glyph", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform glyphRect = (RectTransform)glyphObject.transform;
        glyphRect.SetParent(pillObject.transform, false);
        glyphRect.anchorMin = glyphRect.anchorMax = new Vector2(0.5f, 0.5f);
        glyphRect.sizeDelta = new Vector2(38f, 20f);
        Image glyph = glyphObject.GetComponent<Image>();
        glyph.sprite = AbilityTypeInfo.GetGlyphSprite(type);
        glyph.preserveAspect = true;
        glyph.raycastTarget = false;
        glyph.color = Color.Lerp(rarityColor, Color.white, 0.35f);
    }

    // Mockup-style Details button: near-transparent fill with a thin rarity outline.
    private static void StyleDetailsButton(Button button, Color rarityColor)
    {
        Image image = button.GetComponent<Image>();
        image.sprite = RuntimeSprites.RoundedPanel();
        image.type = Image.Type.Sliced;
        image.color = new Color(rarityColor.r, rarityColor.g, rarityColor.b, 0.08f);

        RuntimeUiKit.AddOutline(button.transform, Color.Lerp(rarityColor, Color.white, 0.2f));

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.6f, 1.6f, 1.6f, 1f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        Text label = button.GetComponentInChildren<Text>();
        if (label != null)
        {
            label.font = RuntimeUiKit.TitleFont;
            label.fontSize = 24;
            label.color = Color.Lerp(rarityColor, Color.white, 0.5f);
        }
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
