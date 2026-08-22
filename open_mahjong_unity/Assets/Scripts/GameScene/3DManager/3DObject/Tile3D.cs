using UnityEngine;

/// <summary>
/// 3D麻将牌组件
/// 负责管理3D卡牌的纹理和材质属性，以及方案 B ObjectID 描边编号。
/// </summary>
public class Tile3D : MonoBehaviour
{
    private static readonly int FrontTilingOffsetId = Shader.PropertyToID("_FrontTilingOffset");
    private static readonly int BackRotationId = Shader.PropertyToID("_BackRotation");
    private static readonly int FrontColorId = Shader.PropertyToID("_FrontColor");
    private static readonly int BackColorId = Shader.PropertyToID("_BackColor");
    private static readonly int BackTexBlendId = Shader.PropertyToID("_BackTexBlend");
    private static readonly int BackTexExtendEdgeId = Shader.PropertyToID("_BackTexExtendEdge");
    private static readonly int SideColorId = Shader.PropertyToID("_SideColor");
    private static readonly int BackEdgeColorId = Shader.PropertyToID("_BackEdgeColor");
    private static readonly int FrontEdgeColorId = Shader.PropertyToID("_FrontEdgeColor");
    private static readonly int TileInstanceParamsId = Shader.PropertyToID("_TileInstanceParams");
    private static readonly int FrontTexId = Shader.PropertyToID("_FrontTex");
    private static readonly int BackTexId = Shader.PropertyToID("_BackTex");
    private static readonly int FrontBgTexId = Shader.PropertyToID("_FrontBgTex");
    private static readonly int FrontBgBlendId = Shader.PropertyToID("_FrontBgBlend");

    private Renderer cardRenderer;
    private Material sharedTileMaterial;
    private Material originalAtlasMaterial;
    private int tileMaterialIndex = -1;
    private int currentTileId = -1;
    private int currentPoolTileId = -1;
    private MaterialPropertyBlock propBlock;
    private int outlineId;
    private Vector4 frontTilingOffset = new Vector4(1f, 1f, 0f, 0f);
    private float backRotation;
    private Color baseFrontColor = Color.white;
    private Color baseBackColor = Color.white;
    private Color baseSideColor = Color.white;
    private Color baseBackEdgeColor = Color.white;
    private Color baseFrontEdgeColor = Color.white;
    private Color instanceFrontColor = Color.white;
    private Color instanceBackColor = Color.white;
    private Color instanceSideColor = Color.white;
    private Color instanceBackEdgeColor = Color.white;
    private Color instanceFrontEdgeColor = Color.white;
    private float baseGrayScale;
    private float instanceGrayScale;
    private bool materialDefaultsCached;

    /// <summary>立直横置标记：用于河中后续牌偏移计算与重连/牌谱重建。
    /// 仅 SetType="Discard" 路径会写入；归还对象池时由 MahjongObjectPool 重置。</summary>
    public bool isRiichiHorizontal;

    public bool IsConcealedFaceDown { get; private set; }

    /// <summary>牌谱展开明牌：固定在独立摸牌区（对齐 2D TileCard.isDrawSlotPinned）。</summary>
    public bool isRecordDrawSlotPinned;

    /// <summary>悬停时可临时翻面：已知牌 id（≥10）且当前为暗面展示（mask 方向位 2）。</summary>
    public bool CanPeekOnHover => currentTileId >= 10 && IsConcealedFaceDown;

    private Transform faceMeshTransform;
    private Quaternion faceUpLocalRotation;
    private Quaternion faceDownLocalRotation;
    private bool hasFaceRotationBaseline;
    private bool isPeekFaceUp;

    private Transform GetFaceMeshTransform() {
        InitializeComponents();
        if (faceMeshTransform != null) return faceMeshTransform;
        faceMeshTransform = cardRenderer != null ? cardRenderer.transform : transform;
        return faceMeshTransform;
    }

    private void EnsureFaceRotationBaseline() {
        if (hasFaceRotationBaseline) return;
        Transform mesh = GetFaceMeshTransform();
        faceUpLocalRotation = mesh.localRotation;
        faceDownLocalRotation = faceUpLocalRotation * Quaternion.Euler(0f, 180f, 0f);
        hasFaceRotationBaseline = true;
    }

