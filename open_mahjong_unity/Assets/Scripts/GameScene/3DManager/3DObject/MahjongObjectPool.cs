using System.Collections;
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

    [SerializeField] GameObject tile3DPrefab;
    [SerializeField] SpriteAtlas cardAtlas;
    private Dictionary<int, Queue<GameObject>> poolDictionary;
    private Dictionary<int, Sprite> spriteCache = new Dictionary<int, Sprite>();

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
        InitializePool("guobiao");
    }
    
    /// <summary>
    /// 预缓存所有需要的 Sprite
    /// </summary>
    private void CacheAllSprites(SpriteAtlas atlas) {
        int[] allIds = {
            0, 11, 12, 13, 14, 15, 16, 17, 18, 19,
            21, 22, 23, 24, 25, 26, 27, 28, 29,
            31, 32, 33, 34, 35, 36, 37, 38, 39,
            41, 42, 43, 44, 45, 46, 47,
            51, 52, 53, 54, 55, 56, 57, 58
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

    public void InitializePool(string rule) {
        const int BLANK_TILE_ID = 0;
        Queue<GameObject> blankTilePool = new Queue<GameObject>();
        for (int i = 0; i < 42; i++) {
            GameObject obj = Instantiate(tile3DPrefab);
            obj.SetActive(false);
            obj.transform.SetParent(transform);
            PrecalculateNormals(obj);
            ApplyCardTexture(obj, BLANK_TILE_ID);
            blankTilePool.Enqueue(obj);
        }
        poolDictionary[BLANK_TILE_ID] = blankTilePool;

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
                PrecalculateNormals(obj);
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
            PrecalculateNormals(obj);
            ApplyCardTexture(obj, tileId);
            objectPool.Enqueue(obj);
            poolDictionary[tileId] = objectPool;
        }
    }
    
    /// <summary>
    /// 预计算对象的平滑法线
    /// </summary>
    private void PrecalculateNormals(GameObject obj) {
        OutlineNormalsCalculator calculator = obj.GetComponent<OutlineNormalsCalculator>();
        if (calculator != null) {
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
            Debug.LogWarning("牌池中已无可用对象: " + type);
            return null;
        }

        GameObject tile = poolDictionary[type].Dequeue();
        tile.SetActive(true);
        tile.transform.position = position;
        tile.transform.rotation = rotation;
        return tile;
    }
    
    /// <summary>
    /// 从池中取出一张空白牌面
    /// </summary>
    public GameObject SpawnBlankTile(Vector3 position, Quaternion rotation) {
        return Spawn(0, position, rotation);
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
            int tileId = tile3D.GetTileId();
            if (tileId != -1) {
                type = tileId;
            }
        }
        
        if (type == 0 || type == -1) {
            type = 0;
        }
        if (!poolDictionary.ContainsKey(type)) {
            type = 0;
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

    /// <summary>
    /// 应用牌面纹理，初始化时一次性完成（含牌面上下拉伸）
    /// </summary>
    private void ApplyCardTexture(GameObject cardObj, int tileId) {
        Tile3D tile3D = cardObj.GetComponent<Tile3D>();
        if (tile3D == null) {
            tile3D = cardObj.AddComponent<Tile3D>();
        }
        if (spriteCache.TryGetValue(tileId, out Sprite cachedSprite)) {
            tile3D.SetCardSprite(tileId, cachedSprite, CARD_FACE_VERTICAL_STRETCH);
        }
    }
}