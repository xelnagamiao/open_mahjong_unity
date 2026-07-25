using UnityEngine;

/// <summary>
/// 3D麻将牌组件
/// 负责管理3D卡牌的纹理和材质属性，以及方案 B ObjectID 描边编号。
/// </summary>
public class Tile3D : MonoBehaviour
{
    private static readonly int FrontTilingOffsetId = Shader.PropertyToID("_FrontTilingOffset");
    private static readonly int OutlineIdProp = Shader.PropertyToID("_OutlineId");

    private Renderer cardRenderer;
    private Material targetMaterial;
    private int currentTileId = -1;
    private int currentPoolTileId = -1;
    private MaterialPropertyBlock propBlock;
    private int outlineId;
    private Vector4 frontTilingOffset = new Vector4(1f, 1f, 0f, 0f);
    private bool hasFrontTilingOffset;

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
        if (targetMaterial != null && cardRenderer != null && propBlock != null) {
            return;
        }

        const int tileLayer = 10;
        // Layer 10：ObjectID 描边过滤 + peek 物理
        SetLayerRecursively(gameObject, tileLayer);

        cardRenderer = GetComponent<Renderer>() ?? GetComponentInChildren<Renderer>();
        if (cardRenderer == null) return;

        Material[] shared = cardRenderer.sharedMaterials;
        int threeDTilesIndex = -1;
        for (int i = 0; i < shared.Length; i++) {
            Material sharedMat = shared[i];
            if (sharedMat != null && sharedMat.shader != null
                && sharedMat.shader.name == "Custom/ThreeDTiles") {
                threeDTilesIndex = i;
                break;
            }
        }
        if (threeDTilesIndex >= 0) {
            targetMaterial = cardRenderer.materials[threeDTilesIndex];
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
        if (cardRenderer != null && propBlock != null) {
            propBlock.Clear();
            if (hasFrontTilingOffset) {
                propBlock.SetVector(FrontTilingOffsetId, frontTilingOffset);
            }
            propBlock.SetFloat(OutlineIdProp, 0f);
            cardRenderer.SetPropertyBlock(propBlock);
        }
    }

    private void ApplyPropertyBlock() {
        if (cardRenderer == null) return;
        if (propBlock == null) {
            propBlock = new MaterialPropertyBlock();
        }
        propBlock.Clear();
        if (hasFrontTilingOffset) {
            propBlock.SetVector(FrontTilingOffsetId, frontTilingOffset);
        }
        if (outlineId > 0) {
            propBlock.SetFloat(OutlineIdProp, outlineId);
        }
        cardRenderer.SetPropertyBlock(propBlock);
    }

    /// <summary>
    /// 设置牌面纹理（使用缓存的Sprite）
    /// 额外做 90° 逆时针旋转补偿（向左旋转 90°）
    /// verticalStretch: 牌面上下拉伸倍数，1.0=不拉伸，1.1=拉伸 1.1 倍（通过 UV 实现，不改变 3D 几何）
    /// </summary>
    public void SetCardSprite(int tileId, Sprite sprite, float verticalStretch = 1f) {
        InitializeComponents();
        if (targetMaterial == null || cardRenderer == null || sprite == null) return;

        currentTileId = tileId;
        currentPoolTileId = tileId;
        Texture2D atlasTexture = sprite.texture;
        targetMaterial.SetTexture("_FrontTex", atlasTexture);

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
        hasFrontTilingOffset = true;
        if (isActiveAndEnabled && outlineId <= 0) {
            AcquireOutlineId();
        }
        ApplyPropertyBlock();
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

    /// <summary>
    /// 获取目标材质（用于Card3DHoverManager访问材质属性）
    /// </summary>
    public Material GetMaterial() {
        InitializeComponents();
        return targetMaterial;
    }
}
