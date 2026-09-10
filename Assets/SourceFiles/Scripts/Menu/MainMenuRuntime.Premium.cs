using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static RuntimeUiKit;

public static partial class MainMenuRuntime
{
    private static GameObject _premiumConfirmation;

    // Only explicit purchase/restore exchanges call this. A background ownership refresh
    // never opens a celebration, and the facade's callback latch prevents double delivery.
    internal static void ShowUnlimitedConfirmation(PremiumStoreResult result)
    {
        if (result != PremiumStoreResult.Purchased && result != PremiumStoreResult.Restored) return;
        if (_premiumConfirmation != null) UnityEngine.Object.Destroy(_premiumConfirmation);
        bool restored = result == PremiumStoreResult.Restored;
        var root = CreateOverlayCanvas("Unlimited Confirmation", 13000);
        _premiumConfirmation = root;
        if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(root); // restore may release the startup gate
        var backdrop = CreateImage(root.transform, "Backdrop", null, new Color(0f, 0f, 0f, .76f));
        Stretch(backdrop.rectTransform);
        backdrop.raycastTarget = true;
        var panel = CreateRect(root.transform, "Panel", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
            new Vector2(.5f, .5f), Vector2.zero, new Vector2(780f, restored ? 570f : 710f));
        panel.gameObject.AddComponent<Image>().raycastTarget = true;
        GameMenuStyle.StylePanel(panel.gameObject, GameMenuStyle.ActiveChapter ?? MenuPresentationChapter);
        ModalSafeFrame.Attach(panel);
        var mark = CreateTmp(panel, "Infinity", "∞", 88, new Color(1f, .87f, .56f),
            TextAnchor.MiddleCenter, FontStyle.Normal, DefaultFont,
            new Vector2(0f, -24f), new Vector2(240f, 144f), new Vector2(.5f, 1f));
        mark.raycastTarget = false;
        CreateTmp(panel, "Title", restored ? "Unlimited restored" : "Welcome to Unlimited", 42, Color.white,
            TextAnchor.MiddleCenter, FontStyle.Normal, DefaultFont,
            new Vector2(0f, -176f), new Vector2(700f, 64f), new Vector2(.5f, 1f));
        CreateTmp(panel, "Thanks", restored ? "Your full game is ready on this device."
            : "Thank you for supporting Hazard Heights.", 27, GameMenuStyle.BodyText,
            TextAnchor.MiddleCenter, FontStyle.Normal, DefaultFont,
            new Vector2(0f, -262f), new Vector2(660f, 80f), new Vector2(.5f, 1f));
        if (!restored)
            CreateTmp(panel, "Benefits", "Unlimited lives. Offline play.\nNo ads. Forever.", 30, Color.white,
                TextAnchor.MiddleCenter, FontStyle.Normal, DefaultFont,
                new Vector2(0f, -370f), new Vector2(660f, 110f), new Vector2(.5f, 1f));
        var action = CreateImage(panel, "Continue", RuntimeSprites.RoundedPanel(), new Color(.97f, .94f, .84f));
        action.type = Image.Type.Sliced;
        SetRect(action.rectTransform, new Vector2(0f, 84f), new Vector2(630f, 94f), new Vector2(.5f, 0f));
        action.rectTransform.pivot = new Vector2(.5f, 0f);
        action.raycastTarget = true;
        CreateTmp(action.transform, "Label", restored ? "Continue" : "Let’s build", 30, Color.black,
            TextAnchor.MiddleCenter, FontStyle.Normal, DefaultFont);
        var button = action.gameObject.AddComponent<Button>();
        button.targetGraphic = action;
        button.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            UnityEngine.Object.Destroy(root);
        });
        UiEntranceFx.Play(panel.gameObject);
    }
}
