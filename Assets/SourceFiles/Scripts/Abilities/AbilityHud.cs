using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The two consumable slots: right-side buttons, built in code like the rest of the
/// runtime UI. The authored ability icon IS the button - drawn full bleed (its opaque
/// near-black ground is the body), corners clipped to the card chrome's rounded geometry
/// by a stencil mask, with the hairline ring as a border sitting right on the icon's edge
/// (the padded sprite overhangs outward). A dark slab backing shows only for icon-less abilities.
/// A gesture exclusion rect is registered over the visible buttons so activating a
/// consumable never steers or rotates the piece.
/// Buttons dim whenever the blanket gates refuse activation (paused, game over, win
/// verification) or a slot's own CanActivate says no - same affordance language as the
/// nudge pills' lockout dim.
/// </summary>
public class AbilityHud : MonoBehaviour
{
    private const float DefaultSlotSize = 124f; // initial rect size; the real size comes from HudLayout
    // The chrome sprites live on a padded canvas (shared card geometry); overhang them past the
    // slot rect by that pad so the ring's bright line sits exactly ON the rect edge and the
    // icon fills the full HudLayout size - no container band eating into the art.
    private const float ChromePad = RuntimeSprites.CardSpritePad;
    private const float DimAlpha = 0.35f;
    // Backing behind the icon (visible only for icon-less abilities and under the icon's
    // clipped corners): the icon set's near-black ground (#0B0E13) and darker.
    private static readonly Color SlabTop = new Color(0.055f, 0.065f, 0.085f, 0.98f);
    private static readonly Color SlabBottom = new Color(0.033f, 0.04f, 0.055f, 0.98f);
    private static readonly Color RingColor = new Color(0.7f, 0.82f, 1f, 0.5f);
    private static readonly Color PunchRingColor = new Color(1f, 1f, 1f, 0.95f);

    private AbilityRuntime _runtime;
    private GameObject _root;
    private Canvas _canvas;
    private RectTransform _slotsLayer; // safe-area container; slots anchor at normalized HudLayout points
    private readonly GameObject[] _slots = new GameObject[AbilityRuntime.ConsumableSlotCount];
    private readonly Image[] _slotFrames = new Image[AbilityRuntime.ConsumableSlotCount];
    private readonly Image[] _slotRings = new Image[AbilityRuntime.ConsumableSlotCount];
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
        SettingsService.Changed += ApplyLayout; // re-seat slots when the player edits the HUD layout
        RefreshSlots();

