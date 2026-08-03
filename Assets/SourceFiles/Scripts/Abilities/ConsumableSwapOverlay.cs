using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The "slots are full" resolution, done on the slots themselves instead of a menu: the offer
/// panel closes, the real HUD slot buttons hide, and enlarged puppet copies (the exact slot
/// chrome, <see cref="AbilityHud.BuildSlotChrome"/>) fly from the slots' live on-screen rects -
/// wherever the player has arranged them - to the screen centre, under a header showing the
/// incoming consumable and "choose one to swap out". Tapping a puppet swaps that slot
/// (<see cref="AbilityRuntime.ReplaceConsumable"/>), punches the new icon in, and flies both
/// puppets home; Back flies them home unchanged and returns to the SAME offer, because the
/// consumable pick may itself have been the mistake the player only discovers here.
/// The game stays paused throughout (AbilityChoiceController owns the pause), so all motion
/// runs on unscaled time.
/// </summary>
public sealed class ConsumableSwapOverlay : MonoBehaviour
{
    private const float EnlargedSize = 230f;   // canvas units; HUD slots are ~124
    private const float PuppetGap = 56f;       // between the two enlarged puppets
    private const float PuppetCenterY = -40f;  // puppets sit just below true centre; header above
    private const float FlyInSeconds = 0.30f;
    private const float FlyOutSeconds = 0.22f;
    private const float PunchSeconds = 0.40f;  // icon-swap ack on the chosen puppet before flying home

    private AbilityRuntime _runtime;
    private AbilityHud _hud;
    private ConsumableAbility _incoming;
    private System.Action _onSwapped;
    private System.Action _onBack;

    private enum Phase { FlyIn, Choose, Punch, FlyOut }
    private Phase _phase = Phase.FlyIn;
    private float _age;
    private System.Action _afterFlyOut;
    private int _punchedSlot = -1;

    private readonly RectTransform[] _puppets = new RectTransform[AbilityRuntime.ConsumableSlotCount];
    private readonly Image[] _puppetIcons = new Image[AbilityRuntime.ConsumableSlotCount];
    private readonly Text[] _puppetLabels = new Text[AbilityRuntime.ConsumableSlotCount];
    private readonly Vector2[] _homePositions = new Vector2[AbilityRuntime.ConsumableSlotCount];
    private readonly float[] _homeSizes = new float[AbilityRuntime.ConsumableSlotCount];
    private readonly Vector2[] _centerPositions = new Vector2[AbilityRuntime.ConsumableSlotCount];

    private Image _backdrop;
    private float _backdropAlpha;
    private CanvasGroup _chrome; // header + back button: fades as one, separate from the puppets
    private bool _hudRestored;

    /// <summary>Build and start the swap flow; returns the overlay root for the caller to own
    /// (destroying it is safe in any phase - the HUD is restored from OnDestroy).</summary>
    public static GameObject Show(AbilityRuntime runtime, AbilityHud hud, ConsumableAbility incoming,
        System.Action onSwapped, System.Action onBack)
    {
        // Defensive: if a slot is somehow free after all, take it and skip the ceremony.
        if (runtime.TryAddConsumable(incoming))
        {
            onSwapped?.Invoke();
            return null;
        }

        GameObject root = RuntimeUiKit.CreateModal("Consumable Swap", 6000);
        Canvas.ForceUpdateCanvases(); // the scaler must resolve before screen->canvas conversion

        ConsumableSwapOverlay overlay = root.AddComponent<ConsumableSwapOverlay>();
        overlay._runtime = runtime;
        overlay._hud = hud;
        overlay._incoming = incoming;
        overlay._onSwapped = onSwapped;
        overlay._onBack = onBack;
        overlay.Build();
        return root;
    }

