using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static RuntimeUiKit;

// The Chapters page: the campaign atlas. Every UNLOCKED chapter is a landmark on a vertical journey
// built from the live ChapterDefinition list (Campaign.LoadChaptersInOrder via _chapters), so
// reordering sortOrder or adding chapters reflows this page with zero UI changes. Locked
// chapters are NOT rendered at all - the campaign's size is a secret (Nick 2026-08-30): no
// "3/15", no row of sealed slabs to count. One dark locked-teaser card ends the list
// instead, and it stays even at the shipped content's edge so the end never announces
// itself. Tapping a card jumps the Home screen to that chapter, so this page doubles as
// long-range navigation once the campaign grows.
// (partial of MainMenuRuntime, split from the main file for readability - same class, shared statics.)
public static partial class MainMenuRuntime
{
    private const float ChapterCardSideInset = 60f;
    private const float ChapterListTopInset = 342f;
    private const float ChapterListBottomInset = 220f;
    private const float ChapterRowHeight = 360f;

    private static void BuildChaptersScreen(Transform parent, ChapterDefinition chapter)
    {
        // The atlas has its own quiet ground so each landmark carries its world's identity.
        Image atlasWash = CreateImage(parent, "AtlasWash", null, new Color(.025f, .04f, .055f, .97f));
        Stretch(atlasWash.rectTransform); atlasWash.raycastTarget = false;
        BuildChaptersHeader(parent, chapter);
        BuildChaptersList(parent, chapter);
    }

    // Completed / total playable levels of one chapter (null level slots don't count).
    private static (int done, int total) ChapterLevelCounts(ChapterDefinition chapter)
    {
        int done = 0;
        int total = 0;
        for (int i = 0; i < chapter.Levels.Count; i++)
        {
            LevelDefinition level = chapter.Levels[i];
            if (level == null) continue;
            total++;
            if (ProgressStore.IsLevelCompleted(level)) done++;
        }
        return (done, total);
    }

    private static bool IsChapterFullyCompleted(ChapterDefinition chapter)
    {
        (int done, int total) = ChapterLevelCounts(chapter);
        return total > 0 && done == total;
    }

    // The chapter the player is "on": the first unlocked chapter with unfinished levels,
    // falling back to the last unlocked one when everything shipped is beaten.
    private static int CurrentCampaignChapterIndex()
    {
        return DefaultChapterIndex(_chapters);
    }

    private static void BuildChaptersHeader(Transform parent, ChapterDefinition chapter)
    {
        TextMeshProUGUI title = CreateTmp(parent, "ChaptersTitle", "CHAPTERS", 60, TextPrimary,
            TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(76f, -196f), new Vector2(520f, 76f), new Vector2(0f, 1f));
        title.characterSpacing = 4f;
        CreateTmp(parent, "JourneySubtitle", "One world at a time.", 25, TextMuted,
            TextAnchor.MiddleLeft, FontStyle.Normal, RuntimeUiKit.DefaultFont,
            new Vector2(78f, -282f), new Vector2(600f, 38f), new Vector2(0f, 1f));

        // No "/ total" and no header progress bar: both would bound the campaign, and its
        // size is a secret (see the file header). Cleared count only, once there is one.
        int cleared = 0;
        for (int i = 0; i < _chapters.Length; i++)
        {
            if (IsChapterFullyCompleted(_chapters[i])) cleared++;
        }
        if (cleared > 0)
        {
            CreateTmp(parent, "ChaptersProgress", $"{cleared} CLEARED", 24,
                MenuAccent, TextAnchor.MiddleRight, FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(-ChapterCardSideInset, -206f), new Vector2(420f, 34f), new Vector2(1f, 1f));
        }
    }

