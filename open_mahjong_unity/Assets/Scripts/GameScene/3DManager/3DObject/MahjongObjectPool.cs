using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 麻将牌数字枚举 标准库
/// </summary>
public enum MahjongTileType {
    Wan1 = 11, Wan2 = 12, Wan3 = 13, Wan4 = 14, Wan5 = 15, Wan6 = 16, Wan7 = 17, Wan8 = 18, Wan9 = 19,
    Tiao1 = 21, Tiao2 = 22, Tiao3 = 23, Tiao4 = 24, Tiao5 = 25, Tiao6 = 26, Tiao7 = 27, Tiao8 = 28, Tiao9 = 29,
    Tong1 = 31, Tong2 = 32, Tong3 = 33, Tong4 = 34, Tong5 = 35, Tong6 = 36, Tong7 = 37, Tong8 = 38, Tong9 = 39,
    Dong = 41, Nan = 42, Xi = 43, Bei = 44,
    Zhong = 45, Fa = 46, Bai = 47
}

/// <summary>
/// 麻将牌数字枚举 国标拓展
/// </summary>
public enum MahjongTileType_extend_GB {
    chun = 51, xia = 52, qiu = 53, dong = 54,
    mei = 55, lan = 56, zhu = 57, jian = 58,
}

public class MahjongObjectPool : MonoBehaviour {
    public static MahjongObjectPool Instance;

    /// <summary>与图集、ConfigManager.BlankFaceImageId 一致：空白立牌池键与牌面 id。</summary>
    private const int BlankPoolTileId = 2;

    [SerializeField] GameObject tile3DPrefab;
    private Dictionary<int, Queue<GameObject>> poolDictionary;
    private readonly Dictionary<Material, Material> washizuMaterials = new Dictionary<Material, Material>();
    private readonly Dictionary<Material, Material> washizuInkMaterials = new Dictionary<Material, Material>();
    private readonly Dictionary<Material, Material> washizuReadingMaterials = new Dictionary<Material, Material>();
    private Mesh washizuReadingMesh;
    // Uncreated slots still count towards the physical limit for this tile.
    private readonly Dictionary<int, int> uncreatedTiles = new Dictionary<int, int>();
    private readonly Dictionary<int, int> hongquePoolCapacity = new Dictionary<int, int>();
    private Dictionary<int, Material> hongqueMaterialCache = new Dictionary<int, Material>();
    private Dictionary<int, Material> customStandardMaterialCache = new Dictionary<int, Material>();

    private void Awake() {
        if (Instance == null) {
            Instance = this;
            // 初始化对象池
            poolDictionary = new Dictionary<int, Queue<GameObject>>();
        } else {
            Destroy(gameObject);
            return;
        }

        TableWorldObjectMount.AttachPool(transform);
        InitializePool();
    }

    public void InitializePool() {
        AddPool(BlankPoolTileId, 56);
        foreach (int id in TilePackIds.StandardFaceIds) {
            if (id == BlankPoolTileId) continue;
            // Four copies of each regular tile and 59, one copy of each flower or red five.
            AddPool(id, id < 50 || id == 59 ? 4 : 1);
        }
        foreach (int id in MalaysiaTileVisual.Ids) AddPool(id, id == 81 ? 4 : 1);
    }

    private void AddPool(int id, int capacity) {
        if (poolDictionary.ContainsKey(id)) return;
        poolDictionary[id] = new Queue<GameObject>(capacity);
        uncreatedTiles[id] = capacity;
    }

    private GameObject CreatePooledTile(int id) {
        var tile = Instantiate(tile3DPrefab, transform);
        tile.SetActive(false);
        EnsureTileCollider(tile);
        var state = tile.GetComponent<Tile3D>() ?? tile.AddComponent<Tile3D>();
        state.SetTileIds(id, id);
        return tile;
    }

    public void PrewarmHongquePool(int copies = 1) {
        for (int colour = 0; colour < HongqueTileVisual.ColourCount; colour++)
            for (int number = 1; number <= HongqueTileVisual.NumberCount; number++)
                EnsureHongqueTilePool(HongqueTileVisual.BaseId + colour * 10 + number, copies);
    }

