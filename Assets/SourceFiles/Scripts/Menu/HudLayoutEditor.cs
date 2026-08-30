using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static RuntimeUiKit;

/// <summary>
/// Full-screen HUD layout editor (opened from Settings -> Controls). You pick a target with a
/// clear segmented control - CONSUMABLE SLOTS or NUDGE BUTTONS - and only that target's controls
/// show: slots get drag-to-move + a size slider + a "move together" toggle; nudge buttons get an
/// opacity slider (their zones stay framed while targeted, so 0%-opacity guides are never a trap).
/// The non-targeted group dims to read as context. Edits a draft HudLayout; Save commits it via
/// SettingsService, Cancel discards, Reset restores defaults. See SETTINGS.md.
/// </summary>
public class HudLayoutEditor : MonoBehaviour
{
    private enum Target { Slots, Nudge }

    private const int SortingOrder = 6000;
    private const float MinSlotSize = 84f;
    private const float MaxSlotSize = 200f;
    private const float PanelSafeFraction = 0.34f; // fraction of safe height the panel occupies
    private const float ContextAlpha = 0.4f;    // the non-targeted group dims to this

    private static readonly Color BubbleColor = new Color(0.92f, 0.97f, 1f, 0.9f);
    private static readonly Color NudgePillColor = new Color(1f, 1f, 1f, 0.09f);
    private static readonly Color NudgeChevronColor = new Color(0.95f, 0.98f, 1f, 0.32f);
    private static readonly Color PanelFill = new Color(0.05f, 0.06f, 0.07f, 0.94f);
    private static readonly Color TextColor = new Color(0.96f, 0.97f, 1f, 1f);
    private static readonly Color MutedText = new Color(0.7f, 0.74f, 0.8f, 1f);

    private static HudLayoutEditor _instance;

    private ChapterDefinition _chapter;
    private Color _accent;
    private Action _onClose;
    private HudLayout _draft;

    private RectTransform _safeArea;
    private CanvasGroup _slotsGroup;
    private readonly RectTransform[] _slotRects = new RectTransform[HudLayout.SlotCount];
    private readonly Image[] _slotRings = new Image[HudLayout.SlotCount];
    private CanvasGroup _nudgeGroup;
    private readonly System.Collections.Generic.List<(Image image, Color baseColor)> _nudgeImages =
        new System.Collections.Generic.List<(Image, Color)>();
    private readonly Image[] _nudgeFrames = new Image[2];
    private RectTransform _control;
    private RectTransform _panel;
    private RectTransform _flipHandle;
    private Image _flipChevron;
    private bool _panelAtTop = true;
    private float _freeMinY;         // slot-placement bounds that flip with the panel
    private float _freeMaxY = 1f;

    private Target _target = Target.Slots;
    private int _selectedSlot;

    public static void Open(ChapterDefinition chapter, Color accent, Action onClose)
    {
        if (_instance != null) return;
        GameObject root = CreateOverlayCanvas("HUD Layout Editor", SortingOrder);
        _instance = root.AddComponent<HudLayoutEditor>();
        _instance._chapter = chapter;
        _instance._accent = accent;
        _instance._onClose = onClose;
        _instance._draft = SettingsService.Hud.Clone();
        EnsureEventSystem();
        _instance.Build();
    }

    private static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

