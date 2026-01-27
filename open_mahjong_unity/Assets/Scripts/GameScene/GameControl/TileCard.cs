using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 麻将牌UI组件
/// 层级结构：
/// TileCard (空物体)
/// ├── TileImage (Image组件)
/// └── TileButton (Button组件)
/// </summary>
public class TileCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
    [Header("UI Components")]
    [SerializeField] private Image tileImage;    // 牌面图片组件
    [SerializeField] private Button tileButton;  // 按钮组件

    // 将私有字段改为公共属性
    public int tileId { get; private set; }    // 牌的ID（如"11"表示一万）
    public bool currentGetTile;   // 是否是当前摸到的牌
    
    private bool isHovering = false; // 是否正在悬停

    private void Start(){
        // 添加按钮点击监听
        tileButton.onClick.AddListener(OnTileClick);
    }

    /// <summary>
    /// 设置牌面图片
    /// </summary>
    public void SetTile(int id,bool isCurrentGetTile) {
        tileId = id;
        currentGetTile = isCurrentGetTile;
        
        // 不需要添加扩展名
        string path = $"image/CardFaceImage_xuefun/{id}";
        Sprite sprite = Resources.Load<Sprite>(path);
        
        if (sprite != null) {
            tileImage.sprite = sprite;
        } else {
            Debug.LogError($"找不到牌面图片: {path}");
        }
    }

    /// OntileClick 是出牌方法 如果牌属性currentGetTile为flase则为手切，如果为true则为摸切
    private void OnTileClick()
    {
        Debug.Log($"点击了牌: {tileId},{currentGetTile}");
        // 如果切牌在允许操作列表中
        if (GameSceneManager.Instance.allowActionList.Contains("cut")){
            int cutIndex = transform.GetSiblingIndex();// 获取切牌是父物体的第几个子物体
            NetworkManager.Instance.SendChineseGameTile(currentGetTile,tileId,cutIndex); // 发送切牌请求
        } else {
            Debug.Log("没有权限出牌");
        }
    }

    /// <summary>
    /// 公共方法：触发出牌（用于自动出牌功能）
    /// </summary>
    public void TriggerClick() {
        OnTileClick();
    }
    
    /// <summary>
    /// 鼠标进入时检测切牌后的听牌
    /// </summary>
    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovering = true;
        // 异步检测切牌后的听牌
        CheckCutTileTips();
    }
    
    /// <summary>
    /// 鼠标离开时隐藏提示
    /// </summary>
    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
        // 直接隐藏提示容器
        TipsContainer.Instance.gameObject.SetActive(false);
    }
    
    /// <summary>
    /// 检测切牌后的听牌提示
    /// </summary>
    private void CheckCutTileTips()
    {
        // 检查是否开启了提示功能
        if (!GameSceneManager.Instance.tips){
            return;
        }
        
        // 检查是否有切牌权限
        if (!GameSceneManager.Instance.allowActionList.Contains("cut")){
            return;
        }
        
        // 临时移除当前牌，进行听牌检测
        List<int> tempHandTiles = new List<int>(GameSceneManager.Instance.selfHandTiles);
        tempHandTiles.Remove(tileId);
        
        // 直接在主线程执行听牌检测（避免 WebGL 平台 Task.Run 线程问题）
        HashSet<int> waitingTiles;
        try
        {
            waitingTiles = GBtingpai.TingpaiCheck(
                tempHandTiles,
                GameSceneManager.Instance.player_to_info["self"].combination_tiles,
                false
            );
        }
        catch (System.Exception e)
        {
            Debug.LogError($"检测切牌提示时出错: {e.Message}");
            waitingTiles = new HashSet<int>();
        }
        
        // 检查是否还在悬停状态（避免异步返回时已经离开）
        if (!isHovering)
        {
            return;
        }
        
        // 如果听牌列表不为空，则显示提示
        if (waitingTiles.Count > 0)
        {
            Debug.Log($"显示切牌提示，听牌列表数量：{waitingTiles.Count}");
            TipsContainer.Instance.SetTips(waitingTiles.ToList());
            TipsContainer.Instance.hasTips = true;
            TipsContainer.Instance.gameObject.SetActive(true);
        }
        else
        {
            Debug.Log($"切牌后无听牌");
            TipsContainer.Instance.hasTips = false;
            TipsContainer.Instance.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        tileButton.onClick.RemoveListener(OnTileClick);
        TipsContainer.Instance.gameObject.SetActive(false);
    }
} 