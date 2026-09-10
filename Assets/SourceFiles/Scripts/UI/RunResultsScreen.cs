using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The end-of-run results card - game over AND level complete share one anatomy, at two
/// temperatures. The card leads with the ONE metric the level's goal cares about (blocks,
/// height, or waves - never both), counts it up, and lands it with a thump; a new personal
/// best gets a chapter-accent NEW BEST pill with a neutral reflected-light sweep,
/// otherwise the stored best shows as a quiet reference line - never as a shortfall.
///
/// Choreography runs on unscaled time (the completion screen opens while the game is
/// paused) and any tap before the buttons arrive fast-forwards to the final state, so a
/// retry is always one impatient tap away. Restraint per JUICE.md: no flashes, no chimes -
/// the count-up thump and material light sweep carry the record beat.
/// </summary>
public sealed class RunResultsScreen : MonoBehaviour
{
    public struct Content
    {
        public bool Victory;            // gold banked: the full victory treatment
        public bool IntroductionComplete; // gold celebration, one highlighted exit to the menu
        public ResultMetric Metric;     // the goal's one stat: run value + previous best
        public float EndlessHeight;     // > 0 on endless runs only: quiet secondary height line
        public int Coins;               // banked this run (incl. win bonus on victory); 0 hides the line
        public bool Boosted;            // run started with purchased supplies (SHOP.md §5) - the honesty tag
        public string PrimaryLabel;     // retry, continue, or first-clear progression
        public System.Action OnPrimary;
        public System.Action OnSecondary; // first-clear retry; otherwise the menu supplies its own exit
        public string UnlockMessage;    // first clear: the next level/chapter, or campaign complete
        public bool PrimaryReturnsToMenu; // first clear: menu primary, retry secondary
        public string VictorySentence;  // why keep playing (victory only)
        // ---- Medal ladder (LevelTiers). Null arrays = no medal row (Endless, no level).
        public MedalTier? TierEarnedThisRun; // highest tier NEWLY earned this run - drives the
                                             // "LEVEL COMPLETE - SILVER" kicker on a loss card
        public float[] TierThresholds;       // bronze/silver/gold goals in the metric's own unit
        public bool[] TierEarnedState;       // earned flags AFTER this run's writes
    }

    // Timeline (seconds, unscaled). Elements enter in reading order, buttons last, whole
    // sequence stays under ~2.5s - a results screen must never become a hurdle to retrying.
    private const float RevealSeconds = 0.28f;
    private const float KickerAt = 0.15f;
    private const float HeroAt = 0.35f;
    private const float CountStartAt = 0.45f;
    private const float CountSeconds = 0.9f;
    private const float RecordAt = 1.48f;   // a breath after the count lands (1.35)
    private const float DetailsAt = 1.62f;
    private const float CoinsAt = 1.75f;
    private const float PrimaryAt = 1.94f;
    private const float SecondaryAt = 2.02f;

    private const float PunchSeconds = 0.6f;
    // The medal drops, compresses on impact, then catches a single light sweep.
    // Earned-tier confetti and rays begin at the impact, .27 s after BadgeAt.
    private const float BadgeAt = 0.2f;
    private const float BadgePopSeconds = 0.6f;
    private const float BadgeSize = 210f;
    // The sanctioned reward-gold (golden brick, sheen) - one gold across the whole game.
    private static readonly Color Gold = GoldenBlockDirector.GoldTint;

    private static RunResultsScreen _active;

    private readonly struct Reveal
    {
        public Reveal(CanvasGroup group, float start, bool isButton)
        {
            Group = group;
            Start = start;
            IsButton = isButton;
        }

        public CanvasGroup Group { get; }
        public float Start { get; }
        public bool IsButton { get; }
    }

