using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class StaticCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
    [SerializeField] private Image tileImage;
    
    private int tileId = -1; // 当前卡牌的tileId

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void SetTileOnlyImage(int tile) {
        tileId = tile;
        string path = $"image/CardFaceImage_xuefun/{tile}";
        Sprite sprite = Resources.Load<Sprite>(path);
        if (sprite != null) {
            tileImage.sprite = sprite;
            Debug.Log($"成功加载静态卡片图片: {path}");
        } else {
            Debug.LogError($"找不到牌面图片: {path}");
        }
    }

    /// <summary>
    /// 鼠标进入时，高亮所有相同tileId的3D卡牌
    /// </summary>
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (tileId != -1 && Card3DHoverManager.Instance != null)
        {
            Card3DHoverManager.Instance.OnCardHover(tileId);
        }
    }

    /// <summary>
    /// 鼠标离开时，恢复所有3D卡牌
    /// </summary>
    public void OnPointerExit(PointerEventData eventData)
    {
        if (Card3DHoverManager.Instance != null)
        {
            Card3DHoverManager.Instance.OnCardExit();
        }
    }
}
