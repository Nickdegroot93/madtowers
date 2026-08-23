using System;
using System.Collections;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static RuntimeUiKit;

// Identity moments (BACKEND.md §3.4/§3.5): the claim-a-name modal and the one-time
// post-Chapter-1 link prompt. Both are pull, not push - the prompt shows exactly once
// (monotonic save flag) and never competes with an unlock reveal for the menu return.
// (partial of MainMenuRuntime - same class, shared statics.)
public static partial class MainMenuRuntime
{
    // Server contract (claim_display_name): 3-16 chars, letters/digits/space/_/-.
    private static readonly Regex NameRule = new Regex("^[A-Za-z0-9 _-]{3,16}$");

    // Auto names are "Builder-XXXX" (server trigger); anything else is a claimed name.
    // Requires Ready: before the profile arrives (or offline) DisplayName is the "PLAYER ONE"
    // placeholder, which must read as unclaimed - never pre-fill or offer to "change" it.
    private static bool HasClaimedName =>
        OnlineService.IsReady &&
        OnlineService.DisplayName != "PLAYER ONE" &&
        !Regex.IsMatch(OnlineService.DisplayName ?? string.Empty, "^Builder-\\d+$");

    // ---- claim / change-name modal -----------------------------------------------------------
    // Centered composition like the sign-in sheet (avatar ring -> title -> input -> CTA):
    // one visual language for every identity moment, none of them shaped like a web form.

    private static void OpenClaimNameModal(Action onClaimed)
    {
        if (GameObject.Find("Claim Name") != null) return; // double-tap / multitouch guard
        SfxPlayer.Play("ui-button-click");

        bool renaming = HasClaimedName;
        bool guest = !OnlineService.IsLinked;

        GameObject overlay = RuntimeUiKit.CreateOverlayCanvas("Claim Name", 5800);
        void Close() => UnityEngine.Object.Destroy(overlay);

        Image backdrop = CreateImage(overlay.transform, "Backdrop", null, new Color(0.02f, 0.02f, 0.03f, 0.88f));
        Stretch(backdrop.rectTransform);
        backdrop.raycastTarget = true;
        Button backdropButton = backdrop.gameObject.AddComponent<Button>();
        backdropButton.transition = Selectable.Transition.None;
        backdropButton.onClick.AddListener(Close);

        const float W = 820f;
        float H = guest ? 700f : 620f;
        const float pad = 48f;
        const float contentW = W - pad * 2f;
        RectTransform panel = CreateRect(overlay.transform, "Panel",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(W, H));
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = RuntimeSprites.RoundedPanel();
        panelImage.type = Image.Type.Sliced;
        panelImage.color = new Color(0.075f, 0.065f, 0.058f, 1f);
        panelImage.raycastTarget = true;
        RuntimeUiKit.AddOutline(panel, GoldOutline(0.3f));

        Image ring = CreateImage(panel, "AvatarRing", MenuSprites.CircleBadge(
            new Color(0.12f, 0.11f, 0.09f, 1f), WithAlpha(GoldBase, 0.55f)), Color.white);
        SetRect(ring.rectTransform, new Vector2(0f, -36f), new Vector2(96f, 96f), new Vector2(0.5f, 1f));
        ring.rectTransform.pivot = new Vector2(0.5f, 1f);
        Image person = CreateImage(ring.transform, "Person", MenuSprites.Person(WithAlpha(GoldBase, 0.85f)), Color.white);
        person.preserveAspect = true;
        SetCenteredAt(person.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(48f, 48f));

        TextMeshProUGUI title = CreateTmp(panel, "Title", renaming ? "CHANGE YOUR NAME" : "CLAIM YOUR NAME",
            38, TextPrimary, TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -152f), new Vector2(contentW, 48f), new Vector2(0.5f, 1f));
        title.characterSpacing = 2f;

