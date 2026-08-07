using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The armed-ability rail: a short vertical column of icons on the left edge, under the Pocket
/// Cache bubble, showing every owned ability that still HOLDS A CHARGE - a one-shot passive waiting
/// to fire (Ward, Sacrifice, Hardline). An icon appears when the ability is picked and burns away
/// the moment its charge is spent, so "do I still have my Ward?" is answered on screen instead of
/// from memory (Nick 2026-08-07).
///
/// Deliberately NOT the whole owned inventory: permanent passives (Air Brake, Recovery, Brace, ...)
/// have nothing to spend, so an icon for them would be wallpaper that never changes and would bury
/// the one icon that does. Worst case here is three icons.
///
/// It is a READOUT, not a control: no Button, no raycast targets, and no gesture-exclusion rect -
/// registering one would eat steer/rotate gestures over a strip the player can't press anyway.
/// Visually it is the consumable slot's chrome (AbilityHud.BuildSlotChrome) at a smaller size with
/// an AMBER ring instead of the slots' blue-white: amber is already the ability-card language for
/// "one-time passive - spends itself" (AbilityTypeInfo). The ring breathes slowly, so an armed
/// ability reads as a live status light rather than a static badge.
/// </summary>
public class ArmedAbilityHud : MonoBehaviour
{
    // Same left margin and column centre as the Hold bubble (UI/HoldButton), so the rail lines up
    // under it as one left-edge column on any device.
    private const float LeftMargin = 92f;
    private const float TopAnchor = 0.49f;   // first chip's centre, just under the hold bubble
    private const float ChipSize = 84f;      // smaller than a 124 consumable slot: clearly not a button
    private const float ChipGap = 14f;
    private const float BurnSeconds = 0.4f;  // the spend animation: punch out and fade

    private static readonly Color RingBase = new Color(0.95f, 0.8f, 0.45f, 0.45f);   // amber = spends itself
    private static readonly Color RingPeak = new Color(1f, 0.92f, 0.7f, 0.85f);

    private sealed class Chip
    {
        public AbilityDefinition Source;   // null = pooled/free
        public GameObject Go;
        public RectTransform Rect;
        public Image Icon;
        public Image Ring;
        public Text Label;
        public Text Count;
        public CanvasGroup Group;
        public float PopAge;
        public float BurnAge;              // < 0 while armed; counts up once the charge is spent
    }

    private AbilityRuntime _runtime;
    private GameObject _root;
    private Canvas _canvas;
    private RectTransform _layer;
    private readonly List<Chip> _chips = new List<Chip>();
    private readonly List<AbilityRuntime.OwnedAbility> _armed = new List<AbilityRuntime.OwnedAbility>();
    private Vector3 _lastScreenState = new Vector3(-1f, -1f, -1f);

    private void Start()
    {
        _runtime = GetComponent<AbilityRuntime>();
        if (_runtime == null) return;

        Build();
        _runtime.InventoryChanged += Refresh;
        Refresh();
    }

    private void OnDestroy()
    {
        if (_runtime != null) _runtime.InventoryChanged -= Refresh;
    }

    private void Build()
    {
        // Under the consumable slots (2500) and the hold button (2480) in sorting order: if the
        // player ever drags a slot on top of the rail, the thing they can press wins.
        _root = RuntimeUiKit.CreateOverlayCanvas("Armed Abilities", 2470);
        _canvas = _root.GetComponent<Canvas>();

        _layer = RuntimeUiKit.CreateRect(_root.transform, "SafeArea",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        RuntimeUiKit.Stretch(_layer);
        _layer.gameObject.AddComponent<SafeAreaFitter>();
    }

    /// <summary>InventoryChanged handler: arm a chip for every newly charged ability, start the
    /// burn on every chip whose ability no longer holds a charge, and refresh the ×N counts.</summary>
    private void Refresh()
    {
        if (_runtime == null || _layer == null) return;

        _runtime.GetArmedAbilities(_armed);

        // Anything armed a moment ago and not armed now has just been spent - burn it away.
        for (int i = 0; i < _chips.Count; i++)
        {
            Chip chip = _chips[i];
            if (chip.Source == null || chip.BurnAge >= 0f) continue;
            if (FindArmed(chip.Source) == null) chip.BurnAge = 0f;
        }

        for (int i = 0; i < _armed.Count; i++)
        {
            AbilityRuntime.OwnedAbility owned = _armed[i];
            Chip chip = FindChip(owned.Source);
            if (chip == null)
            {
                // Re-arming while the last charge's icon is still burning (a second Ward picked, or
                // one stocked from the shop) revives THAT chip instead of adding a second one - two
                // Ward icons on a rail whose whole job is "do I still have my Ward?" reads as a bug.
                chip = FindBurningChip(owned.Source) ?? TakeFreeChip();
                chip.Source = owned.Source;
                chip.BurnAge = -1f;
                chip.PopAge = 0f;
                chip.Group.alpha = 1f;
                chip.Rect.localScale = Vector3.one;
                chip.Go.SetActive(true);
                ApplyIcon(chip, owned.Source);
            }

            // Two stacked one-shots = two saves; show the count so a spent charge still reads.
            chip.Count.text = owned.ChargesLeft > 1 ? $"x{owned.ChargesLeft}" : string.Empty;
        }

        Layout();
    }

    private void Update()
    {
        if (_root == null) return;

        Vector3 screenState = new Vector3(Screen.width, Screen.height, Screen.safeArea.xMin);
        if (screenState != _lastScreenState)
        {
            _lastScreenState = screenState;
            Layout();
        }

        // Unscaled time throughout: hit-stop and pause must never freeze UI feel.
        float dt = Time.unscaledDeltaTime;
        float breathe = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.2f);
        Color armedRing = Color.Lerp(RingBase, RingPeak, breathe);
        bool freed = false;

        for (int i = 0; i < _chips.Count; i++)
        {
            Chip chip = _chips[i];
            if (chip.Source == null) continue;

            if (chip.BurnAge < 0f)
            {
                FxKit.TickSettlePop(chip.Rect, ref chip.PopAge, dt);
                chip.Ring.color = armedRing;
                continue;
            }

            // Spent: punch outward and fade, then hand the chip back to the pool.
            chip.BurnAge += dt;
            float t = Mathf.Clamp01(chip.BurnAge / BurnSeconds);
            float scale = 1f + 0.45f * t * t;
            chip.Rect.localScale = new Vector3(scale, scale, 1f);
            chip.Group.alpha = 1f - t;
            chip.Ring.color = Color.Lerp(RingPeak, new Color(1f, 1f, 1f, 0f), t);

            if (t < 1f) continue;

            chip.Source = null;
            chip.BurnAge = -1f;
            chip.Go.SetActive(false);
            freed = true;
        }

        if (freed) Layout(); // the chips below close the gap once the burn finishes
    }