    private void Build()
    {
        BuildBackdrop();
        BuildNudge();  // before slots so slots draw on top when they overlap the corners

        _safeArea = CreateRect(transform, "SafeArea", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(_safeArea);
        _safeArea.gameObject.AddComponent<SafeAreaFitter>();
        Image bounds = CreateImage(_safeArea, "Bounds", RuntimeSprites.RoundedOutline(), Alpha(_accent, 0.28f));
        bounds.type = Image.Type.Sliced;
        Stretch(bounds.rectTransform);

        BuildSlots();
        BuildControlPanel();
        SetTarget(Target.Slots);
    }

    private void BuildBackdrop()
    {
        Sprite bg;
        if (_chapter != null && _chapter.MenuBackgroundImage != null)
        {
            bg = _chapter.MenuBackgroundImage;
        }
        else if (_chapter != null)
        {
            Color top = Color.Lerp(_chapter.MenuAccentSecondaryColor, Color.black, 0.35f);
            Color bottom = Color.Lerp(_chapter.MenuAccentColor, Color.black, 0.68f);
            bg = MenuSprites.Background(top, bottom, _chapter.MenuAccentColor);
        }
        else
        {
            bg = MenuSprites.Background(new Color(0.1f, 0.12f, 0.14f), new Color(0.03f, 0.04f, 0.05f), _accent);
        }

        Image image = CreateImage(transform, "Backdrop", bg, Color.white);
        Stretch(image.rectTransform);
        FitToCover(image, SpriteAspect(bg));
        Image wash = CreateImage(transform, "Wash", null, new Color(0.02f, 0.03f, 0.04f, 0.62f));
        Stretch(wash.rectTransform);
    }

    // ---- nudge guides --------------------------------------------------------------------------

    private void BuildNudge()
    {
        RectTransform group = CreateRect(transform, "NudgeGroup", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(group);
        _nudgeGroup = group.gameObject.AddComponent<CanvasGroup>();

        float w = TouchGestureInput.NudgeZoneWidthFraction;
        float h = TouchGestureInput.NudgeZoneHeightFraction;
        _nudgeFrames[0] = BuildNudgeGuide(group, new Vector2(0f, 0f), new Vector2(w, h), pointsLeft: true);
        _nudgeFrames[1] = BuildNudgeGuide(group, new Vector2(1f - w, 0f), new Vector2(1f, h), pointsLeft: false);
    }

    // One nudge corner: a fill pill (at the saved opacity) + chevron, plus a framing outline that
    // is shown only while the nudge target is active (so an invisible guide is still findable).
    private Image BuildNudgeGuide(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, bool pointsLeft)
    {
        RectTransform pill = CreateRect(parent, "NudgeGuide", anchorMin, anchorMax, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        pill.offsetMin = Vector2.zero;
        pill.offsetMax = Vector2.zero;
        Image fill = pill.gameObject.AddComponent<Image>();
        fill.sprite = RuntimeSprites.RoundedPanel();
        fill.type = Image.Type.Sliced;
        fill.color = NudgeColor(NudgePillColor);
        fill.raycastTarget = false;
        _nudgeImages.Add((fill, NudgePillColor));

        Image chevron = CreateImage(pill, "Chevron", RuntimeSprites.Chevron(), NudgeColor(NudgeChevronColor));
        chevron.rectTransform.sizeDelta = new Vector2(30f, 30f);
        if (!pointsLeft) chevron.rectTransform.localEulerAngles = new Vector3(0f, 0f, 180f);
        _nudgeImages.Add((chevron, NudgeChevronColor));

        Image frame = CreateImage(pill, "Frame", RuntimeSprites.RoundedOutline(), _accent);
        frame.type = Image.Type.Sliced;
        Stretch(frame.rectTransform);
        frame.enabled = false;
        return frame;
    }

    private Color NudgeColor(Color baseColor) => Alpha(baseColor, baseColor.a * _draft.nudgeGuideOpacity);

    private void RetintNudge()
    {
        for (int i = 0; i < _nudgeImages.Count; i++)
            _nudgeImages[i].image.color = NudgeColor(_nudgeImages[i].baseColor);
    }

    // ---- consumable slots ----------------------------------------------------------------------

    private void BuildSlots()
    {
        RectTransform group = CreateRect(_safeArea, "SlotsGroup", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(group);
        _slotsGroup = group.gameObject.AddComponent<CanvasGroup>();
        for (int i = 0; i < HudLayout.SlotCount; i++) BuildSlot(group, i);
    }

    private void BuildSlot(RectTransform parent, int index)
    {
        HudLayout.SlotLayout s = _draft.slots[index];
        RectTransform slot = CreateRect(parent, $"Slot{index}", new Vector2(s.x, s.y), new Vector2(s.x, s.y),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(s.size, s.size));
        Image bubble = slot.gameObject.AddComponent<Image>();
        bubble.sprite = RuntimeSprites.Bubble();
        bubble.color = BubbleColor;
        bubble.raycastTarget = true;
        _slotRects[index] = slot;

        Image ring = CreateImage(slot, "Ring", MenuSprites.CircleBadge(new Color(0f, 0f, 0f, 0f), _accent), Color.white);
        RectTransform rr = ring.rectTransform;
        rr.anchorMin = Vector2.zero;
        rr.anchorMax = Vector2.one;
        rr.offsetMin = new Vector2(-7f, -7f);
        rr.offsetMax = new Vector2(7f, 7f);
        ring.enabled = false;
        _slotRings[index] = ring;

        CreateTmp(slot, "Num", (index + 1).ToString(), 34, Alpha(TextColor, 0.85f),
            TextAnchor.MiddleCenter, FontStyle.Bold, TitleFont);

        HudDragHandle drag = slot.gameObject.AddComponent<HudDragHandle>();
        drag.bounds = _safeArea;
        int captured = index;
        drag.onSelected = () => SelectSlot(captured);
        drag.onMovedNormalized = n => MoveSlot(captured, n);
    }

    // Normalized centre range that keeps a slot fully inside the safe bounds (and below the panel).
    private void SlotRange(int index, out float minX, out float maxX, out float minY, out float maxY)
    {
        Rect r = _safeArea.rect;
        float half = _draft.slots[index].size * 0.5f;
        float mx = r.width > 1f ? half / r.width : 0f;
        float my = r.height > 1f ? half / r.height : 0f;
        minX = mx; maxX = 1f - mx;
        minY = Mathf.Max(my, _freeMinY);   // stay out of whichever half the panel covers
        maxY = Mathf.Min(1f - my, _freeMaxY);
    }

    private Vector2 ClampToBounds(int index, Vector2 n)
    {
        SlotRange(index, out float minX, out float maxX, out float minY, out float maxY);
        return new Vector2(Mathf.Clamp(n.x, minX, maxX), Mathf.Clamp(n.y, minY, maxY));
    }

    private void ApplySlotPosition(int index, Vector2 n)
    {
        _draft.slots[index].x = n.x;
        _draft.slots[index].y = n.y;
        RectTransform rect = _slotRects[index];
        rect.anchorMin = n;
        rect.anchorMax = n;
        rect.anchoredPosition = Vector2.zero;
    }

    private void MoveSlot(int index, Vector2 n)
    {
        if (!_draft.slotsLinked)
        {
            ApplySlotPosition(index, ClampToBounds(index, n));
            return;
        }

        // Linked (assumes the 2-slot pair): move BOTH by one shared delta, clamped so neither
        // leaves bounds. Clamping each slot independently would let one hit an edge while the other
        // kept moving, permanently shrinking the gap - so clamp the delta against both ranges.
        int other = 1 - index;
        Vector2 cur = new Vector2(_draft.slots[index].x, _draft.slots[index].y);
        Vector2 oth = new Vector2(_draft.slots[other].x, _draft.slots[other].y);
        Vector2 delta = n - cur;

        SlotRange(index, out float aMinX, out float aMaxX, out float aMinY, out float aMaxY);
        SlotRange(other, out float bMinX, out float bMaxX, out float bMinY, out float bMaxY);
        delta.x = Mathf.Clamp(delta.x, Mathf.Max(aMinX - cur.x, bMinX - oth.x), Mathf.Min(aMaxX - cur.x, bMaxX - oth.x));
        delta.y = Mathf.Clamp(delta.y, Mathf.Max(aMinY - cur.y, bMinY - oth.y), Mathf.Min(aMaxY - cur.y, bMaxY - oth.y));

        ApplySlotPosition(index, cur + delta);
        ApplySlotPosition(other, oth + delta);
    }

    private void SetSlotSize(int index, float size)
    {
        _draft.slots[index].size = size;
        _slotRects[index].sizeDelta = new Vector2(size, size);
        ApplySlotPosition(index, ClampToBounds(index, new Vector2(_draft.slots[index].x, _draft.slots[index].y)));
    }

    private void SetSize(float size)
    {
        SetSlotSize(_selectedSlot, size);
        if (_draft.slotsLinked) SetSlotSize(1 - _selectedSlot, size);
    }

    private void SelectSlot(int index)
    {
        if (_target != Target.Slots) return;
        _selectedSlot = index;
        UpdateRings();
        RefreshControls(); // the size slider reflects the newly selected slot
    }

    private void UpdateRings()
    {
        bool show = _target == Target.Slots;
        for (int i = 0; i < HudLayout.SlotCount; i++)
            if (_slotRings[i] != null) _slotRings[i].enabled = show && (i == _selectedSlot || _draft.slotsLinked);
    }

    // ---- target + control panel ----------------------------------------------------------------

    private void SetTarget(Target target)
    {
        _target = target;
        bool slots = target == Target.Slots;

        _slotsGroup.alpha = slots ? 1f : ContextAlpha;
        _slotsGroup.blocksRaycasts = slots;
        _nudgeGroup.alpha = slots ? ContextAlpha : 1f;
        for (int i = 0; i < _nudgeFrames.Length; i++)
            if (_nudgeFrames[i] != null) _nudgeFrames[i].enabled = !slots;

        UpdateRings();
        RefreshControls();
    }

    private void BuildControlPanel()
    {
        const float pad = 30f;
        // Parented inside the safe area so the fitter owns the notch/status-bar inset (RESPONSIVE
        // rule 2) rather than a hard-coded top offset that a taller notch could still clip.
        RectTransform bar = CreateRect(_safeArea, "ControlPanel", new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        _panel = bar; // anchored/offset by ApplyPanelPosition (top or bottom)
        Image fill = bar.gameObject.AddComponent<Image>();
        fill.sprite = RuntimeSprites.RoundedPanel();
        fill.type = Image.Type.Sliced;
        fill.color = PanelFill;
        AddOutline(bar, Alpha(_accent, 0.45f));

        CreateTmp(bar, "Title", "CUSTOMIZE CONTROLS", 30, TextColor, TextAnchor.MiddleLeft, FontStyle.Bold,
            TitleFont, new Vector2(pad, -22f), new Vector2(360f, 44f), new Vector2(0f, 1f));

        // Destructive Reset isolated top-right, far from the frequent Save/Cancel at the bottom -
        // so a mis-tap can't wipe the layout when you meant Cancel/Save.
        RectTransform resetRect = CreateRect(bar, "ResetBtn", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-pad, -14f), new Vector2(172f, 84f));
        StyleButton(resetRect, "RESET", 20, new Color(1f, 1f, 1f, 0.08f), MutedText, Reset);

        // Full-width target picker with large touch segments (>= 44pt tall).
        RectTransform targetBand = CreateRect(bar, "Target", new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0f, -116f), new Vector2(0f, 116f));
        targetBand.offsetMin = new Vector2(pad, targetBand.offsetMin.y);
        targetBand.offsetMax = new Vector2(-pad, targetBand.offsetMax.y);
        CreateSegmentedControl(targetBand, new[] { "CONSUMABLE SLOTS", "NUDGE BUTTONS" }, 0, _accent,
            i => SetTarget(i == 0 ? Target.Slots : Target.Nudge), 26);

        _control = CreateRect(bar, "Controls", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        _control.offsetMin = new Vector2(pad, 168f);
        _control.offsetMax = new Vector2(-pad, -252f);

        // Big, well-separated Cancel (left half) + Save (right half) at the bottom.
        RectTransform cancelRect = CreateRect(bar, "CancelBtn", new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero);
        cancelRect.offsetMin = new Vector2(pad, 30f);
        cancelRect.offsetMax = new Vector2(-16f, 150f);
        StyleButton(cancelRect, "CANCEL", 26, new Color(1f, 1f, 1f, 0.14f), TextColor, Cancel);

        RectTransform saveRect = CreateRect(bar, "SaveBtn", new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero);
        saveRect.offsetMin = new Vector2(16f, 30f);
        saveRect.offsetMax = new Vector2(-pad, 150f);
        StyleButton(saveRect, "SAVE", 26, _accent, new Color(0.1f, 0.1f, 0.09f, 1f), Save);

        // A handle on the panel's outer edge flips it top<->bottom, so a slot can be placed on the
        // half the panel currently covers (the chevron points to where the panel will go).
        _flipHandle = CreateRect(bar, "FlipHandle", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(124f, 46f));
        Image handleFill = _flipHandle.gameObject.AddComponent<Image>();
        handleFill.sprite = RuntimeSprites.RoundedPanel();
        handleFill.type = Image.Type.Sliced;
        handleFill.color = _accent;
        Button flipButton = _flipHandle.gameObject.AddComponent<Button>();
        flipButton.targetGraphic = handleFill;
        flipButton.transition = Selectable.Transition.None;
        flipButton.onClick.AddListener(() => { SfxPlayer.Play("ui-button-click"); Flip(); });
        _flipChevron = CreateImage(_flipHandle, "Chevron", RuntimeSprites.Chevron(), new Color(0.1f, 0.1f, 0.09f, 1f));
        _flipChevron.preserveAspect = true;
        _flipChevron.rectTransform.sizeDelta = new Vector2(30f, 30f);

        ApplyPanelPosition();
    }

    private void Flip()
    {
        _panelAtTop = !_panelAtTop;
        ApplyPanelPosition();
    }

    // Places the panel at the top or bottom of the safe area, positions the flip handle on the
    // panel's outer edge, and sets the slot-placement bounds to the free half.
    private void ApplyPanelPosition()
    {
        if (_panelAtTop)
        {
            _panel.anchorMin = new Vector2(0f, 1f);
            _panel.anchorMax = new Vector2(1f, 1f);
            _panel.pivot = new Vector2(0.5f, 1f);
            _panel.offsetMin = new Vector2(30f, -640f);
            _panel.offsetMax = new Vector2(-30f, -8f);
            _freeMinY = 0f;
            _freeMaxY = 1f - PanelSafeFraction;

            _flipHandle.anchorMin = _flipHandle.anchorMax = new Vector2(0.5f, 0f);
            _flipHandle.pivot = new Vector2(0.5f, 1f);
            _flipHandle.anchoredPosition = new Vector2(0f, -6f);
            _flipChevron.rectTransform.localEulerAngles = new Vector3(0f, 0f, -90f); // points down
        }
        else
        {
            _panel.anchorMin = new Vector2(0f, 0f);
            _panel.anchorMax = new Vector2(1f, 0f);
            _panel.pivot = new Vector2(0.5f, 0f);
            _panel.offsetMin = new Vector2(30f, 8f);
            _panel.offsetMax = new Vector2(-30f, 640f);
            _freeMinY = PanelSafeFraction;
            _freeMaxY = 1f;

            _flipHandle.anchorMin = _flipHandle.anchorMax = new Vector2(0.5f, 1f);
            _flipHandle.pivot = new Vector2(0.5f, 0f);
            _flipHandle.anchoredPosition = new Vector2(0f, 6f);
            _flipChevron.rectTransform.localEulerAngles = new Vector3(0f, 0f, 90f); // points up
        }
    }

    // Turns a pre-sized rect into a themed button (fill + click sound + centred label). Callers
    // size/anchor the rect, so this works for both fixed and stretched buttons.
    private void StyleButton(RectTransform rect, string label, int fontSize, Color fill, Color textColor, UnityEngine.Events.UnityAction onClick)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = RuntimeSprites.RoundedPanel();
        image.type = Image.Type.Sliced;
        image.color = fill;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => { SfxPlayer.Play("ui-button-click"); onClick?.Invoke(); });
        CreateTmp(rect, "Label", label, fontSize, textColor, TextAnchor.MiddleCenter, FontStyle.Bold, TitleFont);
    }

    private void RefreshControls()
    {
        if (_control == null) return;
        for (int i = _control.childCount - 1; i >= 0; i--) Destroy(_control.GetChild(i).gameObject);

        if (_target == Target.Nudge)
        {
            Label("GUIDE VISIBILITY", 0f);
            AddSlider(_draft.nudgeGuideOpacity, -30f, v => { _draft.nudgeGuideOpacity = Mathf.Clamp01(v); RetintNudge(); });
            Hint("The corner buttons steer the falling piece. Set how visible their guides are — they still work at 0%.", -104f);
        }
        else
        {
            Label(_draft.slotsLinked ? "SIZE (BOTH SLOTS)" : $"SIZE (SLOT {_selectedSlot + 1})", 0f);
            float norm = Mathf.InverseLerp(MinSlotSize, MaxSlotSize, _draft.slots[_selectedSlot].size);
            AddSlider(norm, -30f, v => SetSize(Mathf.Lerp(MinSlotSize, MaxSlotSize, v)));

            CreateTmp(_control, "LinkLabel", "Move slots together", 20, TextColor, TextAnchor.MiddleLeft,
                FontStyle.Bold, TitleFont, new Vector2(0f, -104f), new Vector2(340f, 30f), new Vector2(0f, 1f));
            RectTransform pill = CreateRect(_control, "Link", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -100f), new Vector2(116f, 56f));
            CreatePillToggle(pill, _draft.slotsLinked, _accent, on =>
            {
                _draft.slotsLinked = on;
                UpdateRings();
                RefreshControls();
            });
            Hint(_draft.slotsLinked ? "Drag either slot to move both." : "Drag a slot to move it • tap a slot to pick which to resize.", -166f);
        }
    }

    private void Label(string text, float y) =>
        CreateTmp(_control, "Label", text, 20, MutedText, TextAnchor.MiddleLeft, FontStyle.Bold,
            TitleFont, new Vector2(0f, y), new Vector2(420f, 28f), new Vector2(0f, 1f));

    private void Hint(string text, float y)
    {
        TextMeshProUGUI hint = CreateTmp(_control, "Hint", text, 20, MutedText, TextAnchor.UpperLeft,
            FontStyle.Normal, TitleFont, new Vector2(0f, y), new Vector2(620f, 54f), new Vector2(0f, 1f));
        hint.textWrappingMode = TMPro.TextWrappingModes.Normal;
    }

    private void AddSlider(float value, float y, UnityEngine.Events.UnityAction<float> onChanged)
    {
        RectTransform band = CreateRect(_control, "SliderBand", new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(0f, 64f));
        band.offsetMin = new Vector2(0f, band.offsetMin.y);
        band.offsetMax = new Vector2(0f, band.offsetMax.y);
        CreateSlider(band, "Slider", value, _accent, Alpha(TextColor, 0.16f), onChanged);
    }

    // ---- commit / close ------------------------------------------------------------------------

    private void Reset()
    {
        _draft = HudLayout.CreateDefault();
        _selectedSlot = 0;
        for (int i = 0; i < HudLayout.SlotCount; i++)
        {
            _slotRects[i].sizeDelta = new Vector2(_draft.slots[i].size, _draft.slots[i].size);
            ApplySlotPosition(i, new Vector2(_draft.slots[i].x, _draft.slots[i].y));
        }
        RetintNudge();
        SetTarget(_target);
    }

    private void Save()
    {
        SettingsService.ApplyHudLayout(_draft);
        Close();
    }

    private void Cancel() => Close();

    private void Close()
    {
        _instance = null;
        _onClose?.Invoke();
        Destroy(gameObject);
    }
}
