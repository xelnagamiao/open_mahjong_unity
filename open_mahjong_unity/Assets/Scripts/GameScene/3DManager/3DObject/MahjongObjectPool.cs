using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
// 暂不启用的对象池
*/

// 麻将牌数字枚举 标准库
public enum MahjongTileType {
    Wan1 = 11, Wan2 = 12, Wan3 = 13, Wan4 = 14, Wan5 = 15, Wan6 = 16, Wan7 = 17, Wan8 = 18, Wan9 = 19,
    Tiao1 = 21, Tiao2 = 22, Tiao3 = 23, Tiao4 = 24, Tiao5 = 25, Tiao6 = 26, Tiao7 = 27, Tiao8 = 28, Tiao9 = 29,
    Tong1 = 31, Tong2 = 32, Tong3 = 33, Tong4 = 34, Tong5 = 35, Tong6 = 36, Tong7 = 37, Tong8 = 38, Tong9 = 39,
    Dong = 41, Nan = 42, Xi = 43, Bei = 44,
    Zhong = 45, Fa = 46, Bai = 47
}

// 麻将牌数字枚举 国标拓展
public enum MahjongTileType_extend_GB {
    chun = 51, xia = 52, qiu = 53, dong = 54,
    mei = 55, lan = 56, zhu = 57, jian = 58,
}

public class MahjongObjectPool : MonoBehaviour {
    public static MahjongObjectPool Instance;

    [SerializeField] GameObject tile3DPrefab;

    // 不同规则使用的牌面映射
    public Dictionary<string, string> MahjongTileType_mapping = new Dictionary<string, string> {
        {"guobiao","CardFaceMaterial_xuefun"},
    };
    // 对象池
    private Dictionary<int, Queue<GameObject>> poolDictionary;
    
    // 纹理缓存，避免每次 Resources.Load
    private Dictionary<int, Texture2D> _textureCache = new Dictionary<int, Texture2D>();

    private void Awake() {
        if (Instance == null) {
            Instance = this;
            // 初始化对象池
            poolDictionary = new Dictionary<int, Queue<GameObject>>();
        } else {
            Destroy(gameObject);
            return;
        }
        
        // 预加载所有纹理
        PreloadTextures();
        
        InitializePool("guobiao");
    }
    
    /// <summary>
    /// 预加载所有可能用到的纹理到缓存中
    /// </summary>
    private void PreloadTextures() {
        // 所有可能的牌ID
        int[] allTileIds = {
            11, 12, 13, 14, 15, 16, 17, 18, 19,  // 万
            21, 22, 23, 24, 25, 26, 27, 28, 29,  // 饼
            31, 32, 33, 34, 35, 36, 37, 38, 39,  // 条
            41, 42, 43, 44,                      // 东南西北
            45, 46, 47,                          // 中白发
            51, 52, 53, 54, 55, 56, 57, 58        // 花牌
        };
        
        foreach (int tileId in allTileIds) {
            Texture2D texture = Resources.Load<Texture2D>($"image/CardFaceMaterial_xuefun/{tileId}");
            if (texture != null) {
                _textureCache[tileId] = texture;
            }
        }
        
        Debug.Log($"MahjongObjectPool: 预加载了 {_textureCache.Count} 张纹理");
    }

    public void InitializePool(string rule) {
        string useMaterial = MahjongTileType_mapping[rule];
        
        Debug.Log("MahjongObjectPool: 开始初始化对象池并预计算法线...");
        int totalObjects = 0;
        
        // 初始化空白牌面手牌池（用于绘制其他家手牌）：14*3 = 42张
        // 使用特殊ID 0 表示空白牌面
        const int BLANK_TILE_ID = 0;
        Queue<GameObject> blankTilePool = new Queue<GameObject>();
        for (int i = 0; i < 42; i++) {
            GameObject obj = Instantiate(tile3DPrefab);
            obj.SetActive(false);
            obj.transform.SetParent(transform);
            // 预计算法线
            PrecalculateNormals(obj);
            blankTilePool.Enqueue(obj);
            totalObjects++;
        }
        poolDictionary[BLANK_TILE_ID] = blankTilePool;
        
        // 标准牌堆（sth_tiles_set）：每种4张
        // 11-19: 万, 21-29: 饼, 31-39: 条, 41-44: 东南西北, 45-47: 中白发
        int[] standardTiles = {
            11, 12, 13, 14, 15, 16, 17, 18, 19,  // 万
            21, 22, 23, 24, 25, 26, 27, 28, 29,  // 饼
            31, 32, 33, 34, 35, 36, 37, 38, 39,  // 条
            41, 42, 43, 44,                      // 东南西北
            45, 46, 47                           // 中白发
        };
        foreach (int tileId in standardTiles) {
            Queue<GameObject> objectPool = new Queue<GameObject>();
            for (int i = 0; i < 4; i++) {
                GameObject obj = Instantiate(tile3DPrefab);
                obj.SetActive(false);
                obj.transform.SetParent(transform);
                // 预计算法线
                PrecalculateNormals(obj);
                objectPool.Enqueue(obj);
                totalObjects++;
            }
            poolDictionary[tileId] = objectPool;
        }
        
        // 花牌牌堆（hua_tiles_set）：每种1张
        // 51-58: 春夏秋冬 梅兰竹菊
        int[] flowerTiles = { 51, 52, 53, 54, 55, 56, 57, 58 };
        foreach (int tileId in flowerTiles) {
            Queue<GameObject> objectPool = new Queue<GameObject>();
            GameObject obj = Instantiate(tile3DPrefab);
            obj.SetActive(false);
            obj.transform.SetParent(transform);
            // 预计算法线
            PrecalculateNormals(obj);
            objectPool.Enqueue(obj);
            poolDictionary[tileId] = objectPool;
            totalObjects++;
        }
        
        Debug.Log($"MahjongObjectPool: 对象池初始化完成，共创建 {totalObjects} 个对象并完成法线预计算");
    }
    
