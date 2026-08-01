using UnityEngine;
using UnityEngine.UI;
using static RuntimeUiKit;

// The unlock-reveal moment: when the player returns to the menu after completing a level for
// the FIRST time, the thing that completion unlocked (the next level card, or the next-chapter
// card) is built in its locked look and then animated open - strain, flash, punch, sparkles,
// stinger - instead of silently appearing pre-unlocked. UnlockRevealPending carries the "just
// completed" record across the scene reload; MenuUnlockRevealRunner plays the sequence.
// (partial of MainMenuRuntime - same class, shared statics.)
public static partial class MainMenuRuntime
{
    private enum RevealKind
    {
        None,
        Level,   // the next level card in the displayed chapter animates open
        Chapter  // the next-chapter card animates open
    }

    // Valid only DURING the live BuildPlayScreen pass (resolved before, cleared after), so the
    // pager's off-screen neighbour builds - which reuse BuildChapterContent - never see it and
    // can't play a reveal on a panel the player isn't looking at.
    private static RevealKind _revealKind = RevealKind.None;
    private static int _revealLevelIndex = -1;

    /// <summary>The chapter (index into the loaded array) a pending reveal belongs to, or -1.
    /// BuildMenu opens on this chapter on a fresh launch: after completing a chapter's LAST
    /// level and restarting the app, the default index would otherwise land on the newly
    /// unlocked NEXT chapter - and the reveal moment would never play.</summary>
    private static int PendingRevealChapterIndex(ChapterDefinition[] chapters)
    {
        string completedId = UnlockRevealPending.PeekLevelId();
        if (completedId == null) return -1;

        for (int c = 0; c < chapters.Length; c++)
        {
            if (FindLevelIndex(chapters[c], completedId) >= 0) return c;
        }
        return -1;
    }

    private static int FindLevelIndex(ChapterDefinition chapter, string levelId)
    {
        if (chapter == null || chapter.Levels == null) return -1;
        for (int i = 0; i < chapter.Levels.Count; i++)
        {
            if (ProgressStore.LevelId(chapter.Levels[i]) == levelId) return i;
        }
        return -1;
    }

    /// <summary>Turns a pending "first completion" record into a concrete reveal for the chapter
    /// this build is showing. Consumes the record only when it resolves here (or turns out to be
    /// moot); a record belonging to another chapter stays pending for that chapter's screen.</summary>
    private static void ResolvePendingReveal(ChapterDefinition chapter, int chapterIndex)
    {
        _revealKind = RevealKind.None;
        _revealLevelIndex = -1;

        string completedId = UnlockRevealPending.PeekLevelId();
        if (completedId == null || chapter == null || chapter.Levels == null) return;

        int completedIndex = FindLevelIndex(chapter, completedId);
        if (completedIndex < 0)
        {
            // Not this chapter's record. If it matches NO chapter at all (the level asset was
            // renamed/removed between versions), retire it instead of letting it linger forever.
            if (PendingRevealChapterIndex(_chapters) < 0) UnlockRevealPending.Clear();
            return;
        }

        // Sandboxes never show locks, so there is nothing to reveal.
        if (chapter.AlwaysUnlocked)
        {
            UnlockRevealPending.Clear();
            return;
        }

        if (completedIndex + 1 < chapter.Levels.Count)
        {
            UnlockRevealPending.Clear();
            LevelDefinition nextLevel = chapter.Levels[completedIndex + 1];
            // Already completed (out-of-order testing states) = it never read as locked.
            if (nextLevel == null || ProgressStore.IsLevelCompleted(nextLevel)) return;
            _revealKind = RevealKind.Level;
            _revealLevelIndex = completedIndex + 1;
            return;
        }

        // Last level of the chapter: the unlock lives on the next-chapter card.
        UnlockRevealPending.Clear();
        int nextChapterIndex = chapterIndex + 1;
        if (nextChapterIndex >= _chapters.Length) return;          // campaign end
        if (_chapters[nextChapterIndex].AlwaysUnlocked) return;    // never was locked
        if (!Campaign.IsChapterCompleted(chapter)) return;         // stray completion (unlock-all testing)
        // Guards odd states (an earlier chapter still incomplete): never animate open a card
        // that will stay locked.
        if (!Campaign.IsChapterUnlocked(_chapters, nextChapterIndex)) return;
        _revealKind = RevealKind.Chapter;
    }

