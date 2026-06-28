using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

/// <summary>
/// Runtime-only driver for the Overdraw consumable. It removes the active falling block,
/// presents three floating shape choices, and feeds the selected shapes back into the
/// normal controlled-piece loop one at a time. The session never pauses time: the spawner
/// simply withholds automatic bag pieces while the draft is in progress.
/// </summary>
public sealed class OverdrawSession : MonoBehaviour
{
    private sealed class Choice
    {
        public BlockDefinition Definition;
        public Transform Root;
        public SpriteRenderer Renderer;
        public LineRenderer Ring;
        public LineRenderer Halo;
        public Bounds Bounds;
        public Color BaseColor;
        public Vector3 VisualOffset;
        public float Phase;
        public float IntroAge;
        public bool Flying;
        public float FlyAge;
        public Vector3 FlyFrom;
    }

    private const float HudClearanceCells = 1.45f;
    private const float DropBelowQueueCells = 0.55f;
    private const float ChoiceSpacingCells = 2.15f;
    private const float ChoiceScale = 0.62f;
    private const float ChoiceAlpha = 0.88f;
    private const float HoverAmplitude = 0.075f;
    private const float HoverSpeed = 2.55f;
    private const float ReflowLerp = 12f;
    private const float IntroSeconds = 0.22f;
    private const float FlySeconds = 0.28f;
    private const int SortingOrder = 235;
    private const int RingSegments = 72;

    private readonly List<Choice> _choices = new List<Choice>(3);
    private Spawner _spawner;
    private Camera _camera;
    private Choice _flyingChoice;
    private bool _finishing;
    private bool _spawnedChoiceThisFrame;
    private float _queueWorldY;
    private float _dropWorldY;
    private static Material _circleMaterial;

    public static bool IsActive { get; private set; }
    public static bool SuppressesNextPreview { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        IsActive = false;
        SuppressesNextPreview = false;
    }

    public static void Begin(Spawner spawner, int choiceCount)
    {
        if (IsActive || spawner == null || choiceCount <= 0) return;

        GameObject go = new GameObject("OverdrawSession");
        go.AddComponent<OverdrawSession>().StartSession(spawner, choiceCount);
    }

    private void StartSession(Spawner spawner, int choiceCount)
    {
        IsActive = true;
        ActivePieceSession.Enter();
        SuppressesNextPreview = true;
        _spawner = spawner;
        _camera = Camera.main;
        ResolveAnchors();

        _spawner.SetAutoSpawnSuspended(true);
        if (!_spawner.DestroyActivePieceWithoutLock())
        {
            Finish(resumeIfIdle: true);
            return;
        }

        List<BlockDefinition> definitions = _spawner.TakeDistinctQueued(choiceCount);
        if (definitions.Count == 0)
        {
            Finish(resumeIfIdle: true);
            return;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            Choice choice = CreateChoice(definitions[i], i);
            if (choice != null) _choices.Add(choice);
        }

        if (_choices.Count == 0)
        {
            Finish(resumeIfIdle: true);
            return;
        }

        GameEvents.BlockLocked += HandleBlockLocked;
    }

    private void OnDisable() => GameEvents.BlockLocked -= HandleBlockLocked;

    private void OnDestroy()
    {
        if (!_finishing) Finish(resumeIfIdle: true);
    }

    private void HandleBlockLocked(BlockController block)
    {
        if (_finishing) return;
        _spawnedChoiceThisFrame = false;

        if (_choices.Count == 0 && _flyingChoice == null)
        {
            Finish(resumeIfIdle: false);
        }
    }

    private void Update()
    {
        if (_finishing) return;

        if (GameManager.Instance != null && GameManager.Instance.isGameOver)
        {
            Finish(resumeIfIdle: false);
            return;
        }

        _spawnedChoiceThisFrame = false;
        ResolveAnchors();
        AnimateChoices(Time.unscaledDeltaTime);

        if (_flyingChoice != null || BlockController.ActiveControlled != null || _spawnedChoiceThisFrame) return;

        if (_choices.Count == 1) SelectChoice(_choices[0], playSound: false);
        else HandleSelectionInput();
    }

