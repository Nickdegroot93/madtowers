using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>Half-resolution scene samples for the flood and void materials. No extra camera,
/// physics, post stack or UI: redraw only the visible world below each surface's sorting order.
/// The sprite list refreshes on creation/spawn, never by allocating a scene scan every frame.</summary>
public sealed class SurfaceSceneFx : MonoBehaviour
{
    private static SurfaceSceneFx _active;
    private int _floodUsers, _voidUsers;
    private Renderer[] _renderers = Array.Empty<Renderer>();
    private bool _dirty = true;
    private ScenePass _floodPass, _voidPass;
    private Texture2D _noise;

    public static Texture2D Acquire(bool flood)
    {
        if (_active == null) _active = new GameObject("SurfaceSceneFx").AddComponent<SurfaceSceneFx>();
        if (flood) _active._floodUsers++; else _active._voidUsers++;
        _active._dirty = true;
        if (_active._noise == null) _active._noise = BuildNoise();
        return _active._noise;
    }

    public static void Release(bool flood)
    {
        if (_active == null) return;
        if (flood) _active._floodUsers--; else _active._voidUsers--;
        if (_active._floodUsers <= 0 && _active._voidUsers <= 0)
        {
            var retiring = _active;
            _active = null; // a new surface this frame must not acquire a retiring host
            retiring.enabled = false;
            Destroy(retiring.gameObject);
        }
    }

    private void OnEnable()
    {
        _floodPass = new ScenePass("Flood scene", "_FloodSceneTex", 29);
        _voidPass = new ScenePass("Void backdrop", "_VoidSceneTex", -4);
        RenderPipelineManager.beginCameraRendering += BeforeCamera;
        GameEvents.BlockSpawned += OnBlockSpawned;
    }

    private void OnBlockSpawned(BlockController block, BlockData data) => _dirty = true;

    private void BeforeCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera.cameraType != CameraType.Game || camera != Camera.main) return;
        if (_dirty)
        {
            _renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            Array.Sort(_renderers, (a, b) =>
            {
                int layer = SortingLayer.GetLayerValueFromID(a.sortingLayerID).CompareTo(SortingLayer.GetLayerValueFromID(b.sortingLayerID));
                return layer != 0 ? layer : a.sortingOrder.CompareTo(b.sortingOrder);
            });
            _dirty = false;
        }
        var renderer = camera.GetUniversalAdditionalCameraData().scriptableRenderer;
        if (_voidUsers > 0) { _voidPass.Sources = _renderers; renderer.EnqueuePass(_voidPass); }
        if (_floodUsers > 0) { _floodPass.Sources = _renderers; renderer.EnqueuePass(_floodPass); }
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeCamera;
        GameEvents.BlockSpawned -= OnBlockSpawned;
    }

    private void OnDestroy()
    {
        if (_active == this) _active = null;
        if (_noise != null) Destroy(_noise);
    }

    private sealed class ScenePass : ScriptableRenderPass
    {
        private readonly string _name;
        private readonly int _textureId, _maxOrder;
        public Renderer[] Sources;
        private sealed class Data { public Renderer[] Sources; public int MaxOrder; public Rect View; }

        public ScenePass(string name, string texture, int maxOrder)
        {
            _name = name; _textureId = Shader.PropertyToID(texture); _maxOrder = maxOrder;
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var camera = cameraData.camera;
            var desc = cameraData.cameraTargetDescriptor;
            var texture = graph.CreateTexture(new TextureDesc(Mathf.Max(16, desc.width / 2), Mathf.Max(16, desc.height / 2))
            {
                name = _name, colorFormat = desc.graphicsFormat, clearBuffer = true,
                clearColor = camera.backgroundColor, filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            });
            using (var builder = graph.AddRasterRenderPass<Data>(_name, out var data))
            {
                data.Sources = Sources; data.MaxOrder = _maxOrder;
                float h = camera.orthographicSize, w = h * camera.aspect;
                data.View = new Rect(camera.transform.position.x - w, camera.transform.position.y - h, w * 2f, h * 2f);
                builder.SetRenderAttachment(texture, 0);
                builder.SetGlobalTextureAfterPass(texture, _textureId);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (Data d, RasterGraphContext ctx) =>
                {
                    foreach (var r in d.Sources)
                    {
                        if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.sortingOrder > d.MaxOrder) continue;
                        if (!(r is SpriteRenderer) && !(r is MeshRenderer)) continue;
                        var bounds = r.bounds;
                        if (bounds.max.x < d.View.xMin || bounds.min.x > d.View.xMax || bounds.max.y < d.View.yMin || bounds.min.y > d.View.yMax) continue;
                        var material = r.sharedMaterial;
                        if (material == null || material.shader.name == "MadTowers/Flood" || material.shader.name == "MadTowers/VoidZone") continue;
                        ctx.cmd.DrawRenderer(r, material, 0, 0);
                    }
                });
            }
        }
    }

    // Two independent tileable value-noise channels, generated once with a private RNG.
    // The gameplay random stream is never consumed by this resource.
    private static Texture2D BuildNoise()
    {
        const int size = 128, cells = 16;
        var rng = new System.Random(4127);
        var lattice = new Vector2[cells * cells];
        for (int i = 0; i < lattice.Length; i++) lattice[i] = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float px = (x + .5f) * cells / size, py = (y + .5f) * cells / size;
            int ix = (int)px, iy = (int)py;
            float tx = Mathf.SmoothStep(0, 1, px - ix), ty = Mathf.SmoothStep(0, 1, py - iy);
            Vector2 a = Vector2.Lerp(lattice[iy * cells + ix], lattice[iy * cells + (ix + 1) % cells], tx);
            Vector2 b = Vector2.Lerp(lattice[((iy + 1) % cells) * cells + ix], lattice[((iy + 1) % cells) * cells + (ix + 1) % cells], tx);
            Vector2 n = Vector2.Lerp(a, b, ty);
            pixels[y * size + x] = new Color(n.x, n.y, n.x * .65f + n.y * .35f, 1);
        }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
        { name = "Surface tileable noise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixels32(pixels); texture.Apply(false, true);
        return texture;
    }
}
