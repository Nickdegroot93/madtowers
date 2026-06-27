using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

/// <summary>
/// Runtime-only presentation mode for Extract. It never moves real physics bodies: landed pieces
/// are hidden behind visual proxies that expand, float, accept a tap, then collapse away.
/// </summary>
public sealed class ExtractTargetingSession : MonoBehaviour
{
    private sealed class Proxy
    {
        public BlockController Block;
        public Transform Root;
        public SpriteRenderer[] Renderers;
        public Vector3 StartPosition;
        public Vector3 ExpandedPosition;
        public float FloatPhase;
        public Bounds Bounds;
    }

    private struct HiddenRenderer
    {
        public SpriteRenderer Renderer;
        public BlockController Owner;
    }

    private enum State
    {
        Opening,
        Selecting,
        Vanishing,
        Closing
    }

    private enum TargetEffect
    {
        Extract,
        Suspension
    }

    private const float OpenSeconds = 0.18f;
    private const float VanishSeconds = 0.14f;
    private const float CloseSeconds = 0.16f;
    private const float FloatAmplitude = 0.032f;
    private const float FloatSpeed = 2.8f;
    private const float ExpandScale = 1.08f;
    private const float RadialSeparation = 0.48f;
    private const float ProportionalSeparation = 0.11f;
    private const int SortingOrderLift = 220;

    private readonly List<Proxy> _proxies = new List<Proxy>();
    private readonly List<HiddenRenderer> _hiddenRenderers = new List<HiddenRenderer>();
    private State _state;
    private float _age;
    private Proxy _selected;
    private Camera _camera;
    private bool _pausedGame;
    private bool _finishing;
    private TargetEffect _effect;
    private BlockData _anchorVariant;

    public static bool IsActive { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        IsActive = false;
    }

    public static void Begin()
    {
        if (IsActive) return;

        GameObject go = new GameObject("ExtractTargetingSession");
        go.AddComponent<ExtractTargetingSession>().StartSession(TargetEffect.Extract, null);
    }

    public static void BeginSuspension(BlockData anchorVariant)
    {
        if (IsActive) return;

        GameObject go = new GameObject("SuspensionTargetingSession");
        go.AddComponent<ExtractTargetingSession>().StartSession(TargetEffect.Suspension, anchorVariant);
    }

    private void StartSession(TargetEffect effect, BlockData anchorVariant)
    {
        IsActive = true;
        _effect = effect;
        _anchorVariant = anchorVariant;
        _camera = Camera.main;
        BuildProxies();

        if (_proxies.Count == 0)
        {
            Finish();
            return;
        }

        if (GameManager.Instance != null && !GameManager.Instance.IsGamePaused)
        {
            GameManager.Instance.SetGamePaused(true);
            _pausedGame = true;
        }

        _state = State.Opening;
        _age = 0f;
    }

    private void Update()
    {
        _age += Time.unscaledDeltaTime;

        switch (_state)
        {
            case State.Opening:
                ApplyProxyLayout(Smooth01(_age / OpenSeconds));
                if (_age >= OpenSeconds)
                {
                    _state = State.Selecting;
                    _age = 0f;
                }
                break;
            case State.Selecting:
                ApplyProxyLayout(1f);
                HandleSelectionInput();
                break;
            case State.Vanishing:
                ApplyProxyLayout(1f);
                ApplySelectedResolution(Smooth01(_age / VanishSeconds));
                if (_age >= VanishSeconds)
                {
                    ResolveSelectedBlock();
                    _state = State.Closing;
                    _age = 0f;
                }
                break;
            case State.Closing:
                ApplyProxyLayout(1f - Smooth01(_age / CloseSeconds));
                if (_age >= CloseSeconds) Finish();
                break;
        }
    }

    private void BuildProxies()
    {
        Bounds towerBounds = default;
        bool hasBounds = false;
        var blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block == null || !block.HasLanded) continue;
            if (!CanTarget(block)) continue;
            if (!block.TryGetWorldBounds(out Bounds bounds)) continue;
            if (!BlockQuery.IsOnScreen(bounds, _camera)) continue;

