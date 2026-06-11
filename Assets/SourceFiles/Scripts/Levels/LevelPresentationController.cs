using UnityEngine;

[ExecuteAlways]
public class LevelPresentationController : MonoBehaviour
{
    [SerializeField] private LevelDefinition levelDefinition;
    [SerializeField] private SpriteRenderer backgroundRenderer;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Color fallbackBackgroundTint = new Color(0.08f, 0.12f, 0.17f, 1f);
    [SerializeField] private bool fillCameraView = true;
    [SerializeField] private float overscan = 1.12f;
    [SerializeField] private float depthFromCamera = 20f;
    [SerializeField] private int sortingOrder = -100;

    // Refreshed from LateUpdate only ([ExecuteAlways] runs it in edit mode too).
    // No OnEnable/OnValidate calls: assigning a sprite there happens inside Unity's
    // Awake/CheckConsistency phase and triggers "SendMessage cannot be called..." warnings.
    private void LateUpdate()
    {
        ApplyPresentation();
    }

    private ThemeDefinition _resolvedTheme;
    private LevelDefinition _resolvedThemeLevel;

    // The theme supplies the look shared by its levels; a level overrides it by setting its
    // own background image, or a tint other than pure white (white = "use the theme's").
    private ThemeDefinition ResolveTheme(LevelDefinition level)
    {
        if (level == null) return null;
        if (_resolvedThemeLevel == level) return _resolvedTheme;

        // Cached: this runs from LateUpdate every frame, FindThemeOf scans assets.
        _resolvedThemeLevel = level;
        _resolvedTheme = Campaign.FindThemeOf(level);
        return _resolvedTheme;
    }

    private void ApplyPresentation()
    {
        ResolveReferences();
        LevelDefinition activeLevel = LevelSelectionState.SelectedLevel != null
            ? LevelSelectionState.SelectedLevel
            : levelDefinition;
        ThemeDefinition activeTheme = ResolveTheme(activeLevel);

        Color backgroundTint = activeLevel != null ? activeLevel.BackgroundTint : fallbackBackgroundTint;
        if (activeLevel != null && activeTheme != null &&
            activeLevel.BackgroundImage == null && activeLevel.BackgroundTint == Color.white)
        {
            backgroundTint = activeTheme.BackgroundTint;
        }

        if (activeLevel == null)
        {
            backgroundTint = fallbackBackgroundTint;
        }

        // The camera clear color matches the gradient's bottom so anything peeking past
        // the background quad (overscan edges) blends instead of banding.
        Color gradientBottom = Color.Lerp(backgroundTint, Color.black, 0.55f);
        if (targetCamera != null)
        {
            targetCamera.backgroundColor = gradientBottom;
        }

        if (backgroundRenderer == null) return;

        Sprite configuredSprite = activeLevel != null ? activeLevel.BackgroundImage : null;
        if (configuredSprite == null && activeTheme != null)
        {
            configuredSprite = activeTheme.BackgroundImage;
        }
        if (configuredSprite == null)
        {
            // No art configured: a clean vertical gradient (lighter top, darker bottom)
            // built in code. Without a sprite the quad never got fitted to the camera and
            // sat in the world as a stray lighter rectangle.
            configuredSprite = GetGradientSprite(backgroundTint, gradientBottom);
            backgroundTint = Color.white;
        }
        if (backgroundRenderer.sprite != configuredSprite) backgroundRenderer.sprite = configuredSprite;
        if (backgroundRenderer.color != backgroundTint) backgroundRenderer.color = backgroundTint;

        backgroundRenderer.sortingOrder = sortingOrder;

        if (fillCameraView)
        {
            FitBackgroundToCamera();
        }
    }

    // Caches the generated gradient per tint; the previous sprite is destroyed on change
    // (RuntimeSprites.VerticalGradient returns caller-owned sprites).
    private Sprite _gradientSprite;
    private Color _gradientTopTint;

    private Sprite GetGradientSprite(Color top, Color bottom)
    {
        if (_gradientSprite != null && top == _gradientTopTint) return _gradientSprite;
        _gradientTopTint = top;

        if (_gradientSprite != null)
        {
            DestroyImmediate(_gradientSprite.texture);
            DestroyImmediate(_gradientSprite);
        }

        _gradientSprite = RuntimeSprites.VerticalGradient(top, bottom);
        return _gradientSprite;
    }

    private void ResolveReferences()
    {
        if (backgroundRenderer == null)
        {
            backgroundRenderer = GetComponent<SpriteRenderer>();
        }

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    private void FitBackgroundToCamera()
    {
        if (targetCamera == null || backgroundRenderer.sprite == null) return;

        Vector3 cameraPosition = targetCamera.transform.position;
        transform.position = new Vector3(cameraPosition.x, cameraPosition.y, cameraPosition.z + depthFromCamera);

        if (!targetCamera.orthographic) return;

        Vector2 spriteSize = backgroundRenderer.sprite.bounds.size;
        if (spriteSize.x <= 0f || spriteSize.y <= 0f) return;

        float cameraHeight = targetCamera.orthographicSize * 2f;
        float cameraWidth = cameraHeight * targetCamera.aspect;
        float scale = Mathf.Max(cameraWidth / spriteSize.x, cameraHeight / spriteSize.y) * Mathf.Max(1f, overscan);
        transform.localScale = new Vector3(scale, scale, 1f);
    }
}