    private Vector3 DropPos => new Vector3(
        CenterWorldX(),
        _dropWorldY,
        _spawner != null ? _spawner.SpawnPosition.z : 0f);

    private float CenterWorldX()
    {
        if (_camera == null) _camera = Camera.main;
        if (_camera != null)
        {
            return _camera.transform.position.x;
        }

        return _spawner != null ? _spawner.SpawnPosition.x : 0f;
    }

    private void ResolveAnchors()
    {
        float hudBottom;
        if (_camera == null) _camera = Camera.main;
        if (UIManager.Instance != null && _camera != null &&
            UIManager.Instance.TryGetTopHudBottomWorldY(_camera, out float wy))
        {
            hudBottom = wy;
        }
        else
        {
            hudBottom = _spawner != null ? _spawner.SpawnPosition.y : 0f;
        }

        _queueWorldY = hudBottom - HudClearanceCells;
        _dropWorldY = _queueWorldY - DropBelowQueueCells;
    }

    private void AnimateChoices(float dt)
    {
        float time = Time.unscaledTime;
        int liveCount = 0;
        for (int i = 0; i < _choices.Count; i++)
        {
            if (!_choices[i].Flying) liveCount++;
        }

        int slot = 0;
        for (int i = 0; i < _choices.Count; i++)
        {
            Choice choice = _choices[i];
            if (choice.Root == null || choice.Flying) continue;

            choice.IntroAge += dt;
            float intro = Mathf.Clamp01(choice.IntroAge / IntroSeconds);
            float e = Smooth01(intro);
            Vector3 target = SlotPosition(slot, liveCount);
            target -= choice.VisualOffset * ChoiceScale;
            target.y += Mathf.Sin(time * HoverSpeed + choice.Phase) * HoverAmplitude;
            choice.Root.position = Vector3.Lerp(choice.Root.position, target, 1f - Mathf.Exp(-ReflowLerp * dt));
            choice.Root.localScale = Vector3.one * Mathf.Lerp(ChoiceScale * 0.82f, ChoiceScale, e);
            SetChoiceAlpha(choice, ChoiceAlpha * e);
            UpdateRing(choice, time, selected: false);
            RecalculateChoiceBounds(choice);
            slot++;
        }

        if (_flyingChoice == null) return;

        Choice flying = _flyingChoice;
        flying.FlyAge += dt;
        float t = Mathf.Clamp01(flying.FlyAge / FlySeconds);
        float fly = Smooth01(t);
        flying.Root.position = Vector3.Lerp(flying.FlyFrom, DropPos, fly);
        flying.Root.localScale = Vector3.one * Mathf.Lerp(ChoiceScale, 1f, fly);
        SetChoiceAlpha(flying, ChoiceAlpha * (1f - fly * 0.35f));
        UpdateRing(flying, time, selected: true);
        RecalculateChoiceBounds(flying);

        if (t < 1f) return;

        SpawnFlyingChoice();
    }

    private void SpawnFlyingChoice()
    {
        Choice choice = _flyingChoice;
        _flyingChoice = null;
        if (choice == null) return;

        _choices.Remove(choice);
        if (choice.Root != null) Destroy(choice.Root.gameObject);

        BlockController spawned = _spawner.SpawnControlledPieceAt(
            choice.Definition,
            DropPos,
            suspended: false,
            asNewSpawn: true);
        _spawnedChoiceThisFrame = spawned != null;
        if (spawned != null && _choices.Count == 0)
        {
            SuppressesNextPreview = false;
        }

        if (spawned == null)
        {
            Finish(resumeIfIdle: true);
        }
    }

