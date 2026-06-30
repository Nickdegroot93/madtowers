using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The two consumable slots: circular right-side buttons, built in code like the rest of
/// the runtime UI. A gesture exclusion rect is registered over the visible buttons so
/// activating a consumable never steers or rotates the piece.
/// Buttons dim whenever the blanket gates refuse activation (paused, game over, win
/// verification) or a slot's own CanActivate says no - same affordance language as the
/// nudge pills' lockout dim.
/// </summary>
public class AbilityHud : MonoBehaviour
{
    private const float SlotSize = 124f;
    private const float SlotGap = 18f;
    private const float RightMargin = 92f;
    private const float HeightAnchor = 0.58f;
    private const float IconInset = 24f;
    private const float DimAlpha = 0.35f;
    private static readonly Color BubbleColor = new Color(0.92f, 0.97f, 1f, 0.9f);

    private AbilityRuntime _runtime;
    private GameObject _root;
    private Canvas _canvas;
    private readonly GameObject[] _slots = new GameObject[AbilityRuntime.ConsumableSlotCount];
    private readonly Image[] _slotFrames = new Image[AbilityRuntime.ConsumableSlotCount];
    private readonly Image[] _slotIcons = new Image[AbilityRuntime.ConsumableSlotCount];
    private readonly Text[] _slotLabels = new Text[AbilityRuntime.ConsumableSlotCount];
    private readonly CanvasGroup[] _slotGroups = new CanvasGroup[AbilityRuntime.ConsumableSlotCount];
    private readonly bool[] _slotShownUsable = new bool[AbilityRuntime.ConsumableSlotCount];
    private readonly float[] _punchAge = new float[AbilityRuntime.ConsumableSlotCount];
    private System.Func<Rect> _exclusionRect;
    private Vector3 _lastScreenState = new Vector3(-1f, -1f, -1f);

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

    // One rect covering the visible right-side slots; recomputed per query so it survives
    // resolution changes. RectTransform.position is already raw screen pixels on this overlay
    // canvas, while SlotSize still needs the canvas scale to become raw screen size.
    private Rect GetSlotsScreenRect()
    {
        Rect rect = Rect.zero;
        bool hasRect = false;
        float scale = _canvas != null ? _canvas.scaleFactor : 1f;
        float side = SlotSize * scale;

        for (int i = 0; i < AbilityRuntime.ConsumableSlotCount; i++)
        {
            if (_slots[i] == null || !_slots[i].activeSelf) continue;

            Vector3 center = _slotFrames[i].rectTransform.position;
            Rect slotRect = new Rect(center.x - side * 0.5f, center.y - side * 0.5f, side, side);
            rect = hasRect ? Union(rect, slotRect) : slotRect;
            hasRect = true;
        }

        return hasRect ? rect : Rect.zero;
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
        _slots[index] = slot;
        RectTransform rect = (RectTransform)slot.transform;
        rect.SetParent(_root.transform, false);
        rect.anchorMin = new Vector2(1f, HeightAnchor);
        rect.anchorMax = new Vector2(1f, HeightAnchor);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(SlotSize, SlotSize);

        Image frame = slot.GetComponent<Image>();
        frame.sprite = RuntimeSprites.Bubble();
        frame.color = BubbleColor;
        _slotFrames[index] = frame;

        Button button = slot.AddComponent<Button>();
        button.targetGraphic = frame;
        button.transition = Selectable.Transition.None;
        int captured = index;
        button.onClick.AddListener(() =>
        {
            // Successful fire = elastic punch on the slot (game-feel ack of the tap).
            if (_runtime.TryActivateSlot(captured)) _punchAge[captured] = 0f;
        });

        GameObject iconObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform iconRect = (RectTransform)iconObject.transform;
        iconRect.SetParent(slot.transform, false);
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = new Vector2(IconInset, IconInset);
        iconRect.offsetMax = new Vector2(-IconInset, -IconInset);
        Image icon = iconObject.GetComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        _slotIcons[index] = icon;

        Text label = RuntimeUiKit.CreateLabel(slot.transform, string.Empty, 16, SlotSize,
            FontStyle.Bold, RuntimeUiKit.TitleColor);
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(14f, 14f);
        labelRect.offsetMax = new Vector2(-14f, -14f);
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.raycastTarget = false;
        _slotLabels[index] = label;

        _slotGroups[index] = slot.AddComponent<CanvasGroup>();
        _slotShownUsable[index] = true;
        _punchAge[index] = -1f;
        slot.SetActive(false);
    }

