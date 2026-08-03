using UnityEngine;

/// <summary>
/// 3D麻将牌组件
/// 负责管理3D卡牌的纹理和材质属性，以及方案 B ObjectID 描边编号。
/// </summary>
public class Tile3D : MonoBehaviour
{
    private static readonly int FrontTilingOffsetId = Shader.PropertyToID("_FrontTilingOffset");
    private static readonly int FrontColorId = Shader.PropertyToID("_FrontColor");
    private static readonly int BackColorId = Shader.PropertyToID("_BackColor");
    private static readonly int SideColorId = Shader.PropertyToID("_SideColor");
    private static readonly int TileInstanceParamsId = Shader.PropertyToID("_TileInstanceParams");
    private static readonly int FrontTexId = Shader.PropertyToID("_FrontTex");

    private Renderer cardRenderer;
    private Material sharedTileMaterial;
    private int tileMaterialIndex = -1;
    private int currentTileId = -1;
    private int currentPoolTileId = -1;
    private MaterialPropertyBlock propBlock;
    private int outlineId;
    private Vector4 frontTilingOffset = new Vector4(1f, 1f, 0f, 0f);
    private Color baseFrontColor = Color.white;
    private Color baseBackColor = Color.white;
    private Color baseSideColor = Color.white;
    private Color instanceFrontColor = Color.white;
    private Color instanceBackColor = Color.white;
    private Color instanceSideColor = Color.white;
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
        if (sharedTileMaterial != null && cardRenderer != null
            && tileMaterialIndex >= 0 && propBlock != null) {
            return;
        }

        const int tileLayer = 10;
        // Layer 10：ObjectID 描边过滤 + peek 物理
        SetLayerRecursively(gameObject, tileLayer);

        cardRenderer = GetComponent<Renderer>() ?? GetComponentInChildren<Renderer>();
        if (cardRenderer == null) return;

        Material[] shared = cardRenderer.sharedMaterials;
        for (int i = 0; i < shared.Length; i++) {
            Material sharedMat = shared[i];
            if (sharedMat != null && sharedMat.shader != null
                && sharedMat.shader.name == "Custom/ThreeDTiles") {
                tileMaterialIndex = i;
                sharedTileMaterial = sharedMat;
                break;
            }
        }

        if (sharedTileMaterial != null && !materialDefaultsCached) {
            baseFrontColor = sharedTileMaterial.GetColor(FrontColorId);
            baseBackColor = sharedTileMaterial.GetColor(BackColorId);
            baseSideColor = sharedTileMaterial.GetColor(SideColorId);
            baseGrayScale = sharedTileMaterial.GetFloat("_GrayScale");
            instanceFrontColor = baseFrontColor;
            instanceBackColor = baseBackColor;
            instanceSideColor = baseSideColor;
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
        propBlock.SetColor(FrontColorId, instanceFrontColor);
        propBlock.SetColor(BackColorId, instanceBackColor);
        propBlock.SetColor(SideColorId, instanceSideColor);
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
    public void SetStandaloneCardTexture(int tileId, Texture2D texture, Material faceMaterial) {
        InitializeComponents();
        if (cardRenderer == null || tileMaterialIndex < 0 || texture == null || faceMaterial == null) return;
        Material[] materials = cardRenderer.sharedMaterials;
        materials[tileMaterialIndex] = faceMaterial;
        cardRenderer.sharedMaterials = materials;
        sharedTileMaterial = faceMaterial;
        currentTileId = tileId;
        currentPoolTileId = tileId;
        const float stretch = 1.1f;
        float tiling = 1f / stretch;
        frontTilingOffset = new Vector4(tiling, 1f, (1f - tiling) * 0.5f, 0f);
        if (isActiveAndEnabled && outlineId <= 0) AcquireOutlineId();
        ApplyPropertyBlock();
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

    /// <summary>对象池复用前恢复共享材质的默认视觉状态。</summary>
    public void ResetInstanceVisualState() {
        InitializeComponents();
        instanceFrontColor = baseFrontColor;
        instanceBackColor = baseBackColor;
        instanceSideColor = baseSideColor;
        instanceGrayScale = baseGrayScale;
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
