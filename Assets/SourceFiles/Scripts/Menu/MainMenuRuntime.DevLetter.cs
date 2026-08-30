using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static RuntimeUiKit;

// The solo-dev letter and the review ask (DEVLETTER.md beats 1 & 3 - that file is the
// binding design; SHOP.md stays the monetization contract). Beat 1: a one-shot modal at
// first Chapter 2 completion (moved with the meta gate 2026-08-23), landing the moment
// the attempts meter and shop appear -
// inoculation, not sale: no price, no BUY, no review button, one KEEP PLAYING exit and
// every claim in it literally true in-game. Beat 3: the one-lifetime silent review-API
// call at first Chapter 3 completion (StoreReview owns the platform plumbing).
// (partial of MainMenuRuntime - same class as the other menu surfaces.)
public static partial class MainMenuRuntime
{
    /// <summary>The premium one-time-purchase line, shared by every surface that renders
    /// it (Profile premium card, refill offer) - one copy, one future tweak. Was the
    /// DEVLETTER.md beat-2 microcopy ("ONE PURCHASE, SUPPORTS ONE DEVELOPER") until Nick
    /// cut it 2026-08-30 ("sounds dumb") - the line's job is telling the buyer it is a
    /// one-time purchase, not a tip jar.</summary>
    private const string DevSupportLine = "ONE PURCHASE, YOURS FOREVER";

