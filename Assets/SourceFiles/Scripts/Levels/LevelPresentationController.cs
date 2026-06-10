using UnityEngine;

[ExecuteAlways]
public class LevelPresentationController : MonoBehaviour
{
    [SerializeField] private LevelDefinition levelDefinition;
    [SerializeField] private SpriteRenderer backgroundRenderer;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Color fallbackBackgroundTint = new Color(0.08f, 0.12f, 0.17f, 1f);
    [SerializeField] private Color cameraClearColor = new Color(0.06f, 0.08f, 0.11f, 1f);
    [SerializeField] private bool fillCameraView = true;
    [SerializeField] private float overscan = 1.12f;
    [SerializeField] private float depthFromCamera = 20f;
    [SerializeField] private int sortingOrder = -100;

    private void OnEnable()
    {
        ApplyPresentation();
    }

    private void LateUpdate()
    {
        ApplyPresentation();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ApplyPresentation();
    }
#endif

    private ThemeDefinition _resolvedTheme;
    private LevelDefinition _resolvedThemeLevel;

    // The theme supplies the look shared by its levels; a level overrides it by setting its
    // own background image, or a tint other than pure white (white = "use the theme's").
    private ThemeDefinition ResolveTheme(LevelDefinition level)
    {
        if (level == null) return null;
        if (_resolvedThemeLevel == level) return _resolvedTheme;

        _resolvedThemeLevel = level;
        _resolvedTheme = null;
        ThemeDefinition[] themes = Resources.LoadAll<ThemeDefinition>("Themes");
        for (int themeIndex = 0; themeIndex < themes.Length; themeIndex++)
        {
            ThemeDefinition theme = themes[themeIndex];
            if (theme.Levels == null) continue;

            for (int i = 0; i < theme.Levels.Count; i++)
            {
                if (theme.Levels[i] != level) continue;

                _resolvedTheme = theme;
                return _resolvedTheme;
            }
        }

        return null;
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

        if (targetCamera != null)
        {
            targetCamera.backgroundColor = activeLevel != null ? backgroundTint : cameraClearColor;
        }

        if (backgroundRenderer == null) return;

        Sprite configuredSprite = activeLevel != null ? activeLevel.BackgroundImage : null;
        if (configuredSprite == null && activeTheme != null)
        {
            configuredSprite = activeTheme.BackgroundImage;
        }
        if (configuredSprite != null)
        {
            backgroundRenderer.sprite = configuredSprite;
            backgroundRenderer.color = backgroundTint;
        }
        else
        {
            backgroundRenderer.color = backgroundTint;
        }

        backgroundRenderer.sortingOrder = sortingOrder;

        if (fillCameraView)
        {
            FitBackgroundToCamera();
        }
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
