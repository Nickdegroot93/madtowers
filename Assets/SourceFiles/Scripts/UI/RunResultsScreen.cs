using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The end-of-run results card - game over AND level complete share one anatomy, at two
/// temperatures. The card leads with the ONE metric the level's goal cares about (blocks,
/// height, or waves - never both), counts it up, and lands it with a thump; a new personal
/// best gets the gold treatment (gold number + NEW BEST pill with the card-shine sweep),
/// otherwise the stored best shows as a quiet reference line - never as a shortfall.
///
/// Choreography runs on unscaled time (the completion screen opens while the game is
/// paused) and any tap before the buttons arrive fast-forwards to the final state, so a
/// retry is always one impatient tap away. Restraint per JUICE.md: no flashes, no chimes -
/// the count-up thump and the gold sheen are the whole show.
/// </summary>
public sealed class RunResultsScreen : MonoBehaviour
{
    public struct Content
    {
        public bool Victory;            // level complete (hot) vs game over (warm)
        public ResultMetric Metric;     // the goal's one stat: run value + previous best
        public bool GoalReached;        // show the "GOAL ... REACHED" line (level was made)
        public float EndlessHeight;     // > 0 on endless runs only: quiet secondary height line
        public int Coins;               // banked this run (incl. win bonus on victory); 0 hides the line
        public bool Boosted;            // run started with purchased supplies (SHOP.md §5) - the honesty tag
        public string PrimaryLabel;     // "Try Again" / "Keep Playing"
        public System.Action OnPrimary;
        public string VictorySentence;  // why keep playing (victory only)
    }

    // Timeline (seconds, unscaled). Elements enter in reading order, buttons last, whole
    // sequence stays under ~2.5s - a results screen must never become a hurdle to retrying.
    private const float RevealSeconds = 0.28f;
    private const float KickerAt = 0.15f;
    private const float HeroAt = 0.35f;
    private const float CountStartAt = 0.45f;
    private const float CountSeconds = 0.9f;
    private const float RecordAt = 1.6f;   // a breath after the count lands (1.35)
    private const float DetailsAt = 1.8f;
    private const float CoinsAt = 1.95f;
    private const float PrimaryAt = 2.1f;
    private const float SecondaryAt = 2.2f;

    private const float PunchSeconds = 0.6f;
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
    private float _backdropAlpha;
    private readonly List<Reveal> _reveals = new List<Reveal>(10);
    private TextMeshProUGUI _hero;
    private float _clock;
    private float _endTime;
    private bool _landed;
    private float _punchAge;
    private bool _recordSfxPlayed;

