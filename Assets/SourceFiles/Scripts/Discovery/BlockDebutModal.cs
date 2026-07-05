using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The one-time "new brick discovered" card: a chapter-styled panel with the live looping demo
/// up top (the BlockDemoStage's RenderTexture in a rounded RawImage), the brick's name and
/// description below, and a single Continue button. Pure presentation - the freeze, the demo
/// stage and the once-ever bookkeeping belong to BlockDiscoveryController.
/// </summary>
public static class BlockDebutModal
{
    // Matches the ability offer's footprint (sort just above its 6000 so a queued offer can
    // never paint over a debut).
    private const int SortOrder = 6100;
    private const float PanelWidth = 800f;

    /// <summary>Demo texture size the modal's viewport wants (panel width minus padding, 4:3.5).</summary>
    public const int DemoPixelWidth = 728;
    public const int DemoPixelHeight = 620;

    public static GameObject Show(BlockData variant, Texture demoTexture, System.Action onContinue)
    {
        GameObject root = RuntimeUiKit.CreateModal("Block Debut", SortOrder);

        GameObject panel = RuntimeUiKit.CreateCenteredPanel(root.transform, new Vector2(PanelWidth, 1160f),
            drawBackground: false);
        Image panelImage = panel.AddComponent<Image>();
        panelImage.sprite = RuntimeSprites.RoundedPanel();
        panelImage.type = Image.Type.Sliced;
        GameMenuStyle.StylePanel(panel);
        // The kit panel leaves child heights uncontrolled; this layout is height-budgeted via
        // LayoutElements, so they must be honored (the ability offer panel does the same).
        var layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 14f;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;

        Color accent = GameMenuStyle.Accent;

        TextMeshProUGUI eyebrow = RuntimeUiKit.CreateTmp(panel.transform, "Eyebrow", "NEW BRICK DISCOVERED",
            22, new Color(accent.r, accent.g, accent.b, 0.95f), TextAnchor.MiddleCenter, FontStyle.Bold,
            RuntimeUiKit.TitleFont);
        eyebrow.characterSpacing = 8f;
        eyebrow.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;

        TextMeshProUGUI title = RuntimeUiKit.CreateTmp(panel.transform, "Title",
            variant != null ? variant.DisplayName.ToUpperInvariant() : "?",
            52, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 70f;

        // The live demo: fills the panel width, rounded corners, thin accent frame. The rounded
        // mask must live on a HOLDER object - MakeRoundedMask adds an Image stencil, which can't
        // share a GameObject with the RawImage (one Graphic per object).
        var demoHolder = new GameObject("DemoFrame", typeof(RectTransform));
        demoHolder.transform.SetParent(panel.transform, false);
        demoHolder.AddComponent<LayoutElement>().preferredHeight = DemoPixelHeight;
        RuntimeUiKit.MakeRoundedMask((RectTransform)demoHolder.transform);
        RawImage demo = RuntimeUiKit.CreateRawImage(demoHolder.transform, "Demo", demoTexture, Color.white);
        RuntimeUiKit.Stretch(demo.rectTransform);
        RuntimeUiKit.AddOutline(demoHolder.transform, new Color(accent.r, accent.g, accent.b, 0.5f));

        string description = ResolveDescription(variant);
        TextMeshProUGUI body = RuntimeUiKit.CreateTmp(panel.transform, "Description", description,
            27, new Color(0.85f, 0.88f, 0.9f, 1f), TextAnchor.MiddleCenter, FontStyle.Normal,
            RuntimeUiKit.DefaultFont);
        // TMP reports its unwrapped text width as preferred, which lets a long line blow past the
        // panel; pin the width to the panel's inner width and wrap.
        body.textWrappingMode = TextWrappingModes.Normal;
        LayoutElement bodyLayout = body.gameObject.AddComponent<LayoutElement>();
        bodyLayout.preferredHeight = 150f;
        bodyLayout.preferredWidth = PanelWidth - 72f;

        Button cont = RuntimeUiKit.CreateButton(panel.transform, "CONTINUE", 92f, () =>
        {
            SfxPlayer.Play("ui-button-click");
            onContinue?.Invoke();
        });
        GameMenuStyle.StyleButton(cont, primary: true);

        UiEntranceFx.Play(panel);
        return root;
    }

    private static string ResolveDescription(BlockData variant)
    {
        // The authored Vault copy on the asset wins; the demo catalog's caption is the fallback
        // so a debut never shows an empty card.
        if (variant != null && !string.IsNullOrWhiteSpace(variant.VaultDescription))
            return variant.VaultDescription;
        return BlockDemoCatalog.Caption(variant);
    }
}
