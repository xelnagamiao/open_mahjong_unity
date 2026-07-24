using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class Game3DManager : MonoBehaviour
{
    /// <summary>副露组与组之间额外空隙（相对牌宽）。</summary>
    private const float CombinationGroupGapFactor = 0.2f;

    private void ResetCombinationLastSlotWidths() {
        _combinationLastSlotWidth["self"] = 0f;
        _combinationLastSlotWidth["left"] = 0f;
        _combinationLastSlotWidth["top"] = 0f;
        _combinationLastSlotWidth["right"] = 0f;
    }

    private float GetCombinationLastSlotWidth(string playerPosition) {
        return _combinationLastSlotWidth.TryGetValue(playerPosition, out float w) ? w : 0f;
    }

    private void SetCombinationLastSlotWidth(string playerPosition, float slotWidth) {
        _combinationLastSlotWidth[playerPosition] = slotWidth;
    }

    private void StoreCombinationCursor(string playerPosition, Vector3 point) {
        if (playerPosition == "self") selfSetCombinationsPoint = point;
        else if (playerPosition == "left") leftSetCombinationsPoint = point;
        else if (playerPosition == "top") topSetCombinationsPoint = point;
        else if (playerPosition == "right") rightSetCombinationsPoint = point;
    }

    private static float CombinationSlotWidth(int sign, float tileWidth, float tileHeight) {
        // 0竖 / 2暗面：短边；1横：长边
        return sign == 1 ? tileHeight : tileWidth;
    }

    // 鸣牌3D显示
    public IEnumerator ActionAnimationCoroutine(string playerIndex, string actionType, int[] combination_mask, bool doAnimation = false)
    {
        Quaternion rotation = Quaternion.identity;
        Vector3 SetDirection = Vector3.zero;
        Vector3 SetPositionpoint = Vector3.zero;
        Vector3 JiagangDirection = Vector3.zero;
        Transform SetParent = null;
        PosPanel3D panel = GetPosPanel(playerIndex);
        if (panel == null) yield break;

        if (playerIndex == "self")
        {
            rotation = Quaternion.Euler(90, 0, 180);
            SetDirection = LeftDirection;
            JiagangDirection = FrontDirection;
            SetPositionpoint = selfSetCombinationsPoint;
            SetParent = panel.combination3DObjects[Mathf.Max(0, GetPlayerCombinationCount("self") - 1)];
        }
        else if (playerIndex == "left")
        {
            rotation = Quaternion.Euler(90, 0, 90);
            SetDirection = FrontDirection;
            JiagangDirection = RightDirection;
            SetPositionpoint = leftSetCombinationsPoint;
            SetParent = panel.combination3DObjects[Mathf.Max(0, GetPlayerCombinationCount("left") - 1)];
        }
        else if (playerIndex == "top")
        {
            rotation = Quaternion.Euler(90, 0, 0);
            SetDirection = RightDirection;
            JiagangDirection = BackDirection;
            SetPositionpoint = topSetCombinationsPoint;
            SetParent = panel.combination3DObjects[Mathf.Max(0, GetPlayerCombinationCount("top") - 1)];
        }
        else if (playerIndex == "right")
        {
            rotation = Quaternion.Euler(90, 0, 270);
            SetDirection = BackDirection;
            JiagangDirection = LeftDirection;
            SetPositionpoint = rightSetCombinationsPoint;
            SetParent = panel.combination3DObjects[Mathf.Max(0, GetPlayerCombinationCount("right") - 1)];
        }

        List<int> SetTileList = new List<int>();
        List<int> SignDirectionList = new List<int>();

        for (int i = 0; i + 1 < combination_mask.Length; i += 2) {
            SignDirectionList.Add(combination_mask[i]);
            SetTileList.Add(combination_mask[i + 1]);
        }
        SetTileList.Reverse();
        SignDirectionList.Reverse();
        Debug.Log($"actionType: {actionType}, combination_mask: {combination_mask}, SetTileList: {SetTileList}, SignDirectionList: {SignDirectionList}");

        if (actionType == "jiagang")
        {
            for (int i = 0; i < SetTileList.Count; i++)
            {
                if (SignDirectionList[i] != 3) {
                    continue;
                }

                int jiagangTileId = SetTileList[i];
                int? riverTileId = GameRecordMeldCodec.ExtractTileByFlag(combination_mask, 1);
                if (riverTileId == null || riverTileId.Value < 10) {
                    Debug.LogError(
                        $"加杠 mask 缺少 flag=1 河牌 id: player={playerIndex}, jiagangTileId={jiagangTileId}, mask=[{string.Join(",", combination_mask ?? System.Array.Empty<int>())}]");
                    continue;
                }
                int lookupKey = GameRecordMeldCodec.NormalizeMeldsLookupTileId(riverTileId.Value);
                if (!pengToJiagangPosDict.TryGetValue(lookupKey, out Vector3 TempPositionpoint)) {
                    Debug.LogError($"加杠位置未找到: lookupKey={lookupKey}, jiagangTileId={jiagangTileId}, riverTileId={riverTileId}");
                    continue;
                }

                Quaternion TempRotation = Quaternion.Euler(0, -90, 0) * rotation;
                // 叠在碰横牌桌心侧：两横牌短边相对，中心距 = cardWidth
                TempPositionpoint += JiagangDirection * cardWidth;

                GameObject cardObj = MahjongObjectPool.Instance.Spawn(jiagangTileId, TempPositionpoint, TempRotation);
                if (cardObj == null)
                {
                    Debug.LogError($"无法从对象池获取牌: {jiagangTileId}");
                    continue;
                }

                Card3DHoverManager.Instance.RegisterCard(cardObj, jiagangTileId);
                cardObj.transform.SetParent(SetParent, worldPositionStays: true);
                RegisterLastJiagang(playerIndex, cardObj, jiagangTileId);
                if (doAnimation)
                {
                    StartCoroutine(MoveCardAnimation(cardObj, SetDirection, cardWidth, playerIndex));
                }

                if (i < SetTileList.Count - 1)
                {
                    yield return null;
                }
            }
            yield break;
        }

        float acrossGroupLastSlot = GetCombinationLastSlotWidth(playerIndex);
        float prevSlotWidth = 0f;
        bool hasPrevInGroup = false;
        float lastPlacedSlot = 0f;
        float groupGap = cardWidth * CombinationGroupGapFactor;

        for (int i = 0; i < SetTileList.Count; i++)
        {
            int sign = SignDirectionList[i];
            if (sign == 3 || sign == 4) {
                continue;
            }

            Quaternion TempRotation = rotation;
            float slotWidth = CombinationSlotWidth(sign, cardWidth, cardHeight);
            if (sign == 1) {
                TempRotation = Quaternion.Euler(0, -90, 0) * rotation;
            }

            float advance;
            if (!hasPrevInGroup) {
                // 本组第一张：接上组末槽（吃后再吃不再叠），首组仍从原点迈整槽
                advance = acrossGroupLastSlot > 0f
                    ? 0.5f * (acrossGroupLastSlot + slotWidth) + groupGap
                    : slotWidth;
            } else {
                advance = 0.5f * (prevSlotWidth + slotWidth);
            }

            SetPositionpoint += SetDirection * advance;
            Vector3 TempPositionpoint = SetPositionpoint;
            // 横牌底边与竖牌对齐
            if (sign == 1) {
                TempPositionpoint += (-JiagangDirection) * 0.5f * (cardHeight - cardWidth);
            }

            prevSlotWidth = slotWidth;
            hasPrevInGroup = true;
            lastPlacedSlot = slotWidth;

            if (sign == 1 && actionType == "peng") {
                int pengDictKey = GameRecordMeldCodec.NormalizeMeldsLookupTileId(SetTileList[i]);
                pengToJiagangPosDict[pengDictKey] = TempPositionpoint;
            }

            int tileId = SetTileList[i];
            GameObject cardObj;
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

            Card3DHoverManager.Instance.RegisterCard(cardObj, tileId);
            cardObj.transform.SetParent(SetParent, worldPositionStays: true);
            MahjongObjectPool.Instance.RefreshTileCollider(cardObj);

            Tile3D tile3D = cardObj.GetComponent<Tile3D>();
            tile3D?.ApplyCombinationPeekState(tileId, sign);
        }

        StoreCombinationCursor(playerIndex, SetPositionpoint);
        if (lastPlacedSlot > 0f) {
            SetCombinationLastSlotWidth(playerIndex, lastPlacedSlot);
        }

        if (doAnimation)
        {
            StartCoroutine(MoveCardAnimation(SetParent.gameObject, SetDirection, cardWidth, playerIndex));
        }
    }

    private int GetPlayerCombinationCount(string playerPosition) {
        if (GameRecordManager.Instance.gameObject.activeSelf) {
            return GameRecordManager.Instance.recordPlayer_to_info[playerPosition].combinationTiles.Count;
        }
        return NormalGameStateManager.Instance.player_to_info[playerPosition].combination_tiles.Count;
    }
}