    private static void ClearResolvedReveal()
    {
        _revealKind = RevealKind.None;
        _revealLevelIndex = -1;
    }

    // The level row was just built in its LOCKED look; this arms the runner that animates it
    // open. The rebuild callback re-runs the row's normal builders with the real (unlocked,
    // current) state - one source of truth for how an unlocked card looks.
    private static void AttachLevelReveal(RectTransform row, Transform screenRoot,
        ChapterDefinition chapter, LevelDefinition level, int index, int count)
    {
        string cardName = $"LevelCard{index + 1}";
        Transform lockedCard = row.Find(cardName);
        // The level is genuinely unlocked already, but while it LOOKS locked a tap must not
        // open the level summary over the running reveal. The rebuild below restores the
        // real interactable state.
        Button lockedButton = lockedCard != null ? lockedCard.GetComponent<Button>() : null;
        if (lockedButton != null) lockedButton.interactable = false;
        MenuUnlockRevealRunner.Play(row.gameObject, new MenuUnlockRevealRunner.Spec
        {
            ShakeTarget = lockedCard != null ? lockedCard.Find("Action") as RectTransform : null,
            RattleSfx = "unlock_rattle",
            BurstSfx = "unlock_level",
            SparkleColor = Color.Lerp(ChapterLight(chapter), Color.white, 0.55f),
            SparkleCount = 12,
            SparkleLayer = screenRoot as RectTransform,
            // Long chapters scroll: ride the list down first so the unlock happens on screen.
            ScrollTo = row.GetComponentInParent<ScrollRect>(),
            ScrollTarget = row,
            Rebuild = () =>
            {
                if (row == null) return null;
                // Immediate destroy, not Destroy: the rebuild recreates children under the SAME
                // names this frame, and a deferred-dying sibling would poison the Find below.
                for (int i = row.childCount - 1; i >= 0; i--)
                {
                    UnityEngine.Object.DestroyImmediate(row.GetChild(i).gameObject);
                }
                // "Current" is recomputed, not assumed: with out-of-order completions (old
                // unlock-all saves) the halo may belong to an earlier incomplete level, and
                // hardcoding true would light two rail nodes at once.
                bool current = CurrentLevelIndex(chapter) == index;
                BuildLevelRail(row, index, count, chapter, true, false, current);
                BuildLevelCard(row, chapter, level, index, true, false, current);
                RectTransform card = row.Find(cardName) as RectTransform;
                Transform halo = row.Find("ActiveHalo");
                return new MenuUnlockRevealRunner.Result
                {
                    PunchTarget = card,
                    FlashArea = card,
                    FadeIn = halo != null ? halo.GetComponent<Image>() : null
                };
            }
        });
    }

    // Same idea for the next-chapter card: built as the locked mystery, animated open into the
    // preview + name, with the preview swept in radially (the "paint reveal").
    private static void AttachChapterReveal(Transform screenRoot, RectTransform card,
        RectTransform content, ChapterDefinition current, ChapterDefinition next, Button button)
    {
        MenuUnlockRevealRunner.Play(card.gameObject, new MenuUnlockRevealRunner.Spec
        {
            InitialDelay = 0.7f,
            ShakeSeconds = 0.5f,
            ShakeTarget = content.Find("NextLock") as RectTransform,
            RattleSfx = "unlock_rattle",
            BurstSfx = "unlock_chapter",
            SparkleColor = Color.Lerp(ChapterLight(next), Color.white, 0.55f),
            SparkleCount = 18,
            SparkleLayer = screenRoot as RectTransform,
            Rebuild = () =>
            {
                if (content == null) return null;
                for (int i = content.childCount - 1; i >= 0; i--)
                {
                    UnityEngine.Object.DestroyImmediate(content.GetChild(i).gameObject);
                }
                FillNextChapterContent(content, current, next, true);
                if (button != null) button.interactable = true;
                Transform previewImage = content.Find("Preview/Image");
                return new MenuUnlockRevealRunner.Result
                {
                    PunchTarget = card,
                    FlashArea = card,
                    RadialWipe = previewImage != null ? previewImage.GetComponent<Image>() : null,
                    RadialWipeSeconds = 0.5f
                };
            }
        });
    }
}
