using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 管理3D卡牌的鼠标悬停 / 摸切灰显 / 铳牌提示效果（颜色叠加，非透明混合）。
/// </summary>
public class Card3DHoverManager : MonoBehaviour
{
    public static Card3DHoverManager Instance { get; private set; }

    // 存储每个tileId对应的所有3D卡牌GameObject
    private Dictionary<int, List<GameObject>> tileIdToCards = new Dictionary<int, List<GameObject>>();
    // 反向索引：归还/注销时 O(1) 查 tileId，避免扫全表
    private Dictionary<GameObject, int> cardToTileId = new Dictionary<GameObject, int>();

    // 当前悬停的tileId
    private int currentHoveredTileId = -1;

    // 悬停时的蓝色高亮（不改变透明度，仅轻微偏蓝）
    [SerializeField] private Color hoverColor = new Color(0.7f, 0.85f, 1f, 1f);
    [SerializeField, Range(0f, 1f)] private float hoverIntensity = 0.3f;

    [Header("牌谱摸切灰色叠加")]
    [SerializeField] private Color moqieOverlayColor = new Color(0.5f, 0.5f, 0.5f);
    [SerializeField, Range(0f, 1f)] private float moqieOverlayIntensity = 0.45f;

    public Color MoqieOverlayColor => moqieOverlayColor;
    public float MoqieOverlayIntensity => moqieOverlayIntensity;

    [Header("牌谱铳牌红色叠加")]
    [SerializeField] private Color dangerOverlayColor = new Color(1f, 0.2f, 0.2f);
    [SerializeField, Range(0f, 1f)] private float dangerOverlayIntensity = 0.45f;

    public Color DangerOverlayColor => dangerOverlayColor;
    public float DangerOverlayIntensity => dangerOverlayIntensity;

    // 存储每个卡牌的实例视觉状态；不持有或修改材质实例。
    private Dictionary<GameObject, CardMaterialData> cardMaterialData = new Dictionary<GameObject, CardMaterialData>();

    private class CardMaterialData
    {
        public Tile3D tile3D;
        public float originalGrayScale;
        public Color originalFrontColor;
        public Color originalBackColor;
        public Color originalSideColor;
        // 灰色叠加（独立于 original，不会污染原始值）
        public bool hasGrayOverlay;
        public Color grayOverlayColor;
        public float grayOverlayIntensity;
        public bool hasDangerOverlay;
        public Color dangerOverlayColor;
        public float dangerOverlayIntensity;
    }

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// 注册一个3D卡牌
    /// </summary>
    public void RegisterCard(GameObject cardObj, int tileId) {
        int key = TileIdOrder.Normalize(tileId);
        if (!tileIdToCards.ContainsKey(key)) {
            tileIdToCards[key] = new List<GameObject>();
        }
        if (!tileIdToCards[key].Contains(cardObj)) {
            tileIdToCards[key].Add(cardObj);
            cardToTileId[cardObj] = key;

            Tile3D tile3D = cardObj.GetComponent<Tile3D>();
            if (tile3D != null) {
                CardMaterialData data = new CardMaterialData {
                    tile3D = tile3D,
                    originalGrayScale = tile3D.BaseGrayScale,
                    originalFrontColor = tile3D.BaseFrontColor,
                    originalBackColor = tile3D.BaseBackColor,
                    originalSideColor = tile3D.BaseSideColor
                };
                cardMaterialData[cardObj] = data;
            }
        }
    }

    /// <summary>
    /// 取消注册一个3D卡牌
    /// </summary>
    public void UnregisterCard(GameObject cardObj, int tileId) {
        int key = TileIdOrder.Normalize(tileId);
        if (tileIdToCards.ContainsKey(key)) {
            tileIdToCards[key].Remove(cardObj);
            if (tileIdToCards[key].Count == 0) {
                tileIdToCards.Remove(key);
            }
        }
        cardToTileId.Remove(cardObj);
        cardMaterialData.Remove(cardObj);
    }

    /// <summary>
    /// 归还对象池前调用：恢复材质到注册时的真实原始值，并取消注册
    /// </summary>
    public void ResetAndUnregisterCard(GameObject cardObj) {
        if (cardMaterialData.ContainsKey(cardObj)) {
            CardMaterialData data = cardMaterialData[cardObj];
            data.tile3D?.ResetInstanceVisualState();
        }
        else {
            Tile3D tile3D = cardObj != null ? cardObj.GetComponent<Tile3D>() : null;
            tile3D?.ResetInstanceVisualState();
        }
        if (cardToTileId.TryGetValue(cardObj, out int tileId)) {
            UnregisterCard(cardObj, tileId);
        } else {
            cardMaterialData.Remove(cardObj);
        }
    }

    /// <summary>
    /// 当鼠标悬停在某个tileId的卡牌上时调用
    /// </summary>
    public void OnCardHover(int tileId) {
        int key = TileIdOrder.Normalize(tileId);
        if (currentHoveredTileId == key) return;
        if (currentHoveredTileId != -1) {
            RestoreCards(currentHoveredTileId);
        }
        currentHoveredTileId = key;
        if (tileIdToCards.ContainsKey(key)) {
            SetCardsHovered(key);
        }
    }

