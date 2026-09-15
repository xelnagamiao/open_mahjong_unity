using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>桌布上的局部接触阴影。共用两三角形网格，不申请屏幕纹理或读取深度纹理。</summary>
public sealed class TileContactShadowFeature : ScriptableRendererFeature {
    [System.Serializable]
    public sealed class Settings {
        public bool enabled = true;
        [Range(0f, 1f), Tooltip("贴桌时的阴影浓度。所有池中卡牌实时共用。")]
        public float opacity = 0.36f;
        [Range(0.01f, 0.2f), Tooltip("软边宽度 / 牌宽。")]
        public float featherRatio = 0.085f;
        [Range(0.1f, 2f), Tooltip("抬高多少个牌宽后完全消失。")]
        public float fadeHeightRatio = 0.65f;
        [Range(0f, 0.3f), Tooltip("抬牌时向外扩散的最大距离 / 牌宽。")]
        public float liftSpreadRatio = 0.08f;
    }

    public Settings settings = new Settings();
    public Shader shader;
    public int LastSubmittedTileCount => _pass != null ? _pass.tileCount : 0;
    public int LastSubmittedBatchCount => _pass != null ? _pass.batchCount : 0;
    private ContactPass _pass;

    public override void Create() {
        _pass?.Dispose();
        _pass = new ContactPass(shader != null ? shader : Resources.Load<Shader>("Materials/Tiles/TileContactShadow"));
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        if (_pass == null) return;
        _pass.tileCount = _pass.batchCount = 0;
        CameraType type = renderingData.cameraData.cameraType;
        if (!settings.enabled || settings.opacity <= 0f || (type != CameraType.Game && type != CameraType.SceneView)) return;
        _pass.settings = settings;
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing) {
        _pass?.Dispose();
        _pass = null;
    }

    private sealed class ContactPass : ScriptableRenderPass {
        // A small batch also fits devices with limited instancing constant buffers.
        private const int Capacity = 128;
        private static readonly int ShapeId = Shader.PropertyToID("_ContactShape");
        private static readonly int OpacityId = Shader.PropertyToID("_ContactOpacity");
        private readonly Matrix4x4[] _matrices = new Matrix4x4[Capacity];
        private readonly Vector4[] _shapes = new Vector4[Capacity];
        private readonly Vector4[] _opacities = new Vector4[Capacity];
        private readonly Plane[] _planes = new Plane[6];
        private readonly MaterialPropertyBlock _properties = new MaterialPropertyBlock();
        private readonly Dictionary<int, SurfaceFrame> _surfaces = new Dictionary<int, SurfaceFrame>();
        private readonly Mesh _quad;
        private readonly Material _material;
        public Settings settings;
        public int tileCount, batchCount;

        private struct SurfaceFrame {
            public bool valid;
            public Vector3 point, normal;
            public float clearance;
        }

        private sealed class PassData {
            public ContactPass pass;
            public Camera camera;
            public float opacity, feather, fadeHeight, liftSpread;
        }

        public ContactPass(Shader shader) {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            requiresIntermediateTexture = false;
            ConfigureInput(ScriptableRenderPassInput.None);
            if (shader == null) return;
            _material = CoreUtils.CreateEngineMaterial(shader);
            _material.enableInstancing = true;
            _quad = new Mesh { name = "Tile Contact Shadow Quad", hideFlags = HideFlags.HideAndDontSave };
            _quad.vertices = new[] { new Vector3(-1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 0, -1) };
            _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _quad.UploadMeshData(true);
        }

