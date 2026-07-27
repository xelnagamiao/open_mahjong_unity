using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
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
        [Tooltip("可选：覆盖牌附近 mask 的膨胀材质")]
        public Material overrideMaskMaterial;
    }

    public Settings settings = new Settings();

    private TileObjectIdPass _idPass;
    private TileOutlineCompositePass _overlayPass;
    private Material _edgeMaterial;
    private Material _maskMaterial;
    private bool _ownsEdgeMaterial;
    private bool _ownsMaskMaterial;

    private int _tileIdTexId;
    private int _tileMaskTexId;
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
        _tileMaskTexId = Shader.PropertyToID("_TileOutlineMask");
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
        if (_ownsMaskMaterial) {
            CoreUtils.Destroy(_maskMaterial);
            _maskMaterial = null;
            _ownsMaskMaterial = false;
        }
        TileOutline.InvalidateCache();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        if (!settings.enabled) return;

        CameraType camType = renderingData.cameraData.cameraType;
        if (camType != CameraType.Game && camType != CameraType.SceneView) return;

        EnsureMaterials();
        if (_edgeMaterial == null || _maskMaterial == null || _idPass == null || _overlayPass == null) return;

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
            _edgeMaterial, _maskMaterial, _tileIdTexId, _tileMaskTexId,
            _outlineColorId, _outlineWidthId, _outlineExpandId, _debugVisualizeId,
            settings.outlineColor, settings.outlineWidth, settings.outlineExpand, settings.debugVisualizeId);
    }

    private void EnsureMaterials() {
        if (settings.overrideEdgeMaterial != null) {
            _edgeMaterial = settings.overrideEdgeMaterial;
            _ownsEdgeMaterial = false;
        }
        else if (_edgeMaterial == null) {
            Material fromRes = Resources.Load<Material>("Materials/Tiles/TileOutlineEdge");
            if (fromRes != null) {
                _edgeMaterial = new Material(fromRes);
                _ownsEdgeMaterial = true;
            }
            else {
                Shader edgeShader = Shader.Find("Hidden/TileOutlineEdge");
                if (edgeShader == null) {
                    Debug.LogError("TileObjectIdOutlineFeature: missing Hidden/TileOutlineEdge");
                }
                else {
                    _edgeMaterial = CoreUtils.CreateEngineMaterial(edgeShader);
                    _ownsEdgeMaterial = true;
                }
            }
        }

        if (settings.overrideMaskMaterial != null) {
            _maskMaterial = settings.overrideMaskMaterial;
            _ownsMaskMaterial = false;
        }
        else if (_maskMaterial == null) {
            Material fromRes = Resources.Load<Material>("Materials/Tiles/TileOutlineMask");
            if (fromRes != null) {
                _maskMaterial = new Material(fromRes);
                _ownsMaskMaterial = true;
            }
            else {
                Shader maskShader = Shader.Find("Hidden/TileOutlineMask");
                if (maskShader == null) {
                    Debug.LogError("TileObjectIdOutlineFeature: missing Hidden/TileOutlineMask");
                }
                else {
                    _maskMaterial = CoreUtils.CreateEngineMaterial(maskShader);
                    _ownsMaskMaterial = true;
                }
            }
        }
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

            // 单通道、非 MSAA ID + 自有深度：避免主相机 MSAA resolve 平均脏 ID；
            // R8 足够保存 1..255 的牌 ID，并把 ID RT 带宽降到原 RGBA8 的四分之一。
            TextureDesc idDesc = activeColor.GetDescriptor(renderGraph);
            idDesc.name = "_TileIdRT";
            idDesc.colorFormat = GraphicsFormat.R8_UNorm;
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

            if (!TryCreateRendererList(frameData, renderGraph, out RendererListHandle rendererList)) return;

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

            RendererListParams listParams = new RendererListParams(
                renderingData.cullResults, drawSettings, filterSettings);
            rendererList = renderGraph.CreateRendererList(listParams);
            return rendererList.IsValid();
        }
    }

    /// <summary>
    /// 全分辨率限域描边：
    /// 先用两个可分离的 min/max pass 找出 ObjectID 边界候选区，再只在候选区执行精确 ObjectID/深度核。
    /// 最终通过硬件混合直接叠加到相机颜色，不再复制整屏场景颜色。
    /// </summary>
    private sealed class TileOutlineCompositePass : ScriptableRenderPass
    {
        private static readonly MaterialPropertyBlock MaskPropertyBlock = new MaterialPropertyBlock();
        private static readonly MaterialPropertyBlock EdgePropertyBlock = new MaterialPropertyBlock();
        private static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
        private static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
        private static readonly int MaskRadiusId = Shader.PropertyToID("_MaskRadius");

        private Material _edgeMaterial;
        private Material _maskMaterial;
        private int _tileIdTexId;
        private int _tileMaskTexId;
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
            Material maskMaterial,
            int tileIdTexId,
            int tileMaskTexId,
            int outlineColorId,
            int outlineWidthId,
            int outlineExpandId,
            int debugVisualizeId,
            Color outlineColor,
            float outlineWidth,
            float outlineExpand,
            bool debugVisualize) {
            _edgeMaterial = edgeMaterial;
            _maskMaterial = maskMaterial;
            _tileIdTexId = tileIdTexId;
            _tileMaskTexId = tileMaskTexId;
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

        private class MaskPassData
        {
            public Material material;
            public TextureHandle source;
            public float radius;
            public int shaderPass;
        }

        private class OverlayPassData
        {
            public Material material;
            public TextureHandle tileId;
            public TextureHandle tileMask;
            public int tileIdTexId;
            public int tileMaskTexId;
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
            if (_edgeMaterial == null || _maskMaterial == null) return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer) return;
            if (!resourceData.activeColorTexture.IsValid()) return;

            TileOutlineFrameData outlineFrame = frameData.Get<TileOutlineFrameData>();
            TextureHandle tileId = outlineFrame.tileIdTexture;
            if (!tileId.IsValid()) {
                Debug.LogError("TileObjectIdOutline: tileIdTexture invalid in CompositePass");
                return;
            }

            TextureHandle activeColor = resourceData.activeColorTexture;
            TextureDesc maskDesc = tileId.GetDescriptor(renderGraph);
            maskDesc.name = "_TileOutlineIdRangeHorizontal";
            maskDesc.colorFormat = GraphicsFormat.R8G8_UNorm;
            maskDesc.msaaSamples = MSAASamples.None;
            maskDesc.depthBufferBits = 0;
            maskDesc.clearBuffer = false;
            maskDesc.filterMode = FilterMode.Point;
            TextureHandle horizontalMask = renderGraph.CreateTexture(maskDesc);

            maskDesc.name = "_TileOutlineMask";
            maskDesc.colorFormat = GraphicsFormat.R8_UNorm;
            TextureHandle tileMask = renderGraph.CreateTexture(maskDesc);

            float maskRadius = Mathf.Clamp(Mathf.Ceil(_outlineExpand), 1f, 4f);

            AddMaskPass(
                renderGraph,
                "Tile Outline Mask Horizontal",
                _maskMaterial,
                tileId,
                horizontalMask,
                maskRadius,
                0);
            AddMaskPass(
                renderGraph,
                "Tile Outline Mask Vertical",
                _maskMaterial,
                horizontalMask,
                tileMask,
                maskRadius,
                1);

            // 硬件 alpha blend 直接读写 activeColor；shader 不再采样或复制场景颜色。
            using (var builder = renderGraph.AddRasterRenderPass<OverlayPassData>(
                       "Tile Outline Overlay", out var passData)) {
                passData.material = _edgeMaterial;
                passData.tileId = tileId;
                passData.tileMask = tileMask;
                passData.tileIdTexId = _tileIdTexId;
                passData.tileMaskTexId = _tileMaskTexId;
                passData.outlineColorId = _outlineColorId;
                passData.outlineWidthId = _outlineWidthId;
                passData.outlineExpandId = _outlineExpandId;
                passData.debugVisualizeId = _debugVisualizeId;
                passData.outlineColor = _outlineColor;
                passData.outlineWidth = _outlineWidth;
                passData.outlineExpand = _outlineExpand;
                passData.debugVisualize = _debugVisualize;

                builder.UseTexture(tileId, AccessFlags.Read);
                builder.UseTexture(tileMask, AccessFlags.Read);
                if (resourceData.cameraDepthTexture.IsValid()) {
                    builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                }
                // Blend 会隐式读取目标颜色，因此必须向 RenderGraph 声明 ReadWrite。
                builder.SetRenderAttachment(activeColor, 0, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (OverlayPassData data, RasterGraphContext ctx) => {
                    EdgePropertyBlock.Clear();
                    EdgePropertyBlock.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                    EdgePropertyBlock.SetTexture(data.tileIdTexId, data.tileId);
                    EdgePropertyBlock.SetTexture(data.tileMaskTexId, data.tileMask);
                    EdgePropertyBlock.SetColor(data.outlineColorId, data.outlineColor);
                    EdgePropertyBlock.SetFloat(data.outlineWidthId, data.outlineWidth);
                    EdgePropertyBlock.SetFloat(data.outlineExpandId, data.outlineExpand);
                    EdgePropertyBlock.SetFloat(data.debugVisualizeId, data.debugVisualize);

                    ctx.cmd.DrawProcedural(
                        Matrix4x4.identity,
                        data.material,
                        0,
                        MeshTopology.Triangles,
                        3,
                        1,
                        EdgePropertyBlock);
                });
            }
        }

        private static void AddMaskPass(
            RenderGraph renderGraph,
            string passName,
            Material material,
            TextureHandle source,
            TextureHandle destination,
            float radius,
            int shaderPass) {
            using (var builder = renderGraph.AddRasterRenderPass<MaskPassData>(passName, out var passData)) {
                passData.material = material;
                passData.source = source;
                passData.radius = radius;
                passData.shaderPass = shaderPass;

                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (MaskPassData data, RasterGraphContext ctx) => {
                    MaskPropertyBlock.Clear();
                    MaskPropertyBlock.SetTexture(BlitTextureId, data.source);
                    MaskPropertyBlock.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                    MaskPropertyBlock.SetFloat(MaskRadiusId, data.radius);

                    ctx.cmd.DrawProcedural(
                        Matrix4x4.identity,
                        data.material,
                        data.shaderPass,
                        MeshTopology.Triangles,
                        3,
                        1,
                        MaskPropertyBlock);
                });
            }
        }
    }
}