    public void SetConcealedFaceDown(bool concealed) {
        IsConcealedFaceDown = concealed;
        isPeekFaceUp = false;
        Transform mesh = GetFaceMeshTransform();
        EnsureFaceRotationBaseline();
        mesh.localRotation = concealed ? faceDownLocalRotation : faceUpLocalRotation;
        // 暗面/暗杠展示牌背：统一转 180° 使背图正对该玩家；恢复正常面则不旋转。
        if (concealed) {
            SetBackOrientationUpright();
        } else {
            ResetBackOrientation();
        }
        RefreshPeekCollider();
    }

    /// <summary>
    /// 副露 mask 方向位：0 竖 1 横 2 暗面 3 加杠。暗面一律翻面展示；能否 hover peek 见 CanPeekOnHover。
    /// </summary>
    public void ApplyCombinationPeekState(int tileId, int directionFlag) {
        if (directionFlag == 2) {
            SetConcealedFaceDown(true);
        }
    }

    public void SetPeekFaceUp(bool peek) {
        if (!IsConcealedFaceDown || !hasFaceRotationBaseline) return;
        if (isPeekFaceUp == peek) return;
        isPeekFaceUp = peek;
        Transform mesh = GetFaceMeshTransform();
        mesh.localRotation = peek ? faceUpLocalRotation : faceDownLocalRotation;
    }

    public void ResetConcealedState() {
        IsConcealedFaceDown = false;
        isRecordDrawSlotPinned = false;
        isPeekFaceUp = false;
        if (hasFaceRotationBaseline) {
            Transform mesh = GetFaceMeshTransform();
            mesh.localRotation = faceUpLocalRotation;
        }
        hasFaceRotationBaseline = false;
        faceMeshTransform = null;
        ResetBackOrientation();
        RefreshPeekCollider();
    }

    /// <summary>按 CanPeekOnHover 开关碰撞盒，供暗面副露 hover peek 射线检测。</summary>
    public void RefreshPeekCollider() {
        BoxCollider box = GetPeekBoxCollider();
        if (box == null) return;
        box.enabled = CanPeekOnHover;
    }

    private BoxCollider GetPeekBoxCollider() {
        InitializeComponents();
        if (cardRenderer == null) return null;
        return cardRenderer.GetComponent<BoxCollider>();
    }

    private void Awake() {
        InitializeComponents();
    }

    private void OnEnable() {
        InitializeComponents();
        AcquireOutlineId();
        ApplyPropertyBlock();
    }

    private void OnDisable() {
        ReleaseOutlineId();
    }

    /// <summary>
    /// 初始化组件（可在Awake或需要时手动调用，用于处理SetActive(false)的对象）
    /// </summary>
    private void InitializeComponents() {
        // 场景销毁时悬停/恢复路径可能仍持有已销毁实例；Unity 的 == 可识别。
        if (this == null) return;
        if (sharedTileMaterial != null && cardRenderer != null
            && tileMaterialIndex >= 0 && propBlock != null) {
            return;
        }

        const int tileLayer = 10;
        // Layer 10：ObjectID 描边过滤 + peek 物理
        SetLayerRecursively(gameObject, tileLayer);

        // 对象池牌在 Instantiate 后立刻 SetActive(false)；默认 GetComponentInChildren
        // 找不到 inactive 子物体上的 MeshRenderer，后续 Apply*Visual 会静默跳过。
        cardRenderer = GetComponent<Renderer>() ?? GetComponentInChildren<Renderer>(true);
        if (cardRenderer == null) return;

        Material[] shared = cardRenderer.sharedMaterials;
        for (int i = 0; i < shared.Length; i++) {
            Material sharedMat = shared[i];
            if (sharedMat != null && sharedMat.shader != null
                && sharedMat.shader.name == "Custom/ThreeDTiles") {
                tileMaterialIndex = i;
                sharedTileMaterial = sharedMat;
                if (originalAtlasMaterial == null
                    && !sharedMat.name.StartsWith("Hongque_")
                    && !sharedMat.name.StartsWith("CustomFace_")) {
                    originalAtlasMaterial = sharedMat;
                }
                break;
            }
        }

        if (sharedTileMaterial != null && !materialDefaultsCached) {
            baseFrontColor = sharedTileMaterial.GetColor(FrontColorId);
            baseBackColor = sharedTileMaterial.GetColor(BackColorId);
            baseSideColor = sharedTileMaterial.GetColor(SideColorId);
            baseBackEdgeColor = sharedTileMaterial.GetColor(BackEdgeColorId);
            baseFrontEdgeColor = sharedTileMaterial.GetColor(FrontEdgeColorId);
            baseGrayScale = sharedTileMaterial.GetFloat("_GrayScale");
            instanceFrontColor = baseFrontColor;
            instanceBackColor = baseBackColor;
            instanceSideColor = baseSideColor;
            instanceBackEdgeColor = baseBackEdgeColor;
            instanceFrontEdgeColor = baseFrontEdgeColor;
            instanceGrayScale = baseGrayScale;
            materialDefaultsCached = true;
        }

        if (propBlock == null) {
            propBlock = new MaterialPropertyBlock();
        }
    }