    /// <summary>Build and show the card. Replaces any card already on screen (a game over
    /// arriving over a stale victory card must win).</summary>
    public static void Show(Content content, bool muted = false)
    {
        if (_active != null) Destroy(_active.gameObject);

        RuntimeUiKit.EnsureEventSystem();
        // Victory sits below the game-over order so a later game over always covers it.
        GameObject root = RuntimeUiKit.CreateOverlayCanvas("Run Results", content.Victory ? 6500 : 7100);
        RunResultsScreen screen = root.AddComponent<RunResultsScreen>();
        _active = screen;
        screen._content = content;
        screen.Build();

        // Muted on in-place rebuilds (an ad refill re-rendering the card must not
        // replay the game-over sting the player already heard).
        if (!muted)
        {
            SfxPlayer.Play(content.Victory ? "ui-victory" : "game_over", content.Victory ? 0.9f : 0.85f, 0f);
        }
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

        GameObject panel = RuntimeUiKit.CreateCenteredPanel(transform, new Vector2(660f, 100f));
        GameMenuStyle.StylePanel(panel);
        panel.GetComponent<Image>().raycastTarget = false; // taps beside the rows reach the skip
        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.childControlHeight = true; // rows declare their height via LayoutElement
        layout.spacing = 16f;
        ContentSizeFitter fitter = panel.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        bool record = _content.Metric.IsNewRecord;

        // Kicker: quiet on game over (the metric is the headline, not the death), loud on victory.
        TextMeshProUGUI kicker = CreateRow(panel.transform, _content.Victory ? "LEVEL COMPLETE" : "GAME OVER",
            _content.Victory ? 44 : 30,
            _content.Victory ? GameMenuStyle.Accent : new Color(0.85f, 0.88f, 0.90f, 0.70f),
            _content.Victory ? 60f : 42f, display: true);
        kicker.characterSpacing = 8f;
        AddReveal(kicker.gameObject, KickerAt);

        // The boosted honesty tag (SHOP.md §5): a quiet gold line under the kicker so an
        // assisted run always says so - the score below belongs to the boosted board.
        if (_content.Boosted)
        {
            TextMeshProUGUI boosted = CreateRow(panel.transform, "BOOSTED RUN", 22,
                WithAlpha(Gold, 0.85f), 30f, display: false);
            boosted.characterSpacing = 10f;
            AddReveal(boosted.gameObject, KickerAt);
        }

        TextMeshProUGUI metricLabel = CreateRow(panel.transform, _content.Metric.Label, 24,
            new Color(1f, 1f, 1f, 0.55f), 32f, display: false);
        metricLabel.characterSpacing = 14f;
        AddReveal(metricLabel.gameObject, HeroAt);

        _hero = CreateRow(panel.transform, _content.Metric.Format(0f), 116,
            record ? Gold : RuntimeUiKit.TitleColor, 148f, display: true);
        RuntimeUiKit.AutoSize(_hero, 64f, 116f);
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

        if (_content.GoalReached && !string.IsNullOrEmpty(_content.Metric.TargetText))
        {
            TextMeshProUGUI goal = CreateRow(panel.transform,
                $"GOAL {_content.Metric.TargetText} {(_content.Victory ? "CLEARED" : "REACHED")}", 24,
                WithAlpha(GameMenuStyle.Accent, 0.9f), 32f, display: false);
            goal.characterSpacing = 6f;
            AddReveal(goal.gameObject, DetailsAt);
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

        if (_content.Coins > 0)
        {
            AddReveal(CreateRow(panel.transform, $"+{_content.Coins} coins", 28, Gold, 40f,
                display: false).gameObject, CoinsAt);
        }

        // The lives line: a player weighing "Try Again" must see what it costs and what
        // they hold - the meter is otherwise invisible mid-run (Nick 2026-08-09).
        // Game over only: the victory card's primary is Keep Playing, which is free.
        if (!_content.Victory)
        {
            GameObject lives = RunLivesUi.BuildStatusRow(panel.transform);
            if (lives != null) AddReveal(lives, DetailsAt);
        }

        bool outOfLives = !_content.Victory && RunLivesUi.OutOfLives;
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
                if (this == null || _active != this) return;
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
                GameObject hint = CreateRow(panel.transform, "A life regenerates on the timer above.",
                    24, new Color(1f, 1f, 1f, 0.6f), 44f, display: false).gameObject;
                AddReveal(hint, PrimaryAt);
            }
            gameObject.AddComponent<OutOfLivesWatcher>().Screen = this;
        }
        else
        {
            Button primary = RuntimeUiKit.CreateButton(panel.transform,
                string.IsNullOrEmpty(_content.PrimaryLabel) ? "Try Again" : _content.PrimaryLabel, 96f, OnPrimaryClicked);
            GameMenuStyle.StyleButton(primary, primary: true);
            RoundButton(primary);
            AddReveal(primary.gameObject, PrimaryAt, isButton: true);
        }

        Button menu = RuntimeUiKit.CreateButton(panel.transform, "Back to Menu", 84f, () =>
        {
            SfxPlayer.Play("ui-leave-game");
            MainMenuRuntime.ReturnToMenu();
        });
        GameMenuStyle.StyleButton(menu, primary: false);
        RoundButton(menu);
        AddReveal(menu.gameObject, SecondaryAt, isButton: true);

