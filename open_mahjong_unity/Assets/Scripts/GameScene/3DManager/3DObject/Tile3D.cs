using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 3D麻将牌组件
/// 负责管理3D卡牌的纹理和材质属性
/// </summary>
public class Tile3D : MonoBehaviour
{
    private Renderer cardRenderer;
    private Material targetMaterial;
    private int currentTileId = -1;

    private void Awake()
    {
        // 设置游戏对象到图层 10 (outline)
        const int outlineLayer = 10;
        gameObject.layer = outlineLayer;

        // 获取渲染器组件
        cardRenderer = GetComponent<Renderer>();
        if (cardRenderer == null)
        {
            Debug.LogError("Tile3D: 未找到Renderer组件");
            return;
        }

        // 查找使用ThreeDTiles着色器的材质
        for (int i = 0; i < cardRenderer.materials.Length; i++)
        {
            if (cardRenderer.materials[i].shader.name == "Custom/ThreeDTiles")
            {
                targetMaterial = cardRenderer.materials[i];
                break;
            }
        }

        if (targetMaterial == null)
        {
            Debug.LogError($"Tile3D: 未找到ThreeDTiles着色器材质，材质列表: {string.Join(", ", System.Array.ConvertAll(cardRenderer.materials, m => m.shader.name))}");
        }
    }

    /// <summary>
    /// 设置牌面纹理
    /// </summary>
    /// <param name="tileId">牌的ID</param>
    /// <param name="texture">预加载的纹理（可选，如果提供则直接使用，避免 Resources.Load）</param>
    public void SetCardTexture(int tileId, Texture2D texture = null)
    {
        if (targetMaterial == null)
        {
            Debug.LogError("Tile3D: 目标材质为空，无法设置纹理");
            return;
        }

        currentTileId = tileId;

        // 如果传入了纹理，直接使用；否则从 Resources 加载
        if (texture == null)
        {
            texture = Resources.Load<Texture2D>($"image/CardFaceMaterial_xuefun/{tileId}");
            if (texture == null)
            {
                Debug.LogError($"Tile3D: 无法加载纹理: image/CardFaceMaterial_xuefun/{tileId}");
                return;
            }
        }

        // 设置_FrontTex属性，图片将覆盖在uv_FrontTex UV通道上
        targetMaterial.SetTexture("_FrontTex", texture);
        
        // 只在编辑器模式下输出日志，避免运行时日志刷屏
        #if UNITY_EDITOR
        Debug.Log($"Tile3D: 应用纹理到卡片 {tileId} 的_FrontTex属性完成");
        #endif
    }

    /// <summary>
    /// 获取当前牌的ID
    /// </summary>
    public int GetTileId()
    {
        return currentTileId;
    }

    /// <summary>
    /// 获取目标材质（用于Card3DHoverManager访问材质属性）
    /// </summary>
    public Material GetMaterial()
    {
        return targetMaterial;
    }
}