    private static void SetLayerRecursively(GameObject obj, int layer) {
        obj.layer = layer;
        Transform root = obj.transform;
        for (int i = 0; i < root.childCount; i++) {
            SetLayerRecursively(root.GetChild(i).gameObject, layer);
        }
    }

    private void AcquireOutlineId() {
        if (outlineId > 0) return;
        outlineId = TileOutlineIdAllocator.Acquire();
    }

    private void ReleaseOutlineId() {
        if (outlineId <= 0) return;
        TileOutlineIdAllocator.Release(outlineId);
        outlineId = 0;
        ApplyPropertyBlock();
    }

    private void ApplyPropertyBlock() {
        if (cardRenderer == null || tileMaterialIndex < 0) return;
        if (propBlock == null) {
            propBlock = new MaterialPropertyBlock();
        }
        propBlock.Clear();
        // MPB 中只放 Shader 声明为 instanced 的属性，否则 Unity 会拆散 GPU Instancing。
        propBlock.SetVector(FrontTilingOffsetId, frontTilingOffset);
        propBlock.SetFloat(BackRotationId, backRotation);
        propBlock.SetColor(FrontColorId, instanceFrontColor);
        propBlock.SetColor(BackColorId, instanceBackColor);
        propBlock.SetColor(SideColorId, instanceSideColor);
        propBlock.SetColor(BackEdgeColorId, instanceBackEdgeColor);
        propBlock.SetColor(FrontEdgeColorId, instanceFrontEdgeColor);
        propBlock.SetVector(
            TileInstanceParamsId,
            new Vector4(instanceGrayScale, outlineId, 0f, 0f));
        cardRenderer.SetPropertyBlock(propBlock, tileMaterialIndex);
    }

    /// <summary>
    /// 设置牌面纹理（使用缓存的Sprite）
    /// 额外做 90° 逆时针旋转补偿（向左旋转 90°）
    /// verticalStretch: 牌面上下拉伸倍数，1.0=不拉伸，1.1=拉伸 1.1 倍（通过 UV 实现，不改变 3D 几何）
    /// </summary>
    public void SetCardSprite(int tileId, Sprite sprite, float verticalStretch = 1f) {
        InitializeComponents();
        if (sharedTileMaterial == null || cardRenderer == null || sprite == null) return;

        currentTileId = tileId;
        currentPoolTileId = tileId;
        Texture2D atlasTexture = sprite.texture;
        Texture currentAtlas = sharedTileMaterial.GetTexture(FrontTexId);
        if (currentAtlas == null) {
            // 所有牌共享同一图集纹理；只在首次初始化共享材质时写入。
            sharedTileMaterial.SetTexture(FrontTexId, atlasTexture);
        }
        else if (currentAtlas != atlasTexture) {
            Debug.LogError(
                $"Tile3D: GPU Instancing 要求牌面来自同一张图集；"
                + $"当前材质={currentAtlas.name}，新牌面={atlasTexture.name}");
            return;
        }

        Rect uvRect = sprite.textureRect;

        float tilingX = uvRect.width / atlasTexture.width;
        float tilingY = uvRect.height / atlasTexture.height;
        float offsetX = uvRect.x / atlasTexture.width;
        float offsetY = uvRect.y / atlasTexture.height;

        if (verticalStretch > 1f) {
            float origTilingY = tilingY;
            tilingY /= verticalStretch;
            offsetY += origTilingY * (1f - 1f / verticalStretch) * 0.5f;
        }

        float newTilingX = tilingY;
        float newTilingY = tilingX;
        float newOffsetX = 1f - (offsetY + tilingY);
        float newOffsetY = offsetX;

        frontTilingOffset = new Vector4(newTilingX, newTilingY, newOffsetX, newOffsetY);
        if (isActiveAndEnabled && outlineId <= 0) {
            AcquireOutlineId();
        }
        ApplyPropertyBlock();
    }