    private void RefreshSlots()
    {
        for (int i = 0; i < AbilityRuntime.ConsumableSlotCount; i++)
        {
            ConsumableAbility source = _runtime.GetSlotSource(i);
            if (_slotFrames[i] == null) continue;

            bool filled = source != null;
            _slots[i].SetActive(filled);
            _slotFrames[i].color = BubbleColor;

            bool hasIcon = source != null && source.Icon != null;
            if (_slotIcons[i] != null)
            {
                _slotIcons[i].enabled = hasIcon;
                if (hasIcon) _slotIcons[i].sprite = source.Icon;
            }
            _slotLabels[i].text = source != null && !hasIcon ? source.DisplayName : string.Empty;
        }

        ReflowSlots();
    }

    private void Update()
    {
        if (_runtime == null || _root == null) return;

        // Re-seat the slots when the screen geometry / safe area changes (rotation, resize).
        Vector3 screenState = new Vector3(Screen.width, Screen.height, Screen.safeArea.xMax);
        if (screenState != _lastScreenState)
        {
            _lastScreenState = screenState;
            ReflowSlots();
        }

        for (int i = 0; i < AbilityRuntime.ConsumableSlotCount; i++)
        {
            if (_slotGroups[i] == null || _slots[i] == null || !_slots[i].activeSelf) continue;

            // Elastic tap punch - unscaled time so the hit-stop never freezes UI feel.
            if (_punchAge[i] >= 0f)
            {
                _punchAge[i] += Time.unscaledDeltaTime;
                float t = _punchAge[i];
                if (t >= 0.45f)
                {
                    _punchAge[i] = -1f;
                    _slotFrames[i].rectTransform.localScale = Vector3.one;
                    _slotFrames[i].color = BubbleColor;
                }
                else
                {
                    float scale = FxKit.Elastic(t, amplitude: 0.28f, damping: 6f, frequency: 18f);
                    _slotFrames[i].rectTransform.localScale = new Vector3(scale, scale, 1f);
                    _slotFrames[i].color = Color.Lerp(new Color(1f, 1f, 1f, 0.96f), BubbleColor, t / 0.45f);
                }
            }

            bool filled = _runtime.GetSlotSource(i) != null;
            bool usable = filled && _runtime.CanActivateSlot(i);
            if (usable == _slotShownUsable[i]) continue; // don't dirty the canvas for nothing

            _slotShownUsable[i] = usable;
            _slotGroups[i].alpha = usable ? 1f : DimAlpha;
            _slotGroups[i].interactable = usable;
        }
    }

    private void ReflowSlots()
    {
        float rightInset = RuntimeUiKit.SafeAreaRightInset(_canvas);
        int visibleCount = 0;
        for (int i = 0; i < AbilityRuntime.ConsumableSlotCount; i++)
        {
            if (_slots[i] != null && _slots[i].activeSelf) visibleCount++;
        }

        int ordinal = 0;
        for (int i = 0; i < AbilityRuntime.ConsumableSlotCount; i++)
        {
            if (_slotFrames[i] == null || _slots[i] == null || !_slots[i].activeSelf) continue;

            float y = ((visibleCount - 1) * 0.5f - ordinal) * (SlotSize + SlotGap);
            _slotFrames[i].rectTransform.anchoredPosition = new Vector2(-(RightMargin + rightInset), y);
            ordinal++;
        }
    }

    private static Rect Union(Rect a, Rect b)
    {
        float xMin = Mathf.Min(a.xMin, b.xMin);
        float yMin = Mathf.Min(a.yMin, b.yMin);
        float xMax = Mathf.Max(a.xMax, b.xMax);
        float yMax = Mathf.Max(a.yMax, b.yMax);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }
}