    private Content _content;
    private Image _backdrop;
    private RectTransform _panel;
    private MedalLightSweepFx _badgeSweep;
    private float _backdropAlpha;
    private readonly List<Reveal> _reveals = new List<Reveal>(10);
    private TextMeshProUGUI _hero;
    private RectTransform _badge; // the half-out tier cube; null on plain game-over cards
    private ResultsCelebrationFx _celebrationFx; // FastForward must skip its start delay too
    private float _clock;
    private float _endTime;
    private bool _landed;
    private float _punchAge;
    private bool _recordSfxPlayed;
    private bool _closing;

    /// <summary>Build and show the card. Replaces any card already on screen (a game over
    /// arriving over a stale victory card must win).</summary>
    public static void Show(Content content, bool muted = false)
    {
        if (_active != null) _active.CloseAndInvoke(null);

        RuntimeUiKit.EnsureEventSystem();
        // Victory sits below the game-over order so a later game over always covers it.
        GameObject root = RuntimeUiKit.CreateOverlayCanvas("Run Results", content.Victory ? 6500 : 7100);
        RunResultsScreen screen = root.AddComponent<RunResultsScreen>();
        _active = screen;
        screen._content = content;
        screen.Build();

        // Muted on in-place rebuilds (an ad refill re-rendering the card must not
        // replay the game-over sting the player already heard) and when the caller already
        // played the sting at the moment of death, ahead of the delayed card.
        if (!muted) PlaySting(content);
    }

    /// <summary>The end-of-run sting for this card. A loss that banked a medal this run gets the
    /// victory sting: the tower fell, but the headline is "LEVEL COMPLETE - {TIER}" and the
    /// game-over sting would talk over it.</summary>
    public static void PlaySting(Content content)
    {
        bool celebrate = content.Victory || content.TierEarnedThisRun.HasValue;
        SfxPlayer.Play(celebrate ? "ui-victory" : "game_over", celebrate ? 0.9f : 0.85f, 0f);
    }

    private void OnDestroy()
    {
        if (_active == this) _active = null;
    }

    // ---- construction ----------------------------------------------------------------------

