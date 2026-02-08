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
    [SerializeField, Range(0f, 1f)] private float hoverIntensity = 0.3f; // 0-1，越大越接近 hoverColor

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
                if (data.material.HasProperty("_FrontColor")) {
                    Color c = Color.Lerp(data.originalFrontColor, hoverColor, hoverIntensity);
                    c.a = data.originalFrontColor.a;
                    data.material.SetColor("_FrontColor", c);
                }
                if (data.material.HasProperty("_BackColor")) {
                    Color c = Color.Lerp(data.originalBackColor, hoverColor, hoverIntensity);
                    c.a = data.originalBackColor.a;
                    data.material.SetColor("_BackColor", c);
                }
                if (data.material.HasProperty("_SideColor")) {
                    Color c = Color.Lerp(data.originalSideColor, hoverColor, hoverIntensity);
                    c.a = data.originalSideColor.a;
                    data.material.SetColor("_SideColor", c);
                }
            }
        }
    }

    /// <summary>
    /// 恢复指定tileId的所有卡牌到原始状态
    /// </summary>
    private void RestoreCards(int tileId) {
        if (!tileIdToCards.ContainsKey(tileId)) return;
        foreach (GameObject cardObj in tileIdToCards[tileId]) {
            if (cardMaterialData.ContainsKey(cardObj)) {
                CardMaterialData data = cardMaterialData[cardObj];
                if (data.material.HasProperty("_FrontColor")) {
                    data.material.SetColor("_FrontColor", data.originalFrontColor);
                }
                if (data.material.HasProperty("_BackColor")) {
                    data.material.SetColor("_BackColor", data.originalBackColor);
                }
                if (data.material.HasProperty("_SideColor")) {
                    data.material.SetColor("_SideColor", data.originalSideColor);
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