        public void Dispose() {
            CoreUtils.Destroy(_material);
            CoreUtils.Destroy(_quad);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
            if (_material == null || settings == null) return;
            var resources = frameData.Get<UniversalResourceData>();
            if (!resources.activeColorTexture.IsValid() || !resources.activeDepthTexture.IsValid()) return;
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Tile Contact Shadows", out var data)) {
                data.pass = this;
                data.camera = frameData.Get<UniversalCameraData>().camera;
                data.opacity = Mathf.Clamp01(settings.opacity);
                data.feather = Mathf.Clamp(settings.featherRatio, 0.01f, 0.2f);
                data.fadeHeight = Mathf.Clamp(settings.fadeHeightRatio, 0.1f, 2f);
                data.liftSpread = Mathf.Clamp(settings.liftSpreadRatio, 0f, 0.3f);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) => data.pass.Draw(data, context));
            }
        }

        private void Draw(PassData data, RasterGraphContext context) {
            tileCount = batchCount = 0;
            _surfaces.Clear();
            GeometryUtility.CalculateFrustumPlanes(data.camera, _planes);
            int count = 0;
            foreach (TileContactShadow tile in TileContactShadow.ActiveTiles) {
                if (tile == null || !tile.isActiveAndEnabled || tile.Body == null || tile.Body.sharedMesh == null) continue;
                Renderer body = tile.BodyRenderer;
                if (body == null || !body.enabled || body.forceRenderingOff || (data.camera.cullingMask & (1 << body.gameObject.layer)) == 0) continue;
                var scene = tile.gameObject.scene;
#if UNITY_EDITOR
                // Separate model previews must never borrow live-table tiles or surfaces.
                if (UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(scene)
                    || UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(data.camera.scene)) {
                    if (scene != data.camera.scene) continue;
                }
#endif
                if (!_surfaces.TryGetValue(scene.handle, out SurfaceFrame surface)) {
                    surface.valid = TileContactShadow.TryGetSurface(scene, out surface.point, out surface.normal, out surface.clearance);
                    _surfaces.Add(scene.handle, surface);
                }
                if (!surface.valid || !TileContactShadowGeometry.TryProject(tile.Body.sharedMesh.bounds,
                    tile.Body.transform.localToWorldMatrix, surface.point, surface.normal, out var projection)) continue;
                float height = Mathf.Max(0f, projection.Height - Mathf.Max(0f, surface.clearance));
                float lift = Mathf.Clamp01(height / (projection.Width * data.fadeHeight));
                if (lift >= 1f || projection.Height < -projection.Width * 0.1f) continue;
                float feather = projection.Width * data.feather * (1f + lift);
                Vector2 halfSize = projection.HalfSize + Vector2.one * (projection.Width * data.liftSpread * lift);
                Vector2 extent = halfSize + Vector2.one * feather;
                Vector3 center = projection.Center + projection.Normal * Mathf.Min(0.005f, Mathf.Max(0.001f, surface.clearance * 0.25f));
                Vector3 boundsExtent = Abs(projection.Tangent) * extent.x + Abs(projection.Bitangent) * extent.y + Abs(projection.Normal) * 0.01f;
                if (!GeometryUtility.TestPlanesAABB(_planes, new Bounds(center, boundsExtent * 2f))) continue;
                _matrices[count] = Matrix4x4.TRS(center, Quaternion.LookRotation(projection.Bitangent, projection.Normal), new Vector3(extent.x, 1f, extent.y));
                float radius = Mathf.Min(projection.Width * 0.075f, Mathf.Min(halfSize.x, halfSize.y));
                _shapes[count] = new Vector4(halfSize.x, halfSize.y, radius, feather);
                _opacities[count] = new Vector4(data.opacity * (1f - Mathf.SmoothStep(0f, 1f, lift)), 0f, 0f, 0f);
                count++;
                tileCount++;
                if (count == Capacity) { Submit(context, count); count = 0; }
            }
            if (count > 0) Submit(context, count);
        }

        private void Submit(RasterGraphContext context, int count) {
            if (SystemInfo.supportsInstancing) {
                _properties.SetVectorArray(ShapeId, _shapes);
                _properties.SetVectorArray(OpacityId, _opacities);
                context.cmd.DrawMeshInstanced(_quad, 0, _material, 0, _matrices, count, _properties);
                batchCount++;
            } else {
                for (int i = 0; i < count; i++) {
                    _properties.Clear();
                    _properties.SetVector(ShapeId, _shapes[i]);
                    _properties.SetVector(OpacityId, _opacities[i]);
                    context.cmd.DrawMesh(_quad, _matrices[i], _material, 0, 0, _properties);
                    batchCount++;
                }
            }
        }

        private static Vector3 Abs(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }
}