    /// <summary>
    /// 当鼠标离开卡牌时调用
    /// </summary>
    public void OnCardExit() {
        if (currentHoveredTileId != -1) {
            RestoreCards(currentHoveredTileId);
            currentHoveredTileId = -1;
        }
    }

    /// <summary>
    /// 设置指定tileId的所有卡牌为悬停状态
    /// </summary>
    private void SetCardsHovered(int tileId) {
        if (!tileIdToCards.ContainsKey(tileId)) return;
        foreach (GameObject cardObj in tileIdToCards[tileId]) {
            if (cardMaterialData.ContainsKey(cardObj)) {
                ApplyCardVisual(cardMaterialData[cardObj], true);
            }
        }
    }

    /// <summary>
    /// 获取基础显示颜色：原始色 + 灰色叠加（如有）
    /// </summary>
    private static Color GetBaseColor(Color originalColor, CardMaterialData data) {
        Color c = originalColor;
        if (data.hasGrayOverlay) {
            c = Color.Lerp(c, data.grayOverlayColor, data.grayOverlayIntensity);
            c.a = originalColor.a;
        }
        if (data.hasDangerOverlay) {
            c = Color.Lerp(c, data.dangerOverlayColor, data.dangerOverlayIntensity);
            c.a = originalColor.a;
        }
        return c;
    }

    /// <summary>
    /// 恢复指定tileId的所有卡牌到原始状态（含灰色叠加）
    /// </summary>
    private void RestoreCards(int tileId) {
        if (!tileIdToCards.ContainsKey(tileId)) return;
        foreach (GameObject cardObj in tileIdToCards[tileId]) {
            if (cardMaterialData.ContainsKey(cardObj)) {
                CardMaterialData data = cardMaterialData[cardObj];
                ApplyCardVisual(data, false);
            }
        }
    }

    /// <summary>
    /// 对指定卡牌应用灰色叠加（混合 overlayColor），不修改保存的原始颜色值
    /// </summary>
    public void SetCardGrayOverlay(GameObject cardObj, Color overlayColor, float intensity) {
        if (!cardMaterialData.ContainsKey(cardObj)) return;
        CardMaterialData data = cardMaterialData[cardObj];
        data.hasGrayOverlay = true;
        data.grayOverlayColor = overlayColor;
        data.grayOverlayIntensity = intensity;
        ApplyCardBaseColors(cardObj, data);
    }

    public void SetCardDangerOverlay(GameObject cardObj, Color overlayColor, float intensity) {
        if (!cardMaterialData.ContainsKey(cardObj)) return;
        CardMaterialData data = cardMaterialData[cardObj];
        data.hasDangerOverlay = true;
        data.dangerOverlayColor = overlayColor;
        data.dangerOverlayIntensity = intensity;
        ApplyCardBaseColors(cardObj, data);
    }

    public void ClearCardDangerOverlay(GameObject cardObj) {
        if (!cardMaterialData.ContainsKey(cardObj)) return;
        CardMaterialData data = cardMaterialData[cardObj];
        if (!data.hasDangerOverlay) return;
        data.hasDangerOverlay = false;
        ApplyCardBaseColors(cardObj, data);
    }

    public void ClearAllDangerOverlays() {
        foreach (var kvp in cardMaterialData) {
            CardMaterialData data = kvp.Value;
            if (!data.hasDangerOverlay) continue;
            data.hasDangerOverlay = false;
            ApplyCardBaseColors(kvp.Key, data);
        }
        if (currentHoveredTileId != -1) {
            SetCardsHovered(currentHoveredTileId);
        }
    }

    private void ApplyCardBaseColors(GameObject cardObj, CardMaterialData data) {
        bool hovered = currentHoveredTileId != -1
            && cardToTileId.TryGetValue(cardObj, out int tileId)
            && tileId == currentHoveredTileId;
        ApplyCardVisual(data, hovered);
    }

    private void ApplyCardVisual(CardMaterialData data, bool hovered) {
        Tile3D tile3D = data.tile3D;
        if (tile3D == null) return;
        Color origFront = tile3D.BaseFrontColor;
        Color origBack = tile3D.BaseBackColor;
        Color origSide = tile3D.BaseSideColor;
        Color front = GetBaseColor(origFront, data);
        Color back = GetBaseColor(origBack, data);
        Color side = GetBaseColor(origSide, data);
        if (hovered) {
            front = Color.Lerp(front, hoverColor, hoverIntensity);
            back = Color.Lerp(back, hoverColor, hoverIntensity);
            side = Color.Lerp(side, hoverColor, hoverIntensity);
            front.a = origFront.a;
            back.a = origBack.a;
            side.a = origSide.a;
        }
        tile3D.SetInstanceVisualState(
            front, back, side, tile3D.BaseGrayScale);
    }

    /// <summary>
    /// 清理所有注册的卡牌
    /// </summary>
    public void ClearAllCards() {
        foreach (var kvp in tileIdToCards) {
            RestoreCards(kvp.Key);
        }
        tileIdToCards.Clear();
        cardToTileId.Clear();
        cardMaterialData.Clear();
        currentHoveredTileId = -1;
    }

    private void OnDestroy() {
        ClearAllCards();
    }
}