    private static void BuildChaptersList(Transform parent, ChapterDefinition chapter)
    {
        ScrollRect scroll = BuildGalleryScroll(parent, "Chapters", chapter,
            ChapterListTopInset, ChapterListBottomInset);
        RectTransform viewport = scroll.viewport;
        RectTransform content = scroll.content;

        int currentIndex = CurrentCampaignChapterIndex();
        int currentRow = 0, visibleRows = 0;
        for (int i = 0; i < _chapters.Length; i++)
        {
            // The ambiguity rule: locked chapters simply don't exist on this page.
            if (!Campaign.IsChapterUnlocked(_chapters, i)) continue;
            if (i == currentIndex) currentRow = visibleRows;
            visibleRows++;
            RectTransform row = NewGridRow(content, ChapterRowHeight);
            BuildChapterCard(row, i, i == currentIndex);
        }

        BuildJourneyTeaser(content);
        var focus = viewport.gameObject.AddComponent<ChapterJourneyFocus>();
        focus.Scroll = scroll;
        focus.RowIndex = currentRow;
        focus.RowHeight = ChapterRowHeight;
    }

    // The entire landmark row is tappable; art, route and labels are decorative children.
    private static void BuildChapterCard(RectTransform row, int index, bool current)
    {
        ChapterDefinition chapter = _chapters[index];
        (int done, int total) = ChapterLevelCounts(chapter);
        bool completed = total > 0 && done == total;
        Color ink = ChapterLight(chapter);
        Color green = new Color(.56f, .74f, .5f);
        JourneyRail(row, index == 0, current ? ink : WithAlpha(ink, .4f));

        RectTransform card = CreateRect(row, "Card", Vector2.zero, Vector2.one,
            new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
        card.SetAsFirstSibling();
        card.offsetMin = new Vector2(52f, 12f); card.offsetMax = new Vector2(-60f, -12f);
        var hit = card.gameObject.AddComponent<Image>();
        hit.sprite = RuntimeSprites.RoundedPanel(); hit.type = Image.Type.Sliced;
        hit.color = current ? new Color(.10f, .15f, .17f, .48f) : Color.clear;
        // Place on the full row for stable responsive columns, inside the row-wide hit area.
        ChapterArtwork.Place(row, chapter, "ChapterLandmark", new Vector2(.275f, .5f),
            new Vector2(.5f, .5f), Vector2.zero, 280f);
        RectTransform text = CreateRect(row, "ChapterDetails", new Vector2(.47f, 0f),
            new Vector2(.94f, 1f), new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
        var eyebrow = CreateTmp(text, "Eyebrow", $"CHAPTER {chapter.ChapterNumber:00}", 21,
            ink, TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont);
        JourneyTextRect(eyebrow.rectTransform, 46f, 30f); eyebrow.characterSpacing = 5f;
        var title = CreateTmp(text, "Title", chapter.DisplayName, 46, TextPrimary,
            TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont);
        JourneyTextRect(title.rectTransform, 82f, 100f);
        title.textWrappingMode = TextWrappingModes.Normal; AutoSize(title, 30f, 46f);
        var progress = CreateTmp(text, "Progress", $"{done} / {total} LEVELS", 23,
            completed ? green : TextMuted, TextAnchor.MiddleLeft, FontStyle.Normal, RuntimeUiKit.DefaultFont);
        JourneyTextRect(progress.rectTransform, 196f, 32f);
        BuildChapterMedalStrip(text, chapter, new Vector2(0f, 84f), 310f, green);
        var action = CreateTmp(text, "Action", current ? "CONTINUE  ›" : completed ? "REVISIT  ›" : "EXPLORE  ›", 23,
            ink, TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont);
        JourneyTextRect(action.rectTransform, 300f, 36f); action.characterSpacing = 3f;

        Image node = CreateImage(row, "JourneyNode",
            MenuSprites.CircleBadge(new Color(.025f, .04f, .055f), completed ? green : ink), Color.white);
        node.raycastTarget = false;
        SetCenteredAt(node.rectTransform, new Vector2(0f, .5f), new Vector2(80f, 0f), Vector2.one * (current ? 44f : 32f));
        if (completed)
        {
            var check = CreateImage(node.transform, "Cleared", MenuSprites.CheckMark(green), Color.white);
            check.raycastTarget = false;
            SetCenteredAt(check.rectTransform, new Vector2(.5f, .5f), Vector2.zero, Vector2.one * 18f);
        }
        var button = card.gameObject.AddComponent<Button>(); button.targetGraphic = hit;
        var colors = button.colors; colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f); colors.pressedColor = new Color(.7f, .7f, .7f);
        button.colors = colors;
        button.onClick.AddListener(() => { SfxPlayer.Play("ui-button-click"); _chapterIndex = index;
            _activeTab = MenuTab.Home; BuildMenu(); });
    }

    private static void JourneyTextRect(RectTransform rect, float top, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0f, 1f); rect.offsetMin = new Vector2(0f, -top-height);
        rect.offsetMax = new Vector2(0f, -top);
    }