    private void Build()
    {
        _backdrop = RuntimeUiKit.CreateBackdrop(transform, GameMenuStyle.BackdropColor);
        _backdropAlpha = _backdrop.color.a;
        SetBackdropAlpha(0f);
        // Any tap during the entrance fast-forwards to the final state (never blocks retrying).
        Button skip = _backdrop.gameObject.AddComponent<Button>();
        skip.transition = Selectable.Transition.None;
        skip.onClick.AddListener(FastForward);

        // The celebration layer (rays + confetti) must paint BETWEEN backdrop and panel, so it
        // is created before the panel and the burst spills out from behind the card.
        MedalTier? earnedTier = _content.TierEarnedThisRun;
        bool celebrate = earnedTier.HasValue;
        RectTransform fxLayer = null;
        if (celebrate)
        {
            fxLayer = RuntimeUiKit.CreateRect(transform, "CelebrationFx",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        }

        GameObject panel = RuntimeUiKit.CreateCenteredPanel(transform, new Vector2(660f, 100f));
        GameMenuStyle.StylePanel(panel);
        _panel = ModalPresentationFx.Frame(panel);
        panel.GetComponent<Image>().raycastTarget = false; // taps beside the rows reach the skip
        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.childControlHeight = true; // rows declare their height via LayoutElement
        layout.spacing = 12f;
        // The half-out badge claims the card's top band; the first row starts below it.
        if (celebrate) layout.padding.top = Mathf.RoundToInt(BadgeSize * 0.5f + 24f);
        ContentSizeFitter fitter = panel.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var ornaments = panel.transform.Find("ChapterOrnaments");
        if (ornaments != null) AddReveal(ornaments.gameObject, KickerAt);

        bool record = _content.Metric.IsNewRecord;

        if (celebrate)
        {
            BuildCelebration(panel, fxLayer, earnedTier.Value);
        }
        else
        {
            // Plain game over: the quiet in-panel kicker - the metric is the headline, not
            // the death. (A run that banked a medal gets the celebration header instead.)
            TextMeshProUGUI kicker = CreateRow(panel.transform,
                _content.Victory ? "LEVEL COMPLETE" : "GAME OVER", 30,
                _content.Victory ? GameMenuStyle.Accent : new Color(0.85f, 0.88f, 0.90f, 0.70f),
                42f, display: true);
            kicker.characterSpacing = 8f;
            AddReveal(kicker.gameObject, KickerAt);
        }

        // The boosted honesty tag (SHOP.md §5): a quiet chapter-accent line under the kicker so an
        // assisted run always says so - the score below belongs to the boosted board.
        if (_content.Boosted)
        {
            TextMeshProUGUI boosted = CreateRow(panel.transform, "BOOSTED RUN", 22,
                WithAlpha(GameMenuStyle.Accent, 0.85f), 30f, display: false);
            boosted.characterSpacing = 10f;
            AddReveal(boosted.gameObject, KickerAt);
        }

        TextMeshProUGUI metricLabel = CreateRow(panel.transform, _content.Metric.Label, 24,
            new Color(1f, 1f, 1f, 0.55f), 38f, display: false);
        metricLabel.characterSpacing = 14f;
        AddReveal(metricLabel.gameObject, HeroAt);

        _hero = CreateRow(panel.transform, _content.Metric.Format(0f), 132,
            RuntimeUiKit.TitleColor, 156f, display: true);
        RuntimeUiKit.AutoSize(_hero, 64f, 132f);
        if (celebrate)
        {
            // The hero number wears the tier's gradient: cream into the tier's light tone.
            RuntimeUiKit.ApplyHorizontalGradient(_hero,
                MedalStyle.HeroCream, MedalStyle.TierLight(earnedTier.Value));
        }
        AddReveal(_hero.gameObject, HeroAt);

        if (record) BuildNewBestPill(panel.transform);
        else if (_content.Metric.PreviousBest > 0f)
        {
            TextMeshProUGUI best = CreateRow(panel.transform,
                $"BEST  {_content.Metric.Format(_content.Metric.PreviousBest)}", 26,
                new Color(1f, 1f, 1f, 0.45f), 36f, display: false);
            best.characterSpacing = 4f;
            AddReveal(best.gameObject, RecordAt);
        }

        // The three-slot ladder row survives only on the PLAIN game-over card, where "silver
        // at 75 is still on the table" is the motivation to retry; a celebration card's story
        // is the badge + chip, and the row would double-tell it (screenshot is authoritative).
        if (_content.TierThresholds != null && _content.TierEarnedState != null && !celebrate)
        {
            BuildMedalRow(panel.transform);
        }

        if (_content.EndlessHeight > 0.05f)
        {
            AddReveal(CreateRow(panel.transform, $"HEIGHT  {_content.EndlessHeight:F1}m", 26,
                new Color(1f, 1f, 1f, 0.45f), 36f, display: false).gameObject, DetailsAt);
        }

        if (_content.Victory && !string.IsNullOrEmpty(_content.VictorySentence))
        {
            TextMeshProUGUI sentence = CreateRow(panel.transform, _content.VictorySentence, 27,
                GameMenuStyle.BodyText, 84f, display: false);
            sentence.textWrappingMode = TextWrappingModes.Normal;
            AddReveal(sentence.gameObject, DetailsAt);
        }

        if (_content.Coins > 0) BuildCoinsRow(panel.transform);

        if (!string.IsNullOrEmpty(_content.UnlockMessage))
        {
            TextMeshProUGUI unlocked = CreateRow(panel.transform, _content.UnlockMessage, 27,
                GameMenuStyle.Accent, 76f, display: false);
            unlocked.textWrappingMode = TextWrappingModes.Normal;
            AddReveal(unlocked.gameObject, DetailsAt);
        }

        // The lives line: a player weighing "Try Again" must see what it costs and what
        // they hold - the meter is otherwise invisible mid-run (Nick 2026-08-09).
        // Game over only: the victory card's primary is Keep Playing, which is free.
        if (!_content.Victory)
        {
            GameObject lives = RunLivesUi.BuildStatusRow(panel.transform);
            if (lives != null) AddReveal(lives, DetailsAt);
        }

        // Returning to the menu is always available; only retries need an attempt.
        bool outOfLives = !_content.Victory && !_content.PrimaryReturnsToMenu
            && RunLivesUi.OutOfLives;
        if (outOfLives)
        {
            // Zero lives: "Try Again" would only bounce to the menu after a doomed server
            // round trip - the one exit this screen must never take silently. Pitch the
            // refills instead; a successful one rebuilds this card with Try Again back.
            int before = panel.transform.childCount;
            int added = RunLivesUi.BuildOutOfLivesActions(panel.transform, () =>
            {
                // A slow claim can land after the player already left this screen -
                // never resurrect a game-over card over whatever they moved on to.
                if (this == null || _closing || _active != this) return;
                Show(_content, muted: true);
            });
            for (int i = 0; i < added; i++)
            {
                Transform action = panel.transform.GetChild(before + i);
                Button button = action.GetComponent<Button>();
                if (button != null) RoundButton(button);
                AddReveal(action.gameObject, PrimaryAt, isButton: true);
            }
            // Neither an ad nor a store on hand: say so, like the pause sheet does - the
            // ticking lives row above carries the regen countdown, and the card watches
            // for the meter to heal (regen or a late SSV grant) so Try Again can reappear
            // without the player doing anything.
            if (added == 0)
            {
                GameObject hint = CreateRow(panel.transform, "An attempt regenerates on the timer above.",
                    24, new Color(1f, 1f, 1f, 0.6f), 44f, display: false).gameObject;
                AddReveal(hint, PrimaryAt);
            }
            gameObject.AddComponent<AttemptsWatcher>().Screen = this;
        }
        else
        {
            Button primary = RuntimeUiKit.CreateButton(panel.transform,
                string.IsNullOrEmpty(_content.PrimaryLabel) ? "Try Again" : _content.PrimaryLabel, 120f, OnPrimaryClicked);
            GameMenuStyle.StyleButton(primary, primary: true);
            RoundButton(primary);
            AddReveal(primary.gameObject, PrimaryAt, isButton: true);
        }

        if (!_content.IntroductionComplete)
        {
            bool retry = _content.PrimaryReturnsToMenu;
            Button secondary = RuntimeUiKit.CreateButton(panel.transform,
                retry ? "Try Again" : "Back to Main Menu", 120f, OnSecondaryClicked);
            GameMenuStyle.StyleButton(secondary, primary: false);
            if (retry)
            {
                secondary.interactable = !RunLivesUi.OutOfLives;
                AttemptsWatcher watcher = gameObject.AddComponent<AttemptsWatcher>();
                watcher.Screen = this;
                watcher.RetryButton = secondary;
            }
            RoundButton(secondary);
            AddReveal(secondary.gameObject, SecondaryAt, isButton: true);
        }

        _endTime = SecondaryAt + RevealSeconds;
        ApplyTimeline(); // first frame: everything hidden, not one visible frame of raw layout
    }

    private void OnPrimaryClicked()
    {
        CloseAndInvoke(_content.OnPrimary);
    }

    private void OnSecondaryClicked()
    {
        if (_content.PrimaryReturnsToMenu)
        {
            // Recheck at the click: a server update may have changed the meter since Build.
            if (!RunLivesUi.OutOfLives) CloseAndInvoke(_content.OnSecondary);
            return;
        }
        CloseAndInvoke(() =>
        {
            SfxPlayer.Play("ui-leave-game");
            MainMenuRuntime.ReturnToMenu();
        });
    }

    // Destroy is deferred until frame end. Retire the card immediately so another pointer
    // or a late refill cannot invoke a second action or resurrect the screen being left.
    private void CloseAndInvoke(System.Action action)
    {
        if (!TryRetire()) return;
        Destroy(gameObject);
        action?.Invoke();
    }

    private bool TryRetire()
    {
        if (_closing) return false;
        _closing = true;
        if (_active == this) _active = null;
        gameObject.SetActive(false);
        return true;
    }

    /// <summary>Follow regen and server updates. A first-clear card only toggles its
    /// secondary retry, preserving the menu action and entrance. A refill-only loss card
    /// rebuilds once an attempt arrives so its primary retry can return.</summary>
    private sealed class AttemptsWatcher : MonoBehaviour
    {
        public RunResultsScreen Screen;
        public Button RetryButton;
        private float _next;

        private void Update()
        {
            if (Screen == null || Screen._closing || _active != Screen || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            bool outOfLives = RunLivesUi.OutOfLives;
            if (RetryButton != null)
            {
                RetryButton.interactable = !outOfLives;
            }
            else if (!outOfLives)
            {
                enabled = false;
                Show(Screen._content, muted: true);
            }
        }
    }

    /// <summary>The tier celebration (modal redesign, Nick 2026-08-29): the tier cube
    /// half-in/half-out over the card's top edge, the gradient "{TIER} TIER REACHED" chip as
    /// the card's first row, and the confetti + ray layer behind the card. No header outside
    /// the card (cut - it collided with the HUD). Shared verbatim by the gold victory card and
    /// the bronze/silver on-death card - one treatment, different tier data.</summary>
    private void BuildCelebration(GameObject panel, RectTransform fxLayer, MedalTier tier)
    {
        // No header outside the card (a screen-top "LEVEL COMPLETE" line collided with the
        // HUD and read as clutter - Nick 2026-08-29): the badge + chip carry the whole story.

        // The badge: the tier cube centered ON the card's top edge - half in, half out. It
        // rides the panel (ignoreLayout) so the dynamic card height can never detach it;
        // ApplyTimeline drives its weighted drop and compression from BadgeAt.
        GameObject badge = new GameObject("TierBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _badge = (RectTransform)badge.transform;
        _badge.SetParent(panel.transform, false);
        _badge.anchorMin = _badge.anchorMax = new Vector2(0.5f, 1f);
        _badge.pivot = new Vector2(0.5f, 0.5f);
        _badge.anchoredPosition = Vector2.zero;
        _badge.sizeDelta = new Vector2(BadgeSize, BadgeSize);
        _badge.localScale = Vector3.zero;
        badge.AddComponent<LayoutElement>().ignoreLayout = true;
        Image badgeImage = badge.GetComponent<Image>();
        badgeImage.sprite = MedalStyle.Sprite(tier, earned: true);
        badgeImage.color = MedalStyle.IconTint(earned: true);
        badgeImage.preserveAspect = true;
        badgeImage.raycastTarget = false;
        _badgeSweep = MedalLightSweepFx.Attach(badgeImage, BadgeAt + .42f);

        // "{TIER} TIER REACHED" - dark text on the tier's gradient capsule, the card's first row.
        GameObject chipRow = new GameObject("ChipRow", typeof(RectTransform));
        chipRow.transform.SetParent(panel.transform, false);
        chipRow.AddComponent<LayoutElement>().preferredHeight = 56f;
        RectTransform chip = RuntimeUiKit.CreateRect(chipRow.transform, "TierChip",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, MedalStyle.ChipSize); // the sprite is generated at exactly this size
        Image chipImage = chip.gameObject.AddComponent<Image>();
        chipImage.color = Color.clear;
        chipImage.raycastTarget = false;
        TextMeshProUGUI chipLabel = RuntimeUiKit.CreateTmp(chip, "Label",
            _content.IntroductionComplete ? "TUTORIAL COMPLETE" : $"{MedalStyle.DisplayName(tier)} TIER REACHED", 24, GameMenuStyle.BodyText,
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        chipLabel.font = RuntimeUiKit.TmpTitleFont;
        chipLabel.characterSpacing = 5f;
        AddReveal(chipRow, KickerAt);

        // Confetti + rays begin on the badge's impact frame, behind the card.
        _celebrationFx = ResultsCelebrationFx.Attach(fxLayer, _badge, tier, BadgeAt + .27f);
    }

    // The card's centered horizontal row scaffold (medal row, coins row): fixed-size
    // children centered as one group.
    private static RectTransform CreateCenteredRow(Transform parent, string name, float height, float spacing)
    {
        GameObject row = new GameObject(name, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        row.AddComponent<LayoutElement>().preferredHeight = height;

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = spacing;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return (RectTransform)row.transform;
    }

    // The banked-coins line: the coin icon + "+N", no word (the icon IS the word - Nick
    // 2026-08-29). CoinHud owns the art + fallback pair.
    private void BuildCoinsRow(Transform parent)
    {
        RectTransform row = CreateCenteredRow(parent, "CoinsRow", 44f, 10f);

        Sprite coin = CoinHud.CoinSprite(out bool isFallback);
        Image iconImage = RuntimeUiKit.CreateImage(row, "Coin", coin,
            isFallback ? CoinHud.FallbackCoinGold : Color.white);
        iconImage.rectTransform.sizeDelta = new Vector2(34f, 34f);
        iconImage.preserveAspect = true;

        TextMeshProUGUI amount = RuntimeUiKit.CreateTmp(row, "Amount",
            $"+{_content.Coins}", 30, Gold, TextAnchor.MiddleLeft, FontStyle.Bold,
            RuntimeUiKit.TitleFont);
        amount.rectTransform.sizeDelta = new Vector2(0f, 40f);
        amount.textWrappingMode = TextWrappingModes.NoWrap;
        amount.overflowMode = TextOverflowModes.Overflow;
        // Self-sizing width so the icon+amount pair truly centers (the layout group only
        // centers what declares its size).
        amount.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        AddReveal(row.gameObject, CoinsAt);
    }

    // A single centered text row, height-driven for the panel's vertical layout.
    private static TextMeshProUGUI CreateRow(Transform parent, string text, int size, Color color,
        float height, bool display)
    {
        TextMeshProUGUI tmp = RuntimeUiKit.CreateTmp(parent, "Row", text, size, color,
            TextAnchor.MiddleCenter, FontStyle.Normal, RuntimeUiKit.TitleFont);
        if (display) tmp.font = RuntimeUiKit.TmpTitleFont; // Manrope: shared with the gameplay HUD
        tmp.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        return tmp;
    }

    // The record marker: a borderless dark pill, chapter-accent type and a restrained
    // neutral sweep. Reward gold belongs to currency and earned medal material.
    private void BuildNewBestPill(Transform parent)
    {
        GameObject row = new GameObject("NewBestRow", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        row.AddComponent<LayoutElement>().preferredHeight = 64f;

        RectTransform pill = RuntimeUiKit.CreateRect(row.transform, "NewBest",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(264f, 56f));
        Image fill = pill.gameObject.AddComponent<Image>();
        fill.sprite = RuntimeSprites.RoundedPanel();
        fill.type = Image.Type.Sliced;
        fill.color = new Color(.105f, .105f, .115f, 1f);
        fill.raycastTarget = false;

        TextMeshProUGUI label = RuntimeUiKit.CreateTmp(pill, "Label", "NEW BEST", 26, GameMenuStyle.Accent,
            TextAnchor.MiddleCenter, FontStyle.Normal, RuntimeUiKit.TitleFont);
        label.font = RuntimeUiKit.TmpTitleFont;
        label.characterSpacing = 8f;

        AbilityCardShine.Attach(pill, new Color(1f, 1f, 1f, .18f), 2.8f);
        AddReveal(row, RecordAt);
    }

    // The medal ladder: three slots with the tier's goal under each - earned in full color,
    // unearned as the locked slate so the row always shows what is still on the table. The
    // tier earned THIS run gets the card-shine sweep, the same visual word as NEW BEST.
    private void BuildMedalRow(Transform parent)
    {
        RectTransform row = CreateCenteredRow(parent, "MedalRow", 104f, 34f);

        // The arrays are the source of truth for the ladder's size (the controller fills them
        // from LevelTiers.TierCount) - no literal rung count here.
        for (int i = 0; i < _content.TierThresholds.Length && i < _content.TierEarnedState.Length; i++)
        {
            MedalTier tier = (MedalTier)i;
            bool earned = _content.TierEarnedState[i];

            RectTransform cell = RuntimeUiKit.CreateRect(row.transform, $"Medal{tier}",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(110f, 96f));

            RectTransform icon = RuntimeUiKit.CreateRect(cell, "Icon",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -2f), new Vector2(54f, 54f));
            Image image = icon.gameObject.AddComponent<Image>();
            image.sprite = MedalStyle.Sprite(tier, earned);
            image.color = MedalStyle.IconTint(earned);
            image.preserveAspect = true;
            image.raycastTarget = false;

            string goal = _content.Metric.IsMeters
                ? $"{Mathf.RoundToInt(_content.TierThresholds[i])}m"
                : Mathf.RoundToInt(_content.TierThresholds[i]).ToString();
            RuntimeUiKit.CreateTmp(cell, "Goal", goal, 22,
                earned ? MedalStyle.TierColor(tier) : WithAlpha(MedalStyle.Unearned, 0.8f),
                TextAnchor.LowerCenter, FontStyle.Normal, RuntimeUiKit.TitleFont,
                new Vector2(0f, 2f), new Vector2(110f, 34f), new Vector2(0.5f, 0f));

            if (earned && _content.TierEarnedThisRun.HasValue && tier == _content.TierEarnedThisRun.Value)
            {
                AbilityCardShine.Attach(icon,
                    WithAlpha(Color.Lerp(MedalStyle.TierColor(tier), Color.white, 0.5f), 0.30f), 1.8f);
            }
        }

        AddReveal(row.gameObject, RecordAt);
    }

    private static void RoundButton(Button button)
    {
        ModalPresentationFx.StyleAction(button);
        ((RectTransform)button.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 120f);
        var layout = button.GetComponent<LayoutElement>();
        if (layout != null) layout.preferredHeight = 120f;
        Image image = button.GetComponent<Image>();
        image.sprite = RuntimeSprites.RoundedPanel();
        image.type = Image.Type.Sliced;
    }

    private void AddReveal(GameObject target, float start, bool isButton = false)
    {
        CanvasGroup group = target.GetComponent<CanvasGroup>();
        if (group == null) group = target.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
        _reveals.Add(new Reveal(group, start, isButton));
    }

    private static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }

    // ---- choreography ------------------------------------------------------------------------

    private void Update()
    {
        _clock += Time.unscaledDeltaTime;
        ApplyTimeline();

        // Everything (reveals, count-up, punch, record beat) settles by _endTime; after the
        // final frame the card is static, so stop paying the per-frame tween/text cost while
        // the player reads it. Button clicks and FastForward don't need this loop.
        if (_clock >= _endTime) enabled = false;
    }

    private void ApplyTimeline()
    {
        ModalPresentationFx.Pose(_panel, _clock);
        if (_clock < 0.3f || _backdrop.color.a < _backdropAlpha)
        {
            SetBackdropAlpha(_backdropAlpha * Mathf.Clamp01(_clock / 0.25f));
        }

        for (int i = 0; i < _reveals.Count; i++)
        {
            Reveal reveal = _reveals[i];
            if (reveal.Group == null) continue;
            float t = Mathf.Clamp01((_clock - reveal.Start) / RevealSeconds);
            reveal.Group.alpha = 1f - Mathf.Pow(1f - t, 3f);
            // Same curve as UiEntranceFx (the house UI arrival); hand-rolled here only because
            // the tap-to-skip fast-forward must be able to jump every element to its end state.
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            reveal.Group.transform.localScale = Vector3.one * Mathf.Lerp(reveal.IsButton ? .985f : .96f, 1f, ease);
            if (reveal.IsButton && t >= 1f && !reveal.Group.interactable)
            {
                reveal.Group.interactable = true;
                reveal.Group.blocksRaycasts = true;
            }
        }

        TickBadge();
        TickHero();
    }

    // The badge's weighted drop and compression, computed from the clock so
    // FastForward lands it at rest like every other element. Nulled once settled: the card
    // ticks on well past the pop, and re-writing an identical scale re-dirties the layout.
    private void TickBadge()
    {
        if (_badge == null) return;
        float t = (_clock - BadgeAt) / BadgePopSeconds;
        float u = Mathf.Clamp01(t);
        float drop = Mathf.Clamp01(u / .45f);
        float settle = Mathf.Clamp01((u - .45f) / .55f);
        float compression = Mathf.Sin(settle * Mathf.PI * 2f) * Mathf.Exp(-settle * 4f);
        float size = Mathf.Lerp(.76f, 1f, drop * drop);
        _badge.anchoredPosition = new Vector2(0f, 74f * (1f - drop * drop));
        _badge.localScale = _clock < BadgeAt ? Vector3.zero : new Vector3(size * (1f + .12f * compression), size * (1f - .10f * compression), 1f);
        if (t >= 1f) _badge = null;
    }

    // Count 0 -> value linearly (an eased number reads as broken), then land with the game's
    // thump language and an elastic settle - the physical vocabulary, not a fanfare.
    private void TickHero()
    {
        if (_hero == null) return;

        float value = _content.Metric.Value;
        if (value <= 0f) return; // nothing to count; the hero just shows 0

        float t = Mathf.Clamp01((_clock - CountStartAt) / CountSeconds);
        if (t < 1f || !_landed) _hero.text = _content.Metric.Format(value * t); // final frame included, then frozen

        if (t >= 1f && !_landed)
        {
            _landed = true;
            _punchAge = 0f;
            SfxPlayer.Play("impact_soft_01", 0.5f, 0.03f);
        }

        if (_landed && _punchAge < PunchSeconds)
        {
            _punchAge += Time.unscaledDeltaTime;
            float scale = _punchAge >= PunchSeconds ? 1f : FxKit.Elastic(_punchAge, 0.09f, 8f, 18f);
            _hero.rectTransform.localScale = new Vector3(scale, scale, 1f);
        }

        // The record moment gets its one quiet clink - the coin vocabulary, no fanfare.
        if (_content.Metric.IsNewRecord && !_recordSfxPlayed && _clock >= RecordAt)
        {
            _recordSfxPlayed = true;
            SfxPlayer.Play("coin_settle_01", 0.35f, 0.05f);
        }
    }

    private void SetBackdropAlpha(float alpha)
    {
        if (_backdrop == null) return;
        Color color = _backdrop.color;
        color.a = alpha;
        _backdrop.color = color;
    }

    // Jump the whole entrance to its final state: full card, live buttons, no late sounds.
    private void FastForward()
    {
        if (_clock >= _endTime) return;
        _clock = _endTime;
        // The fx runs its own real-time clock: without this, a skip-tap in the first frames
        // lands the final card and THEN the confetti erupts, out of sync with everything.
        if (_celebrationFx != null) _celebrationFx.SkipDelay();
        if (_badgeSweep != null) _badgeSweep.Finish();
        _landed = true;
        _punchAge = PunchSeconds;
        _recordSfxPlayed = true;
        if (_hero != null)
        {
            _hero.text = _content.Metric.Format(_content.Metric.Value);
            _hero.rectTransform.localScale = Vector3.one;
        }
        ApplyTimeline();
        // Subtracting the final reveal's start can round just below its duration.
        // A skip must expose every action immediately, including within this frame.
        foreach (var reveal in _reveals)
        {
            if (reveal.Group == null) continue;
            reveal.Group.alpha = 1f;
            reveal.Group.transform.localScale = Vector3.one;
            if (!reveal.IsButton) continue;
            reveal.Group.interactable = true;
            reveal.Group.blocksRaycasts = true;
        }
    }
}