            if (!hasBounds)
            {
                towerBounds = bounds;
                hasBounds = true;
            }
            else
            {
                towerBounds.Encapsulate(bounds);
            }
        }
        if (!hasBounds) return;

        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block == null || !block.HasLanded) continue;
            if (!CanTarget(block)) continue;
            if (!block.TryGetWorldBounds(out Bounds bounds)) continue;
            if (!BlockQuery.IsOnScreen(bounds, _camera)) continue;

            Proxy proxy = CreateProxy(block, bounds, towerBounds.center);
            if (proxy != null) _proxies.Add(proxy);
        }
    }

    private Proxy CreateProxy(BlockController block, Bounds bounds, Vector3 towerCenter)
    {
        SpriteRenderer[] sourceRenderers = block.GetComponentsInChildren<SpriteRenderer>();
        var copied = new List<SpriteRenderer>();
        MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();

        GameObject root = new GameObject("ExtractProxy");
        root.transform.position = block.transform.position;
        root.transform.rotation = block.transform.rotation;
        root.transform.localScale = Vector3.one;

        for (int i = 0; i < sourceRenderers.Length; i++)
        {
            SpriteRenderer source = sourceRenderers[i];
            if (source == null || !source.enabled || source.sprite == null) continue;
            if (source.gameObject.name == "PlacementBeam") continue;

            GameObject child = new GameObject(source.gameObject.name);
            child.transform.position = source.transform.position;
            child.transform.rotation = source.transform.rotation;
            child.transform.localScale = source.transform.lossyScale;
            child.transform.SetParent(root.transform, true);

            SpriteRenderer clone = child.AddComponent<SpriteRenderer>();
            clone.sprite = source.sprite;
            clone.sharedMaterial = source.sharedMaterial;
            clone.color = source.color;
            clone.flipX = source.flipX;
            clone.flipY = source.flipY;
            clone.drawMode = source.drawMode;
            clone.size = source.size;
            clone.sortingLayerID = source.sortingLayerID;
            clone.sortingOrder = source.sortingOrder + SortingOrderLift;
            propertyBlock.Clear();
            source.GetPropertyBlock(propertyBlock);
            clone.SetPropertyBlock(propertyBlock);
            copied.Add(clone);

            // Hide the real renderer by DISABLING it, not by zeroing its color alpha: procedural brick
            // shaders (Maw, Magma/Lava) ignore the SpriteRenderer vertex colour, so an alpha of 0 leaves
            // them fully visible - which used to leave the real brick sitting behind the moving proxy (a
            // ghostly "second layer"). enable/disable is shader-independent and never touches RGB, so any
            // recolour Suspension applies to the real block also survives untouched.
            _hiddenRenderers.Add(new HiddenRenderer { Renderer = source, Owner = block });
            source.enabled = false;
        }

        if (copied.Count == 0)
        {
            Destroy(root);
            return null;
        }

        Vector3 fromCenter = bounds.center - towerCenter;
        Vector3 direction = fromCenter.sqrMagnitude > 0.001f ? fromCenter.normalized : Vector3.up;
        Vector3 offset = direction * RadialSeparation + fromCenter * ProportionalSeparation;

        return new Proxy
        {
            Block = block,
            Root = root.transform,
            Renderers = copied.ToArray(),
            StartPosition = block.transform.position,
            ExpandedPosition = block.transform.position + offset,
            FloatPhase = Random.Range(0f, Mathf.PI * 2f),
            Bounds = bounds
        };
    }

    private void ApplyProxyLayout(float open)
    {
        float time = Time.unscaledTime;
        for (int i = 0; i < _proxies.Count; i++)
        {
            Proxy proxy = _proxies[i];
            if (proxy.Root == null) continue;

            float floatOffset = _state == State.Selecting || _state == State.Vanishing
                ? Mathf.Sin(time * FloatSpeed + proxy.FloatPhase) * FloatAmplitude
                : 0f;
            Vector3 position = Vector3.Lerp(proxy.StartPosition, proxy.ExpandedPosition, open);
            position.y += floatOffset * open;
            proxy.Root.position = position;
            float scale = Mathf.Lerp(1f, ExpandScale, open);
            proxy.Root.localScale = Vector3.one * scale;
            RecalculateProxyBounds(proxy);
        }
    }

    private void ApplySelectedResolution(float amount)
    {
        if (_selected == null || _selected.Root == null) return;

        float scale = _effect == TargetEffect.Extract
            ? Mathf.Lerp(ExpandScale, 0.08f, amount)
            : ExpandScale + Mathf.Sin(amount * Mathf.PI) * 0.1f;
        _selected.Root.localScale = Vector3.one * scale;
        for (int i = 0; i < _selected.Renderers.Length; i++)
        {
            SpriteRenderer renderer = _selected.Renderers[i];
            if (renderer == null) continue;
            Color color = renderer.color;
            if (_effect == TargetEffect.Extract)
            {
                color.a = 1f - amount;
            }
            else
            {
                color = Color.Lerp(color, new Color(0.5f, 0.82f, 1f, color.a), Mathf.Sin(amount * Mathf.PI));
            }
            renderer.color = color;
        }
    }

    private void HandleSelectionInput()
    {
        if (TryGetSelectionPoint(out Vector2 screenPoint) && TryPickProxy(screenPoint, out Proxy proxy))
        {
            _selected = proxy;
            _state = State.Vanishing;
            _age = 0f;
        }
    }

    private bool TryGetSelectionPoint(out Vector2 screenPoint)
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            screenPoint = Mouse.current.position.ReadValue();
            if (!IsPointerOverUi()) return true;
        }
