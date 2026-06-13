using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The two consumable slots: bottom-center buttons between the nudge corners, built in
/// code like the rest of the runtime UI. Each registers a gesture exclusion rect with
/// TouchGestureInput so activating a consumable never steers or rotates the piece.
/// Buttons dim whenever the blanket gates refuse activation (paused, game over, win
/// verification) or a slot's own CanActivate says no - same affordance language as the
/// nudge pills' lockout dim.
/// </summary>
public class AbilityHud : MonoBehaviour
{
    private const float SlotSize = 110f;
    private const float SlotGap = 18f;
    private const float BottomInset = 14f;
    private const float DimAlpha = 0.35f;
    private static readonly Color SlotEmptyColor = new Color(1f, 1f, 1f, 0.06f);
    private static readonly Color SlotFilledColor = new Color(0.13f, 0.19f, 0.22f, 0.92f);

    private AbilityRuntime _runtime;
    private GameObject _root;
    private Canvas _canvas;
    private readonly Image[] _slotFrames = new Image[AbilityRuntime.ConsumableSlotCount];
    private readonly Image[] _slotIcons = new Image[AbilityRuntime.ConsumableSlotCount];
    private readonly Text[] _slotLabels = new Text[AbilityRuntime.ConsumableSlotCount];
    private readonly CanvasGroup[] _slotGroups = new CanvasGroup[AbilityRuntime.ConsumableSlotCount];
    private readonly bool[] _slotShownUsable = new bool[AbilityRuntime.ConsumableSlotCount];
    private readonly float[] _punchAge = new float[AbilityRuntime.ConsumableSlotCount];
    private System.Func<Rect> _exclusionRect;

    private void Start()
    {
        _runtime = GetComponent<AbilityRuntime>();
        if (_runtime == null) return;

        BuildHud();
        _runtime.InventoryChanged += RefreshSlots;
        RefreshSlots();

        _exclusionRect = GetSlotsScreenRect;
        TouchGestureInput.RegisterUiExclusionRect(_exclusionRect);
    }

    private void OnDestroy()
    {
        if (_runtime != null) _runtime.InventoryChanged -= RefreshSlots;
        if (_exclusionRect != null) TouchGestureInput.UnregisterUiExclusionRect(_exclusionRect);
    }

    // One rect covering both slots (they are adjacent); recomputed per query so it
    // survives resolution changes. Slot dimensions are authored in the canvas's
    // 1080x1920 reference space - TouchGestureInput compares RAW screen pixels, so the
    // rect must scale by the canvas factor or it under-covers on high-DPI phones (taps
    // on a slot's edge would both click the button AND rotate the piece).
    private Rect GetSlotsScreenRect()
    {
        float scale = _canvas != null ? _canvas.scaleFactor : 1f;
        float width = (SlotSize * AbilityRuntime.ConsumableSlotCount + SlotGap) * scale;
        float left = (Screen.width - width) * 0.5f;
        return new Rect(left, 0f, width, (BottomInset + SlotSize) * scale);
    }

    private void BuildHud()
    {
        _root = RuntimeUiKit.CreateOverlayCanvas("Ability Slots", 2500);
        _canvas = _root.GetComponent<Canvas>();

        for (int i = 0; i < AbilityRuntime.ConsumableSlotCount; i++)
        {
            CreateSlot(i);
        }
    }

    private void CreateSlot(int index)
    {
        GameObject slot = new GameObject($"Slot{index}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)slot.transform;
        rect.SetParent(_root.transform, false);
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        float offset = (index - (AbilityRuntime.ConsumableSlotCount - 1) * 0.5f) * (SlotSize + SlotGap);
        rect.anchoredPosition = new Vector2(offset, BottomInset);
        rect.sizeDelta = new Vector2(SlotSize, SlotSize);

        Image frame = slot.GetComponent<Image>();
        frame.sprite = RuntimeSprites.RoundedPanel();
        frame.type = Image.Type.Sliced;
        frame.color = SlotEmptyColor;
        _slotFrames[index] = frame;

        Button button = slot.AddComponent<Button>();
        button.targetGraphic = frame;
        int captured = index;
        button.onClick.AddListener(() =>
        {
            // Successful fire = elastic punch on the slot (game-feel ack of the tap).
            if (_runtime.TryActivateSlot(captured)) _punchAge[captured] = 0f;
        });

        // Icon fills the slot when the ability has one; the text label is the fallback.
        GameObject iconObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform iconRect = (RectTransform)iconObject.transform;
        iconRect.SetParent(slot.transform, false);
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = new Vector2(10f, 10f);
        iconRect.offsetMax = new Vector2(-10f, -10f);
        Image icon = iconObject.GetComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        icon.enabled = false;
        _slotIcons[index] = icon;

        Text label = RuntimeUiKit.CreateLabel(slot.transform, string.Empty, 20, SlotSize,
            FontStyle.Bold, RuntimeUiKit.TitleColor);
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(6f, 6f);
        labelRect.offsetMax = new Vector2(-6f, -6f);
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        _slotLabels[index] = label;

        _slotGroups[index] = slot.AddComponent<CanvasGroup>();
        _slotShownUsable[index] = true;
        _punchAge[index] = -1f;
    }

    private void RefreshSlots()
    {
        for (int i = 0; i < AbilityRuntime.ConsumableSlotCount; i++)
        {
            ConsumableAbility source = _runtime.GetSlotSource(i);
            if (_slotFrames[i] == null) continue;

            _slotFrames[i].color = source != null ? SlotFilledColor : SlotEmptyColor;

            bool hasIcon = source != null && source.Icon != null;
            _slotIcons[i].enabled = hasIcon;
            _slotIcons[i].sprite = hasIcon ? source.Icon : null;
            _slotLabels[i].text = source != null && !hasIcon ? source.DisplayName : string.Empty;
        }
    }

    private void Update()
    {
        if (_runtime == null || _root == null) return;

        for (int i = 0; i < AbilityRuntime.ConsumableSlotCount; i++)
        {
            if (_slotGroups[i] == null) continue;

            // Elastic tap punch - unscaled time so the hit-stop never freezes UI feel.
            // Activation empties the slot BEFORE this runs (TryActivateSlot fires
            // InventoryChanged synchronously), so the punch alone would scale a
            // near-invisible empty frame: a white flash settling into the slot's
            // final color carries the "consumed!" read instead.
            if (_punchAge[i] >= 0f)
            {
                _punchAge[i] += Time.unscaledDeltaTime;
                float t = _punchAge[i];
                Color settled = _runtime.GetSlotSource(i) != null ? SlotFilledColor : SlotEmptyColor;
                if (t >= 0.45f)
                {
                    _punchAge[i] = -1f;
                    _slotFrames[i].rectTransform.localScale = Vector3.one;
                    _slotFrames[i].color = settled;
                }
                else
                {
                    float scale = FxKit.Elastic(t, amplitude: 0.28f, damping: 6f, frequency: 18f);
                    _slotFrames[i].rectTransform.localScale = new Vector3(scale, scale, 1f);
                    _slotFrames[i].color = Color.Lerp(new Color(1f, 1f, 1f, 0.9f), settled, t / 0.45f);
                }
            }

            bool filled = _runtime.GetSlotSource(i) != null;
            bool usable = !filled || _runtime.CanActivateSlot(i);
            if (usable == _slotShownUsable[i]) continue; // don't dirty the canvas for nothing

            _slotShownUsable[i] = usable;
            _slotGroups[i].alpha = usable ? 1f : DimAlpha;
            _slotGroups[i].interactable = usable;
        }
    }
}