    private void EnsureHongqueTilePool(int tileId, int copies) {
        if (!poolDictionary.TryGetValue(tileId, out var objectPool)) {
            objectPool = new Queue<GameObject>();
            poolDictionary[tileId] = objectPool;
        }
        hongquePoolCapacity.TryGetValue(tileId, out int capacity);
        for (int i = capacity; i < copies; i++) {
            GameObject obj = CreatePooledTile(tileId);
            ApplyCardTexture(obj, tileId);
            objectPool.Enqueue(obj);
        }
        hongquePoolCapacity[tileId] = Mathf.Max(capacity, copies);
    }

    public const int TilePhysicsLayer = 10;

    public void RefreshTileCollider(GameObject obj) {
        EnsureTileCollider(obj);
        Tile3D tile3D = GetTile3D(obj);
        tile3D?.RefreshPeekCollider();
    }

    /// <summary>
    /// 遍历对象池内所有牌对象（含未部署的 inactive 牌），供批量同步牌背等共享视觉。
    /// FindObjectsByType 默认找不到 inactive 实例，池内牌必须走本方法或 FindObjectsInactive.Include。
    /// </summary>
    public void ForEachPooledTile(System.Action<GameObject> action) {
        if (action == null || poolDictionary == null) return;
        foreach (Queue<GameObject> queue in poolDictionary.Values) {
            foreach (GameObject tile in queue) {
                if (tile != null) action(tile);
            }
        }
    }

    /// <summary>虹雀 / 自定义牌面的独立材质（不共享 3DTile.mat），改牌边/背景时必须一并写入。</summary>
    public void ForEachStandaloneMaterial(System.Action<Material> action) {
        if (action == null) return;
        foreach (Material material in hongqueMaterialCache.Values) {
            if (material != null) action(material);
        }
        foreach (Material material in customStandardMaterialCache.Values) {
            if (material != null) action(material);
        }
        foreach (Material material in washizuMaterials.Values) {
            if (material != null) action(material);
        }
        foreach (Material material in washizuInkMaterials.Values) {
            if (material != null) action(material);
        }
        foreach (Material material in washizuReadingMaterials.Values) {
            if (material != null) action(material);
        }
    }

    public static Tile3D GetTile3D(GameObject obj) {
        if (obj == null) return null;
        Tile3D tile = obj.GetComponent<Tile3D>();
        return tile != null ? tile : obj.GetComponentInChildren<Tile3D>(true);
    }

    private static void EnsureTileCollider(GameObject obj) {
        Renderer renderer = obj.GetComponent<Renderer>() ?? obj.GetComponentInChildren<Renderer>(true);
        if (renderer == null) return;

        GameObject colliderHost = renderer.gameObject;
        if (colliderHost != obj) {
            BoxCollider staleRootBox = obj.GetComponent<BoxCollider>();
            if (staleRootBox != null) {
                Object.Destroy(staleRootBox);
            }
        }

        BoxCollider box = colliderHost.GetComponent<BoxCollider>();
        if (box == null) {
            box = colliderHost.AddComponent<BoxCollider>();
        }
        box.isTrigger = false;
        box.enabled = false;
        FitBoxColliderFromMesh(box, renderer);
        SetLayerRecursively(obj, TilePhysicsLayer);
    }