        _exclusionRect = GetSlotsScreenRect;
        TouchGestureInput.RegisterUiExclusionRect(_exclusionRect);
    }

    private void OnDestroy()
    {
        if (_runtime != null) _runtime.InventoryChanged -= RefreshSlots;
        SettingsService.Changed -= ApplyLayout;
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
        HudLayout hud = SettingsService.Hud;

        for (int i = 0; i < AbilityRuntime.ConsumableSlotCount; i++)
        {
            if (_slots[i] == null || !_slots[i].activeSelf) continue;

            float side = (i < hud.slots.Length ? hud.slots[i].size : DefaultSlotSize) * scale;
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

        // Slots live in a safe-area container and anchor at normalized points from HudLayout, so the
        // layout the player arranged in the editor maps onto any device (the fitter owns the insets).
        _slotsLayer = RuntimeUiKit.CreateRect(_root.transform, "SafeArea",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        RuntimeUiKit.Stretch(_slotsLayer);
        _slotsLayer.gameObject.AddComponent<SafeAreaFitter>();

        for (int i = 0; i < AbilityRuntime.ConsumableSlotCount; i++)
        {
            CreateSlot(i);
        }
        ApplyLayout();
    }

    private void CreateSlot(int index)
    {
        GameObject slot = new GameObject($"Slot{index}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _slots[index] = slot;
        RectTransform rect = (RectTransform)slot.transform;
        rect.SetParent(_slotsLayer, false);
        rect.pivot = new Vector2(0.5f, 0.5f);
        // anchor + size are applied from HudLayout in ApplyLayout().

        // The root Image is a pure touch target (and the layout/punch-scale rect); the
        // visible chrome is the full-bleed icon behind a hairline ring, per the card recipe.
        Image frame = slot.GetComponent<Image>();
        frame.color = Color.clear;
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

        BuildSlotChrome(rect, out _slotIcons[index], out _slotRings[index], out _slotLabels[index]);

        _slotGroups[index] = slot.AddComponent<CanvasGroup>();
        _slotShownUsable[index] = true;
        _punchAge[index] = -1f;
        slot.SetActive(false);
    }

    /// <summary>The slot's visual recipe - dark slab, full-bleed icon clipped to the rounded
    /// card geometry (border and icon edge stay aligned at any size; square icon corners are
    /// clipped instead of poking past the rounded border), hairline ring over the clipped edge,
    /// fallback name label. Shared with ConsumableSwapOverlay's flying puppets so an enlarged
    /// copy is indistinguishable from the real button.</summary>
    public static void BuildSlotChrome(RectTransform slot, out Image icon, out Image ring, out Text label)
    {
        Image body = RuntimeUiKit.CreateImage(slot, "Body",
            RuntimeSprites.CardGradient(SlabTop, SlabBottom), Color.white);
        body.type = Image.Type.Sliced;
        body.raycastTarget = false;
        StretchPadded(body.rectTransform);

        GameObject maskObject = new GameObject("IconMask",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
        RectTransform maskRect = (RectTransform)maskObject.transform;
        maskRect.SetParent(slot, false);
        StretchPadded(maskRect);
        Image maskImage = maskObject.GetComponent<Image>();
        maskImage.sprite = RuntimeSprites.CardGradient(Color.white, Color.white);
        maskImage.type = Image.Type.Sliced;
        maskImage.raycastTarget = false;
        maskObject.GetComponent<Mask>().showMaskGraphic = false;

        GameObject iconObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform iconRect = (RectTransform)iconObject.transform;
        iconRect.SetParent(maskRect, false);
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        // Back out the mask's overhang: the icon's visible rect is exactly the slot rect,
        // full bleed to the ring line.
        iconRect.offsetMin = new Vector2(ChromePad, ChromePad);
        iconRect.offsetMax = new Vector2(-ChromePad, -ChromePad);
        icon = iconObject.GetComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        ring = RuntimeUiKit.CreateImage(slot, "Ring", RuntimeSprites.CardHairlineRing(), RingColor);
        ring.type = Image.Type.Sliced;
        ring.raycastTarget = false;
        StretchPadded(ring.rectTransform);

        label = RuntimeUiKit.CreateLabel(slot, string.Empty, 18, DefaultSlotSize,
            FontStyle.Bold, RuntimeUiKit.TitleColor);
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(14f, 14f);
        labelRect.offsetMax = new Vector2(-14f, -14f);
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.raycastTarget = false;
    }

    /// <summary>Screen-pixel rect of a filled slot's button, from the live player-arranged
    /// layout - the swap overlay flies its puppets out of (and back into) exactly this rect,
    /// wherever the player keeps their slots.</summary>
    public bool TryGetSlotScreenRect(int index, out Rect rect)
    {
        rect = default;
        if (index < 0 || index >= _slots.Length) return false;
        if (_slots[index] == null || !_slots[index].activeSelf || _slotFrames[index] == null) return false;

        HudLayout hud = SettingsService.Hud;
        float scale = _canvas != null ? _canvas.scaleFactor : 1f;
        float side = (index < hud.slots.Length ? hud.slots[index].size : DefaultSlotSize) * scale;
        Vector3 center = _slotFrames[index].rectTransform.position;
        rect = new Rect(center.x - side * 0.5f, center.y - side * 0.5f, side, side);
        return true;
    }

    /// <summary>Hide/show the real slot buttons while the swap overlay's puppets stand in for
    /// them - both visible at once would read as four consumables.</summary>
    public void SetSlotsHidden(bool hidden)
    {
        if (_root != null) _root.SetActive(!hidden);
    }

    private void RefreshSlots()
    {
        for (int i = 0; i < AbilityRuntime.ConsumableSlotCount; i++)
        {
            ConsumableAbility source = _runtime.GetSlotSource(i);
            if (_slotFrames[i] == null) continue;

            bool filled = source != null;
            _slots[i].SetActive(filled);
            _slotRings[i].color = RingColor;

            bool hasIcon = source != null && source.Icon != null;
            if (_slotIcons[i] != null)
            {
                _slotIcons[i].enabled = hasIcon;
                if (hasIcon) _slotIcons[i].sprite = source.Icon;
            }
            _slotLabels[i].text = source != null && !hasIcon ? source.DisplayName : string.Empty;
        }

        ApplyLayout();
    }

    private void Update()
    {
        if (_runtime == null || _root == null) return;

        // Re-seat the slots when the screen geometry / safe area changes (rotation, resize).
        Vector3 screenState = new Vector3(Screen.width, Screen.height, Screen.safeArea.xMax);
        if (screenState != _lastScreenState)
        {
            _lastScreenState = screenState;
            ApplyLayout();
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
                    _slotRings[i].color = RingColor;
                }
                else
                {
                    float scale = FxKit.Elastic(t, amplitude: 0.28f, damping: 6f, frequency: 18f);
                    _slotFrames[i].rectTransform.localScale = new Vector3(scale, scale, 1f);
                    _slotRings[i].color = Color.Lerp(PunchRingColor, RingColor, t / 0.45f);
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

    // Anchor and size each slot from the saved HudLayout (normalized within the safe-area
    // container). Replaces the old fixed right-edge stack; the editor writes these per slot.
    private void ApplyLayout()
    {
        HudLayout hud = SettingsService.Hud;
        for (int i = 0; i < AbilityRuntime.ConsumableSlotCount; i++)
        {
            if (_slotFrames[i] == null || i >= hud.slots.Length) continue;

            HudLayout.SlotLayout s = hud.slots[i];
            RectTransform rect = _slotFrames[i].rectTransform;
            rect.anchorMin = new Vector2(s.x, s.y);
            rect.anchorMax = new Vector2(s.x, s.y);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(s.size, s.size);
        }
    }

    // Chrome pieces overhang the slot rect by the sprites' padded margin, so the ring's
    // line lands exactly on the rect edge (see ChromePad).
    private static void StretchPadded(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(-ChromePad, -ChromePad);
        rect.offsetMax = new Vector2(ChromePad, ChromePad);
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