    private void Build()
    {
        Canvas canvas = GetComponent<Canvas>();
        float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        Vector2 canvasSize = ((RectTransform)transform).rect.size;

        // The modal backdrop fades in with the fly-in instead of popping.
        Transform backdropChild = transform.Find("Backdrop");
        _backdrop = backdropChild != null ? backdropChild.GetComponent<Image>() : null;
        if (_backdrop != null)
        {
            _backdropAlpha = _backdrop.color.a;
            SetBackdropAlpha(0f);
        }

        Color accent = AbilityRarityInfo.GetColor(_incoming.Rarity);

        // ---- header + back button (static chrome, fades as one group) ----
        RectTransform chromeRect = RuntimeUiKit.CreateRect(transform, "Chrome",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        RuntimeUiKit.Stretch(chromeRect);
        _chrome = chromeRect.gameObject.AddComponent<CanvasGroup>();
        _chrome.alpha = 0f;

        RuntimeUiKit.CreateTmp(chromeRect, "SwapKicker", "SWAPPING IN", 26,
            new Color(accent.r, accent.g, accent.b, 0.9f), TextAnchor.MiddleCenter, FontStyle.Bold,
            RuntimeUiKit.TitleFont, new Vector2(0f, 372f), new Vector2(600f, 40f), new Vector2(0.5f, 0.5f));

        if (_incoming.Icon != null)
        {
            // The incoming ability, in the same slot chrome the puppets wear - it reads as
            // "this tile wants one of those two places".
            RectTransform incomingRect = RuntimeUiKit.CreateRect(chromeRect, "Incoming",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 268f), new Vector2(128f, 128f));
            AbilityHud.BuildSlotChrome(incomingRect, out Image incomingIcon, out _, out Text incomingLabel);
            incomingIcon.sprite = _incoming.Icon;
            incomingLabel.text = string.Empty;
        }

        RuntimeUiKit.CreateTmp(chromeRect, "IncomingName", _incoming.DisplayName, 40,
            Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, _incoming.Icon != null ? 172f : 240f), new Vector2(900f, 52f), new Vector2(0.5f, 0.5f));

        RuntimeUiKit.CreateTmp(chromeRect, "Prompt", "Choose one consumable to swap out", 30,
            new Color(0.82f, 0.86f, 0.88f, 1f), TextAnchor.MiddleCenter, FontStyle.Normal,
            RuntimeUiKit.DefaultFont, new Vector2(0f, 118f), new Vector2(900f, 44f), new Vector2(0.5f, 0.5f));

        Button back = RuntimeUiKit.CreateButton(chromeRect, "Back", 84f, OnBackPressed);
        RectTransform backRect = (RectTransform)back.transform;
        backRect.anchorMin = backRect.anchorMax = backRect.pivot = new Vector2(0.5f, 0.5f);
        backRect.sizeDelta = new Vector2(280f, 84f);
        backRect.anchoredPosition = new Vector2(0f, PuppetCenterY - EnlargedSize * 0.5f - 108f);
        AbilityCardView.StyleGhostButton(back, accent);

        // ---- the two puppets, seeded at the REAL slots' screen rects ----
        for (int i = 0; i < AbilityRuntime.ConsumableSlotCount; i++)
        {
            ConsumableAbility source = _runtime.GetSlotSource(i);
            if (source == null) continue; // both are full by contract; belt and braces

            // Home = the live button's screen rect in this canvas's units, centre-anchored.
            Vector2 homePos = new Vector2(canvasSize.x * 0.5f, canvasSize.y * 0.5f) * -1f;
            float homeSize = 124f;
            if (_hud != null && _hud.TryGetSlotScreenRect(i, out Rect screenRect))
            {
                homePos = screenRect.center / scale - canvasSize * 0.5f;
                homeSize = screenRect.width / scale;
            }
            _homePositions[i] = homePos;
            _homeSizes[i] = homeSize;
            float dx = (EnlargedSize + PuppetGap) * 0.5f;
            _centerPositions[i] = new Vector2(i == 0 ? -dx : dx, PuppetCenterY);

            GameObject puppet = new GameObject($"SwapSlot{i}",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = (RectTransform)puppet.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = homePos;
            rect.sizeDelta = new Vector2(homeSize, homeSize);
            _puppets[i] = rect;

            Image frame = puppet.GetComponent<Image>();
            frame.color = Color.clear; // pure touch target, like the real slot

            Button button = puppet.AddComponent<Button>();
            button.targetGraphic = frame;
            button.transition = Selectable.Transition.None;
            int captured = i;
            button.onClick.AddListener(() => OnPuppetTapped(captured));

            AbilityHud.BuildSlotChrome(rect, out _puppetIcons[i], out _, out _puppetLabels[i]);
            bool hasIcon = source.Icon != null;
            _puppetIcons[i].enabled = hasIcon;
            if (hasIcon) _puppetIcons[i].sprite = source.Icon;
            _puppetLabels[i].text = hasIcon ? string.Empty : source.DisplayName;
            _puppetLabels[i].fontSize = 24; // the fallback name at enlarged size, not HUD size
        }

        // The puppets ARE the slots now; the real buttons vanish under them.
        if (_hud != null) _hud.SetSlotsHidden(true);
    }