    private void HandleSelectionInput()
    {
        if (!TryGetSelectionPoint(out Vector2 screenPoint)) return;
        if (TryPickChoice(screenPoint, out Choice picked)) SelectChoice(picked, playSound: true);
    }

    private void SelectChoice(Choice choice, bool playSound)
    {
        if (choice == null || choice.Root == null || _flyingChoice != null) return;

        choice.Flying = true;
        choice.FlyAge = 0f;
        choice.FlyFrom = choice.Root.position;
        _flyingChoice = choice;
        if (playSound) SfxPlayer.Play("swoosh_01", 0.45f, 0.08f);
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

    private bool TryPickChoice(Vector2 screenPoint, out Choice picked)
    {
        picked = null;
        if (_camera == null) _camera = Camera.main;
        if (_camera == null) return false;

        Vector3 world = _camera.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, -_camera.transform.position.z));
        float bestY = float.MinValue;
        for (int i = 0; i < _choices.Count; i++)
        {
            Choice choice = _choices[i];
            if (choice.Root == null || choice.Flying) continue;
            if (!choice.Bounds.Contains(new Vector3(world.x, world.y, choice.Bounds.center.z))) continue;

            if (choice.Bounds.center.y <= bestY) continue;
            picked = choice;
            bestY = choice.Bounds.center.y;
        }