    /// <summary>
    /// 虹雀资源并非现有麻将 SpriteAtlas 的一部分，因此为每个唯一牌面复用一份独立材质。
    /// 虹雀牌在牌库中各一张，这条低频路径不会影响普通麻将的 GPU Instancing。
    /// </summary>
    public void SetStandaloneCardTexture(int tileId, Texture2D texture, Material faceMaterial, bool contain = false) {
        InitializeComponents();
        if (cardRenderer == null || tileMaterialIndex < 0 || texture == null || faceMaterial == null) return;
        Material[] materials = cardRenderer.sharedMaterials;
        materials[tileMaterialIndex] = faceMaterial;
        cardRenderer.sharedMaterials = materials;
        sharedTileMaterial = faceMaterial;
        currentTileId = tileId;
        currentPoolTileId = tileId;
        frontTilingOffset = contain
            ? ComputeCoverTiling(texture)
            : new Vector4(1f / 1.1f, 1f, (1f - 1f / 1.1f) * 0.5f, 0f);
        if (isActiveAndEnabled && outlineId <= 0) AcquireOutlineId();
        ApplyPropertyBlock();
    }

    /// <summary>自定义标准牌面：已是 220:366 则铺满（缩小和留白在贴图里）；否则按原图比例居中，不拉伸。</summary>
    public void SetStandaloneCardTextureContain(int tileId, Texture2D texture, Material faceMaterial) {
        SetStandaloneCardTexture(tileId, texture, faceMaterial, true);
    }

    public void RestoreAtlasMaterial() {
        InitializeComponents();
        if (originalAtlasMaterial == null || cardRenderer == null || tileMaterialIndex < 0) {
            return;
        }
        if (sharedTileMaterial == originalAtlasMaterial) {
            return;
        }
        Material[] materials = cardRenderer.sharedMaterials;
        materials[tileMaterialIndex] = originalAtlasMaterial;
        cardRenderer.sharedMaterials = materials;
        sharedTileMaterial = originalAtlasMaterial;
    }

    /// <summary>
    /// 按 220:366 覆盖裁切：源图更宽则切左右，更高则切上下。
    /// 已是该比例时 UV 铺满。
    /// </summary>
    private static Vector4 ComputeCoverTiling(Texture2D texture) {
        const float faceAspect = 220f / 366f;
        if (texture == null || texture.height <= 0) {
            return new Vector4(1f, 1f, 0f, 0f);
        }
        float texAspect = (float)texture.width / texture.height;
        if (Mathf.Abs(texAspect - faceAspect) <= 0.01f) {
            return new Vector4(1f, 1f, 0f, 0f);
        }
        if (texAspect > faceAspect) {
            float tilingX = faceAspect / texAspect;
            return new Vector4(tilingX, 1f, (1f - tilingX) * 0.5f, 0f);
        }
        float tilingY = texAspect / faceAspect;
        return new Vector4(1f, tilingY, 0f, (1f - tilingY) * 0.5f);
    }

    /// <summary>应用逐牌颜色/灰度，只更新实例数据，不创建或修改材质实例。</summary>
    public void SetInstanceVisualState(
        Color frontColor,
        Color backColor,
        Color sideColor,
        float grayScale) {
        InitializeComponents();
        instanceFrontColor = frontColor;
        instanceBackColor = backColor;
        instanceSideColor = sideColor;
        instanceGrayScale = grayScale;
        ApplyPropertyBlock();
    }

    /// <summary>
    /// 应用牌背颜色与牌背贴图：颜色走实例 MPB，贴图写共享材质（所有牌共享同一张背图）。
    /// </summary>
    public void ApplyBackVisual(Color backColor, Texture2D backTexture) {
        InitializeComponents();
        if (cardRenderer == null || tileMaterialIndex < 0) return;
        instanceBackColor = backColor;
        baseBackColor = backColor;
        if (sharedTileMaterial != null) {
            sharedTileMaterial.SetTexture(BackTexId, backTexture);
            sharedTileMaterial.SetFloat(BackTexBlendId, backTexture != null ? 1f : 0f);
            sharedTileMaterial.SetFloat(BackTexExtendEdgeId, CardBackManager.BackTexExtendEdge && backTexture != null ? 1f : 0f);
        }
        ApplyPropertyBlock();
    }

    /// <summary>
    /// 应用 3D 牌面背景：写共享材质 _FrontBgTex 与 _FrontBgBlend；所有牌共享同一张底图。
    /// </summary>
    public void ApplyFrontBgVisual(Texture2D bgTexture) {
        InitializeComponents();
        if (cardRenderer == null || tileMaterialIndex < 0) return;
        if (sharedTileMaterial != null) {
            sharedTileMaterial.SetTexture(FrontBgTexId, bgTexture);
            sharedTileMaterial.SetFloat(FrontBgBlendId, bgTexture != null ? 1f : 0f);
        }
    }