    // Chips stack downward from TopAnchor in _chips order, which IS acquisition order: a chip taken
    // from the pool moves to the end of the list, so a recycled slot can never place a newly armed
    // ability above one picked earlier (which would make the live icons jump rows). A burning chip
    // keeps its place until the animation ends, so nothing slides under a spend that is still playing.
    private void Layout()
    {
        float left = LeftMargin + RuntimeUiKit.SafeAreaLeftInset(_canvas);
        int index = 0;

        for (int i = 0; i < _chips.Count; i++)
        {
            Chip chip = _chips[i];
            if (chip.Source == null) continue;

            chip.Rect.anchorMin = new Vector2(0f, TopAnchor);
            chip.Rect.anchorMax = new Vector2(0f, TopAnchor);
            chip.Rect.pivot = new Vector2(0.5f, 0.5f);
            chip.Rect.anchoredPosition = new Vector2(left, -index * (ChipSize + ChipGap));
            chip.Rect.sizeDelta = new Vector2(ChipSize, ChipSize);
            index++;
        }
    }

    private void ApplyIcon(Chip chip, AbilityDefinition source)
    {
        bool hasIcon = source != null && source.Icon != null;
        chip.Icon.enabled = hasIcon;
        if (hasIcon) chip.Icon.sprite = source.Icon;
        chip.Label.text = hasIcon || source == null ? string.Empty : source.DisplayName;
    }

    private AbilityRuntime.OwnedAbility FindArmed(AbilityDefinition source)
    {
        for (int i = 0; i < _armed.Count; i++)
        {
            if (_armed[i].Source == source) return _armed[i];
        }
        return null;
    }

    private Chip FindChip(AbilityDefinition source)
    {
        for (int i = 0; i < _chips.Count; i++)
        {
            if (_chips[i].Source == source && _chips[i].BurnAge < 0f) return _chips[i];
        }
        return null;
    }

    private Chip FindBurningChip(AbilityDefinition source)
    {
        for (int i = 0; i < _chips.Count; i++)
        {
            if (_chips[i].Source == source && _chips[i].BurnAge >= 0f) return _chips[i];
        }
        return null;
    }

    // Chips are pooled, never destroyed: the chrome allocates its own gradient sprites per build,
    // so re-arming Ward five times in a run would otherwise leak five sets of them.
    private Chip TakeFreeChip()
    {
        for (int i = 0; i < _chips.Count; i++)
        {
            Chip free = _chips[i];
            if (free.Source != null || free.BurnAge >= 0f) continue;

            _chips.RemoveAt(i);  // to the back: list order is display order, newest armed lowest
            _chips.Add(free);
            return free;
        }

        Chip chip = new Chip();
        GameObject go = new GameObject($"Armed{_chips.Count}",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        chip.Go = go;
        chip.Rect = (RectTransform)go.transform;
        chip.Rect.SetParent(_layer, false);

        Image frame = go.GetComponent<Image>();
        frame.color = Color.clear;
        frame.raycastTarget = false; // a readout, never a button

        AbilityHud.BuildSlotChrome(chip.Rect, out chip.Icon, out chip.Ring, out chip.Label);
        chip.Ring.color = RingBase;

        chip.Count = RuntimeUiKit.CreateLabel(chip.Rect.transform, string.Empty, 20, 28f,
            FontStyle.Bold, new Color(1f, 0.92f, 0.7f), TextAnchor.LowerRight);
        RectTransform countRect = chip.Count.rectTransform;
        countRect.anchorMin = Vector2.zero;
        countRect.anchorMax = Vector2.one;
        countRect.offsetMin = new Vector2(6f, 4f);
        countRect.offsetMax = new Vector2(-8f, -6f);
        chip.Count.raycastTarget = false;

        chip.Group = go.AddComponent<CanvasGroup>();
        chip.Group.interactable = false;
        chip.Group.blocksRaycasts = false;
        chip.BurnAge = -1f;
        go.SetActive(false);

        _chips.Add(chip);
        return chip;
    }
}
