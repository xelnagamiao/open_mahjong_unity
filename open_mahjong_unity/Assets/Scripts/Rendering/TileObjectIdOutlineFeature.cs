using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 3D 牌 ObjectID 全屏描边（URP RenderGraph）。
/// IdPass 写非 MSAA ID RT；CompositePass 外扩合成到相机颜色。
/// </summary>
public class TileObjectIdOutlineFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public bool enabled = true;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        public LayerMask tileLayerMask = 1 << 10;
        public Color outlineColor = Color.black;
        [Range(0.5f, 4f)]
        [Tooltip("描边线宽（屏幕像素）。小于外扩时画外环，等于/大于外扩时实心外扩")]
        public float outlineWidth = 2f;
        [Range(1f, 4f)]
        [Tooltip("外扩距离（屏幕像素）：轮廓向外延伸多远")]
        public float outlineExpand = 2f;
        [Tooltip("调试：牌面叠红确认 ObjectID")]
        public bool debugVisualizeId = false;
        public Material overrideEdgeMaterial;
    }

    public Settings settings = new Settings();

    private TileObjectIdPass _idPass;
    private TileOutlineCompositePass _overlayPass;
    private Material _edgeMaterial;
    private bool _ownsEdgeMaterial;

    private int _tileIdTexId;
    private int _outlineColorId;
    private int _outlineWidthId;
    private int _outlineExpandId;
    private int _debugVisualizeId;

    /// <summary>跨 ScriptableRenderPass 传递本帧 ID RT。</summary>
    private sealed class TileOutlineFrameData : ContextItem
    {
        public TextureHandle tileIdTexture;

        public override void Reset() {
            tileIdTexture = TextureHandle.nullHandle;
        }
    }

    public override void Create() {
        _tileIdTexId = Shader.PropertyToID("_TileIdTex");
        _outlineColorId = Shader.PropertyToID("_OutlineColor");
        _outlineWidthId = Shader.PropertyToID("_OutlineWidth");
        _outlineExpandId = Shader.PropertyToID("_OutlineExpand");
        _debugVisualizeId = Shader.PropertyToID("_DebugVisualizeId");

        EnsureMaterials();
        _idPass = new TileObjectIdPass();
        _overlayPass = new TileOutlineCompositePass();
        ApplySettingsToPasses();
    }

    /// <summary>运行时改描边颜色（不写资产）。</summary>
    public void SetOutlineColor(Color color) {
        settings.outlineColor = color;
        if (_edgeMaterial != null) {
            _edgeMaterial.SetColor(_outlineColorId, color);
        }
        ApplySettingsToPasses();
    }

    /// <summary>运行时改描边线宽（不写资产）。</summary>
    public void SetOutlineWidth(float widthPx) {
        settings.outlineWidth = Mathf.Clamp(widthPx, 0.5f, 4f);
        if (_edgeMaterial != null) {
            _edgeMaterial.SetFloat(_outlineWidthId, settings.outlineWidth);
        }
        ApplySettingsToPasses();
    }

    /// <summary>运行时改外扩距离（不写资产）。</summary>
    public void SetOutlineExpand(float expandPx) {
        settings.outlineExpand = Mathf.Clamp(expandPx, 1f, 4f);
        if (_edgeMaterial != null) {
            _edgeMaterial.SetFloat(_outlineExpandId, settings.outlineExpand);
        }
        ApplySettingsToPasses();
    }

    protected override void Dispose(bool disposing) {
        if (_ownsEdgeMaterial) {
            CoreUtils.Destroy(_edgeMaterial);
            _edgeMaterial = null;
            _ownsEdgeMaterial = false;
        }
        TileOutline.InvalidateCache();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        if (!settings.enabled) return;

        CameraType camType = renderingData.cameraData.cameraType;
        if (camType != CameraType.Game && camType != CameraType.SceneView) return;

        EnsureMaterials();
        if (_edgeMaterial == null || _idPass == null || _overlayPass == null) return;

        _edgeMaterial.SetColor(_outlineColorId, settings.outlineColor);
        _edgeMaterial.SetFloat(_outlineWidthId, settings.outlineWidth);
        _edgeMaterial.SetFloat(_outlineExpandId, settings.outlineExpand);
        _edgeMaterial.SetFloat(_debugVisualizeId, settings.debugVisualizeId ? 1f : 0f);

        ApplySettingsToPasses();
        renderer.EnqueuePass(_idPass);
        renderer.EnqueuePass(_overlayPass);
    }

    private void ApplySettingsToPasses() {
        if (_idPass == null || _overlayPass == null) return;

        RenderPassEvent idEvent = settings.renderPassEvent;
        RenderPassEvent overlayEvent = (RenderPassEvent)((int)idEvent + 1);

        _idPass.renderPassEvent = idEvent;
        _idPass.Setup(settings);

        _overlayPass.renderPassEvent = overlayEvent;
        _overlayPass.Setup(
            _edgeMaterial, _tileIdTexId, _outlineColorId, _outlineWidthId, _outlineExpandId, _debugVisualizeId,
            settings.outlineColor, settings.outlineWidth, settings.outlineExpand, settings.debugVisualizeId);
    }

    private void EnsureMaterials() {
        if (settings.overrideEdgeMaterial != null) {
            _edgeMaterial = settings.overrideEdgeMaterial;
            _ownsEdgeMaterial = false;
            return;
        }
        if (_edgeMaterial != null) return;

        Material fromRes = Resources.Load<Material>("Materials/Tiles/TileOutlineEdge");
        if (fromRes != null) {
            _edgeMaterial = new Material(fromRes);
            _ownsEdgeMaterial = true;
            return;
        }

        Shader edgeShader = Shader.Find("Hidden/TileOutlineEdge");
        if (edgeShader == null) {
            Debug.LogError("TileObjectIdOutlineFeature: missing Hidden/TileOutlineEdge");
            return;
        }
        _edgeMaterial = CoreUtils.CreateEngineMaterial(edgeShader);
        _ownsEdgeMaterial = true;
    }

    private sealed class TileObjectIdPass : ScriptableRenderPass
    {
        private static readonly List<ShaderTagId> TileIdTags = new List<ShaderTagId> {
            new ShaderTagId("TileId")
        };

        private Settings _settings;

        public void Setup(Settings settings) {
            _settings = settings;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        private class DrawIdPassData
        {
            public RendererListHandle rendererListHandle;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
            if (_settings == null) return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer) return;

            TextureHandle activeColor = resourceData.activeColorTexture;
            if (!activeColor.IsValid()) return;
            if (!TryCreateRendererList(frameData, renderGraph, out RendererListHandle rendererList)) return;

            // 非 MSAA ID + 自有深度：避免主相机 MSAA resolve 平均脏 ID；牌之间仍正确遮挡。
            TextureDesc idDesc = activeColor.GetDescriptor(renderGraph);
            idDesc.name = "_TileIdRT";
            idDesc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
            idDesc.msaaSamples = MSAASamples.None;
            idDesc.clearBuffer = true;
            idDesc.clearColor = Color.clear;
            idDesc.depthBufferBits = 0;
            idDesc.filterMode = FilterMode.Point;
            TextureHandle tileIdRT = renderGraph.CreateTexture(idDesc);

            TextureDesc idDepthDesc = idDesc;
            idDepthDesc.name = "_TileIdDepth";
            idDepthDesc.colorFormat = GraphicsFormat.None;
            idDepthDesc.depthBufferBits = DepthBits.Depth24;
            idDepthDesc.clearBuffer = true;
            TextureHandle tileIdDepth = renderGraph.CreateTexture(idDepthDesc);

            using (var builder = renderGraph.AddRasterRenderPass<DrawIdPassData>("Tile ObjectID", out var passData)) {
                passData.rendererListHandle = rendererList;
                builder.UseRendererList(passData.rendererListHandle);
                builder.SetRenderAttachment(tileIdRT, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(tileIdDepth, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (DrawIdPassData data, RasterGraphContext ctx) => {
                    ctx.cmd.DrawRendererList(data.rendererListHandle);
                });
            }

            // 交给 CompositePass
            TileOutlineFrameData frame = frameData.GetOrCreate<TileOutlineFrameData>();
            frame.tileIdTexture = tileIdRT;
        }

        private bool TryCreateRendererList(
            ContextContainer frameData,
            RenderGraph renderGraph,
            out RendererListHandle rendererList) {
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            FilteringSettings filterSettings = new FilteringSettings(RenderQueueRange.opaque, _settings.tileLayerMask);
            DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(
                TileIdTags, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);
            drawSettings.perObjectData = PerObjectData.None;

            RendererListParams listParams = new RendererListParams(renderingData.cullResults, drawSettings, filterSettings);
            rendererList = renderGraph.CreateRendererList(listParams);
            return rendererList.IsValid();
        }
    }

    /// <summary>FullScreen Raster 路径：Copy Color → DrawProcedural 写回 activeColor。</summary>
    private sealed class TileOutlineCompositePass : ScriptableRenderPass
    {
        private static readonly MaterialPropertyBlock SharedPropertyBlock = new MaterialPropertyBlock();
        private static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
        private static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");

        private Material _edgeMaterial;
        private int _tileIdTexId;
        private int _outlineColorId;
        private int _outlineWidthId;
        private int _outlineExpandId;
        private int _debugVisualizeId;
        private Color _outlineColor;
        private float _outlineWidth;
        private float _outlineExpand;
        private float _debugVisualize;

        public void Setup(
            Material edgeMaterial,
            int tileIdTexId,
            int outlineColorId,
            int outlineWidthId,
            int outlineExpandId,
            int debugVisualizeId,
            Color outlineColor,
            float outlineWidth,
            float outlineExpand,
            bool debugVisualize) {
            _edgeMaterial = edgeMaterial;
            _tileIdTexId = tileIdTexId;
            _outlineColorId = outlineColorId;
            _outlineWidthId = outlineWidthId;
            _outlineExpandId = outlineExpandId;
            _debugVisualizeId = debugVisualizeId;
            _outlineColor = outlineColor;
            _outlineWidth = outlineWidth;
            _outlineExpand = outlineExpand;
            _debugVisualize = debugVisualize ? 1f : 0f;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        private class OverlayPassData
        {
            public Material material;
            public TextureHandle sceneColor;
            public TextureHandle tileId;
            public int tileIdTexId;
            public int outlineColorId;
            public int outlineWidthId;
            public int outlineExpandId;
            public int debugVisualizeId;
            public Color outlineColor;
            public float outlineWidth;
            public float outlineExpand;
            public float debugVisualize;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
            if (_edgeMaterial == null) return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer) return;
            if (!resourceData.cameraColor.IsValid()) return;

            TileOutlineFrameData outlineFrame = frameData.Get<TileOutlineFrameData>();
            TextureHandle tileId = outlineFrame.tileIdTexture;
            if (!tileId.IsValid()) {
                Debug.LogError("TileObjectIdOutline: tileIdTexture invalid in CompositePass");
                return;
            }

            // FullScreen：Copy Color（保留 cameraColor 描述，含 MSAA）
            TextureHandle activeColor = resourceData.activeColorTexture;
            TextureDesc targetDesc = renderGraph.GetTextureDesc(resourceData.cameraColor);
            targetDesc.name = "_TileOutlineSceneCopy";
            targetDesc.clearBuffer = false;
            TextureHandle sceneCopy = renderGraph.CreateTexture(targetDesc);

            renderGraph.AddBlitPass(
                activeColor,
                sceneCopy,
                Vector2.one,
                Vector2.zero,
                passName: "Resolve Scene For Outline");

            // FullScreen：Raster 写回 activeColor（与 AddFullscreenRenderPassInputPass 相同）
            using (var builder = renderGraph.AddRasterRenderPass<OverlayPassData>(
                       "Tile Outline Overlay", out var passData)) {
                passData.material = _edgeMaterial;
                passData.sceneColor = sceneCopy;
                passData.tileId = tileId;
                passData.tileIdTexId = _tileIdTexId;
                passData.outlineColorId = _outlineColorId;
                passData.outlineWidthId = _outlineWidthId;
                passData.outlineExpandId = _outlineExpandId;
                passData.debugVisualizeId = _debugVisualizeId;
                passData.outlineColor = _outlineColor;
                passData.outlineWidth = _outlineWidth;
                passData.outlineExpand = _outlineExpand;
                passData.debugVisualize = _debugVisualize;

                builder.UseTexture(sceneCopy, AccessFlags.Read);
                builder.UseTexture(tileId, AccessFlags.Read);
                if (resourceData.cameraDepthTexture.IsValid()) {
                    builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                }
                builder.SetRenderAttachment(activeColor, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (OverlayPassData data, RasterGraphContext ctx) => {
                    SharedPropertyBlock.Clear();
                    SharedPropertyBlock.SetTexture(BlitTextureId, data.sceneColor);
                    SharedPropertyBlock.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                    SharedPropertyBlock.SetTexture(data.tileIdTexId, data.tileId);
                    SharedPropertyBlock.SetColor(data.outlineColorId, data.outlineColor);
                    SharedPropertyBlock.SetFloat(data.outlineWidthId, data.outlineWidth);
                    SharedPropertyBlock.SetFloat(data.outlineExpandId, data.outlineExpand);
                    SharedPropertyBlock.SetFloat(data.debugVisualizeId, data.debugVisualize);

                    ctx.cmd.DrawProcedural(
                        Matrix4x4.identity,
                        data.material,
                        0,
                        MeshTopology.Triangles,
                        3,
                        1,
                        SharedPropertyBlock);
                });
            }
        }
    }
}