        return picked != null;
    }

    private Choice CreateChoice(BlockDefinition definition, int index)
    {
        if (definition == null) return null;

        Sprite sprite = ResolveSprite(definition);
        if (sprite == null) return null;

        GameObject root = new GameObject($"OverdrawChoice_{definition.DisplayName}");
        root.transform.position = DropPos;
        root.transform.localScale = Vector3.one * ChoiceScale * 0.82f;

        Vector3 visualOffset = ResolveVisualOffset(definition);
        GameObject visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = visualOffset;

        SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = SortingOrder;
        Color color = Color.white;
        color.a = 0f;
        renderer.color = color;

        LineRenderer halo = CreateCircle(visual.transform, "Halo", SortingOrder - 1, 0.065f, new Color(0.2f, 0.78f, 1f, 0.16f));
        LineRenderer ring = CreateCircle(visual.transform, "Ring", SortingOrder + 1, 0.023f, new Color(0.72f, 0.9f, 1f, 0.66f));
        float radius = Mathf.Max(sprite.bounds.extents.x, sprite.bounds.extents.y) + 0.32f;
        ConfigureCircle(halo, radius * 1.08f);
        ConfigureCircle(ring, radius);

        Choice choice = new Choice
        {
            Definition = definition,
            Root = root.transform,
            Renderer = renderer,
            Ring = ring,
            Halo = halo,
            BaseColor = Color.white,
            VisualOffset = visualOffset,
            Phase = index * 1.7f + Random.Range(0f, Mathf.PI * 2f)
        };
        RecalculateChoiceBounds(choice);
        return choice;
    }

    private static Sprite ResolveSprite(BlockDefinition definition)
    {
        string shape = ChapterSkins.ExtractShapeToken(definition.DisplayName);
        if (string.IsNullOrEmpty(shape) && definition.Prefab != null)
        {
            shape = ChapterSkins.ExtractShapeToken(definition.Prefab.name);
        }
        return ChapterSkins.LoadPiece(shape);
    }

    private static Vector3 ResolveVisualOffset(BlockDefinition definition)
    {
        if (definition == null || definition.Prefab == null) return Vector3.zero;

        SpriteRenderer[] renderers = definition.Prefab.GetComponentsInChildren<SpriteRenderer>(true);
        if (renderers == null || renderers.Length == 0) return Vector3.zero;

        bool any = false;
        Vector2 min = Vector2.zero;
        Vector2 max = Vector2.zero;
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || renderer.gameObject.name == "PlacementBeam") continue;

            Vector2 p = renderer.transform.localPosition;
            if (!any)
            {
                min = p;
                max = p;
                any = true;
            }
            else
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
        }

        if (!any) return Vector3.zero;
        Vector2 center = (min + max) * 0.5f;
        return new Vector3(center.x, center.y, 0f);
    }

    private static LineRenderer CreateCircle(Transform parent, string name, int sortingOrder, float width, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        LineRenderer line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = RingSegments;
        line.widthMultiplier = width;
        line.numCapVertices = 6;
        line.numCornerVertices = 6;
        line.sortingOrder = sortingOrder;
        line.sharedMaterial = CircleMaterial();
        line.startColor = color;
        line.endColor = color;
        return line;
    }

    private static Material CircleMaterial()
    {
        if (_circleMaterial == null)
        {
            _circleMaterial = new Material(Shader.Find("Sprites/Default"));
        }
        return _circleMaterial;
    }

    private static void ConfigureCircle(LineRenderer line, float radius)
    {
        if (line == null) return;
        for (int i = 0; i < RingSegments; i++)
        {
            float a = (Mathf.PI * 2f * i) / RingSegments;
            line.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, -0.02f));
        }
    }

    private Vector3 SlotPosition(int index, int liveCount)
    {
        float totalWidth = Mathf.Max(0, liveCount - 1) * ChoiceSpacingCells;
        float centerX = CenterWorldX();
        float x = centerX - totalWidth * 0.5f + index * ChoiceSpacingCells;
        return new Vector3(x, _queueWorldY, _spawner != null ? _spawner.SpawnPosition.z : 0f);
    }

    private static void SetChoiceAlpha(Choice choice, float alpha)
    {
        if (choice.Renderer != null)
        {
            Color color = choice.BaseColor;
            color.a = alpha;
            choice.Renderer.color = color;
        }

        SetLineAlpha(choice.Ring, alpha * 0.72f);
        SetLineAlpha(choice.Halo, alpha * 0.22f);
    }

    private static void SetLineAlpha(LineRenderer line, float alpha)
    {
        if (line == null) return;
        Color start = line.startColor;
        Color end = line.endColor;
        start.a = alpha;
        end.a = alpha;
        line.startColor = start;
        line.endColor = end;
    }

    private static void UpdateRing(Choice choice, float time, bool selected)
    {
        if (choice.Ring == null || choice.Halo == null) return;
        float pulse = selected ? 1.08f : 1f + Mathf.Sin(time * 2.2f + choice.Phase) * 0.035f;
        choice.Ring.transform.localScale = Vector3.one * pulse;
        choice.Halo.transform.localScale = Vector3.one * (pulse * 1.02f);
    }

    private static void RecalculateChoiceBounds(Choice choice)
    {
        if (choice.Renderer == null)
        {
            choice.Bounds = default;
            return;
        }

        Bounds bounds = choice.Renderer.bounds;
        float padding = Mathf.Max(bounds.size.x, bounds.size.y) * 0.28f + 0.35f;
        bounds.Expand(padding);
        choice.Bounds = bounds;
    }

    private void Finish(bool resumeIfIdle)
    {
        if (_finishing) return;
        _finishing = true;

        for (int i = 0; i < _choices.Count; i++)
        {
            if (_choices[i].Root != null) Destroy(_choices[i].Root.gameObject);
        }
        _choices.Clear();

        if (_spawner != null)
        {
            _spawner.SetAutoSpawnSuspended(false);
            if (resumeIfIdle && BlockController.ActiveControlled == null &&
                (GameManager.Instance == null || !GameManager.Instance.isGameOver))
            {
                _spawner.ResumeSpawning();
            }
        }

        SuppressesNextPreview = false;
        IsActive = false;
        ActivePieceSession.Exit();
        Destroy(gameObject);
    }

    private static bool IsPointerOverUi(int pointerId = -1)
    {
        if (EventSystem.current == null) return false;
        return pointerId >= 0
            ? EventSystem.current.IsPointerOverGameObject(pointerId)
            : EventSystem.current.IsPointerOverGameObject();
    }

    private static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