        CreateTmp(panel, "Note", "SHOWN ON EVERY LEADERBOARD", 18,
            WithAlpha(GoldBase, 0.75f), TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -206f), new Vector2(contentW, 26f), new Vector2(0.5f, 1f));

        // Renaming pre-fills the current name; a fresh claim starts empty (never pre-fill
        // the auto Builder name - one accidental tap would claim it for good).
        TMP_InputField input = CreateNameInput(panel, new Vector2(pad, -258f), new Vector2(contentW, 100f));
        if (renaming) input.text = OnlineService.DisplayName;

        const string RulesCopy = "3-16 CHARACTERS - LETTERS, NUMBERS, SPACES, _ -";
        TextMeshProUGUI hint = CreateTmp(panel, "Hint", RulesCopy, 18,
            WithAlpha(TextMuted, 0.75f), TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -372f), new Vector2(contentW, 26f), new Vector2(0.5f, 1f));

        float ctaBottom = guest ? 130f : 48f;
        Image claimBg = CreateImage(panel, "Claim", MenuSprites.RoundedGradient(
            new Color(1f, 0.86f, 0.45f, 1f), new Color(0.82f, 0.58f, 0.18f, 1f)), Color.white);
        claimBg.type = Image.Type.Sliced;
        SetRect(claimBg.rectTransform, new Vector2(pad, ctaBottom), new Vector2(contentW, 96f), new Vector2(0f, 0f));
        claimBg.raycastTarget = true;
        TextMeshProUGUI claimLabel = CreateTmp(claimBg.transform, "Label", renaming ? "SAVE NAME" : "CLAIM", 30,
            new Color(0.16f, 0.11f, 0.04f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        Button claimButton = claimBg.gameObject.AddComponent<Button>();
        claimButton.targetGraphic = claimBg;

        // Guest footer: the sign-in door lives in every identity moment (BACKEND.md §3.4
        // moment 2). Opens the shared sheet on top of this modal (5900 > 5800).
        if (guest)
        {
            TextMeshProUGUI signIn = CreateTmp(panel, "GuestSignIn",
                "GUEST ACCOUNT - <u>SIGN IN</u> TO KEEP YOUR PROGRESS", 18,
                WithAlpha(GoldBase, 0.9f), TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(0f, 34f), new Vector2(contentW, 64f), new Vector2(0.5f, 0f));
            signIn.rectTransform.pivot = new Vector2(0.5f, 0f);
            signIn.raycastTarget = true;
            Button signInButton = signIn.gameObject.AddComponent<Button>();
            signInButton.transition = Selectable.Transition.None;
            signInButton.onClick.AddListener(() => { SfxPlayer.Play("ui-button-click"); OpenSignInSheet(); });
        }

        Color hintNormal = WithAlpha(TextMuted, 0.75f);
        Color hintError = new Color(1f, 0.55f, 0.42f, 0.95f);
        bool inFlight = false;

        void RefreshValidity()
        {
            // Validate what the click handler will actually submit (trimmed) - " ab " must
            // not light the button for a submission that would silently no-op.
            bool valid = NameRule.IsMatch((input.text ?? string.Empty).Trim());
            claimButton.interactable = valid && !inFlight;
            claimBg.color = valid && !inFlight ? Color.white : new Color(0.62f, 0.62f, 0.62f, 1f);
            if (!inFlight)
            {
                hint.text = RulesCopy;
                hint.color = hintNormal;
            }
        }

        input.onValueChanged.AddListener(_ => { if (!inFlight) RefreshValidity(); });
        RefreshValidity();

        claimButton.onClick.AddListener(() =>
        {
            if (inFlight) return;
            string name = input.text.Trim();
            if (!NameRule.IsMatch(name)) return;
            SfxPlayer.Play("ui-button-click");
            inFlight = true;
            claimLabel.text = renaming ? "SAVING..." : "CLAIMING...";
            claimButton.interactable = false;
            OnlineService.ClaimDisplayName(name, (ok, reason) =>
            {
                if (overlay == null) return; // closed while in flight
                if (ok)
                {
                    Close();
                    onClaimed?.Invoke();
                    return;
                }
                inFlight = false;
                claimLabel.text = renaming ? "SAVE NAME" : "CLAIM";
                RefreshValidity();
                // After RefreshValidity so the verdict isn't clobbered by the default copy.
                hint.text = reason switch
                {
                    "taken" => "THAT NAME IS TAKEN",
                    "invalid" => RulesCopy,
                    "not_allowed" => "THAT NAME ISN'T ALLOWED",
                    _ => "COULDN'T REACH THE SERVER - TRY AGAIN",
                };
                hint.color = hintError;
            });
        });

        // Close (X), matching the sibling modals.
        Color closeFill = new Color(0.03f, 0.03f, 0.04f, 0.55f);
        Image closeBg = CreateImage(panel, "Close", MenuSprites.CircleBadge(closeFill, closeFill), Color.white);
        SetRect(closeBg.rectTransform, new Vector2(-24f, -24f), new Vector2(64f, 64f), new Vector2(1f, 1f));
        closeBg.raycastTarget = true;
        CreateTmp(closeBg.transform, "X", "X", 30, TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        Button closeButton = closeBg.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = closeBg;
        closeButton.onClick.AddListener(Close);

        input.Select();
        input.ActivateInputField();
    }

    // A TMP input field built by hand (first in the project - nothing in the kit makes one):
    // rounded dark well, viewport mask, placeholder + live text.
    private static TMP_InputField CreateNameInput(Transform parent, Vector2 anchoredPosition, Vector2 size)
    {
        Image well = CreateImage(parent, "NameInput", RuntimeSprites.RoundedPanel(), new Color(0.11f, 0.10f, 0.085f, 1f));
        well.type = Image.Type.Sliced;
        SetRect(well.rectTransform, anchoredPosition, size, new Vector2(0f, 1f));
        well.raycastTarget = true;
        RuntimeUiKit.AddOutline(well.transform, GlassBorder);

        RectTransform viewport = CreateRect(well.transform, "Text Area",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        viewport.offsetMin = new Vector2(26f, 10f);
        viewport.offsetMax = new Vector2(-26f, -10f);
        viewport.gameObject.AddComponent<RectMask2D>();

        TextMeshProUGUI placeholder = CreateTmp(viewport, "Placeholder", "YOUR NAME", 32,
            WithAlpha(TextMuted, 0.45f), TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        TextMeshProUGUI text = CreateTmp(viewport, "Text", string.Empty, 32,
            TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);

        TMP_InputField input = well.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = well;
        input.textViewport = viewport;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.characterLimit = 16;
        input.lineType = TMP_InputField.LineType.SingleLine;
        return input;
    }

    // ---- the one-time link prompt (BACKEND.md §3.4 moment 1) --------------------------------

    private static bool _linkPromptSubscribed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetLinkPromptForPlayMode()
    {
        _linkPromptSubscribed = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void HookLinkPrompt()
    {
        if (!_linkPromptSubscribed)
        {
            // Menu returns are scene reloads, and RuntimeInitialize hooks fire only once per
            // session - the sceneLoaded event is what sees every later menu build.
            SceneManager.sceneLoaded += (_, _) => TryScheduleLinkPrompt();
            _linkPromptSubscribed = true;
        }
        TryScheduleLinkPrompt();
    }

    /// <summary>Is the one-time sign-in card still owed? Shared with the dev-beats runner,
    /// whose review ask must never land on the same visit as this card.</summary>
    private static bool LinkPromptOwesAVisit()
        => OnlineService.Enabled
        && !ProgressStore.WasLinkPromptShown()
        && AttemptsService.MetaEnabled;                           // pre-meta-unlock (ch1-2): monetization-silent

    private static void TryScheduleLinkPrompt()
    {
        if (!LevelSelectionState.IsSelectionPending) return;      // a run is launching, not the menu
        if (!LinkPromptOwesAVisit()) return;
        OnlineService.Run(LinkPromptCo());
    }

    private static IEnumerator LinkPromptCo()
    {
        // Let the menu build and any unlock reveal arm first (both happen this frame).
        yield return null;
        yield return null;
        if (ProgressStore.WasLinkPromptShown()) yield break;
        if (!LevelSelectionState.IsSelectionPending) yield break;
        // An unlock reveal owns this menu return - the prompt simply waits for a later visit.
        if (UnlockRevealPending.PeekLevelId() != null) yield break;
        if (UnityEngine.Object.FindFirstObjectByType<MenuUnlockRevealRunner>() != null) yield break;
        // So does the dev letter (DEVLETTER.md beat 1): it explains the systems that just
        // switched on, and one-shots never stack - the prompt takes the NEXT visit.
        if (DevLetterOwnsAVisit()) yield break;
        ShowLinkPromptCard();
    }

    private static void ShowLinkPromptCard()
    {
        // Marked on SHOW, not on interaction: once-ever is the contract, and a force-quit
        // mid-card must not turn the prompt into a recurring one.
        ProgressStore.MarkLinkPromptShown();
        BuildSignInSheet("LATER");
    }

    /// <summary>The sign-in sheet on demand (Profile button, claim-modal footer). Marks the
    /// one-time prompt as shown too - a player who opened the pitch themselves never needs
    /// the automatic one.</summary>
    private static void OpenSignInSheet()
    {
        ProgressStore.MarkLinkPromptShown();
        BuildSignInSheet("CLOSE");
    }

    private static void BuildSignInSheet(string dismissLabel)
    {
        if (GameObject.Find("Link Prompt") != null) return; // double-tap / multitouch guard
        GameObject overlay = RuntimeUiKit.CreateOverlayCanvas("Link Prompt", 5900);
        void Close() => UnityEngine.Object.Destroy(overlay);

        Image backdrop = CreateImage(overlay.transform, "Backdrop", null, new Color(0.02f, 0.02f, 0.03f, 0.85f));
        Stretch(backdrop.rectTransform);
        backdrop.raycastTarget = true;
        Button backdropButton = backdrop.gameObject.AddComponent<Button>();
        backdropButton.transition = Selectable.Transition.None;
        backdropButton.onClick.AddListener(Close);

        const float W = 820f;
        const float H = 720f;
        const float pad = 48f;
        const float contentW = W - pad * 2f;
        RectTransform panel = CreateRect(overlay.transform, "Panel",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(W, H));
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = RuntimeSprites.RoundedPanel();
        panelImage.type = Image.Type.Sliced;
        panelImage.color = new Color(0.075f, 0.065f, 0.058f, 1f);
        panelImage.raycastTarget = true;
        RuntimeUiKit.AddOutline(panel, GoldOutline(0.3f));

        Image ring = CreateImage(panel, "AvatarRing", MenuSprites.CircleBadge(
            new Color(0.12f, 0.11f, 0.09f, 1f), WithAlpha(GoldBase, 0.55f)), Color.white);
        SetRect(ring.rectTransform, new Vector2(0f, -40f), new Vector2(104f, 104f), new Vector2(0.5f, 1f));
        ring.rectTransform.pivot = new Vector2(0.5f, 1f);
        Image person = CreateImage(ring.transform, "Person", MenuSprites.Person(WithAlpha(GoldBase, 0.85f)), Color.white);
        person.preserveAspect = true;
        SetCenteredAt(person.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(52f, 52f));

        TextMeshProUGUI title = CreateTmp(panel, "Title", "DON'T LOSE YOUR PROGRESS", 36, TextPrimary,
            TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -168f), new Vector2(contentW, 46f), new Vector2(0.5f, 1f));
        title.characterSpacing = 2f;

        CreateTmp(panel, "Body",
            "SIGN IN TO KEEP YOUR PROGRESS ON EVERY DEVICE.\nWITHOUT IT, UNINSTALLING LOSES EVERYTHING.", 20,
            WithAlpha(TextMuted, 0.9f), TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -226f), new Vector2(contentW, 62f), new Vector2(0.5f, 1f));

        TextMeshProUGUI status = CreateTmp(panel, "Status", string.Empty, 18,
            WithAlpha(GoldBase, 0.9f), TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -302f), new Vector2(contentW, 26f), new Vector2(0.5f, 1f));

        void LinkResult(bool ok, string message)
        {
            if (overlay == null || status == null) return;
            if (ok)
            {
                // A real link (mobile plugins) invalidates every open identity surface's
                // captured guest state - tear them down and rebuild the menu fresh.
                GameObject claimModal = GameObject.Find("Claim Name");
                if (claimModal != null) UnityEngine.Object.Destroy(claimModal);
                UnityEngine.Object.Destroy(overlay);
                BuildMenu();
                return;
            }
            status.text = string.IsNullOrEmpty(message) ? string.Empty : message.ToUpperInvariant();
        }

        BuildLinkButton(panel, "Apple", "SIGN IN WITH APPLE", new Vector2(pad, -352f), contentW,
            () => OnlineService.LinkWithApple(LinkResult));
        BuildLinkButton(panel, "Google", "SIGN IN WITH GOOGLE", new Vector2(pad, -456f), contentW,
            () => OnlineService.LinkWithGoogle(LinkResult));

        // LATER/CLOSE: a quiet exit, not a punished one.
        TextMeshProUGUI later = CreateTmp(panel, "Later", dismissLabel, 22, WithAlpha(TextMuted, 0.8f),
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, 44f), new Vector2(240f, 72f), new Vector2(0.5f, 0f));
        later.rectTransform.pivot = new Vector2(0.5f, 0f);
        later.raycastTarget = true;
        Button laterButton = later.gameObject.AddComponent<Button>();
        laterButton.transition = Selectable.Transition.None;
        laterButton.onClick.AddListener(() => { SfxPlayer.Play("ui-button-click"); Close(); });
    }

    private static void BuildLinkButton(Transform panel, string name, string label,
        Vector2 anchoredPosition, float width, Action onClick)
    {
        Image bg = CreateImage(panel, name, RuntimeSprites.RoundedPanel(), new Color(0.13f, 0.12f, 0.10f, 1f));
        bg.type = Image.Type.Sliced;
        SetRect(bg.rectTransform, anchoredPosition, new Vector2(width, 92f), new Vector2(0f, 1f));
        bg.raycastTarget = true;
        RuntimeUiKit.AddOutline(bg.transform, GoldOutline(0.35f));
        CreateTmp(bg.transform, "Label", label, 26, TextPrimary,
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        Button button = bg.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        button.onClick.AddListener(() => { SfxPlayer.Play("ui-button-click"); onClick?.Invoke(); });
    }
}
