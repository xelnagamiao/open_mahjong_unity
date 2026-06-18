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
            SetParent = panel.combination3DObjects[Mathf.Max(0, GetPlayerCombinationCount("self") - 1)];
        }
        else if (playerIndex == "left")
        {
            rotation = Quaternion.Euler(90, 0, 90); // 左侧玩家
            SetDirection = FrontDirection; // 向前
            JiagangDirection = RightDirection; // 左侧玩家加杠指针是向右
            SetPositionpoint = leftSetCombinationsPoint;
            SetParent = panel.combination3DObjects[Mathf.Max(0, GetPlayerCombinationCount("left") - 1)];
        }
        else if (playerIndex == "top")
        {
            rotation = Quaternion.Euler(90, 0, 0); // 上方玩家
            SetDirection = RightDirection; // 向右
            JiagangDirection = BackDirection; // 上方玩家加杠指针是向后
            SetPositionpoint = topSetCombinationsPoint;
            SetParent = panel.combination3DObjects[Mathf.Max(0, GetPlayerCombinationCount("top") - 1)];
        }
        else if (playerIndex == "right")
        {
            rotation = Quaternion.Euler(90, 0, 270); // 右侧玩家
            SetDirection = BackDirection; // 向后
            JiagangDirection = LeftDirection; // 右侧玩家加杠指针是向左
            SetPositionpoint = rightSetCombinationsPoint;
            SetParent = panel.combination3DObjects[Mathf.Max(0, GetPlayerCombinationCount("right") - 1)];
        }

        // 获取了rotation(卡牌旋转角度) SetDirection(放置方向) 以及公共变量 $SetCombinationsPoint
        List<int> SetTileList = new List<int>();
        List<int> SignDirectionList = new List<int>();

        // 解码 combination_mask：[方向, 牌id, 方向, 牌id, ...]；牌 id 可为 0（国标暗杠脱敏占位）
        for (int i = 0; i + 1 < combination_mask.Length; i += 2) {
            SignDirectionList.Add(combination_mask[i]);
            SetTileList.Add(combination_mask[i + 1]);
        }
        // 倒转SetTileList和SignDirectionList 因为卡牌的逻辑顺序是从左到右，但我们需要从右到左放置
        SetTileList.Reverse();
        SignDirectionList.Reverse();
        Debug.Log($"actionType: {actionType}, combination_mask: {combination_mask}, SetTileList: {SetTileList}, SignDirectionList: {SignDirectionList}");

        // 执行动画
        // 加杠
        if (actionType == "jiagang")
        {
            for (int i = 0; i < SetTileList.Count; i++)
            {
                if (SignDirectionList[i] == 3)
                {
                    GameObject cardObj;
                    int jiagangTileId = SetTileList[i];
                    // 碰时按归一化河牌 id 存位置；加杠时用 mask 里 flag=1 的河牌 id 归一化后查表（105/15 等同）
                    int? riverTileId = GameRecordMeldCodec.ExtractTileByFlag(combination_mask, 1);
                    int lookupKey = GameRecordMeldCodec.NormalizeMeldsLookupTileId(riverTileId ?? jiagangTileId);
                    if (!pengToJiagangPosDict.TryGetValue(lookupKey, out Vector3 TempPositionpoint)) {
                        Debug.LogError($"加杠位置未找到: lookupKey={lookupKey}, jiagangTileId={jiagangTileId}, riverTileId={riverTileId}");
                        continue;
                    }
                    Quaternion TempRotation = Quaternion.Euler(0, 90, 0) * rotation; // 横
                    TempPositionpoint += JiagangDirection * cardWidth; // 加杠向上一个宽度单位
                    // 从对象池获取麻将牌
                    cardObj = MahjongObjectPool.Instance.Spawn(jiagangTileId, TempPositionpoint, TempRotation);
                    if (cardObj == null)
                    {
                        Debug.LogError($"无法从对象池获取牌: {jiagangTileId}");
                        continue;
                    }
                    // 注册到悬停管理器
                    if (Card3DHoverManager.Instance != null)
                    {
                        Card3DHoverManager.Instance.RegisterCard(cardObj, jiagangTileId);
                    }
                    // 设置父对象
                    cardObj.transform.SetParent(SetParent, worldPositionStays: true);
                    lastCutJiagang3DObject = cardObj;
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
                    TempPositionpoint += SetDirection * cardWidth ;
                    SetPositionpoint += SetDirection * cardWidth; // 吃碰的竖置牌每张牌向左一个宽度单位
                }
                // 卡牌横置,放置角度叠加横置 指针增加一个高度单位
                else if (SignDirectionList[i] == 1)
                {
                    TempRotation = Quaternion.Euler(0, 90, 0) * rotation; // 横
                    SetPositionpoint += SetDirection * cardHeight * 1.08f; // 指针移动
                    TempPositionpoint += SetDirection; // 保存当前放置位置（基础偏移）
                    // 沿 SetDirection 反向偏移 0.4 高度，沿 JiagangDirection 反向偏移 0.5 宽度
                    TempPositionpoint += (SetDirection) * 1.15f * cardWidth;
                    TempPositionpoint += (-JiagangDirection) * 0.2f * cardWidth;
                    if (actionType == "peng")
                    {
                        int pengDictKey = GameRecordMeldCodec.NormalizeMeldsLookupTileId(SetTileList[i]);
                        pengToJiagangPosDict[pengDictKey] = TempPositionpoint;
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

                // 从对象池获取麻将牌；奇数位 tileId==0 为国标暗杠脱敏占位，显示牌背
                int tileId = SetTileList[i];
                if (tileId == 0) {
                    cardObj = MahjongObjectPool.Instance.SpawnBlankTile(TempPositionpoint, TempRotation, 0);
                } else {
                    cardObj = MahjongObjectPool.Instance.Spawn(tileId, TempPositionpoint, TempRotation);
                }
                if (cardObj == null)
                {
                    Debug.LogError($"无法从对象池获取牌: {SetTileList[i]}");
                    continue;
                }
                // 注册到悬停管理器
                if (Card3DHoverManager.Instance != null)
                {
                    Card3DHoverManager.Instance.RegisterCard(cardObj, tileId);
                }
                // 设置父对象
                cardObj.transform.SetParent(SetParent, worldPositionStays: true);
                if (MahjongObjectPool.Instance != null) {
                    MahjongObjectPool.Instance.RefreshTileCollider(cardObj);
                }

                Tile3D tile3D = cardObj.GetComponent<Tile3D>();
                tile3D?.ApplyCombinationPeekState(tileId, SignDirectionList[i]);
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

    private int GetPlayerCombinationCount(string playerPosition) {
        // 如果是牌谱模式，则返回牌谱模式下的组合数
        if (GameRecordManager.Instance != null && GameRecordManager.Instance.gameObject.activeSelf) {
            return GameRecordManager.Instance.recordPlayer_to_info[playerPosition].combinationTiles.Count;
        }
        return NormalGameStateManager.Instance.player_to_info[playerPosition].combination_tiles.Count;
    }
}