#endif

        foreach (Touch touch in Touch.activeTouches)
        {
            if (touch.phase != UnityEngine.InputSystem.TouchPhase.Began) continue;
            screenPoint = touch.screenPosition;
            if (!IsPointerOverUi(touch.touchId)) return true;
        }

        screenPoint = default;
        return false;
    }

    private bool TryPickProxy(Vector2 screenPoint, out Proxy picked)
    {
        picked = null;
        if (_camera == null) _camera = Camera.main;
        if (_camera == null) return false;

        Vector3 world = _camera.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, -_camera.transform.position.z));
        float bestDepth = float.MinValue;
        for (int i = 0; i < _proxies.Count; i++)
        {
            Proxy proxy = _proxies[i];
            if (proxy.Block == null || proxy.Root == null) continue;
            if (!proxy.Bounds.Contains(new Vector3(world.x, world.y, proxy.Bounds.center.z))) continue;

            float depth = proxy.Bounds.center.y;
            if (depth <= bestDepth) continue;
            bestDepth = depth;
            picked = proxy;
        }

        return picked != null;
    }

    private void ResolveSelectedBlock()
    {
        if (_selected == null || _selected.Block == null) return;

        if (_effect == TargetEffect.Extract)
        {
            if (GameManager.Instance != null) GameManager.Instance.RemovePlacedBlock(_selected.Block);
            SfxPlayer.Play("impact_soft_01", 0.7f, 0.06f);
            Destroy(_selected.Block.gameObject);
            return;
        }

        // Convert into the Anchor variant first so it adopts the shared anchor look
        // (ApplyData re-tints the existing skin), then freeze it as a Static body.
        if (_anchorVariant != null) _selected.Block.ApplyData(_anchorVariant);
        _selected.Block.FreezeInPlace();
        SfxPlayer.Play("pop_01", 0.7f, 0.04f);
    }

    private bool CanTarget(BlockController block) => IsTargetable(block, _effect == TargetEffect.Suspension);

    /// <summary>
    /// The single targetability rule for the Extract/Suspension fly-out, shared by this session's
    /// CanTarget AND the two abilities' CanActivate so the rules can't drift across the three.
    /// Maws never participate (they stay stacked, can't be selected, and render through a
    /// vertex-colour-ignoring shader that would leave the real maw visible behind a proxy);
    /// Suspension (<paramref name="excludeFrozen"/>) also skips an already-frozen/static block.
    /// </summary>
    public static bool IsTargetable(BlockController block, bool excludeFrozen)
    {
        if (block == null || !block.HasLanded) return false;
        if (block.GetComponent<MawBlockSkin>() != null) return false;
        if (excludeFrozen && block.IsFrozenInPlace) return false;
        return true;
    }

    /// <summary>True if at least one ON-SCREEN block can be targeted - the abilities' CanActivate gate.</summary>
    public static bool HasAnyTargetable(bool excludeFrozen)
    {
        IReadOnlyList<BlockController> blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (IsTargetable(block, excludeFrozen) && BlockQuery.IsOnScreen(block)) return true;
        }
        return false;
    }

    private static void RecalculateProxyBounds(Proxy proxy)
    {
        bool hasBounds = false;
        Bounds bounds = default;
        for (int i = 0; i < proxy.Renderers.Length; i++)
        {
            SpriteRenderer renderer = proxy.Renderers[i];
            if (renderer == null) continue;
            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        proxy.Bounds = bounds;
    }

    private void RestoreHiddenRenderers()
    {
        for (int i = 0; i < _hiddenRenderers.Count; i++)
        {
            HiddenRenderer hidden = _hiddenRenderers[i];
            if (hidden.Renderer == null) continue;
            // Re-enable the renderers we disabled to hide the real block - EXCEPT the selected one.
            // Suspension converts the selected block into an Anchor (ApplyData re-skins it and deliberately
            // disables the chapter art), so re-enabling here would resurrect the old art under the anchor.
            // Extract destroys its selection, so those renderers are already null and skipped above.
            if (_selected != null && hidden.Owner == _selected.Block) continue;
            hidden.Renderer.enabled = true;
        }
    }

    private void Finish(bool destroySelf = true)
    {
        if (_finishing) return;
        _finishing = true;

        RestoreHiddenRenderers();
        for (int i = 0; i < _proxies.Count; i++)
        {
            if (_proxies[i].Root != null) Destroy(_proxies[i].Root.gameObject);
        }
        _proxies.Clear();

        if (_pausedGame && GameManager.Instance != null)
        {
            GameManager.Instance.SetGamePaused(false);
        }
        IsActive = false;
        if (destroySelf) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (!_finishing && IsActive) Finish(false);
    }

    private static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private static bool IsPointerOverUi(int pointerId = -1)
    {
        if (EventSystem.current == null) return false;
        return pointerId >= 0
            ? EventSystem.current.IsPointerOverGameObject(pointerId)
            : EventSystem.current.IsPointerOverGameObject();
    }
}
