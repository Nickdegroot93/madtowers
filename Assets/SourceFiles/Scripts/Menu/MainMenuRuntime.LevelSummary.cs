using Coffee.UIEffects;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static RuntimeUiKit;

// The level pre-launch summary modal.
// (partial of MainMenuRuntime, split from the main file for readability - same class, shared statics.)
public static partial class MainMenuRuntime
{
    // Level pre-launch summary modal: tapping a level no longer launches it directly - it
    // opens this (level image + stats + a big Start Game button). Close button (top-right) or
    // a tap on the dimmed backdrop dismisses it. Styling is intentionally minimal for now.
    private static void OpenLevelSummary(ChapterDefinition chapter, LevelDefinition level, int index, bool completed)
    {
        SfxPlayer.Play("ui-button-click");

        GameObject overlay = RuntimeUiKit.CreateOverlayCanvas("Level Summary", 5500);
        void Close() => UnityEngine.Object.Destroy(overlay);

        Color lightChapter = ChapterLight(chapter);                 // labels + challenge type
        Color darkChapter = chapter.MenuAccentColor;                // "your best" value (the amber)

        // Heavily blurred, darkened copy of the chapter backdrop so the sharp menu behind reads as
        // fully out of focus; the modal itself stays 100% opaque on top of it.
        Sprite backdropSprite = chapter.MenuBackgroundImage;
        if (backdropSprite != null)
        {
            Image blur = CreateImage(overlay.transform, "BlurBackdrop", backdropSprite, Color.white);
            Stretch(blur.rectTransform);
            blur.preserveAspect = false;
            UIEffect blurFx = blur.gameObject.AddComponent<UIEffect>();
            blurFx.samplingFilter = SamplingFilter.BlurFast;
            blurFx.samplingScale = 7f;
        }
        Image backdrop = CreateImage(overlay.transform, "Backdrop", null,
            new Color(0.02f, 0.02f, 0.03f, backdropSprite != null ? 0.58f : 0.92f));
        Stretch(backdrop.rectTransform);
        backdrop.raycastTarget = true;
        Button backdropButton = backdrop.gameObject.AddComponent<Button>();
        backdropButton.transition = Selectable.Transition.None;
        backdropButton.onClick.AddListener(Close);

        // Centered, fully opaque panel. Its own raycast target swallows taps so they don't reach
        // the backdrop. Layout flows top-down: thumbnail, challenge type, title, stat cards,
        // description, (supplies + attempts once the meta systems unlock, SHOP.md §7.1), then
        // the play / ranks buttons pinned to the bottom.
        const float W = 880f;
        const float pad = 44f;
        const float contentW = W - pad * 2f;
        // Supplies exist for campaign levels only (runtime levels have no save identity) and
        // stay invisible until Chapter 1 is done - the soft-landing rule.
        bool suppliesOn = AttemptsService.MetaEnabled && ProgressStore.LevelId(level) != null;
        // 768 not 840: the description is one line, so the supplies section moves up into
        // the slack instead of floating below dead space (Nick's whitespace note).
        float H = suppliesOn ? 768f + SuppliesSectionHeight : 840f;
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

        // Thumbnail, full-bleed across the top (rounded corners match the panel).
        const float imgH = 360f;
        Sprite thumb = level.MenuThumbnail != null
            ? level.MenuThumbnail
            : MenuSprites.LevelThumbnail(index, chapter.MenuAccentColor, chapter.MenuAccentSecondaryColor);
        CreateCoverImage(panel, "Image", thumb, Color.white,
            new Vector2(0f, 0f), new Vector2(W, imgH), new Vector2(0.5f, 1f));

        // Scrim: the image's lower half fades to the panel colour, so the type/title read on it
        // and the bottom edge blends seamlessly into the panel body.
        Image scrim = CreateImage(panel, "ImageScrim",
            MenuSprites.VerticalFade(WithAlpha(panelColor, 0f), panelColor), Color.white);
        SetRect(scrim.rectTransform, new Vector2(0f, -120f), new Vector2(W, imgH - 120f), new Vector2(0.5f, 1f));
        scrim.raycastTarget = false;

        // Challenge type + title sit on the image's lower-left, over the scrim.
        LevelMenuPresentation.Snapshot presentation = LevelMenuPresentation.Build(level, completed);
        TextMeshProUGUI challenge = CreateTmp(panel, "Challenge", presentation.ChallengeLabel, 20,
            lightChapter, TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(pad, -258f), new Vector2(contentW, 28f), new Vector2(0f, 1f));
        challenge.characterSpacing = 4f;

        // Title (bold white), baseline near the image bottom.
        CreateTmp(panel, "Title", level.DisplayName.ToUpperInvariant(), 50, TextPrimary, TextAnchor.UpperLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(pad, -290f), new Vector2(contentW, 64f), new Vector2(0f, 1f));

        // Target + Your Best stat cards.
        DeriveTargetAndBest(level, presentation, completed, out string targetText, out string bestText);
        float cardW = (contentW - 18f) / 2f;
        BuildSummaryStat(panel, "Target", new Vector2(pad, -394f), cardW, "TARGET", targetText, lightChapter, TextPrimary);
        BuildSummaryStat(panel, "Best", new Vector2(pad + cardW + 18f, -394f), cardW, "YOUR BEST", bestText, lightChapter, darkChapter);

        // A boosted best exists on its own board (SHOP.md §5): a one-line caption under the
        // stat cards, never mixed into YOUR BEST.
        ProgressStore.LevelBest bestRecord = ProgressStore.GetBest(level);
        if (bestRecord != null && (bestRecord.bestScoreBoosted > 0 || bestRecord.bestHeightMetersBoosted > 0f))
        {
            // Height-goal levels can hold a boosted best with score 0 - show the metric
            // that actually exists instead of printing "BOOSTED BEST 0". Goals with ENCODED
            // stored scores (ClearWaves) print through the condition, like the RANKS rows.
            string boostedValue = bestRecord.bestScoreBoosted > 0
                ? (level.WinCondition.FormatBoardScore(bestRecord.bestScoreBoosted)
                    ?? bestRecord.bestScoreBoosted.ToString())
                : $"{bestRecord.bestHeightMetersBoosted:F1}m";
            CreateTmp(panel, "BoostedBest",
                $"BOOSTED BEST  {boostedValue}", 15, WithAlpha(GoldBase, 0.75f),
                TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(pad + cardW + 18f, -502f), new Vector2(cardW, 20f), new Vector2(0f, 1f));
        }

        // Description (thin, muted) - the level's instruction line.
        if (!string.IsNullOrWhiteSpace(level.Instruction))
        {
            CreateTmp(panel, "Description", level.Instruction, 23, new Color(0.78f, 0.75f, 0.70f, 1f),
                TextAnchor.UpperLeft, FontStyle.Normal, RuntimeUiKit.DefaultFont,
                new Vector2(pad, -528f), new Vector2(contentW, 130f), new Vector2(0f, 1f));
        }

        // The supplies section (RUN LIVES + BOOSTS card rows + the status line, SHOP.md §9.1)
        // sits between the description and the buttons; the extra panel height made room.
        SuppliesUi suppliesUi = null;
        if (suppliesOn)
        {
            suppliesUi = BuildSuppliesSection(panel, level, pad, contentW, -600f);
        }

        // Play (gradient gold) + Ranks (dark) buttons, pinned to the bottom.
        LevelDefinition selected = level;
        float playW = 524f;
        Image playBg = CreateImage(panel, "Play", MenuSprites.RoundedGradient(
            Color.Lerp(chapter.PlayButtonTopColor, Color.white, 0.06f), chapter.PlayButtonBottomColor), Color.white);
        playBg.type = Image.Type.Sliced;
        SetRect(playBg.rectTransform, new Vector2(pad, 44f), new Vector2(playW, 112f), new Vector2(0f, 0f));
        playBg.raycastTarget = true;
        Image playIcon = CreateImage(playBg.transform, "PlayIcon", MenuSprites.TrianglePlay(), TextPrimary);
        SetCenteredAt(playIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-64f, 0f), new Vector2(38f, 38f));
        TextMeshProUGUI playLabel = CreateTmp(playBg.transform, "PlayLabel", "PLAY", 36, TextPrimary,
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(24f, 0f), new Vector2(260f, 48f), new Vector2(0.5f, 0.5f));
        Button playButton = playBg.gameObject.AddComponent<Button>();
        playButton.targetGraphic = playBg;
        playButton.onClick.AddListener(() =>
        {
            if (suppliesUi != null && suppliesUi.StartPending) return;
            SfxPlayer.Play("ui-start-game");

            // Campaign runs must win the server's start_run grant BEFORE the launch reload
            // (BACKEND.md §6.1); Custom Game / online-disabled answer instantly. The button
            // freezes while the grant is in flight so a double-tap can't start two runs.
            bool boosted = suppliesUi != null && suppliesUi.Selection.Boosted;
            string loadoutJson = suppliesUi?.Selection.ToLoadoutJson();
            if (suppliesUi != null) suppliesUi.StartPending = true;
            playButton.interactable = false;
            playLabel.text = "STARTING...";
            playLabel.fontSize = 27f;

            RunGate.BeginRun(selected, boosted, loadoutJson, result =>
            {
                if (result.Allowed)
                {
                    // The loadout rides the same static-carrier as the level;
                    // RunSuppliesApplier charges + applies it in the loaded scene (atomic
                    // with run start). Null when nothing was picked - a clean run. The
                    // grant charged the attempt, so launch even if the modal was closed
                    // while the answer was in flight.
                    RunSuppliesState.Pending = suppliesUi?.Selection.ToLoadout();
                    SelectLevel(selected);
                    return;
                }

                if (suppliesUi != null) suppliesUi.StartPending = false;
                if (playLabel == null) return;   // modal closed while pending - drop the denial quietly

                if (result.DeniedReason == "busy")
                {
                    // A previous grant is still in flight (modal closed + reopened during
                    // the window). Quiet no-op: restore the button; the pending grant's
                    // landing decides what happens.
                    if (suppliesUi != null) RefreshSuppliesSection(suppliesUi);
                    else
                    {
                        playButton.interactable = true;
                        playLabel.text = "PLAY";
                        playLabel.fontSize = 36f;
                    }
                    return;
                }

                if (result.Offline) OnlineService.RetryConnect();
                if (suppliesUi != null)
                {
                    // The status row owns the messaging (offline / out-of-attempts states).
                    RefreshSuppliesSection(suppliesUi);
                }
                else
                {
                    // Pre-meta modal (no supplies section): the button itself has to speak.
                    playButton.interactable = true;
                    playLabel.text = result.Offline ? "OFFLINE - RETRY" : "PLAY";
                    playLabel.fontSize = result.Offline ? 24f : 36f;
                }
            });
        });

