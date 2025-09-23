using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
// 暂不启用的对象池
*/

// 麻将牌数字枚举 标准库
public enum MahjongTileType
{
    Wan1 = 11, Wan2 = 12, Wan3 = 13, Wan4 = 14, Wan5 = 15, Wan6 = 16, Wan7 = 17, Wan8 = 18, Wan9 = 19,
    Tiao1 = 21, Tiao2 = 22, Tiao3 = 23, Tiao4 = 24, Tiao5 = 25, Tiao6 = 26, Tiao7 = 27, Tiao8 = 28, Tiao9 = 29,
    Tong1 = 31, Tong2 = 32, Tong3 = 33, Tong4 = 34, Tong5 = 35, Tong6 = 36, Tong7 = 37, Tong8 = 38, Tong9 = 39,
    Dong = 41, Nan = 42, Xi = 43, Bei = 44,
    Zhong = 45, Fa = 46, Bai = 47
}

// 麻将牌数字枚举 国标拓展
public enum MahjongTileType_extend_GB
{
    chun = 51, xia = 52, qiu = 53, dong = 54,
    mei = 55, lan = 56, zhu = 57, jian = 58,
}

public class MahjongObjectPool : MonoBehaviour
{
    public static MahjongObjectPool Instance;

    [SerializeField] GameObject tile3DPrefab;

    // 不同规则使用的牌面映射
    public Dictionary<string, string> MahjongTileType_mapping = new Dictionary<string, string>
    {
        {"GB","CardFaceMaterial_xuefun"},
    };
    // 对象池
    private Dictionary<int, Queue<GameObject>> poolDictionary;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            // 初始化对象池
            poolDictionary = new Dictionary<int, Queue<GameObject>>();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void InitializePool(string rule)
    {
        string useMaterial = MahjongTileType_mapping[rule];
        foreach (MahjongTileType tile in System.Enum.GetValues(typeof(MahjongTileType)))
        {
            // 初始化对象池
            int tileId = (int)tile;
            Queue<GameObject> objectPool = new Queue<GameObject>();
            for (int i = 0; i < 4; i++)
            {
                GameObject obj = Instantiate(tile3DPrefab);
                obj.SetActive(false);
                obj.transform.SetParent(transform); // 作为子对象管理
                objectPool.Enqueue(obj);
            }
            poolDictionary[tileId] = objectPool;
        }
        foreach (MahjongTileType_extend_GB tile in System.Enum.GetValues(typeof(MahjongTileType_extend_GB)))
        {
            int tileId = (int)tile;
            Queue<GameObject> objectPool = new Queue<GameObject>();
            GameObject obj = Instantiate(tile3DPrefab);
            obj.SetActive(false);
            obj.transform.SetParent(transform); // 作为子对象管理
            objectPool.Enqueue(obj);
            poolDictionary[tileId] = objectPool;
        }
    }

    /// <summary>
    /// 从池中取出一张指定类型的牌
    /// </summary>
    public GameObject Spawn(int type, Vector3 position, Quaternion rotation)
    {
        if (!poolDictionary.ContainsKey(type))
        {
            Debug.LogError("牌型不存在于对象池中: " + type);
            return null;
        }

        if (poolDictionary[type].Count == 0)
        {
            Debug.LogWarning("牌池中已无可用对象，建议预加载更多或动态扩容: " + type);
            return null; // 可在此扩展：动态 Instantiate
        }

        GameObject tile = poolDictionary[type].Dequeue();
        tile.SetActive(true);
        tile.transform.position = position;
        tile.transform.rotation = rotation;

        // 设置牌面（通过脚本控制 UV 或 Sprite）
        // MahjongTile mahjongTile = tile.GetComponent<MahjongTile>();
        // if (mahjongTile != null)
        // {
        //     mahjongTile.SetTileType(type);
        // }
        
        // 临时：直接应用纹理
        ApplyCardTexture(tile, type);

        return tile;
    }

    /// <summary>
    /// 将牌归还到池中
    /// </summary>
    public void Return(int type, GameObject tile)
    {
        if (!poolDictionary.ContainsKey(type))
        {
            Debug.LogError("无法归还：牌型不存在于池中 " + type);
            Destroy(tile);
            return;
        }

        tile.SetActive(false);
        tile.transform.SetParent(transform);
        poolDictionary[type].Enqueue(tile);
    }

    // 应用牌面纹理
    private void ApplyCardTexture(GameObject cardObj, int tileId) {

        // 从Resources加载对应ID的图片
        Texture2D texture = Resources.Load<Texture2D>($"image/CardMaterial/{tileId}");
        if (texture == null) {
            Debug.LogError($"无法加载纹理: image/CardMaterial/{tileId}");
            return;
        }
        
        // 获取预制体的渲染器
        Renderer renderer = cardObj.GetComponent<Renderer>();
        
        // 通过材质名称找到SetImage材质
        int setImageMaterialIndex = -1;
        for (int i = 0; i < renderer.materials.Length; i++) {
            if (renderer.materials[i].name.Contains("SetImage")) {
                setImageMaterialIndex = i;
                break;
            }
        }
        
        if (setImageMaterialIndex != -1) {
            renderer.materials[setImageMaterialIndex].mainTexture = texture;
            Debug.Log($"应用纹理到卡片 {tileId} 的SetImage材质完成");
        } else {
            Debug.LogError($"未找到SetImage材质，材质列表: {string.Join(", ", System.Array.ConvertAll(renderer.materials, m => m.name))}");
        }
    }
}