    /// <summary>
    /// 预计算对象的平滑法线（在对象池初始化时调用）
    /// </summary>
    private void PrecalculateNormals(GameObject obj) {
        OutlineNormalsCalculator calculator = obj.GetComponent<OutlineNormalsCalculator>();
        if (calculator != null) {
            // 强制计算，即使 UV1 已有数据（确保每个对象都有独立的计算）
            calculator.CalculateAndApplySmoothNormals(true);
        }
    }

    /// <summary>
    /// 从池中取出一张指定类型的牌
    /// </summary>
    public GameObject Spawn(int type, Vector3 position, Quaternion rotation) {
        if (!poolDictionary.ContainsKey(type)) {
            Debug.LogError("牌型不存在于对象池中: " + type);
            return null;
        }

        if (poolDictionary[type].Count == 0) {
            Debug.LogWarning("牌池中已无可用对象，建议预加载更多或动态扩容: " + type);
            return null; // 可在此扩展：动态 Instantiate
        }

        GameObject tile = poolDictionary[type].Dequeue();
        tile.SetActive(true);
        tile.transform.position = position;
        tile.transform.rotation = rotation;

        // 如果不是空白牌面（ID为0），则应用纹理
        if (type != 0) {
            ApplyCardTexture(tile, type);
        }

        return tile;
    }
    
    /// <summary>
    /// 从池中取出一张空白牌面（用于其他家手牌显示）
    /// </summary>
    public GameObject SpawnBlankTile(Vector3 position, Quaternion rotation) {
        return Spawn(0, position, rotation);
    }

    /// <summary>
    /// 将牌归还到池中
    /// </summary>
    public void Return(int type, GameObject tile) {
        if (tile == null) {
            Debug.LogWarning("尝试归还空的牌对象");
            return;
        }
        
        // 如果牌有Tile3D组件，可以通过它获取tileId
        Tile3D tile3D = tile.GetComponent<Tile3D>();
        if (tile3D != null && type == -1) {
            // 如果type为-1，尝试从Tile3D组件获取
            int tileId = tile3D.GetTileId();
            if (tileId != -1) {
                type = tileId;
            }
        }
        
        // 如果是空白牌面或无法确定类型，使用空白牌面池
        if (type == 0 || type == -1) {
            type = 0;
        }
        
        if (!poolDictionary.ContainsKey(type)) {
            Debug.LogWarning($"无法归还：牌型不存在于池中 {type}，将归还到空白牌面池");
            type = 0; // 默认归还到空白牌面池
        }

        tile.SetActive(false);
        tile.transform.SetParent(transform);
        poolDictionary[type].Enqueue(tile);
    }
    
    /// <summary>
    /// 将空白牌面归还到池中
    /// </summary>
    public void ReturnBlankTile(GameObject tile) {
        Return(0, tile);
    }

    // 应用牌面纹理（通过Tile3D组件）
    private void ApplyCardTexture(GameObject cardObj, int tileId) {
        // 获取或添加Tile3D组件
        Tile3D tile3D = cardObj.GetComponent<Tile3D>();
        if (tile3D == null) {
            // 如果预制体上没有Tile3D组件，则添加一个
            tile3D = cardObj.AddComponent<Tile3D>();
        }

        // 通过Tile3D组件设置纹理，优先使用缓存的纹理
        if (tile3D != null) {
            Texture2D cachedTexture = null;
            if (_textureCache.TryGetValue(tileId, out cachedTexture)) {
                tile3D.SetCardTexture(tileId, cachedTexture);
            } else {
                // 如果缓存中没有，则使用默认加载方式
                tile3D.SetCardTexture(tileId);
            }
        } else {
            Debug.LogError($"无法获取或创建Tile3D组件，无法设置纹理: {tileId}");
        }
    }
}