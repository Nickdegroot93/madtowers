using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using static RuntimeUiKit;

// The chapter backdrop (sky / looping video / image).
// (partial of MainMenuRuntime, split from the main file for readability - same class, shared statics.)
public static partial class MainMenuRuntime
{
    private static void BuildBackground(Transform parent, ChapterDefinition chapter)
    {
        Color top = Color.Lerp(chapter.MenuAccentSecondaryColor, Color.black, 0.35f);
        Color bottom = Color.Lerp(chapter.MenuAccentColor, Color.black, 0.68f);

        // The chapter imagery (image + video) lives on a movable track so a swipe can slide
        // it - and the incoming chapter's background, parented into the same track - as one
        // motion with the foreground. The dimming overlays below sit on the fixed layer so
        // the whole screen stays evenly dimmed no matter where the track is panned.
        RectTransform track = (RectTransform)CreateLayer(parent, "BgTrack");

        if (chapter.MenuBackgroundImage != null)
        {
            Image image = CreateImage(track, "BackgroundImage", chapter.MenuBackgroundImage, Color.white);
            Stretch(image.rectTransform);
            image.preserveAspect = false;
        }
        else
        {
            Image fallback = CreateImage(track, "GeneratedBackground",
                MenuSprites.Background(top, bottom, chapter.MenuAccentColor), Color.white);
            Stretch(fallback.rectTransform);
        }

        if (chapter.MenuBackgroundVideo != null)
        {
            _videoTexture = new RenderTexture(720, 1280, 0, RenderTextureFormat.ARGB32);
            _videoTexture.name = "MenuBackgroundVideoRT";
            _videoTexture.hideFlags = HideFlags.HideAndDontSave;
            _videoTexture.Create();

            RawImage videoImage = CreateRawImage(track, "BackgroundVideo", _videoTexture, Color.white);
            Stretch(videoImage.rectTransform);
            videoImage.color = new Color(1f, 1f, 1f, 0f);

            GameObject playerObject = new GameObject("BackgroundVideoPlayer");
            playerObject.transform.SetParent(track, false);
            VideoPlayer player = playerObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.isLooping = true;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = _videoTexture;
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.clip = chapter.MenuBackgroundVideo;
            player.prepareCompleted += source =>
            {
                if (videoImage == null || source == null) return;
                videoImage.color = Color.white;
                source.Play();
            };
            player.Prepare();
        }

        Image dim = CreateImage(parent, "ReadabilityOverlay", RuntimeSprites.Square(),
            new Color(0.02f, 0.018f, 0.014f, 0.24f));
        Stretch(dim.rectTransform);
    }

}
