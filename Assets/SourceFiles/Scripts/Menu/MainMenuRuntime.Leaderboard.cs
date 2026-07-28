using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static RuntimeUiKit;

// The per-level leaderboard overlay (BACKEND.md §7, SHOP.md §5): two boards per level,
// CLEAN and BOOSTED, never mixed - CLEAN is always the default tab. Opened from the level
// modal's RANKS button. Reads are one RPC (top-100 + your own rank); scores are written
// only by the server's finish_run, so this screen is a pure viewer.
// (partial of MainMenuRuntime - same class, shared statics.)
public static partial class MainMenuRuntime
{
    private static void OpenLeaderboard(LevelDefinition level, ChapterDefinition chapter, bool defaultBoosted = false)
    {
        string levelId = ProgressStore.LevelId(level);
        if (levelId == null) return; // runtime levels have no identity and no boards

        GameObject overlay = RuntimeUiKit.CreateOverlayCanvas("Leaderboard", 5700);
        void Close() => UnityEngine.Object.Destroy(overlay);

        Color accent = chapter != null ? ChapterLight(chapter) : GoldBase;

        // Dimmed backdrop; tap closes (same dismissal contract as the level modal above it).
        Image backdrop = CreateImage(overlay.transform, "Backdrop", null, new Color(0.02f, 0.02f, 0.03f, 0.88f));
        Stretch(backdrop.rectTransform);
        backdrop.raycastTarget = true;
        Button backdropButton = backdrop.gameObject.AddComponent<Button>();
        backdropButton.transition = Selectable.Transition.None;
        backdropButton.onClick.AddListener(Close);

        const float W = 880f;
        const float H = 1280f;
        const float pad = 44f;
        const float contentW = W - pad * 2f;
        Color panelColor = new Color(0.075f, 0.065f, 0.058f, 1f);
        RectTransform panel = CreateRect(overlay.transform, "Panel",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(W, H));
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = RuntimeSprites.RoundedPanel();
        panelImage.type = Image.Type.Sliced;
        panelImage.color = panelColor;
        panelImage.raycastTarget = true;
        RuntimeUiKit.AddOutline(panel, GoldOutline(0.22f));

        Image trophy = CreateImage(panel, "Trophy", MenuSprites.Trophy(accent), Color.white);
        trophy.preserveAspect = true;
        SetRect(trophy.rectTransform, new Vector2(pad, -40f), new Vector2(40f, 40f), new Vector2(0f, 1f));

        TextMeshProUGUI kicker = CreateTmp(panel, "Kicker", "RANKS", 20, accent,
            TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(pad + 56f, -44f), new Vector2(contentW - 56f, 26f), new Vector2(0f, 1f));
        kicker.characterSpacing = 6f;
        CreateTmp(panel, "Title", level.DisplayName.ToUpperInvariant(), 44, TextPrimary,
            TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(pad, -84f), new Vector2(contentW, 56f), new Vector2(0f, 1f));

        // CLEAN | BOOSTED. Two boards, never mixed (SHOP.md §5); CLEAN is the default.
        RectTransform tabs = CreateRect(panel, "Tabs",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(pad, -156f), new Vector2(contentW, 68f));

        // Body region between the tabs and the panel bottom; each state rebuilds into it.
        RectTransform body = CreateRect(panel, "Body",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(pad, -248f), new Vector2(contentW, H - 248f - 40f));

        // Close (X), matching the level modal's.
        Color closeFill = new Color(0.03f, 0.03f, 0.04f, 0.55f);
        Image closeBg = CreateImage(panel, "Close", MenuSprites.CircleBadge(closeFill, closeFill), Color.white);
        SetRect(closeBg.rectTransform, new Vector2(-24f, -24f), new Vector2(64f, 64f), new Vector2(1f, 1f));
        closeBg.raycastTarget = true;
        CreateTmp(closeBg.transform, "X", "X", 30, TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        Button closeButton = closeBg.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = closeBg;
        closeButton.onClick.AddListener(Close);

        // Per-open cache so tab flips don't refetch a board already loaded this visit.
        var cache = new Dictionary<bool, Leaderboards.LeaderboardResult>();
        bool showingBoosted = defaultBoosted;
        // The goal knows how its stored scores print (ClearWaves boards store ENCODED waves;
        // showing the raw int would leak the encoding).
        WinCondition condition = level.WinCondition;

        void Load()
        {
            bool boosted = showingBoosted;
            if (cache.TryGetValue(boosted, out Leaderboards.LeaderboardResult cached))
            {
                BuildLeaderboardList(body, cached, accent, condition);
                return;
            }
            BuildLeaderboardMessage(body, "FETCHING SCORES...", null, null);
            Leaderboards.Fetch(levelId, boosted,
                result =>
                {
                    if (overlay == null) return;                 // closed while in flight
                    cache[boosted] = result;
                    if (boosted == showingBoosted) BuildLeaderboardList(body, result, accent, condition);
                },
                err =>
                {
                    if (overlay == null || boosted != showingBoosted) return;
                    BuildLeaderboardMessage(body, "COULDN'T REACH THE TOWER NETWORK", "RETRY", () =>
                    {
                        if (!OnlineService.IsReady) OnlineService.RetryConnect();
                        Load();
                    });
                });
        }

        RuntimeUiKit.CreateSegmentedControl(tabs, new[] { "CLEAN", "BOOSTED" },
            defaultBoosted ? 1 : 0, GoldBase, index =>
            {
                SfxPlayer.Play("ui-button-click");
                showingBoosted = index == 1;
                Load();
            }, 24);

        Load();
    }

    // A single centered message (loading / error / empty), optionally with one button under it.
    private static void BuildLeaderboardMessage(RectTransform body, string message,
        string buttonLabel, UnityEngine.Events.UnityAction onButton)
    {
        ClearChildren(body);

        CreateTmp(body, "Message", message, 24, WithAlpha(TextMuted, 0.9f),
            TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -220f), new Vector2(body.sizeDelta.x, 34f), new Vector2(0.5f, 1f));

        if (buttonLabel == null) return;

        Image bg = CreateImage(body, "Action", RuntimeSprites.RoundedPanel(), new Color(0.13f, 0.12f, 0.10f, 1f));
        bg.type = Image.Type.Sliced;
        SetRect(bg.rectTransform, new Vector2(0f, -300f), new Vector2(320f, 80f), new Vector2(0.5f, 1f));
        bg.rectTransform.pivot = new Vector2(0.5f, 1f);
        bg.raycastTarget = true;
        RuntimeUiKit.AddOutline(bg.transform, GoldOutline(0.35f));
        CreateTmp(bg.transform, "Label", buttonLabel, 26, GoldBase,
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        Button button = bg.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        button.onClick.AddListener(() => { SfxPlayer.Play("ui-button-click"); onButton?.Invoke(); });
    }

    private static void BuildLeaderboardList(RectTransform body, Leaderboards.LeaderboardResult result, Color accent,
        WinCondition condition)
    {
        ClearChildren(body);

        if (result.entries.Count == 0)
        {
            BuildLeaderboardMessage(body, "NO SCORES YET - BE THE FIRST", null, null);
            return;
        }

        // Your own row inside the page means no pinned row; outside it, the list shortens
        // slightly and your row rides below, always visible.
        bool youInPage = false;
        for (int i = 0; i < result.entries.Count; i++)
        {
            if (result.entries[i].is_you) { youInPage = true; break; }
        }
        bool pinYou = !youInPage && result.you != null;

        float listH = body.sizeDelta.y - (pinYou ? 104f : 0f);
        GameObject scroll = RuntimeUiKit.CreateScrollColumn(body, new Vector2(body.sizeDelta.x, listH), out Transform content);
        RectTransform scrollRect = (RectTransform)scroll.transform;
        SetRect(scrollRect, Vector2.zero, new Vector2(body.sizeDelta.x, listH), new Vector2(0.5f, 1f));
        scroll.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.22f);

        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 14, 14);
        layout.spacing = 10f;

