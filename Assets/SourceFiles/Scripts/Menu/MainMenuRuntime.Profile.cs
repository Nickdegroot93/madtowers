using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static RuntimeUiKit;

// The Profile tab, v2 (Nick's 2026-07-20 pass killed the v1 stats/attempts clutter):
// three cards only -
//   1. identity: avatar + name, with the promise that Game Center / Google Play Games
//      sign-in (and their avatars) arrives with online play
//   2. MADTOWERS UNLIMITED: the one real pitch - the full game, forever (no ads,
//      unlimited attempts), one purchase
//   3. ONLINE PLAY: a big locked coming-soon block (leaderboards, achievements,
//      profiles & avatars, titles & banners) so players SEE that online is on the way
// No wallet, no meter, no hour pass, no lifetime stats - this page is identity and
// promises, not a dashboard. (partial of MainMenuRuntime - same class, shared statics.)
public static partial class MainMenuRuntime
{
    private static void BuildProfileScreen(Transform parent, ChapterDefinition chapter)
    {
        GameObject scroll = RuntimeUiKit.CreateScrollColumn(parent, Vector2.zero, out Transform content);
        RectTransform scrollRect = (RectTransform)scroll.transform;
        scrollRect.anchorMin = Vector2.zero;
        scrollRect.anchorMax = Vector2.one;
        scrollRect.offsetMin = new Vector2(60f, 236f);
        scrollRect.offsetMax = new Vector2(-60f, -344f);
        scroll.GetComponent<Image>().color = Color.clear; // the chapter backdrop IS the background

        Color accent = chapter != null ? ChapterLight(chapter) : GoldBase;

        BuildProfileIdentityCard(content, accent);
        BuildProfileUnlimitedCard(content);
        BuildProfileOnlineCard(content, accent);
    }

    // 1. Who you are - and where the real profile comes from later.
    private static void BuildProfileIdentityCard(Transform content, Color accent)
    {
        RectTransform card = CreateProfileCard(content, 190f);

        // Avatar: a circular slot with the person glyph - visibly a PLACEHOLDER for the
        // platform profile picture (Game Center / Play Games) that arrives with online play.
        Image ring = CreateImage(card, "AvatarRing", MenuSprites.CircleBadge(
            new Color(0.12f, 0.11f, 0.09f, 1f), WithAlpha(accent, 0.55f)), Color.white);
        SetRect(ring.rectTransform, new Vector2(32f, 0f), new Vector2(110f, 110f), new Vector2(0f, 0.5f));
        Image person = CreateImage(ring.transform, "Person", MenuSprites.Person(WithAlpha(accent, 0.85f)), Color.white);
        person.preserveAspect = true;
        SetCenteredAt(person.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56f, 56f));

