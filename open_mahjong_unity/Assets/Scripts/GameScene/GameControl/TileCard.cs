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

    private void OnEnable()
    {
        // Unity 的 EventSystem 在“物体出现在鼠标下方”时不会自动触发 OnPointerEnter。
        // 这里做一次主动检测，确保提示能立刻出现。
        StartCoroutine(CheckHoverOnEnableNextFrame());
    }

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
        if (NormalGameStateManager.Instance.allowActionList.Contains("cut")){
            int cutIndex = transform.GetSiblingIndex();// 获取切牌是父物体的第几个子物体
            GameStateNetworkManager.Instance.SendChineseGameTile(currentGetTile,tileId,cutIndex); // 发送切牌请求
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
        // 直接隐藏提示容器（内部会先清空内容）
        TipsContainer.Instance.HideTips();
    }
    
    /// <summary>
    /// 检测切牌后的听牌提示
    /// </summary>
    private void CheckCutTileTips()
    {
        // 检查是否开启了提示功能
        if (!NormalGameStateManager.Instance.tips){
            return;
        }
        
        // 检查是否有切牌权限
        if (!NormalGameStateManager.Instance.allowActionList.Contains("cut")){
            return;
        }
        
        // 临时移除当前牌，进行听牌检测
        List<int> tempHandTiles = new List<int>(NormalGameStateManager.Instance.selfHandTiles);
        tempHandTiles.Remove(tileId);
        
        // 执行听牌检测
        HashSet<int> waitingTiles = new HashSet<int>();
        try
        {
            if (NormalGameStateManager.Instance.roomType == "guobiao"){
                waitingTiles = GBtingpai.TingpaiCheck(
                    tempHandTiles,
                    NormalGameStateManager.Instance.player_to_info["self"].combination_tiles,
                    false
                );
            }
            else if (NormalGameStateManager.Instance.roomType == "qingque"){
                waitingTiles = Qingque13External.TingpaiCheck(
                    tempHandTiles,
                    NormalGameStateManager.Instance.player_to_info["self"].combination_tiles ?? new List<string>(),
                    false
                );
            }
            else
            {
                Debug.LogWarning($"未知的规则类型: {NormalGameStateManager.Instance.roomType}");
                waitingTiles = new HashSet<int>();
            }
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
            // 这里传入“切掉当前牌后的手牌”tempHandTiles，避免多算一张牌
            TipsContainer.Instance.SetTipsWithHand(tempHandTiles, waitingTiles.ToList());
            TipsContainer.Instance.hasTips = true;
            TipsContainer.Instance.ShowTips();
        }
        else
        {
            Debug.Log($"切牌后无听牌");
            TipsContainer.Instance.hasTips = false;
            TipsContainer.Instance.HideTips();
        }
    }

    private System.Collections.IEnumerator CheckHoverOnEnableNextFrame()
    {
        // 等一帧，保证 UI 布局/RectTransform 已就绪，否则射线检测可能拿到旧位置
        yield return null;

        if (!isActiveAndEnabled) yield break;
        if (EventSystem.current == null) yield break;

        // 构造一次 PointerEventData，进行 UI Raycast
        PointerEventData pointerData = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);

        // 找到射线命中的第一个 TileCard 是否就是自己（或自己的子物体）
        foreach (var r in results)
        {
            if (r.gameObject == null) continue;
            if (r.gameObject == gameObject || r.gameObject.transform.IsChildOf(transform))
            {
                isHovering = true;
                CheckCutTileTips();
                break;
            }
        }
    }

    private void OnDestroy()
    {
        tileButton.onClick.RemoveListener(OnTileClick);
        TipsContainer.Instance.HideTips();
    }
} 