    private void Update()
    {
        _age += Time.unscaledDeltaTime;
        switch (_phase)
        {
            case Phase.FlyIn:
            {
                float t = Mathf.Clamp01(_age / FlyInSeconds);
                float eased = 1f - Mathf.Pow(1f - t, 3f); // ease-out cubic
                PlacePuppets(eased);
                SetBackdropAlpha(t * _backdropAlpha);
                if (_chrome != null) _chrome.alpha = t;
                if (t >= 1f) SetPhase(Phase.Choose);
                break;
            }

            case Phase.Punch:
            {
                if (_punchedSlot >= 0 && _puppets[_punchedSlot] != null)
                {
                    float s = FxKit.Elastic(_age, amplitude: 0.28f, damping: 6f, frequency: 18f);
                    _puppets[_punchedSlot].localScale = new Vector3(s, s, 1f);
                }
                if (_age >= PunchSeconds)
                {
                    if (_punchedSlot >= 0 && _puppets[_punchedSlot] != null)
                        _puppets[_punchedSlot].localScale = Vector3.one;
                    SetPhase(Phase.FlyOut);
                }
                break;
            }

            case Phase.FlyOut:
            {
                float t = Mathf.Clamp01(_age / FlyOutSeconds);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                PlacePuppets(1f - eased);
                SetBackdropAlpha((1f - t) * _backdropAlpha);
                if (_chrome != null) _chrome.alpha = 1f - t;
                if (t >= 1f)
                {
                    RestoreHud();
                    System.Action finish = _afterFlyOut;
                    _afterFlyOut = null;
                    enabled = false; // terminal - the callback owner destroys this root
                    finish?.Invoke();
                }
                break;
            }
        }
    }

    // 0 = at the real slot rects, 1 = enlarged at centre.
    private void PlacePuppets(float t)
    {
        for (int i = 0; i < _puppets.Length; i++)
        {
            if (_puppets[i] == null) continue;
            _puppets[i].anchoredPosition = Vector2.LerpUnclamped(_homePositions[i], _centerPositions[i], t);
            float size = Mathf.LerpUnclamped(_homeSizes[i], EnlargedSize, t);
            _puppets[i].sizeDelta = new Vector2(size, size);
        }
    }

    private void OnPuppetTapped(int slot)
    {
        if (_phase != Phase.Choose) return;

        _runtime.ReplaceConsumable(slot, _incoming); // refreshes the (hidden) real HUD too
        SfxPlayer.Play("ability_pick", 0.8f, 0.03f);

        // The puppet visibly BECOMES the incoming consumable before flying home.
        bool hasIcon = _incoming.Icon != null;
        if (_puppetIcons[slot] != null)
        {
            _puppetIcons[slot].enabled = hasIcon;
            if (hasIcon) _puppetIcons[slot].sprite = _incoming.Icon;
        }
        if (_puppetLabels[slot] != null) _puppetLabels[slot].text = hasIcon ? string.Empty : _incoming.DisplayName;

        _punchedSlot = slot;
        _afterFlyOut = _onSwapped;
        SetPhase(Phase.Punch);
    }

    private void OnBackPressed()
    {
        if (_phase != Phase.Choose) return;
        _afterFlyOut = _onBack;
        SetPhase(Phase.FlyOut);
    }

    private void SetPhase(Phase phase)
    {
        _phase = phase;
        _age = 0f;
    }

    private void SetBackdropAlpha(float a)
    {
        if (_backdrop == null) return;
        Color c = _backdrop.color;
        c.a = a;
        _backdrop.color = c;
    }

    private void RestoreHud()
    {
        if (_hudRestored) return;
        _hudRestored = true;
        if (_hud != null) _hud.SetSlotsHidden(false);
    }

    // The owner may destroy the root in any phase (scene teardown, controller disable);
    // the real slots must never stay hidden behind a dead overlay.
    private void OnDestroy() => RestoreHud();
}
