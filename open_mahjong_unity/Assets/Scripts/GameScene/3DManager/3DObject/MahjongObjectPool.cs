using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

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
    [SerializeField] SpriteAtlas cardAtlas;
    private Dictionary<int, Queue<GameObject>> poolDictionary;
    private Dictionary<int, Sprite> spriteCache = new Dictionary<int, Sprite>();
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

        CacheAllSprites(cardAtlas);
        InitializePool();
    }

    /// <summary>
    /// 预缓存所有需要的 Sprite
    /// </summary>
    private void CacheAllSprites(SpriteAtlas atlas) {
        int[] allIds = {
            2, 11, 12, 13, 14, 15, 16, 17, 18, 19, // 2 = 空白白板 46 = 回形白板
            21, 22, 23, 24, 25, 26, 27, 28, 29,
            31, 32, 33, 34, 35, 36, 37, 38, 39,
            41, 42, 43, 44, 45, 46, 47,
            51, 52, 53, 54, 55, 56, 57, 58,
            105, 205, 305 // 立直麻将：赤 5m / 赤 5p / 赤 5s
        };

        foreach (int id in allIds) {
            string spriteName = id.ToString();
            Sprite sprite = atlas.GetSprite(spriteName);
            if (sprite == null) {
                sprite = atlas.GetSprite(spriteName + "(Clone)");
            }
            if (sprite != null) {
                spriteCache[id] = sprite;
            }
        }
    }

    private const float CARD_FACE_VERTICAL_STRETCH = 1.1f;

    public void InitializePool() {
        int blankId = BlankPoolTileId;
        Queue<GameObject> blankTilePool = new Queue<GameObject>();
        for (int i = 0; i < 56; i++) {
            GameObject obj = Instantiate(tile3DPrefab);
            obj.SetActive(false);
            obj.transform.SetParent(transform);
            SetupPooledTile(obj);
            ApplyCardTexture(obj, blankId);
            blankTilePool.Enqueue(obj);
        }
        poolDictionary[blankId] = blankTilePool;

        int[] standardTiles = {
            11, 12, 13, 14, 15, 16, 17, 18, 19,
            21, 22, 23, 24, 25, 26, 27, 28, 29,
            31, 32, 33, 34, 35, 36, 37, 38, 39,
            41, 42, 43, 44, 45, 46, 47
        };
        foreach (int tileId in standardTiles) {
            Queue<GameObject> objectPool = new Queue<GameObject>();
            for (int i = 0; i < 4; i++) {
                GameObject obj = Instantiate(tile3DPrefab);
                obj.SetActive(false);
                obj.transform.SetParent(transform);
                SetupPooledTile(obj);
                ApplyCardTexture(obj, tileId);
                objectPool.Enqueue(obj);
            }
            poolDictionary[tileId] = objectPool;
        }

        int[] flowerTiles = { 51, 52, 53, 54, 55, 56, 57, 58 };
        foreach (int tileId in flowerTiles) {
            Queue<GameObject> objectPool = new Queue<GameObject>();
            GameObject obj = Instantiate(tile3DPrefab);
            obj.SetActive(false);
            obj.transform.SetParent(transform);
            SetupPooledTile(obj);
            ApplyCardTexture(obj, tileId);
            objectPool.Enqueue(obj);
            poolDictionary[tileId] = objectPool;
        }

        // 立直麻将赤宝牌：每种仅 1 张，与普通 5m/5p/5s 作为同点数不同实体
        int[] redDoraTiles = { 105, 205, 305 };
        foreach (int tileId in redDoraTiles) {
            Queue<GameObject> objectPool = new Queue<GameObject>();
            GameObject obj = Instantiate(tile3DPrefab);
            obj.SetActive(false);
            obj.transform.SetParent(transform);
            SetupPooledTile(obj);
            ApplyCardTexture(obj, tileId);
            objectPool.Enqueue(obj);
            poolDictionary[tileId] = objectPool;
        }

    }

    /// <summary>
    /// 仅在进入虹雀时建立 126 张唯一牌的 3D 对象与材质。
    /// 虹雀每张牌只出现一次，因此每个牌面一个对象即可；把 Instantiate/材质创建集中到开局，
    /// 避免实战中每次首次亮出新牌都卡住一帧。
    /// </summary>
    public void PrewarmHongquePool() {
        for (int colour = 0; colour < HongqueTileVisual.ColourCount; colour++) {
            for (int number = 1; number <= HongqueTileVisual.NumberCount; number++) {
                EnsureHongqueTilePool(HongqueTileVisual.BaseId + colour * 10 + number);
            }
        }
    }

    private void EnsureHongqueTilePool(int tileId) {
        if (poolDictionary.ContainsKey(tileId)) return;
        Queue<GameObject> objectPool = new Queue<GameObject>();
        GameObject obj = Instantiate(tile3DPrefab);
        obj.SetActive(false);
        obj.transform.SetParent(transform);
        SetupPooledTile(obj);
        ApplyCardTexture(obj, tileId);
        objectPool.Enqueue(obj);
        poolDictionary[tileId] = objectPool;
    }

    private void SetupPooledTile(GameObject obj) {
        EnsureTileCollider(obj);
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
        if (HongqueTileVisual.IsHongqueId(type) && !poolDictionary.ContainsKey(type)) {
            // 兼容旧入口/重连：正常虹雀对局会在首个快照时整批预热。
            EnsureHongqueTilePool(type);
        }
        if (!poolDictionary.ContainsKey(type)) {
            Debug.LogError("牌型不存在于对象池中: " + type);
            return null;
        }
        if (poolDictionary[type].Count == 0) {
            Debug.LogWarning("牌池中已无可用对象: " + type);
            return null;
        }

        GameObject tile = poolDictionary[type].Dequeue();
        // 首次取牌时确保保存的牌背颜色/图片已应用（含对象池同步），
        // 否则重启后池内牌仍带着初始化时的默认牌背颜色。
        CardBackManager.EnsureSavedConfigApplied();
        tile.SetActive(true);
        tile.transform.position = position;
        tile.transform.rotation = rotation;
        EnsureTileCollider(tile);
        ApplyCardTexture(tile, type);
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

        int blankId = BlankPoolTileId;
        if (type == 0 || type == 1) {
            type = blankId;
        }
        if (!poolDictionary.ContainsKey(type)) {
            type = blankId;
        }

        ApplyCardTexture(tile, type);

        tile.SetActive(false);
        tile.transform.SetParent(transform);
        poolDictionary[type].Enqueue(tile);
    }

    /// <summary>
    /// 将空白牌面归还到池中
    /// </summary>
    public void ReturnBlankTile(GameObject tile) {
        Return(BlankPoolTileId, tile);
    }

    /// <summary>
    /// 应用牌面纹理，初始化时一次性完成（含牌面上下拉伸）
    /// </summary>
    private void ApplyCardTexture(GameObject cardObj, int tileId) {
        Tile3D tile3D = cardObj.GetComponent<Tile3D>();
        if (tile3D == null) {
            tile3D = cardObj.AddComponent<Tile3D>();
        }
        if (HongqueTileVisual.IsHongqueId(tileId)) {
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
            tile3D.SetStandaloneCardTextureContain(tileId, customTexture, customMaterial);
            return;
        }

        tile3D.RestoreAtlasMaterial();
        if (spriteCache.TryGetValue(tileId, out Sprite cachedSprite)) {
            tile3D.SetCardSprite(tileId, cachedSprite, CARD_FACE_VERTICAL_STRETCH);
        }
        if (ConfigManager.Instance != null && ConfigManager.Instance.UseBlankWhiteDragonFace(tileId)
            && spriteCache.TryGetValue(BlankPoolTileId, out Sprite blankSprite)) {
            tile3D.SetCardSprite(tileId, blankSprite, CARD_FACE_VERTICAL_STRETCH);
        }
    }

    /// <summary>自定义标准牌面变更后，刷新池内与场上非虹雀 3D 牌。</summary>
    public void RefreshCustomStandardFaces() {
        Dictionary<int, Material> previous = customStandardMaterialCache;
        customStandardMaterialCache = new Dictionary<int, Material>();
        ForEachPooledTile(tile => {
            if (tile == null) return;
            Tile3D pooled = GetTile3D(tile);
            int poolId = pooled != null ? pooled.GetPoolTileId() : -1;
            if (poolId > 0 && !HongqueTileVisual.IsHongqueId(poolId)) {
                ApplyCardTexture(tile, poolId);
                CardBackManager.ApplyInstanceVisuals(pooled);
            }
        });
        Tile3D[] active = Object.FindObjectsByType<Tile3D>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < active.Length; i++) {
            Tile3D activeTile = active[i];
            if (activeTile == null) continue;
            int poolId = activeTile.GetPoolTileId();
            if (poolId > 0 && !HongqueTileVisual.IsHongqueId(poolId)) {
                ApplyCardTexture(activeTile.gameObject, poolId);
                CardBackManager.ApplyInstanceVisuals(activeTile);
            }
        }
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
        material.SetColor("_TableFaceFallbackColor", ConfigManager.DefaultTableFaceFallbackColor);
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
