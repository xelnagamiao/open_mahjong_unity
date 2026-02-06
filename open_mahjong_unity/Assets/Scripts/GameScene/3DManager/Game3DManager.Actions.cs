using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class Game3DManager : MonoBehaviour
{
    // 鸣牌3D显示
    public IEnumerator ActionAnimationCoroutine(string playerIndex, string actionType, int[] combination_mask, bool doAnimation = false)
    {
        // 根据actionType执行动画
        Quaternion rotation = Quaternion.identity; // 卡牌旋转角度
        Vector3 SetDirection = Vector3.zero; // 放置方向
        Vector3 SetPositionpoint = Vector3.zero; // 放置位置
        Vector3 JiagangDirection = Vector3.zero; // 加杠方向
        Transform SetParent = null; // 设置父对象
        PosPanel3D panel = GetPosPanel(playerIndex);
        if (panel == null) yield break;
        
        if (playerIndex == "self")
        {
            rotation = Quaternion.Euler(90, 0, 180); // 获取卡牌旋转角度
            SetDirection = LeftDirection; // 获取放置方向 向左
            JiagangDirection = FrontDirection; // 自家加杠指针是向前
            SetPositionpoint = selfSetCombinationsPoint; // 获取放置指针
            // 获取父对象 父对象 = 玩家组合数 => 玩家组合父对象列表
            SetParent = panel.combination3DObjects[NormalGameStateManager.Instance.player_to_info["self"].combination_tiles.Count - 1];
        }
        else if (playerIndex == "left")
        {
            rotation = Quaternion.Euler(90, 0, 90); // 左侧玩家
            SetDirection = FrontDirection; // 向前
            JiagangDirection = RightDirection; // 左侧玩家加杠指针是向右
            SetPositionpoint = leftSetCombinationsPoint;
            SetParent = panel.combination3DObjects[NormalGameStateManager.Instance.player_to_info["left"].combination_tiles.Count - 1];
        }
        else if (playerIndex == "top")
        {
            rotation = Quaternion.Euler(90, 0, 0); // 上方玩家
            SetDirection = RightDirection; // 向右
            JiagangDirection = BackDirection; // 上方玩家加杠指针是向后
            SetPositionpoint = topSetCombinationsPoint;
            SetParent = panel.combination3DObjects[NormalGameStateManager.Instance.player_to_info["top"].combination_tiles.Count - 1];
        }
        else if (playerIndex == "right")
        {
            rotation = Quaternion.Euler(90, 0, 270); // 右侧玩家
            SetDirection = BackDirection; // 向后
            JiagangDirection = LeftDirection; // 右侧玩家加杠指针是向左
            SetPositionpoint = rightSetCombinationsPoint;
            SetParent = panel.combination3DObjects[NormalGameStateManager.Instance.player_to_info["right"].combination_tiles.Count - 1];
        }

        // 获取了rotation(卡牌旋转角度) SetDirection(放置方向) 以及公共变量 $SetCombinationsPoint
        List<int> SetTileList = new List<int>();
        List<int> SignDirectionList = new List<int>();

        // 解码combination_mask 获得需要放置的卡牌列表和卡牌朝向列表
        foreach (int tileId in combination_mask)
        {
            if (tileId >= 10)
            {
                SetTileList.Add(tileId);
            }
            else if (tileId < 5)
            {
                SignDirectionList.Add(tileId);
            }
        }
        // 倒转SetTileList和SignDirectionList 因为卡牌的逻辑顺序是从左到右，但我们需要从右到左放置
        SetTileList.Reverse();
        SignDirectionList.Reverse();
        Debug.Log($"actionType: {actionType}, combination_mask: {combination_mask}, SetTileList: {SetTileList}, SignDirectionList: {SignDirectionList}");

        // 等待一帧，避免与其他操作在同一帧执行
        yield return null;

        // 执行动画
        // 加杠
        if (actionType == "jiagang")
        {
            for (int i = 0; i < SetTileList.Count; i++)
            {
                if (SignDirectionList[i] == 3)
                {
                    GameObject cardObj;
                    Vector3 TempPositionpoint = pengToJiagangPosDict[SetTileList[i]]; // 获取加杠位置
                    Quaternion TempRotation = Quaternion.Euler(0, 90, 0) * rotation; // 横
                    TempPositionpoint += JiagangDirection * cardWidth; // 加杠向上一个宽度单位
                    cardObj = Instantiate(tile3DPrefab, TempPositionpoint, TempRotation);
                    ApplyCardTexture(cardObj, SetTileList[i]);
                    // 注册到悬停管理器
                    if (Card3DHoverManager.Instance != null)
                    {
                        Card3DHoverManager.Instance.RegisterCard(cardObj, SetTileList[i]);
                    }
                    // 设置父对象
                    cardObj.transform.SetParent(SetParent, worldPositionStays: true);
                    // 加杠动画：将加杠牌移动到3个卡牌宽度以左，然后移回原位
                    if (doAnimation)
                    {
                        StartCoroutine(MoveCardAnimation(cardObj, SetDirection, cardWidth));
                    }
                    
                    // 每创建一张卡牌等待一帧，避免单帧创建太多对象
                    if (i < SetTileList.Count - 1)
                    {
                        yield return null;
                    }
                }
            }
        }
        // 正常放置卡牌
        else
        {
            for (int i = 0; i < SetTileList.Count; i++)
            {
                GameObject cardObj;
                Quaternion TempRotation = Quaternion.identity;
                Vector3 TempPositionpoint = SetPositionpoint;
                // 0代表竖 1代表横 2代表暗面 3代表上侧(加杠) 4代表空
                // 卡牌竖置 指针增加一个宽度单位
                if (SignDirectionList[i] == 0)
                {
                    TempRotation = rotation; // 竖
                    TempPositionpoint += SetDirection * cardWidth;
                    SetPositionpoint += SetDirection * cardWidth; // 吃碰的竖置牌每张牌向左一个宽度单位
                }
                // 卡牌横置,放置角度叠加横置 指针增加一个高度单位
                else if (SignDirectionList[i] == 1)
                {
                    TempRotation = Quaternion.Euler(0, 90, 0) * rotation; // 横
                    SetPositionpoint += SetDirection * cardHeight; // 指针移动
                    TempPositionpoint += SetDirection; // 保存当前放置位置（基础偏移）
                    // 沿 SetDirection 反向偏移 0.4 高度，沿 JiagangDirection 反向偏移 0.5 宽度
                    TempPositionpoint += (-SetDirection) * 0.4f * cardHeight;
                    TempPositionpoint += (-JiagangDirection) * 0.5f * cardWidth;
                    if (actionType == "peng")
                    {
                        pengToJiagangPosDict.Add(SetTileList[i], TempPositionpoint); // 碰牌的加杠预留指针 保存在碰牌int id的横置位置
                    }
                }
                // 卡牌暗面 指针增加一个宽度单位
                else if (SignDirectionList[i] == 2)
                {
                    // 暗面：不要用 Z 轴翻转（会影响布局视觉对齐）
                    // 保持和竖牌一致的朝向，后续仅在自身 Y 轴翻面显示暗面
                    TempRotation = rotation;
                    SetPositionpoint += SetDirection * cardWidth;
                    TempPositionpoint += SetDirection * cardWidth; // 暗杠每张牌向左一个宽度单位
                }
                // 卡牌加杠
                else if (SignDirectionList[i] == 3)
                {
                    // 加杠牌在加杠中单独处理，如果生成加杠牌，则调用一次peng一次jiagang即可，掩码操作会自动互相屏蔽
                    continue;
                }

                // 创建卡牌
                cardObj = Instantiate(tile3DPrefab, TempPositionpoint, TempRotation);
                // 设置卡牌纹理
                ApplyCardTexture(cardObj, SetTileList[i]);
                // 注册到悬停管理器
                if (Card3DHoverManager.Instance != null)
                {
                    Card3DHoverManager.Instance.RegisterCard(cardObj, SetTileList[i]);
                }
                // 设置父对象
                cardObj.transform.SetParent(SetParent, worldPositionStays: true);

                // 暗面翻转：仅翻面不改变位置（避免位置浮动）
                if (SignDirectionList[i] == 2)
                {
                    // 仅翻转渲染子节点，不改父物体的世界位姿，避免位置偏移
                    Transform mesh = cardObj.transform;
                    // 如果预制有子节点，优先翻子节点
                    if (cardObj.transform.childCount > 0)
                    {
                        mesh = cardObj.transform.GetChild(0);
                    }
                    var localEuler = mesh.localEulerAngles;
                    localEuler.z += 180f; // 绕Z轴翻面
                    mesh.localEulerAngles = localEuler;

                    // 暗杠抬高：沿世界上方向抬高 0.8 个厚度，修正翻面造成的视觉浮动/穿插
                    cardObj.transform.position += UpDirection * (cardThickness * 0.6f);
                    // 暗杠向右偏移 0.1 个宽度单位
                    cardObj.transform.position += RightDirection * (cardWidth * 0.25f);
                }
                
                // 每创建一张卡牌等待一帧，避免单帧创建太多对象
                if (i < SetTileList.Count - 1)
                {
                    yield return null;
                }
            }

            // 将更新后的指针位置赋值给公共变量
            if (playerIndex == "self")
            {
                selfSetCombinationsPoint = SetPositionpoint;
            }
            else if (playerIndex == "left")
            {
                leftSetCombinationsPoint = SetPositionpoint;
            }
            else if (playerIndex == "top")
            {
                topSetCombinationsPoint = SetPositionpoint;
            }
            else if (playerIndex == "right")
            {
                rightSetCombinationsPoint = SetPositionpoint;
            }

            // 组合牌动画：将父物体移动到3个卡牌宽度以左，然后移回原位
            if (doAnimation)
            {
                StartCoroutine(MoveCardAnimation(SetParent.gameObject, SetDirection, cardWidth));
            }
        }
    }
    
    // 鸣牌3D显示（同步版本，用于向后兼容）
    public void ActionAnimation(string playerIndex, string actionType, int[] combination_mask, bool doAnimation = false)
    {
        StartCoroutine(ActionAnimationCoroutine(playerIndex, actionType, combination_mask, doAnimation));
    }
}


