using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 麻将牌UI组件
/// 层级结构：
/// TileCard (空物体)
/// ├── TileImage (Image组件)
/// └── TileButton (Button组件)
/// </summary>
public class TileCard : MonoBehaviour
{
    [Header("UI Components")]
    [SerializeField] private Image tileImage;    // 牌面图片组件
    [SerializeField] private Button tileButton;  // 按钮组件

    // 将私有字段改为公共属性
    public int tileId { get; private set; }    // 牌的ID（如"11"表示一万）
    private bool currentGetTile;   // 是否是当前摸到的牌

    private void Start()
    {
        // 添加按钮点击监听
        tileButton.onClick.AddListener(OnTileClick);
    }

    /// <summary>
    /// 设置牌面图片
    /// </summary>
    public void SetTile(int id,bool isCurrentGetTile)
    {
        tileId = id;
        currentGetTile = isCurrentGetTile;
        
        // 不需要添加扩展名
        string path = $"image/ChineseCard/{id}";
        Sprite sprite = Resources.Load<Sprite>(path);
        
        if (sprite != null)
        {
            tileImage.sprite = sprite;
            Debug.Log($"成功加载图片: {path}");
        }
        else
        {
            Debug.LogError($"找不到牌面图片: {path}");
        }
    }

    /// OntileClick 是出牌方法 如果牌属性currentGetTile为flase则为手切，如果为true则为摸切
    private void OnTileClick()
    {
        Debug.Log($"点击了牌: {tileId},{currentGetTile}");
        // TODO: 添加选中效果或其他交互
        if (GameSceneManager.Instance.NowCurrentIndex == GameSceneManager.Instance.selfCurrentIndex && 
            GameSceneManager.Instance.CutAction == true){
            if (currentGetTile){
                NetworkManager.Instance.SendChineseGameTile(currentGetTile,tileId,Administrator.Instance.room_id);
            }
            else{
                NetworkManager.Instance.SendChineseGameTile(currentGetTile,tileId,Administrator.Instance.room_id);
            }
            GameSceneManager.Instance.CutCards(tileId,GameSceneManager.Instance.NowCurrentIndex,currentGetTile);
            Destroy(gameObject);
        }
        else{
            Debug.Log("没有权限出牌");
        }
    }

    private void OnDestroy()
    {
        tileButton.onClick.RemoveListener(OnTileClick);
    }
} 