using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 3D 牌几何外壳与贴邻轮廓补线：直接使用相机颜色和深度，不创建描边 RT。
/// 保留类型名称以兼容现有 Renderer 资产的脚本引用。
/// </summary>
public class TileObjectIdOutlineFeature : ScriptableRendererFeature
{
    public const float DefaultOutlineWidth = 2.4f;
    public static Color DefaultOutlineColor => new Color(36f / 255f, 39f / 255f, 42f / 255f, 1f);

    [System.Serializable]
    public class Settings
    {
        public bool enabled = true;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        public LayerMask tileLayerMask = 1 << 10;
        public Color outlineColor = DefaultOutlineColor;
        [Range(0.5f, 4f)]
        [Tooltip("几何轮廓线宽（渲染目标像素）")]
        public float outlineWidth = DefaultOutlineWidth;
        [Range(0f, 1f)]
        [Tooltip("贴邻牌补线宽度 / 外壳线宽。默认 8/9；设为 0 仅保留原外壳。所有池对象实时共用。")]
        public float silhouetteWidthRatio = 8f / 9f;
    }

    public Settings settings = new Settings();
    private TileOutlineHullPass _hullPass;

    public override void Create() {
        _hullPass = new TileOutlineHullPass();
        ApplySettingsToPass();
    }

    /// <summary>运行时改描边颜色（不写资产）。</summary>
    public void SetOutlineColor(Color color) {
        settings.outlineColor = color;
        ApplySettingsToPass();
    }

    /// <summary>运行时改描边线宽（不写资产）。</summary>
    public void SetOutlineWidth(float widthPx) {
        settings.outlineWidth = Mathf.Clamp(widthPx, 0.5f, 4f);
        ApplySettingsToPass();
    }

    protected override void Dispose(bool disposing) {
        _hullPass = null;
        TileOutline.InvalidateCache();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        if (!settings.enabled || _hullPass == null) return;

        CameraType camType = renderingData.cameraData.cameraType;
        if (camType != CameraType.Game && camType != CameraType.SceneView) return;

        ApplySettingsToPass();
        renderer.EnqueuePass(_hullPass);
    }

    private void ApplySettingsToPass() {
        if (_hullPass == null) return;
        _hullPass.renderPassEvent = settings.renderPassEvent;
        _hullPass.Setup(settings);
    }

    /// <summary>Draw both outline geometries against the real scene depth; no full-screen textures.</summary>
    private sealed class TileOutlineHullPass : ScriptableRenderPass
    {
        private static readonly List<ShaderTagId> HullTags = new List<ShaderTagId> {
            new ShaderTagId("TileOutlineHull")
        };
        private static readonly int HullColorId = Shader.PropertyToID("_TileHullOutlineColor");
        private static readonly int HullWidthId = Shader.PropertyToID("_TileHullOutlineWidth");
        private static readonly int SilhouetteWidthId = Shader.PropertyToID("_TileSilhouetteOutlineWidth");
        private Settings _settings;

        public void Setup(Settings settings) {
            _settings = settings;
            requiresIntermediateTexture = false;
            // Fixed-function depth testing reads the active attachment. It does
            // not need a sampled camera-depth copy or a separate depth prepass.
            ConfigureInput(ScriptableRenderPassInput.None);
        }

        private class HullPassData
        {
            public RendererListHandle rendererList;
            public Color color;
            public float width;
            public float silhouetteWidth;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
            if (_settings == null) return;
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            if (!resources.activeColorTexture.IsValid() || !resources.activeDepthTexture.IsValid()) return;

            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(
                HullTags, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);
            drawing.perObjectData = PerObjectData.None;
            drawing.enableInstancing = true;
            FilteringSettings filtering = new FilteringSettings(RenderQueueRange.opaque, _settings.tileLayerMask);
            RendererListHandle rendererList = renderGraph.CreateRendererList(
                new RendererListParams(renderingData.cullResults, drawing, filtering));
            if (!rendererList.IsValid()) return;

            using (var builder = renderGraph.AddRasterRenderPass<HullPassData>("Tile Outline Geometry", out var passData)) {
                passData.rendererList = rendererList;
                passData.color = QualitySettings.activeColorSpace == ColorSpace.Linear
                    ? _settings.outlineColor.linear
                    : _settings.outlineColor;
                passData.width = Mathf.Clamp(_settings.outlineWidth, 0.5f, 4f);
                passData.silhouetteWidth = passData.width * Mathf.Clamp01(_settings.silhouetteWidthRatio);
                builder.UseRendererList(rendererList);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (HullPassData data, RasterGraphContext ctx) => {
                    ctx.cmd.SetGlobalColor(HullColorId, data.color);
                    ctx.cmd.SetGlobalFloat(HullWidthId, data.width);
                    ctx.cmd.SetGlobalFloat(SilhouetteWidthId, data.silhouetteWidth);
                    ctx.cmd.DrawRendererList(data.rendererList);
                });
            }
        }
    }
}
