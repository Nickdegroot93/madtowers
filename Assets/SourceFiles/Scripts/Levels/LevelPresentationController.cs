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

    private void ApplyPresentation()
    {
        ResolveReferences();

        if (targetCamera != null)
        {
            targetCamera.backgroundColor = cameraClearColor;
        }

        if (backgroundRenderer == null) return;

        Sprite configuredSprite = levelDefinition != null ? levelDefinition.BackgroundImage : null;
        if (configuredSprite != null)
        {
            backgroundRenderer.sprite = configuredSprite;
            backgroundRenderer.color = levelDefinition.BackgroundTint;
        }
        else
        {
            backgroundRenderer.color = fallbackBackgroundTint;
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