    private static bool _devBeatsSubscribed;
    // Editor sessions show the letter without touching the save: marking the real flag
    // from a play-mode test consumes the once-ever showing for the device build (and the
    // cloud merge is max, so it never un-consumes - review 2026-08-22, it happened).
    // Static = once per editor play session; wiped by the domain reload on play enter.
    private static bool _devLetterShownThisEditorSession;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDevBeatsForPlayMode()
    {
        _devBeatsSubscribed = false;
        _devLetterShownThisEditorSession = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void HookDevBeats()
    {
        if (!_devBeatsSubscribed)
        {
            // Menu returns are scene reloads and RuntimeInitialize hooks fire once per
            // session - sceneLoaded sees every later menu build (the link-prompt pattern).
            SceneManager.sceneLoaded += (_, _) => TryScheduleDevBeats();
            _devBeatsSubscribed = true;
        }
        TryScheduleDevBeats();
    }

    /// <summary>Does the letter still owe this player its one showing? The link prompt
    /// defers its visit while this is true - one-shots never stack (the reveal-defer
    /// precedent), and the letter outranks the prompt because it explains the systems
    /// that just switched on. Flag first: MetaEnabled walks the chapter list.</summary>
    private static bool DevLetterOwnsAVisit()
        => (Application.isEditor ? !_devLetterShownThisEditorSession : !ProgressStore.WasDevLetterShown())
           && AttemptsService.MetaEnabled;

    /// <summary>Is the one-lifetime review ask still owed? (StoreReview.Asked carries the
    /// editor-session vs. real-save distinction.) Flag first: the chapter walk is O(levels)
    /// and this runs on every scene load forever. The milestone is the first CHAPTER 3
    /// completion - one chapter after the letter/meter unlock (which moved to chapter 2
    /// on 2026-08-23), preserving DEVLETTER.md's spacing: the letter explains the systems
    /// at their unlock, the review ask fires alone at the NEXT emotional peak.</summary>
    private static bool ReviewAskOwed()
        => !StoreReview.Asked && ThirdChapterCompleted();

    private static bool ThirdChapterCompleted()
    {
        ChapterDefinition[] chapters = Campaign.LoadChaptersInOrder();
        return chapters.Length > 2 && Campaign.IsChapterCompleted(chapters[2]);
    }

    private static void TryScheduleDevBeats()
    {
        if (!LevelSelectionState.IsSelectionPending) return;   // a run is launching, not the menu
        if (!DevLetterOwnsAVisit() && !ReviewAskOwed()) return;

        // Scene-local host: if the player taps into a run mid-wait the host dies with
        // the menu and the beat simply retries on the next visit.
        GameObject host = new GameObject("[DevBeats]");
        host.hideFlags = HideFlags.HideInHierarchy;
        host.AddComponent<DevBeatsRunner>();
    }

    private sealed class DevBeatsRunner : MonoBehaviour
    {
        private IEnumerator Start()
        {
            // Let the menu build and any unlock reveal ARM first (both happen this frame).
            yield return null;
            yield return null;
            // The reveal owns the screen while it plays. The letter WAITS it out instead of
            // deferring a whole visit (the link prompt's move): its job is to land the
            // moment the meter appears, and that moment is this menu return. The refill
            // offer shares the letter's canvas sort order (5900) and can open inside the
            // settle window (out-of-attempts player taps a level) - wait that out too
            // rather than stacking two full-screen modals (review 2026-08-22).
            while (UnlockRevealPending.PeekLevelId() != null
                   || Object.FindFirstObjectByType<MenuUnlockRevealRunner>() != null
                   || GameObject.Find("Refill Offer") != null)
            {
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.35f);    // menu runs at timeScale 0
            if (LevelSelectionState.IsSelectionPending && GameObject.Find("Refill Offer") == null)
            {
                if (DevLetterOwnsAVisit())
                {
                    ShowDevLetter();
                }
                // The review ask never shares a visit with another one-shot: the letter
                // takes this visit (else-if), and if the sign-in card is still owed it
                // shows at frame +2 - the OS review dialog must not land on top of it,
                // so the ask simply waits for a later visit (review 2026-08-22).
                else if (ReviewAskOwed() && !LinkPromptOwesAVisit())
                {
                    // Beat 3 is a silent API call - the OS owns everything the player
                    // sees, and it may choose to show nothing (that still counts).
                    StoreReview.RequestReviewOnce();
                }
            }
            Destroy(gameObject);
        }
    }

    // ---- the letter itself -------------------------------------------------------------

    private static void ShowDevLetter()
    {
        if (GameObject.Find("Dev Letter") != null) return;   // double-schedule guard
        // Marked on SHOW, not on dismissal: once-ever is the contract, and a force-quit
        // mid-letter must not turn it into a recurring one (the link-prompt rule).
        // Editor play sessions mark only the session static - the real save flag must
        // never be consumed (or cloud-max-merged) by a test.
        if (Application.isEditor) _devLetterShownThisEditorSession = true;
        else ProgressStore.MarkDevLetterShown();

        GameObject overlay = RuntimeUiKit.CreateOverlayCanvas("Dev Letter", 5900);
        void Close()
        {
            if (overlay != null) Object.Destroy(overlay);
        }

        // Tap-outside dismisses - the letter must never trap (DEVLETTER.md §2).
        Image backdrop = CreateImage(overlay.transform, "Backdrop", null, new Color(0.02f, 0.02f, 0.03f, 0.85f));
        Stretch(backdrop.rectTransform);
        backdrop.raycastTarget = true;
        Button backdropButton = backdrop.gameObject.AddComponent<Button>();
        backdropButton.transition = Selectable.Transition.None;
        backdropButton.onClick.AddListener(() => { SfxPlayer.Play("ui-button-click"); Close(); });

        const float W = 820f;
        const float pad = 48f;
        const float contentW = W - pad * 2f;
        // Height is measured from the body text below - a fixed height put the button
        // flush against "- Nick" the moment the copy grew (Nick 2026-08-22).
        RectTransform panel = CreateRect(overlay.transform, "Panel",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(W, 840f));
        Image panelImage = panel.gameObject.AddComponent<Image>();
        GameMenuStyle.StylePanel(panel.gameObject); // the one modal-panel treatment
        panelImage.raycastTarget = true;

        // The sign-in sheet's person badge: the letter IS a person, lead with that.
        Image ring = CreateImage(panel, "AvatarRing", MenuSprites.CircleBadge(
            new Color(0.12f, 0.11f, 0.09f, 1f), WithAlpha(MenuAccent, 0.55f)), Color.white);
        SetRect(ring.rectTransform, new Vector2(0f, -40f), new Vector2(104f, 104f), new Vector2(0.5f, 1f));
        ring.rectTransform.pivot = new Vector2(0.5f, 1f);
        Image person = CreateImage(ring.transform, "Person", MenuSprites.Person(WithAlpha(MenuAccent, 0.85f)), Color.white);
        person.preserveAspect = true;
        SetCenteredAt(person.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(52f, 52f));

        TextMeshProUGUI title = CreateTmp(panel, "Title", "FROM THE DEVELOPER", 34, TextPrimary,
            TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -168f), new Vector2(contentW, 44f), new Vector2(0.5f, 1f));
        title.characterSpacing = 3f;

        // Copy is Nick's, verbatim (DEVLETTER.md §2, rewritten 2026-08-22) - including
        // "If you can be bothered.", deliberate personality, not sloppiness. The rules
        // that still bind: every claim literally true in-game, the price is the LIVE
        // store price (localized tiers make a hardcoded number a lie somewhere), and the
        // review line is a standalone mention, never traded against the purchase.
        string letter =
            "Hey - I'm Nick, the developer of Hazard Heights.\n\n" +
            "The whole game is free and nothing is pay-to-win. When you run out of " +
            "lives, they refill on their own, you just wait a little.\n\n" +
            $"If you'd rather never wait, you can purchase the full game for " +
            $"{PremiumStore.PriceText} on the Profile page. It gets you unlimited " +
            "lives, offline play and no ads. One purchase, forever.\n\n" +
            "If you're enjoying the game, a quick review in the store helps me out " +
            "a lot too. If you can be bothered.\n\n" +
            "Thanks for playing.\n\n- Nick";
        TextMeshProUGUI body = CreateTmp(panel, "Body", letter, 27, WithAlpha(TextPrimary, 0.92f),
            TextAnchor.UpperLeft, FontStyle.Normal, RuntimeUiKit.DefaultFont,
            new Vector2(pad, -236f), new Vector2(contentW, 430f), new Vector2(0f, 1f));
        body.textWrappingMode = TextWrappingModes.Normal;   // CreateTmp defaults to NoWrap
        body.lineSpacing = 6f;

        // Size the sheet from the text it actually holds: 236 header block above, then the
        // measured body, a 32px breather, the 96px button and its 44px bottom margin. The
        // button anchors to the panel's bottom, so it rides the resize.
        float bodyH = Mathf.Ceil(body.GetPreferredValues(letter, contentW, 0f).y);
        body.rectTransform.sizeDelta = new Vector2(contentW, bodyH);
        panel.sizeDelta = new Vector2(W, 236f + bodyH + 32f + 96f + 44f);

        // One exit, styled like a primary - the letter asks for nothing.
        Image keepBg = CreateImage(panel, "KeepPlaying", MenuSprites.RoundedGradient(
            Color.Lerp(MenuAccent, Color.white, 0.12f), Color.Lerp(MenuAccent, Color.black, 0.22f)), Color.white);
        keepBg.type = Image.Type.Sliced;
        SetRect(keepBg.rectTransform, new Vector2(pad, 44f), new Vector2(contentW, 96f), new Vector2(0f, 0f));
        keepBg.raycastTarget = true;
        TextMeshProUGUI keepLabel = CreateTmp(keepBg.transform, "Label", "KEEP PLAYING", 30,
            new Color(0.10f, 0.08f, 0.03f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        keepLabel.characterSpacing = 3f;
        Button keepButton = keepBg.gameObject.AddComponent<Button>();
        keepButton.targetGraphic = keepBg;
        keepButton.onClick.AddListener(() => { SfxPlayer.Play("ui-button-click"); Close(); });

        // Android back dismisses too (never trap). The back button arrives as the Input
        // System's escape key; no other modal handles it, but this one is a full-screen
        // one-shot a brand-new player meets - it must yield to every exit they try.
        DevLetterBackCloser closer = overlay.AddComponent<DevLetterBackCloser>();
        closer.OnClose = () => { SfxPlayer.Play("ui-button-click"); Close(); };

        UiEntranceFx.Play(panel.gameObject, 0.02f);
    }

    private sealed class DevLetterBackCloser : MonoBehaviour
    {
        public System.Action OnClose;

        private void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) OnClose?.Invoke();
        }
    }
}
