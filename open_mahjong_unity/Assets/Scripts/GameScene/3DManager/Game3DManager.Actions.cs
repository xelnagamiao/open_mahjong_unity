using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class Game3DManager : MonoBehaviour
{
    /// <summary>副露组与组之间额外空隙（相对牌宽）；开启“副露间距”设置时生效。</summary>
    private const float CombinationGroupGapFactor = 0.2f;

    /// <summary>虹雀副露统一竖排：认走张（flag=1）也按竖牌摆放，杠张顺排左右。</summary>
    private static bool IsHongqueVerticalMelds() {
        NormalGameStateManager gsm = NormalGameStateManager.Instance;
        return gsm != null && gsm.roomRule == "hongque";
    }

    /// <summary>副露组间距：虹雀固定保留（优先于设置），其它规则按设置开关。</summary>
    private float MeldSpacingGap() {
        bool enabled = IsHongqueVerticalMelds()
            || (ConfigManager.Instance != null && ConfigManager.Instance.MeldSpacingEnabled);
        return enabled ? cardWidth * CombinationGroupGapFactor : 0f;
    }

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
        float groupGap = MeldSpacingGap();

        for (int i = 0; i < SetTileList.Count; i++)
        {
            int sign = SignDirectionList[i];
            if (sign == 3 || sign == 4) {
                continue;
            }

            // 虹雀竖排：认走张不旋转、不用长槽；其它规则保持原横置约定。
            bool claimedHorizontal = sign == 1 && !IsHongqueVerticalMelds();
            Quaternion TempRotation = rotation;
            float slotWidth = claimedHorizontal ? cardHeight : cardWidth;
            if (claimedHorizontal) {
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
            if (claimedHorizontal) {
                TempPositionpoint += (-JiagangDirection) * 0.5f * (cardHeight - cardWidth);
            }

            prevSlotWidth = slotWidth;
            hasPrevInGroup = true;
            lastPlacedSlot = slotWidth;

            if (sign == 1 && actionType == "peng" && !IsHongqueVerticalMelds()) {
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

    /// <summary>
    /// 虹雀副露增长（补顺/补杠 3→4→5→6）后重建指定玩家的全部副露：
    /// 以权威 combination_masks 重新摆放，不依赖加杠动画的碰牌缓存与“末组”假设。
    /// </summary>
    public void RebuildPlayerMelds(string playerPosition) {
        PosPanel3D panel = GetPosPanel(playerPosition);
        if (panel == null) return;
        if (!NormalGameStateManager.Instance.player_to_info.TryGetValue(
                playerPosition, out PlayerInfoClass playerInfo)) {
            return;
        }
        List<int[]> masks = playerInfo.combination_masks ?? new List<int[]>();

        // 归还该家全部副露牌，重置组合光标。
        foreach (Transform comboParent in panel.combination3DObjects) {
            if (comboParent == null) continue;
            for (int i = comboParent.childCount - 1; i >= 0; i--) {
                MahjongObjectPool.Instance.Return(-1, comboParent.GetChild(i).gameObject);
            }
        }
        SetCombinationLastSlotWidth(playerPosition, 0f);
        StoreCombinationCursor(playerPosition, panel.combinationsPosition.position);

        Quaternion rotation;
        Vector3 setDirection;
        Vector3 jiagangDirection;
        if (playerPosition == "self") {
            rotation = Quaternion.Euler(90, 0, 180);
            setDirection = LeftDirection;
            jiagangDirection = FrontDirection;
        } else if (playerPosition == "left") {
            rotation = Quaternion.Euler(90, 0, 90);
            setDirection = FrontDirection;
            jiagangDirection = RightDirection;
        } else if (playerPosition == "top") {
            rotation = Quaternion.Euler(90, 0, 0);
            setDirection = RightDirection;
            jiagangDirection = BackDirection;
        } else {
            rotation = Quaternion.Euler(90, 0, 270);
            setDirection = BackDirection;
            jiagangDirection = LeftDirection;
        }

        float acrossGroupLastSlot = GetCombinationLastSlotWidth(playerPosition);
        for (int meldIndex = 0; meldIndex < masks.Count && meldIndex < panel.combination3DObjects.Length; meldIndex++) {
            int[] combinationMask = masks[meldIndex];
            if (combinationMask == null || combinationMask.Length == 0) continue;

            List<int> tileList = new List<int>();
            List<int> signList = new List<int>();
            for (int i = 0; i + 1 < combinationMask.Length; i += 2) {
                signList.Add(combinationMask[i]);
                tileList.Add(combinationMask[i + 1]);
            }
            tileList.Reverse();
            signList.Reverse();

            Transform setParent = panel.combination3DObjects[meldIndex];
            Vector3 setPositionpoint = playerPosition == "self" ? selfSetCombinationsPoint
                : playerPosition == "left" ? leftSetCombinationsPoint
                : playerPosition == "top" ? topSetCombinationsPoint
                : rightSetCombinationsPoint;
            float prevSlotWidth = 0f;
            bool hasPrevInGroup = false;
            float lastPlacedSlot = 0f;
            float groupGap = MeldSpacingGap();

            for (int i = 0; i < tileList.Count; i++) {
                int sign = signList[i];
                // 虹雀杠：本次杠入的副露张以 flag=3 下发，必须正常摆放；
                // 标准规则加杠动画单独摆放 flag=3 张，但 RebuildPlayerMelds 仅虹雀调用，
                // 因此这里只跳过未使用的 flag=4。
                if (sign == 4) continue;

                // 虹雀竖排：认走张（flag=1）也不旋转、不用长槽。
                bool claimedHorizontal = sign == 1 && !IsHongqueVerticalMelds();
                Quaternion tileRotation = rotation;
                float slotWidth = claimedHorizontal ? cardHeight : cardWidth;
                if (claimedHorizontal) {
                    tileRotation = Quaternion.Euler(0, -90, 0) * rotation;
                }
                float advance;
                if (!hasPrevInGroup) {
                    advance = acrossGroupLastSlot > 0f
                        ? 0.5f * (acrossGroupLastSlot + slotWidth) + groupGap
                        : slotWidth;
                } else {
                    advance = 0.5f * (prevSlotWidth + slotWidth);
                }
                setPositionpoint += setDirection * advance;
                Vector3 tilePosition = setPositionpoint;
                if (claimedHorizontal) {
                    tilePosition += (-jiagangDirection) * 0.5f * (cardHeight - cardWidth);
                }
                prevSlotWidth = slotWidth;
                hasPrevInGroup = true;
                lastPlacedSlot = slotWidth;

                int tileId = tileList[i];
                GameObject cardObj = MahjongObjectPool.Instance.Spawn(tileId, tilePosition, tileRotation);
                if (cardObj == null) {
                    Debug.LogError($"无法从对象池获取牌: {tileId}");
                    continue;
                }
                Card3DHoverManager.Instance.RegisterCard(cardObj, tileId);
                cardObj.transform.SetParent(setParent, worldPositionStays: true);
                MahjongObjectPool.Instance.RefreshTileCollider(cardObj);
                Tile3D tile3D = cardObj.GetComponent<Tile3D>();
                tile3D?.ApplyCombinationPeekState(tileId, sign);
            }

            StoreCombinationCursor(playerPosition, setPositionpoint);
            if (lastPlacedSlot > 0f) {
                SetCombinationLastSlotWidth(playerPosition, lastPlacedSlot);
                acrossGroupLastSlot = lastPlacedSlot;
            }
        }
    }
}