    private static void JourneyRail(RectTransform row, bool first, Color color)
    {
        Image rail = CreateImage(row, "JourneyRail", null, WithAlpha(color, .32f));
        rail.raycastTarget = false;
        var rect = rail.rectTransform; rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(0f, first ? .5f : 1f);
        rect.offsetMin = new Vector2(79f, 0f); rect.offsetMax = new Vector2(81f, 0f);
    }

    private static void BuildJourneyTeaser(Transform content)
    {
        var row = NewGridRow(content, 224f);
        JourneyRail(row, false, TextMuted);
        var lockImage = CreateImage(row, "JourneyContinues", MenuSprites.Lock(TextMuted), Color.white);
        lockImage.raycastTarget = false;
        SetCenteredAt(lockImage.rectTransform, new Vector2(0f, .5f), new Vector2(80f, 0f), Vector2.one * 28f);
        var text = CreateRect(row, "Beyond", new Vector2(.17f, 0f), new Vector2(.94f, 1f),
            new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
        var title = CreateTmp(text, "Title", "The journey continues", 30, TextPrimary,
            TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont);
        JourneyTextRect(title.rectTransform, 62f, 44f);
        var body = CreateTmp(text, "Body", "Finish the chapter above to discover what’s next.", 23, TextMuted,
            TextAnchor.MiddleLeft, FontStyle.Normal, RuntimeUiKit.DefaultFont);
        JourneyTextRect(body.rectTransform, 118f, 68f); body.textWrappingMode = TextWrappingModes.Normal;
    }

    private static void BuildChapterMedalStrip(RectTransform card, ChapterDefinition chapter,
        Vector2 anchoredPosition, float maxWidth, Color green)
    {
        const float maxCube = 26f;
        const float gap = 6f;
        int n = 0;
        for (int i = 0; i < chapter.Levels.Count; i++) if (chapter.Levels[i] != null) n++;
        if (n == 0) return;
        float cube = Mathf.Min(maxCube, (maxWidth - (n - 1) * gap) / n);

        RectTransform strip = CreateRect(card, "MedalStrip", new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(0f, 0f), anchoredPosition, new Vector2(n * cube + (n - 1) * gap, cube));
        int slot = 0;
        for (int i = 0; i < chapter.Levels.Count; i++)
        {
            LevelDefinition level = chapter.Levels[i];
            if (level == null) continue;
            bool completed = ProgressStore.IsLevelCompleted(level);
            MedalTier? medal = completed ? LevelTiers.HighestEarned(level) : null;
            Sprite sprite;
            Color tint;
            if (medal.HasValue)
            {
                sprite = MedalStyle.Sprite(medal.Value, earned: true);
                tint = Color.white;
            }
            else if (completed)
            {
                sprite = MenuSprites.CheckMark(green);   // cleared, no ladder (Endless)
                tint = Color.white;
            }
            else
            {
                sprite = MedalStyle.Sprite(MedalTier.Bronze, earned: false);
                tint = MedalStyle.IconTint(false);
            }
            Image img = CreateImage(strip, "Level" + (i + 1), sprite, tint);
            img.preserveAspect = true;
            img.raycastTarget = false;
            SetCenteredAt(img.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(slot * (cube + gap) + cube * 0.5f, 0f), new Vector2(cube, cube));
            slot++;
        }
    }

}
