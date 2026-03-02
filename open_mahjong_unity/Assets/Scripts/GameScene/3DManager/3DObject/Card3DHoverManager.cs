using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 管理3D卡牌的鼠标悬停效果
/// 当鼠标悬停在StaticCard上时，将所有相同tileId的3D卡牌设置为半透明或变灰
/// </summary>
public class Card3DHoverManager : MonoBehaviour
{
    public static Card3DHoverManager Instance { get; private set; }

    // 存储每个tileId对应的所有3D卡牌GameObject
    private Dictionary<int, List<GameObject>> tileIdToCards = new Dictionary<int, List<GameObject>>();

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

    // 存储每个卡牌的原始材质属性
    private Dictionary<GameObject, CardMaterialData> cardMaterialData = new Dictionary<GameObject, CardMaterialData>();

    private class CardMaterialData
    {
        public Material material;
        public float originalAlpha;
        public float originalGrayScale;
        public Color originalFrontColor;
        public Color originalBackColor;
        public Color originalSideColor;
        // 灰色叠加（独立于 original，不会污染原始值）
        public bool hasGrayOverlay;
        public Color grayOverlayColor;
        public float grayOverlayIntensity;
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
        if (!tileIdToCards.ContainsKey(tileId)) {
            tileIdToCards[tileId] = new List<GameObject>();
        }
        if (!tileIdToCards[tileId].Contains(cardObj)) {
            tileIdToCards[tileId].Add(cardObj);
            
            Renderer renderer = cardObj.GetComponent<Renderer>();
            Material mat = renderer.materials[0];
            if (mat.shader.name == "Custom/ThreeDTiles") {
                CardMaterialData data = new CardMaterialData {
                    material = mat,
                    originalAlpha = mat.HasProperty("_Alpha") ? mat.GetFloat("_Alpha") : 1.0f,
                    originalGrayScale = mat.HasProperty("_GrayScale") ? mat.GetFloat("_GrayScale") : 0.0f,
                    originalFrontColor = mat.HasProperty("_FrontColor") ? mat.GetColor("_FrontColor") : Color.white,
                    originalBackColor = mat.HasProperty("_BackColor") ? mat.GetColor("_BackColor") : Color.white,
                    originalSideColor = mat.HasProperty("_SideColor") ? mat.GetColor("_SideColor") : Color.white
                };
                cardMaterialData[cardObj] = data;
            }
        }
    }

    /// <summary>
    /// 取消注册一个3D卡牌
    /// </summary>
    public void UnregisterCard(GameObject cardObj, int tileId) {
        if (tileIdToCards.ContainsKey(tileId)) {
            tileIdToCards[tileId].Remove(cardObj);
            if (tileIdToCards[tileId].Count == 0) {
                tileIdToCards.Remove(tileId);
            }
        }
        cardMaterialData.Remove(cardObj);
    }

    /// <summary>
    /// 归还对象池前调用：恢复材质到注册时的真实原始值，并取消注册
    /// </summary>
    public void ResetAndUnregisterCard(GameObject cardObj) {
        if (cardMaterialData.ContainsKey(cardObj)) {
            CardMaterialData data = cardMaterialData[cardObj];
            if (data.material.HasProperty("_FrontColor")) data.material.SetColor("_FrontColor", data.originalFrontColor);
            if (data.material.HasProperty("_BackColor")) data.material.SetColor("_BackColor", data.originalBackColor);
            if (data.material.HasProperty("_SideColor")) data.material.SetColor("_SideColor", data.originalSideColor);
            if (data.material.HasProperty("_Alpha")) data.material.SetFloat("_Alpha", data.originalAlpha);
            if (data.material.HasProperty("_GrayScale")) data.material.SetFloat("_GrayScale", data.originalGrayScale);
        }
        int tileId = -1;
        foreach (var kvp in tileIdToCards) {
            if (kvp.Value.Contains(cardObj)) {
                tileId = kvp.Key;
                break;
            }
        }
        if (tileId >= 0) {
            UnregisterCard(cardObj, tileId);
        } else {
            cardMaterialData.Remove(cardObj);
        }
    }

    /// <summary>
    /// 当鼠标悬停在某个tileId的卡牌上时调用
    /// </summary>
    public void OnCardHover(int tileId) {
        if (currentHoveredTileId == tileId) return;
        if (currentHoveredTileId != -1) {
            RestoreCards(currentHoveredTileId);
        }
        currentHoveredTileId = tileId;
        if (tileIdToCards.ContainsKey(tileId)) {
            SetCardsHovered(tileId);
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
                CardMaterialData data = cardMaterialData[cardObj];
                Color baseFront = GetBaseColor(data.originalFrontColor, data);
                Color baseBack = GetBaseColor(data.originalBackColor, data);
                Color baseSide = GetBaseColor(data.originalSideColor, data);
                if (data.material.HasProperty("_FrontColor")) {
                    Color c = Color.Lerp(baseFront, hoverColor, hoverIntensity);
                    c.a = baseFront.a;
                    data.material.SetColor("_FrontColor", c);
                }
                if (data.material.HasProperty("_BackColor")) {
                    Color c = Color.Lerp(baseBack, hoverColor, hoverIntensity);
                    c.a = baseBack.a;
                    data.material.SetColor("_BackColor", c);
                }
                if (data.material.HasProperty("_SideColor")) {
                    Color c = Color.Lerp(baseSide, hoverColor, hoverIntensity);
                    c.a = baseSide.a;
                    data.material.SetColor("_SideColor", c);
                }
            }
        }
    }

    /// <summary>
    /// 获取基础显示颜色：原始色 + 灰色叠加（如有）
    /// </summary>
    private static Color GetBaseColor(Color originalColor, CardMaterialData data) {
        if (!data.hasGrayOverlay) return originalColor;
        Color c = Color.Lerp(originalColor, data.grayOverlayColor, data.grayOverlayIntensity);
        c.a = originalColor.a;
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
                if (data.material.HasProperty("_FrontColor")) {
                    data.material.SetColor("_FrontColor", GetBaseColor(data.originalFrontColor, data));
                }
                if (data.material.HasProperty("_BackColor")) {
                    data.material.SetColor("_BackColor", GetBaseColor(data.originalBackColor, data));
                }
                if (data.material.HasProperty("_SideColor")) {
                    data.material.SetColor("_SideColor", GetBaseColor(data.originalSideColor, data));
                }
                if (data.material.HasProperty("_Alpha")) {
                    data.material.SetFloat("_Alpha", data.originalAlpha);
                }
                if (data.material.HasProperty("_GrayScale")) {
                    data.material.SetFloat("_GrayScale", data.originalGrayScale);
                }
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
        if (data.material.HasProperty("_FrontColor")) {
            data.material.SetColor("_FrontColor", GetBaseColor(data.originalFrontColor, data));
        }
        if (data.material.HasProperty("_BackColor")) {
            data.material.SetColor("_BackColor", GetBaseColor(data.originalBackColor, data));
        }
        if (data.material.HasProperty("_SideColor")) {
            data.material.SetColor("_SideColor", GetBaseColor(data.originalSideColor, data));
        }
    }

    /// <summary>
    /// 清理所有注册的卡牌
    /// </summary>
    public void ClearAllCards() {
        foreach (var kvp in tileIdToCards) {
            RestoreCards(kvp.Key);
        }
        tileIdToCards.Clear();
        cardMaterialData.Clear();
        currentHoveredTileId = -1;
    }

    private void OnDestroy() {
        ClearAllCards();
    }
}