        _endTime = SecondaryAt + RevealSeconds;
        ApplyTimeline(); // first frame: everything hidden, not one visible frame of raw layout
    }

    private void OnPrimaryClicked()
    {
        System.Action action = _content.OnPrimary;
        Destroy(gameObject);
        action?.Invoke();
    }

    /// <summary>While the out-of-lives card is up, a life can arrive on its own (regen
    /// ticking over, or an SSV grant landing late). Rebuild once so Try Again returns
    /// without the player having to leave and come back.</summary>
    private sealed class OutOfLivesWatcher : MonoBehaviour
    {
        public RunResultsScreen Screen;
        private float _next;

        private void Update()
        {
            if (Screen == null || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            if (!RunLivesUi.OutOfLives)
            {
                enabled = false;
                Show(Screen._content, muted: true);
            }
        }
    }

    // A single centered text row, height-driven for the panel's vertical layout.
    private static TextMeshProUGUI CreateRow(Transform parent, string text, int size, Color color,
        float height, bool display)
    {
        TextMeshProUGUI tmp = RuntimeUiKit.CreateTmp(parent, "Row", text, size, color,
            TextAnchor.MiddleCenter, FontStyle.Normal, RuntimeUiKit.TitleFont);
        if (display) tmp.font = RuntimeUiKit.TmpDisplayFont; // Archivo: the hero voice
        tmp.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        return tmp;
    }

    // The record celebration: a dark pill with a gold edge, gold NEW BEST, and the recurring
    // card-shine sweep - the game's one sanctioned "this earned gold" visual word.
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
        fill.color = new Color(0.10f, 0.085f, 0.04f, 0.9f);
        fill.raycastTarget = false;
        RuntimeUiKit.AddOutline(pill, WithAlpha(Gold, 0.8f));

        TextMeshProUGUI label = RuntimeUiKit.CreateTmp(pill, "Label", "NEW BEST", 30, Gold,
            TextAnchor.MiddleCenter, FontStyle.Normal, RuntimeUiKit.TitleFont);
        label.font = RuntimeUiKit.TmpDisplayFont;
        label.characterSpacing = 8f;

        AbilityCardShine.Attach(pill, new Color(1f, 0.92f, 0.65f, 0.30f), 1.8f);
        AddReveal(row, RecordAt);
    }

    private static void RoundButton(Button button)
    {
        Image image = button.GetComponent<Image>();
        image.sprite = RuntimeSprites.RoundedPanel();
        image.type = Image.Type.Sliced;
    }

    private void AddReveal(GameObject target, float start, bool isButton = false)
    {
        CanvasGroup group = target.AddComponent<CanvasGroup>();
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
        if (_clock < 0.3f || _backdrop.color.a < _backdropAlpha)
        {
            SetBackdropAlpha(_backdropAlpha * Mathf.Clamp01(_clock / 0.25f));
        }

        for (int i = 0; i < _reveals.Count; i++)
        {
            Reveal reveal = _reveals[i];
            if (reveal.Group == null) continue;
            float t = Mathf.Clamp01((_clock - reveal.Start) / RevealSeconds);
            reveal.Group.alpha = t;
            // Same curve as UiEntranceFx (the house UI arrival); hand-rolled here only because
            // the tap-to-skip fast-forward must be able to jump every element to its end state.
            float overshoot = 1f + 0.06f * Mathf.Sin(t * Mathf.PI);
            reveal.Group.transform.localScale = Vector3.one * (Mathf.Lerp(0.92f, 1f, t) * overshoot);
            if (reveal.IsButton && t >= 1f && !reveal.Group.interactable)
            {
                reveal.Group.interactable = true;
                reveal.Group.blocksRaycasts = true;
            }
        }

        TickHero();
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
            float scale = _punchAge >= PunchSeconds ? 1f : FxKit.Elastic(_punchAge, 0.16f, 6f, 18f);
            _hero.rectTransform.localScale = new Vector3(scale, scale, 1f);
        }

        // The gold moment gets its one quiet clink - the coin vocabulary, no fanfare.
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
        _landed = true;
        _punchAge = PunchSeconds;
        _recordSfxPlayed = true;
        if (_hero != null)
        {
            _hero.text = _content.Metric.Format(_content.Metric.Value);
            _hero.rectTransform.localScale = Vector3.one;
        }
        ApplyTimeline();
    }
}