    /// <summary>正面侧边颜色：跟随 3D 牌面背景开关的颜色或独立设置。</summary>
    public void ApplyFrontEdgeVisual(Color color) {
        InitializeComponents();
        if (cardRenderer == null || tileMaterialIndex < 0) return;
        instanceFrontEdgeColor = color;
        baseFrontEdgeColor = color;
        ApplyPropertyBlock();
    }

    /// <summary>
    /// 统一牌背朝向：暗面/暗杠与和牌倒牌立牌的“牌背可见”场景统一旋转 180°（u/v 同时取反），
    /// 使背图正对该牌所属玩家的正面方向（图案顶朝桌心，玩家可正常阅读），并保持与正常立牌背面一致。
    /// 只影响牌背 UV 采样，不影响牌面；悬停 peek 只翻转网格（SetPeekFaceUp），不会重算此值，
    /// 移开鼠标恢复暗面后牌背仍保持正向。
    /// </summary>
    public void SetBackOrientationUpright() {
        SetBackRotation(180f);
    }

    /// <summary>设置牌背贴图的逐牌旋转角（度，0/90/180/270）。</summary>
    public void SetBackRotation(float rotationDegrees) {
        InitializeComponents();
        backRotation = rotationDegrees;
        ApplyPropertyBlock();
    }

    /// <summary>恢复牌背贴图默认朝向（不旋转）。</summary>
    public void ResetBackOrientation() {
        if (backRotation == 0f) {
            return;
        }
        SetBackRotation(0f);
    }

    /// <summary>当前实例实际使用的 ThreeDTiles 材质（图集共享或虹雀/自定义克隆）。</summary>
    public Material SharedTileMaterial
    {
        get
        {
            InitializeComponents();
            return sharedTileMaterial;
        }
    }

    /// <summary>
    /// 应用正面侧边颜色：走实例 MPB，不影响共享材质上的 _SideTex。
    /// </summary>
    public void ApplySideVisual(Color sideColor) {
        InitializeComponents();
        if (cardRenderer == null || tileMaterialIndex < 0) return;
        instanceSideColor = sideColor;
        baseSideColor = sideColor;
        ApplyPropertyBlock();
    }

    /// <summary>
    /// 应用背面侧边颜色：走实例 MPB，不影响共享材质上的 _BackColor。
    /// </summary>
    public void ApplyBackEdgeVisual(Color backEdgeColor) {
        InitializeComponents();
        if (cardRenderer == null || tileMaterialIndex < 0) return;
        instanceBackEdgeColor = backEdgeColor;
        baseBackEdgeColor = backEdgeColor;
        ApplyPropertyBlock();
    }

    /// <summary>对象池复用前恢复共享材质的默认视觉状态。</summary>
    public void ResetInstanceVisualState() {
        InitializeComponents();
        instanceFrontColor = baseFrontColor;
        instanceBackColor = baseBackColor;
        instanceSideColor = baseSideColor;
        instanceBackEdgeColor = baseBackEdgeColor;
        instanceFrontEdgeColor = baseFrontEdgeColor;
        instanceGrayScale = baseGrayScale;
        backRotation = 0f;
        ApplyPropertyBlock();
    }

    public Color BaseFrontColor {
        get {
            InitializeComponents();
            return baseFrontColor;
        }
    }

    public Color BaseBackColor {
        get {
            InitializeComponents();
            return baseBackColor;
        }
    }

    public Color BaseSideColor {
        get {
            InitializeComponents();
            return baseSideColor;
        }
    }

    public float BaseGrayScale {
        get {
            InitializeComponents();
            return baseGrayScale;
        }
    }

    /// <summary>
    /// 获取当前牌的ID
    /// </summary>
    public int GetTileId() {
        return currentTileId;
    }

    /// <summary>
    /// 设置逻辑牌 id 与对象池 id；自家手牌可显示空白牌面但保留真实牌值。
    /// </summary>
    public void SetTileIds(int tileId, int poolTileId) {
        currentTileId = tileId;
        currentPoolTileId = poolTileId;
    }

    /// <summary>
    /// 获取当前对象应归还的对象池 id。
    /// </summary>
    public int GetPoolTileId() {
        return currentPoolTileId;
    }

}