        CreateTmp(card, "Name", "PLAYER ONE", 34, TextPrimary, TextAnchor.UpperLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(170f, -52f), new Vector2(560f, 42f), new Vector2(0f, 1f));
        CreateTmp(card, "SignIn", "SIGN IN WITH GAME CENTER / GOOGLE PLAY GAMES\nARRIVES WITH ONLINE PLAY",
            14, WithAlpha(TextMuted, 0.8f), TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(170f, -100f), new Vector2(560f, 42f), new Vector2(0f, 1f));
    }

    // 2. The one thing worth advertising, built like a real store package (IAP offer
    // anatomy: hero art → title → benefit bullets → one big CTA - GameRefinery/IAP
    // merchandising playbook). Hero art is a sprite-composed placeholder until real
    // package art is made.
    private static void BuildProfileUnlimitedCard(Transform content)
    {
        const float heroH = 170f;
        RectTransform card = CreateProfileCard(content, 520f);
        // The pitch card carries the gold edge - the single accent on this page.
        RuntimeUiKit.AddOutline(card, WithAlpha(GoldBase, 0.55f));

        // Hero band: a warm glow field with the goods spilling out of it - the big coin
        // flanked by full hearts. PLACEHOLDER composition; swap for painted key art later.
        Image hero = CreateImage(card, "Hero", MenuSprites.VerticalFade(
            new Color(0.24f, 0.16f, 0.05f, 1f), new Color(0.07f, 0.06f, 0.05f, 1f)), Color.white);
        SetRect(hero.rectTransform, new Vector2(0f, 0f), new Vector2(0f, heroH), new Vector2(0.5f, 1f));
        hero.rectTransform.anchorMin = new Vector2(0f, 1f);
        hero.rectTransform.anchorMax = new Vector2(1f, 1f);
        hero.rectTransform.offsetMin = new Vector2(8f, -heroH - 8f);
        hero.rectTransform.offsetMax = new Vector2(-8f, -8f);

        Sprite coin = MenuIcon("coin");
        if (coin != null)
        {
            Image bigCoin = CreateImage(hero.transform, "Coin", coin, Color.white);
            bigCoin.preserveAspect = true;
            SetCenteredAt(bigCoin.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 6f), new Vector2(110f, 110f));
        }
        Sprite heart = HeartSprites.Full();
        if (heart != null)
        {
            for (int i = 0; i < 2; i++)
            {
                Image side = CreateImage(hero.transform, $"Heart{i}", heart, Color.white);
                side.preserveAspect = true;
                float x = i == 0 ? -110f : 110f;
                SetCenteredAt(side.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(x, -6f), new Vector2(64f, 64f));
                side.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 12f : -12f);
            }
        }

        TextMeshProUGUI title = CreateTmp(card, "Title", "MADTOWERS UNLIMITED", 34, GoldBase,
            TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -heroH - 26f), new Vector2(720f, 42f), new Vector2(0.5f, 1f));
        title.characterSpacing = 2f;
        CreateTmp(card, "Pitch", "THE FULL GAME, FOREVER", 17, TextPrimary,
            TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -heroH - 74f), new Vector2(720f, 24f), new Vector2(0.5f, 1f));

        // Benefits, one per line with the shared checkmark - benefits, not features.
        string[] benefits = { "NO ADS, EVER", "UNLIMITED LIVES - NEVER WAIT TO PLAY", "ONE PURCHASE, YOURS FOREVER" };
        for (int i = 0; i < benefits.Length; i++)
        {
            float y = -heroH - 116f - i * 36f;
            Image check = CreateImage(card, $"Check{i}", MenuSprites.CheckMark(GoldBase), Color.white);
            check.preserveAspect = true;
            SetRect(check.rectTransform, new Vector2(150f, y - 2f), new Vector2(24f, 24f), new Vector2(0f, 1f));
            CreateTmp(card, $"Benefit{i}", benefits[i], 16, WithAlpha(TextPrimary, 0.92f),
                TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(188f, y), new Vector2(540f, 24f), new Vector2(0f, 1f));
        }

        // The CTA: full-width, 92px, gold gradient - a real button, not a chip. Disabled
        // until the IAP path ships, and it says so ON the button.
        Image cta = CreateImage(card, "Cta", MenuSprites.RoundedGradient(
            new Color(1f, 0.86f, 0.45f, 1f), new Color(0.82f, 0.58f, 0.18f, 1f)), Color.white);
        cta.type = Image.Type.Sliced;
        cta.rectTransform.anchorMin = new Vector2(0f, 0f);
        cta.rectTransform.anchorMax = new Vector2(1f, 0f);
        cta.rectTransform.pivot = new Vector2(0.5f, 0f);
        cta.rectTransform.offsetMin = new Vector2(28f, 26f);
        cta.rectTransform.offsetMax = new Vector2(-28f, 26f + 92f);
        cta.color = new Color(0.75f, 0.75f, 0.75f, 1f); // dimmed: not purchasable yet
        CreateTmp(cta.transform, "Label", "GET UNLIMITED - $3.99", 26,
            new Color(0.16f, 0.11f, 0.04f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, 10f), new Vector2(600f, 34f), new Vector2(0.5f, 0.5f));
        CreateTmp(cta.transform, "Soon", "COMING SOON", 13,
            new Color(0.24f, 0.17f, 0.07f, 0.9f), TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -22f), new Vector2(600f, 18f), new Vector2(0.5f, 0.5f));
    }

    // 3. The locked door players should SEE: online play is on the way.
    private static void BuildProfileOnlineCard(Transform content, Color accent)
    {
        RectTransform card = CreateProfileCard(content, 330f);

        Image lockIcon = CreateImage(card, "Lock", MenuSprites.Lock(WithAlpha(TextMuted, 0.9f)), Color.white);
        lockIcon.preserveAspect = true;
        SetRect(lockIcon.rectTransform, new Vector2(0f, -40f), new Vector2(56f, 56f), new Vector2(0.5f, 1f));
        lockIcon.rectTransform.anchoredPosition = new Vector2(0f, -40f);
        lockIcon.rectTransform.pivot = new Vector2(0.5f, 1f);

        TextMeshProUGUI title = CreateTmp(card, "Title", "ONLINE PLAY", 40, TextPrimary,
            TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -112f), new Vector2(720f, 50f), new Vector2(0.5f, 1f));
        title.characterSpacing = 4f;

        TextMeshProUGUI soon = CreateTmp(card, "Soon", "COMING SOON", 18,
            Color.Lerp(accent, TextPrimary, 0.35f), TextAnchor.UpperCenter, FontStyle.Bold,
            RuntimeUiKit.TitleFont, new Vector2(0f, -166f), new Vector2(720f, 26f), new Vector2(0.5f, 1f));
        soon.characterSpacing = 8f;

        CreateTmp(card, "Features",
            "LEADERBOARDS   -   ACHIEVEMENTS\nPROFILES & AVATARS   -   TITLES & BANNERS", 15,
            WithAlpha(TextMuted, 0.8f), TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -218f), new Vector2(720f, 52f), new Vector2(0.5f, 1f));

        // Nick's direction (2026-07-20): online won't be a side feature - accounts become
        // the standard, with lives and leaderboards living on the server.
        CreateTmp(card, "Account", "ONE ACCOUNT, EVERY DEVICE - LIVES & SCORES CHECKED ONLINE", 13,
            WithAlpha(TextMuted, 0.55f), TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -286f), new Vector2(720f, 20f), new Vector2(0.5f, 1f));
    }

    // ---- small shared pieces --------------------------------------------------------------

    private static RectTransform CreateProfileCard(Transform content, float height)
    {
        RectTransform card = CreateRect(content, "Card",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, height));
        Image fill = card.gameObject.AddComponent<Image>();
        fill.sprite = RuntimeSprites.RoundedPanel();
        fill.type = Image.Type.Sliced;
        fill.color = CardDark;
        RuntimeUiKit.AddOutline(card, GoldOutline(0.18f));
        LayoutElement layout = card.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        return card;
    }
}