    private static void FitBoxColliderFromMesh(BoxCollider box, Renderer renderer) {
        MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null) {
            Bounds meshBounds = meshFilter.sharedMesh.bounds;
            box.center = meshBounds.center;
            box.size = meshBounds.size;
            return;
        }
        Transform host = box.transform;
        Bounds worldBounds = renderer.bounds;
        Vector3 lossyScale = host.lossyScale;
        box.center = host.InverseTransformPoint(worldBounds.center);
        box.size = new Vector3(
            worldBounds.size.x / Mathf.Max(Mathf.Abs(lossyScale.x), 0.001f),
            worldBounds.size.y / Mathf.Max(Mathf.Abs(lossyScale.y), 0.001f),
            worldBounds.size.z / Mathf.Max(Mathf.Abs(lossyScale.z), 0.001f));
    }

    private static void SetLayerRecursively(GameObject obj, int layer) {
        obj.layer = layer;
        Transform root = obj.transform;
        for (int i = 0; i < root.childCount; i++) {
            SetLayerRecursively(root.GetChild(i).gameObject, layer);
        }
    }

    /// <summary>
    /// 从池中取出一张指定类型的牌
    /// </summary>
    public GameObject Spawn(int type, Vector3 position, Quaternion rotation) {
        if(DuocaiTileVisual.IsCurrent(type))return SpawnPresentationTile(type,position,rotation);
        int logicalType = type;
        if(VietnameseTileVisual.IsExtra(type) && !poolDictionary.ContainsKey(type)) {
            AddPool(type, 1);
        }
        type = WashizuTile.Face(type);
        if (HongqueTileVisual.IsHongqueId(type)) {
            // 兼容旧入口/重连：正常虹雀对局会在首个快照时整批预热。
            EnsureHongqueTilePool(type, HongqueWTips.IsActive ? 2 : 1);
        }
        if (!poolDictionary.ContainsKey(type)) {
            Debug.LogError("牌型不存在于对象池中: " + type);
            return null;
        }
        // 空白池也承载独立展示牌；血流连续多响的历史标记可超过初始容量。
        // 只扩展这个视觉池，实体牌仍保留每种四张的限制。
        if (poolDictionary[type].Count == 0 && uncreatedTiles.TryGetValue(type, out int remaining) && remaining > 0) {
            poolDictionary[type].Enqueue(CreatePooledTile(type));
            uncreatedTiles[type] = remaining - 1;
        } else if (type == BlankPoolTileId && poolDictionary[type].Count == 0) {
            poolDictionary[type].Enqueue(CreatePooledTile(type));
        }
        if (poolDictionary[type].Count == 0) {
            Debug.LogWarning("牌池中已无可用对象: " + type);
            return null;
        }

        GameObject tile = poolDictionary[type].Dequeue();
        GetTile3D(tile)?.InvalidatePoolLease();
        // 首次取牌时确保保存的牌背颜色/图片已应用（含对象池同步），
        // 否则重启后池内牌仍带着初始化时的默认牌背颜色。
        CardBackManager.EnsureSavedConfigApplied();
        tile.SetActive(true);
        tile.transform.position = position;
        tile.transform.rotation = rotation;
        EnsureTileCollider(tile);
        ApplyCardTexture(tile, logicalType);
        CardBackManager.ApplyInstanceVisuals(GetTile3D(tile));
        return tile;
    }

    /// <summary>
    /// 从池中取出一张空白牌面
    /// </summary>
    public GameObject SpawnBlankTile(Vector3 position, Quaternion rotation) {
        return Spawn(BlankPoolTileId, position, rotation);
    }

    /// <summary>
    /// 倒牌展示副本不占用每种仅四张的实体牌池；例如荣和张仍在牌河中、多家同时荣和。
    /// 复用预热的空白牌对象，保留真实牌面，但归还时仍回到空白池。
    /// </summary>
    public GameObject SpawnPresentationTile(int tileId, Vector3 position, Quaternion rotation) {
        GameObject tile = SpawnBlankTile(position, rotation);
        if (tile == null) return null;
        ApplyCardTexture(tile, tileId);
        GetTile3D(tile).SetTileIds(tileId, BlankPoolTileId);
        return tile;
    }

    /// <summary>
    /// 桌面公开牌面：复用展示池，但只绘制贴在桌布上的圆角平面。
    /// Return 会恢复实体网格、描边与阴影，不能把平面状态带到下一张普通牌。
    /// </summary>
    public GameObject SpawnWashizuReadingTile(int tileId, Vector3 position, Quaternion rotation) {
        GameObject tile = SpawnPresentationTile(tileId, position, rotation);
        if (tile == null) return null;
        var surface = tile.GetComponent<WashizuReadingSurface>() ?? tile.AddComponent<WashizuReadingSurface>();
        surface.Show(ref washizuReadingMesh);
        ApplyWashizuReadingMaterial(GetTile3D(tile));
        return tile;
    }

    private void ApplyWashizuReadingMaterial(Tile3D state) {
        Material source = state.TileMaterial;
        if (!washizuReadingMaterials.TryGetValue(source, out Material paper) || paper == null) {
            Shader shader = Resources.Load<Shader>("Washizu/ReadingFace");
            paper = new Material(source) { shader = shader, name = "WashizuReading_" + source.name, renderQueue = 2990 };
            washizuReadingMaterials[source] = paper;
        }
        state.SetGlassMaterial(paper);
    }

    /// <summary>独立展示实例不借用实体牌池；调用方注销悬停并 Destroy，不能 Return。</summary>
    public GameObject CreatePresentationTile(int tileId) {
        return CreatePresentationTile(tileId, TableWorldObjectMount.RuntimeRoot);
    }

    public GameObject CreatePresentationTile(int tileId, Transform parent) {
        int face = WashizuTile.Face(tileId);
        if (tile3DPrefab == null || (!TilePackIds.IsStandardFaceId(face) && !MalaysiaTileVisual.Contains(face)
            && !HongqueTileVisual.IsHongqueId(tileId) && !VietnameseTileVisual.IsExtra(tileId) && !DuocaiTileVisual.IsCurrent(tileId)))
            return null;
        CardBackManager.EnsureSavedConfigApplied();
        Transform prefab = tile3DPrefab.transform;
        GameObject tile = TableWorldObjectMount.InstantiatePrefab(tile3DPrefab, parent, prefab.position, prefab.rotation);
        EnsureTileCollider(tile);
        ApplyCardTexture(tile, tileId);
        CardBackManager.ApplyInstanceVisuals(GetTile3D(tile));
        return tile;
    }

    /// <summary>
    /// 从空白池取牌，但保留真实牌值用于自家 3D 手牌删除。
    /// </summary>
    public GameObject SpawnBlankTile(Vector3 position, Quaternion rotation, int logicalTileId) {
        GameObject tile = SpawnBlankTile(position, rotation);
        if (tile != null) {
            Tile3D tile3D = tile.GetComponent<Tile3D>();
            tile3D.SetTileIds(logicalTileId, BlankPoolTileId);
        }
        return tile;
    }

    /// <summary>
    /// 将牌归还到池中
    /// </summary>
    public void Return(int type, GameObject tile) {
        tile.GetComponent<WashizuReadingSurface>()?.Restore();
        GetTile3D(tile)?.InvalidatePoolLease();
        // 归还前重置材质颜色并取消悬停管理器注册
        if (Card3DHoverManager.Instance != null) {
            Card3DHoverManager.Instance.ResetAndUnregisterCard(tile);
        }

        Tile3D tile3D = tile.GetComponent<Tile3D>();
        if (tile3D != null && type == -1) {
            int resolvedId = tile3D.GetPoolTileId();
            if (resolvedId != -1) {
                type = resolvedId;
            }
        }
        if (tile3D != null) {
            tile3D.isRiichiHorizontal = false;
            tile3D.ResetConcealedState();
        }

        type = WashizuTile.Face(type);
        int blankId = BlankPoolTileId;
        if (type == 0 || type == 1) {
            type = blankId;
        }
        if (!poolDictionary.ContainsKey(type)) {
            type = blankId;
        }

        tile.SetActive(false);
        tile3D?.ReleasePooledFace(type);
        // The tile already carries the active world scale. Reparent it to the
        // pool while preserving that pose, otherwise a normalized profile
        // would apply its scale a second time on every Return.
        TableWorldObjectMount.AttachPreservingWorld(tile.transform, transform);
        poolDictionary[type].Enqueue(tile);
    }

    /// <summary>
    /// 将空白牌面归还到池中
    /// </summary>
    public void ReturnBlankTile(GameObject tile) {
        Return(BlankPoolTileId, tile);
    }

    /// <summary>
    /// 应用牌面纹理：标准牌完整等比居中，虹雀保持原独立映射。
    /// </summary>
    private void ApplyCardTexture(GameObject cardObj, int tileId) {
        Tile3D tile3D = cardObj.GetComponent<Tile3D>();
        if (tile3D == null) {
            tile3D = cardObj.AddComponent<Tile3D>();
        }
        if (WashizuTile.IsTransparent(tileId)) {
            int face = WashizuTile.Face(tileId);
            ApplyCardTexture(cardObj, face);
            Material source = tile3D.TileMaterial;
            if (source != null) {
                if (!washizuMaterials.TryGetValue(source, out Material glass) || glass == null) {
                    Shader shader = Resources.Load<Shader>("Washizu/TransparentTile");
                    if (shader == null) { Debug.LogError("Washizu glass shader is missing"); return; }
                    glass = new Material(source) { shader = shader, name = "WashizuGlass_" + source.name, renderQueue = 3000 };
                    glass.SetFloat("_InkPass", 0f);
                    // Keep the far printed plane visible through the body.
                    // The shader rejects unneeded interior side/back surfaces.
                    glass.SetFloat("_Cull", 0f);
                    glass.SetFloat("_ZWrite", 0f);
                    washizuMaterials[source] = glass;
                }
                if (!washizuInkMaterials.TryGetValue(glass, out Material ink) || ink == null) {
                    // Include opaque paint in the camera color/depth before glass
                    // refraction, so the paint never samples the cloth behind it.
                    ink = new Material(glass) { name = "WashizuInk_" + source.name, renderQueue = 2450 };
                    ink.SetFloat("_InkPass", 1f);
                    ink.SetFloat("_Cull", 0f);
                    ink.SetFloat("_ZWrite", 1f);
                    washizuInkMaterials[glass] = ink;
                }
                tile3D.SetGlassMaterial(glass, ink);
            }
            tile3D.SetTileIds(tileId, face);
            return;
        }
        if (!DuocaiTileVisual.IsCurrent(tileId) && HongqueTileVisual.IsHongqueId(tileId)) {
            Texture2D texture = HongqueTileVisual.LoadTableTexture(tileId);
            if (texture == null) {
                Debug.LogError($"找不到虹雀 3D 牌面: {HongqueTileVisual.TableResourcePath(tileId)}");
                return;
            }
            if (!hongqueMaterialCache.TryGetValue(tileId, out Material material)) {
                Material template = FindTileMaterialTemplate(cardObj);
                if (template == null) return;
                material = new Material(template) { name = $"Hongque_{HongqueTileVisual.ToCode(tileId)}" };
                material.SetTexture("_FrontTex", texture);
                ApplyTableFaceFallback(material);
                CardBackManager.SyncSharedVisualsToMaterial(material);
                hongqueMaterialCache[tileId] = material;
            }
            tile3D.SetStandaloneCardTexture(tileId, texture, material);
            return;
        }

        Texture2D customTexture = TileFaceResolver.LoadTableTexture(tileId);
        if (customTexture != null) {
            if (!customStandardMaterialCache.TryGetValue(tileId, out Material customMaterial)) {
                Material template = FindTileMaterialTemplate(cardObj);
                if (template == null) return;
                customMaterial = new Material(template) { name = $"CustomFace_{tileId}" };
                customStandardMaterialCache[tileId] = customMaterial;
            }
            customMaterial.SetTexture("_FrontTex", customTexture);
            ApplyTableFaceFallback(customMaterial);
            CardBackManager.SyncSharedVisualsToMaterial(customMaterial);
            tile3D.SetStandaloneCardTextureContain(tileId, customTexture, customMaterial,
                TileFaceResolver.TableImageScaleFor(tileId));
            return;
        }

        tile3D.RestoreAtlasMaterial();
        Sprite fallback = TilePackIds.IsStandardFaceId(tileId) ? TileFaceResolver.LoadTableSprite(tileId) : null;
        if (fallback != null) tile3D.SetCardSprite(tileId, fallback);
        else tile3D.SetTileIds(tileId, tileId);
    }

    public void SetPresentationFace(GameObject tile, int tileId) {
        Tile3D component = GetTile3D(tile);
        int poolId = component != null ? component.GetPoolTileId() : BlankPoolTileId;
        ApplyCardTexture(tile, tileId > 0 ? tileId : BlankPoolTileId);
        component = GetTile3D(tile);
        component.SetTileIds(tileId, poolId);
        CardBackManager.ApplyInstanceVisuals(component);
        if (tile.GetComponent<WashizuReadingSurface>()?.IsReadingSurface == true)
            ApplyWashizuReadingMaterial(component);
    }

    private void ClearWashizuMaterials() {
        foreach (Material ink in washizuInkMaterials.Values) if (ink != null) Destroy(ink);
        washizuInkMaterials.Clear();
        foreach (Material glass in washizuMaterials.Values) if (glass != null) Destroy(glass);
        washizuMaterials.Clear();
        foreach (Material paper in washizuReadingMaterials.Values) if (paper != null) Destroy(paper);
        washizuReadingMaterials.Clear();
    }

    private void OnDestroy() {
        ClearWashizuMaterials();
        if (washizuReadingMesh != null) Destroy(washizuReadingMesh);
        if (Instance == this) Instance = null;
    }

    /// <summary>自定义标准牌面变更后，刷新池内与场上非虹雀 3D 牌。</summary>
    public void RefreshCustomStandardFaces() {
        // Derived textures must not outlive a user-selected face pack.
        ClearWashizuMaterials();
        Dictionary<int, Material> previous = customStandardMaterialCache;
        customStandardMaterialCache = new Dictionary<int, Material>();
        TileInstances<Tile3D>.ForEach(activeTile => {
            // Queued tiles bind their face only when leased. They have no pack dependency.
            if (!activeTile.gameObject.activeSelf && activeTile.transform.parent == transform) return;
            int poolId = activeTile.GetPoolTileId();
            if (poolId > 0 && !HongqueTileVisual.IsHongqueId(poolId)) {
                int logicalId = activeTile.GetTileId();
                int renderedId = WashizuTile.IsTransparent(logicalId) ? logicalId : activeTile.VisibleFaceTileId;
                SetPresentationFace(activeTile.gameObject, renderedId);
                activeTile.SetTileIds(logicalId, poolId);
            }
        }, includeInactive: true);
        foreach (var pair in previous) {
            if (pair.Value != null && !customStandardMaterialCache.ContainsValue(pair.Value)) {
                Object.Destroy(pair.Value);
            }
        }
    }

    /// <summary>
    /// fluffy / hkmahjong 3D 牌面已把原米色做成透明；无 3D 牌面背景时用兜底色填回。
    /// </summary>
    private static void ApplyTableFaceFallback(Material material) {
        if (material == null) return;
        material.SetColor("_TableFaceFallbackColor", GameSettings.Current.DefaultTableFaceFallbackColor);
        material.SetFloat("_TableFaceFallbackEnabled", 1f);
    }

    private static Material FindTileMaterialTemplate(GameObject cardObj) {
        Renderer renderer = cardObj.GetComponent<Renderer>() ?? cardObj.GetComponentInChildren<Renderer>(true);
        if (renderer == null) {
            return Resources.Load<Material>(CardBackManager.MaterialResourcePath);
        }
        foreach (Material candidate in renderer.sharedMaterials) {
            if (candidate != null && candidate.shader != null && candidate.shader.name == "Custom/ThreeDTiles"
                && !candidate.name.StartsWith("Hongque_")
                && !candidate.name.StartsWith("CustomFace_")) {
                return candidate;
            }
        }
        foreach (Material candidate in renderer.sharedMaterials) {
            if (candidate != null && candidate.shader != null && candidate.shader.name == "Custom/ThreeDTiles") {
                return candidate;
            }
        }
        return Resources.Load<Material>(CardBackManager.MaterialResourcePath);
    }
}