        // The button tells the truth (SHOP.md §9.1): CLEAN or BOOSTED, gold-edged when
        // boosted, disabled when the attempts meter is empty.
        if (suppliesUi != null)
        {
            // The CLEAN/BOOSTED label is wider than plain "PLAY" and collides with the
            // triangle icon - the words carry the meaning now, drop the glyph.
            playIcon.gameObject.SetActive(false);
            playLabel.rectTransform.anchoredPosition = Vector2.zero;
            suppliesUi.PlayBg = playBg;
            suppliesUi.PlayLabel = playLabel;
            suppliesUi.PlayButton = playButton;
            RefreshPlayButton(suppliesUi);
        }

        float ranksX = pad + playW + 18f;
        float ranksW = contentW - playW - 18f;
        Image ranksBg = CreateImage(panel, "Ranks", RuntimeSprites.RoundedPanel(), new Color(0.13f, 0.12f, 0.10f, 1f));
        ranksBg.type = Image.Type.Sliced;
        SetRect(ranksBg.rectTransform, new Vector2(ranksX, 44f), new Vector2(ranksW, 112f), new Vector2(0f, 0f));
        ranksBg.raycastTarget = true;
        RuntimeUiKit.AddOutline(ranksBg.transform, WithAlpha(lightChapter, 0.4f));
        Image trophy = CreateImage(ranksBg.transform, "RanksIcon", MenuSprites.Trophy(lightChapter), Color.white);
        trophy.preserveAspect = true;
        SetCenteredAt(trophy.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-58f, 0f), new Vector2(36f, 36f));
        CreateTmp(ranksBg.transform, "RanksLabel", "RANKS", 28, lightChapter, TextAnchor.MiddleCenter,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(16f, 0f), new Vector2(150f, 40f), new Vector2(0.5f, 0.5f));
        Button ranksButton = ranksBg.gameObject.AddComponent<Button>();
        ranksButton.targetGraphic = ranksBg;
        ranksButton.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            OpenLeaderboard(level, chapter);
        });

        // Close (X), top-right - a solid translucent dark circle (not a ring), over the thumbnail.
        Color closeFill = new Color(0.03f, 0.03f, 0.04f, 0.55f);
        Image closeBg = CreateImage(panel, "Close", MenuSprites.CircleBadge(closeFill, closeFill), Color.white);
        SetRect(closeBg.rectTransform, new Vector2(-24f, -24f), new Vector2(64f, 64f), new Vector2(1f, 1f));
        closeBg.raycastTarget = true;
        CreateTmp(closeBg.transform, "X", "X", 30, TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        Button closeButton = closeBg.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = closeBg;
        closeButton.onClick.AddListener(Close);

        // Help at the wall (SHOP.md §7.2): after 3 straight losses on this level the boost
        // tray is simply already open - once per streak, no popup, no discount theatre.
        if (suppliesUi != null && RunSuppliesState.ShouldNudge(level))
        {
            OpenBoostTray(suppliesUi);
        }
    }

    // A small stat card (TARGET / YOUR BEST): label on top in the light chapter colour, value
    // below. The two callers pass different value colours (white target, amber best).
    private static void BuildSummaryStat(Transform panel, string name, Vector2 anchoredPosition,
        float width, string label, string value, Color labelColor, Color valueColor)
    {
        RectTransform card = CreateRect(panel, $"Stat{name}",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            anchoredPosition, new Vector2(width, 104f));
        Image fill = card.gameObject.AddComponent<Image>();
        fill.sprite = RuntimeSprites.RoundedPanel();
        fill.type = Image.Type.Sliced;
        fill.color = new Color(0.11f, 0.10f, 0.085f, 1f);
        RuntimeUiKit.AddOutline(card, GlassBorder);

        TextMeshProUGUI labelText = CreateTmp(card, "Label", label, 16, labelColor, TextAnchor.UpperLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(22f, -18f), new Vector2(width - 36f, 24f), new Vector2(0f, 1f));
        labelText.characterSpacing = 3f;
        CreateTmp(card, "Value", value, 30, valueColor, TextAnchor.UpperLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(22f, -48f), new Vector2(width - 36f, 40f), new Vector2(0f, 1f));
    }

    // Goal text + the player's best (an em dash when never attempted). Handles the two built-in
    // target types directly; provider-driven levels fall back to the presentation's parts.
    private static void DeriveTargetAndBest(LevelDefinition level, LevelMenuPresentation.Snapshot presentation,
        bool completed, out string targetText, out string bestText)
    {
        ProgressStore.LevelBest best = ProgressStore.GetBest(level);
        bool attempted = completed || (best != null && (best.bestScore > 0 || best.bestHeightMeters > 0f));

        // Goal-bearing levels (block count, height, future types) get their lines from the win
        // condition; Endless / provider-driven levels fall back to the presentation snapshot.
        if (level.WinCondition.HasGoal)
        {
            (targetText, bestText) = level.WinCondition.TargetAndBest(best, completed, attempted);
            return;
        }

        string suffix = presentation.ProgressSuffix.StartsWith("/")
            ? presentation.ProgressSuffix.Substring(1).Trim()
            : presentation.ProgressSuffix;
        targetText = string.IsNullOrWhiteSpace(suffix) ? "Endless" : suffix;
        bestText = attempted ? presentation.ProgressPrimary : "-";
    }

    private static void SelectLevel(LevelDefinition level)
    {
        LevelSelectionState.SelectLevel(level);
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
