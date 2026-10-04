using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class Game3DManager : MonoBehaviour
{

    /// <summary>副露统一竖排（由规则清单声明，如虹雀）：认走张（flag=1）也按竖牌摆放，杠张顺排左右。</summary>
    private static bool IsVerticalMelds() {
        RuleManifest rule = RuleRegistry.Current;
        return rule != null && rule.VerticalMelds;
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

    private static void EnsureMeldGroups(PosPanel3D panel, int requiredCount) {
        // Scene presets contain four groups; sixteen-tile rules need five.
        // Actual group count also preserves free-mode groups beyond a standard hand.
        GameRecordManager.ResolveActionRuleContext(null, null, out string roomRule, out string subRule);
        int count = Mathf.Max(requiredCount, HandStructures.Resolve(roomRule, subRule).MeldCount);
        int existing = panel.combination3DObjects?.Length ?? 0;
        if (existing >= count) return;
        Transform parent = existing > 0 && panel.combination3DObjects[0] != null
            ? panel.combination3DObjects[0].parent : panel.combinationsPosition;
        System.Array.Resize(ref panel.combination3DObjects, count);
        for (int i = existing; i < count; i++) {
            var group = new GameObject("Meld_" + i);
            group.layer = panel.gameObject.layer;
            group.transform.SetParent(parent, false);
            panel.combination3DObjects[i] = group.transform;
        }
    }

    // 鸣牌3D显示
    public IEnumerator ActionAnimationCoroutine(string playerIndex, string actionType, int[] combination_mask, bool doAnimation = false, int meldIndex = -1)
    {
        Quaternion rotation = Quaternion.identity;
        Vector3 SetDirection = Vector3.zero;
        Vector3 SetPositionpoint = Vector3.zero;
        Vector3 JiagangDirection = Vector3.zero;
        Transform SetParent = null;
        PosPanel3D panel = GetPosPanel(playerIndex);
        if (panel == null) yield break;
        int groupIndex = meldIndex >= 0 ? meldIndex : Mathf.Max(0, GetPlayerCombinationCount(playerIndex) - 1);
        // An added tile belongs to its upgraded pung, which need not be the last group.
        if (meldIndex < 0 && actionType == "jiagang" && combination_mask != null) {
            var masks = GameRecordManager.Instance.gameObject.activeSelf
                ? GameRecordManager.Instance.recordPlayer_to_info[playerIndex].combinationMasks
                : NormalGameStateManager.Instance.player_to_info[playerIndex].combination_masks;
            int sourceGroup = masks?.FindIndex(mask => mask != null
                && System.Linq.Enumerable.SequenceEqual(mask, combination_mask)) ?? -1;
            if (sourceGroup >= 0) groupIndex = sourceGroup;
        }
        EnsureMeldGroups(panel, groupIndex + 1);
        if (panel.combination3DObjects == null || groupIndex >= panel.combination3DObjects.Length) yield break;
        SetParent = panel.combination3DObjects[groupIndex];
        if (SetParent == null || combination_mask == null) yield break;

        if (playerIndex == "self")
        {
            rotation = Quaternion.Euler(90, 0, 180);
            SetDirection = LeftDirection;
            JiagangDirection = FrontDirection;
            SetPositionpoint = selfSetCombinationsPoint;
        }
        else if (playerIndex == "left")
        {
            rotation = Quaternion.Euler(90, 0, 90);
            SetDirection = FrontDirection;
            JiagangDirection = RightDirection;
            SetPositionpoint = leftSetCombinationsPoint;
        }
        else if (playerIndex == "top")
        {
            rotation = Quaternion.Euler(90, 0, 0);
            SetDirection = RightDirection;
            JiagangDirection = BackDirection;
            SetPositionpoint = topSetCombinationsPoint;
        }
        else if (playerIndex == "right")
        {
            rotation = Quaternion.Euler(90, 0, 270);
            SetDirection = BackDirection;
            JiagangDirection = LeftDirection;
            SetPositionpoint = rightSetCombinationsPoint;
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

                TempPositionpoint = PlaceTileOnTable(TempPositionpoint, TempRotation);
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

        for (int i = 0; i < SetTileList.Count; i++)
        {
            int sign = SignDirectionList[i];
            if (sign == 3 || sign == 4) {
                continue;
            }

            // 虹雀竖排：认走张不旋转、不用长槽；其它规则保持原横置约定。
            bool claimedHorizontal = sign == 1 && !IsVerticalMelds();
            Quaternion TempRotation = rotation;
            float slotWidth = claimedHorizontal ? cardHeight : cardWidth;
            if (claimedHorizontal) {
                TempRotation = Quaternion.Euler(0, -90, 0) * rotation;
            }

            float advance;
            if (!hasPrevInGroup) {
                // 本组第一张：接上组末槽（吃后再吃不再叠），首组仍从原点迈整槽
                advance = acrossGroupLastSlot > 0f
                    ? 0.5f * (acrossGroupLastSlot + slotWidth)
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

            if (sign == 1 && actionType == "peng" && !IsVerticalMelds()) {
                int pengDictKey = GameRecordMeldCodec.NormalizeMeldsLookupTileId(SetTileList[i]);
                pengToJiagangPosDict[pengDictKey] = TempPositionpoint;
            }

            TempPositionpoint = PlaceTileOnTable(TempPositionpoint, TempRotation);
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
            if (actionType == FreeActionWords.Meld) {
                var cards = new List<Transform>();
                var targets = new List<Vector3>();
                foreach (Transform card in SetParent) {
                    cards.Add(card);
                    targets.Add(card.position);
                    card.position += SetDirection * (cardWidth * 3f);
                }
                // 生成当帧即放在动画起点；只移动牌，取消/收回时父节点不会留下位移。
                yield return Animate3DCardsToPositions(cards, targets, SetParent);
            } else {
                StartCoroutine(MoveCardAnimation(SetParent.gameObject, SetDirection, cardWidth, playerIndex));
            }
        }
    }

    private int GetPlayerCombinationCount(string playerPosition) {
        if (GameRecordManager.Instance.gameObject.activeSelf) {
            return GameRecordManager.Instance.recordPlayer_to_info[playerPosition].combinationTiles.Count;
        }
        return NormalGameStateManager.Instance.player_to_info[playerPosition].combination_tiles.Count;
    }

    /// <summary>
    /// 虹雀或长春特殊副露增长后，按各自摆放约定重建指定玩家的全部副露：
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
        EnsureMeldGroups(panel, masks.Count);

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
            Vector3? claimedPosition = null;
            bool vertical = IsVerticalMelds();

            for (int i = 0; i < tileList.Count; i++) {
                int sign = signList[i];
                // 竖排规则把追加张排入本组；普通加杠须放在认走横牌旁，
                // 待基础三张放好后再处理，不能占用下一张的横向位置。
                if (sign == 4 || (sign == 3 && !vertical)) continue;

                // 虹雀竖排：认走张（flag=1）也不旋转、不用长槽。
                bool claimedHorizontal = sign == 1 && !IsVerticalMelds();
                Quaternion tileRotation = rotation;
                float slotWidth = claimedHorizontal ? cardHeight : cardWidth;
                if (claimedHorizontal) {
                    tileRotation = Quaternion.Euler(0, -90, 0) * rotation;
                }
                float advance;
                if (!hasPrevInGroup) {
                    advance = acrossGroupLastSlot > 0f
                        ? 0.5f * (acrossGroupLastSlot + slotWidth)
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

                if (sign == 1 && !vertical) {
                    claimedPosition = tilePosition;
                    string combination = playerInfo.combination_tiles != null && meldIndex < playerInfo.combination_tiles.Count
                        ? playerInfo.combination_tiles[meldIndex] : null;
                    if (!string.IsNullOrEmpty(combination) && (combination.StartsWith("k") || combination.StartsWith("g"))) {
                        int lookupKey = GameRecordMeldCodec.NormalizeMeldsLookupTileId(tileList[i]);
                        pengToJiagangPosDict[lookupKey] = tilePosition;
                    }
                }
                int tileId = tileList[i];
                tilePosition = PlaceTileOnTable(tilePosition, tileRotation);
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

            if (!vertical && signList.Contains(3)) {
                if (!claimedPosition.HasValue) {
                    Debug.LogError($"加杠重建缺少认走横牌：{playerPosition}/{meldIndex}");
                } else {
                    Quaternion addedRotation = Quaternion.Euler(0, -90, 0) * rotation;
                    Vector3 addedPosition = PlaceTileOnTable(claimedPosition.Value + jiagangDirection * cardWidth, addedRotation);
                    for (int i = 0; i < tileList.Count; i++) {
                        if (signList[i] != 3) continue;
                        GameObject added = MahjongObjectPool.Instance.Spawn(tileList[i], addedPosition, addedRotation);
                        if (added == null) { Debug.LogError($"无法从对象池获取加杠牌: {tileList[i]}"); continue; }
                        added.transform.SetParent(setParent, worldPositionStays: true);
                        Card3DHoverManager.Instance.RegisterCard(added, tileList[i]);
                        MahjongObjectPool.Instance.RefreshTileCollider(added);
                        added.GetComponent<Tile3D>()?.ApplyCombinationPeekState(tileList[i], 3);
                        RegisterLastJiagang(playerPosition, added, tileList[i]);
                    }
                }
            }

            StoreCombinationCursor(playerPosition, setPositionpoint);
            if (lastPlacedSlot > 0f) {
                SetCombinationLastSlotWidth(playerPosition, lastPlacedSlot);
                acrossGroupLastSlot = lastPlacedSlot;
            }
        }
    }
}
