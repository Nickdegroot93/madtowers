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
        _backgroundTrack = track;

        Sprite sprite = chapter.MenuBackgroundImage != null
            ? chapter.MenuBackgroundImage
            : MenuSprites.Background(top, bottom, chapter.MenuAccentColor);
        RectTransform backdrop = CreateTrackBackdrop(track, "BackgroundImage", sprite);

        if (chapter.MenuBackgroundVideo != null)
        {
            _videoTexture = new RenderTexture(720, 1280, 0, RenderTextureFormat.ARGB32);
            _videoTexture.name = "MenuBackgroundVideoRT";
            _videoTexture.hideFlags = HideFlags.HideAndDontSave;
            _videoTexture.Create();

            // Lives inside the image's clip window so the crossfading pair cover-fits as one.
            RawImage videoImage = CreateRawImage(backdrop, "BackgroundVideo", _videoTexture, Color.white);
            Stretch(videoImage.rectTransform);
            FitToCover(videoImage, (float)_videoTexture.width / _videoTexture.height);
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
                // Fade the video over the (identical first-frame) static image instead of
                // popping in - after a chapter swipe the backdrop must not visibly "switch on".
                videoImage.color = Color.white;
                videoImage.canvasRenderer.SetAlpha(0f);
                source.Play();
                videoImage.CrossFadeAlpha(1f, 0.6f, true);
            };
            player.Prepare();
        }

        Image dim = CreateImage(parent, "ReadabilityOverlay", RuntimeSprites.Square(),
            new Color(0.02f, 0.018f, 0.014f, 0.24f));
        Stretch(dim.rectTransform);
    }

    // One chapter backdrop on the swipe track: a screen-sized RectMask2D window with the art
    // cover-fit inside (aspect preserved, overflow cropped - stretching to the screen squashed
    // the art on tall phones). The mask is load-bearing during swipes: cover-fit art is wider
    // than its own screen slot, and unclipped it would overlap the neighbouring backdrop as
    // the two travel side by side on the track.
    private static RectTransform CreateTrackBackdrop(RectTransform track, string name, Sprite sprite)
    {
        RectTransform frame = (RectTransform)CreateLayer(track, name);
        frame.gameObject.AddComponent<RectMask2D>();

        Image image = CreateImage(frame, "Image", sprite, Color.white);
        Stretch(image.rectTransform);
        FitToCover(image, SpriteAspect(sprite));
        return frame;
    }

}