        for (int i = 0; i < result.entries.Count; i++)
        {
            Leaderboards.Entry entry = result.entries[i];
            BuildLeaderboardRow(content, entry, entry.display_name, entry.is_you, accent, condition);
        }

        if (pinYou)
        {
            // The server's "you" object carries rank/score/height only - the client knows
            // its own name, and the row is by definition yours.
            RectTransform pinned = BuildLeaderboardRow(body, result.you, OnlineService.DisplayName, true, accent, condition);
            SetRect(pinned, new Vector2(0f, 16f), new Vector2(body.sizeDelta.x, 84f), new Vector2(0.5f, 0f));
            pinned.pivot = new Vector2(0.5f, 0f);
        }
    }

    private static RectTransform BuildLeaderboardRow(Transform parent, Leaderboards.Entry entry,
        string displayName, bool isYou, Color accent, WinCondition condition)
    {
        RectTransform row = CreateRect(parent, "Row",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(0f, 84f));
        Image fill = row.gameObject.AddComponent<Image>();
        fill.sprite = RuntimeSprites.RoundedPanel();
        fill.type = Image.Type.Sliced;
        fill.color = new Color(0.11f, 0.10f, 0.085f, 1f);
        LayoutElement layoutElement = row.gameObject.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = 84f;

        if (isYou)
        {
            fill.color = new Color(0.15f, 0.13f, 0.10f, 1f);
            RuntimeUiKit.AddOutline(row, GoldOutline(0.6f));
        }

        Color rankColor = entry.rank <= 3 ? GoldBase : WithAlpha(TextMuted, 0.9f);
        CreateTmp(row, "Rank", $"#{entry.rank}", 26, rankColor, TextAnchor.MiddleLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(26f, 0f), new Vector2(96f, 34f), new Vector2(0f, 0.5f));

        string shownName = string.IsNullOrEmpty(displayName) ? "BUILDER" : displayName;
        CreateTmp(row, "Name", isYou ? $"{shownName}  (YOU)" : shownName, 24, TextPrimary,
            TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(132f, 0f), new Vector2(420f, 32f), new Vector2(0f, 0.5f));

        // Score is the board metric; height-goal boards can hold score 0 - show the metric
        // that exists (same rule as the modal's BOOSTED BEST caption). Goals with encoded
        // stored scores print through the condition (waves, not the packed int).
        string value = entry.best_score > 0
            ? (condition?.FormatBoardScore(entry.best_score) ?? entry.best_score.ToString())
            : $"{entry.best_height:F1}m";
        CreateTmp(row, "Score", value, 30, accent, TextAnchor.MiddleRight,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(-26f, 0f), new Vector2(220f, 38f), new Vector2(1f, 0.5f));

        return row;
    }
}
