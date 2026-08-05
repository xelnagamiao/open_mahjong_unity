using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 随机桌面：清空 3D 牌桌后，为四家随机生成副露 + 手牌 + 牌河。
/// 副露与手牌互相匹配：每家「副露张数 + 手牌张数 = 13」（暗杠/明杠按 4 张计），
/// 全部从同一副 136 张牌堆抽取，保证每种牌最多 4 张、与对象池一致。
/// 自家手牌也会以明牌立姿显示在 3D 桌面上（不排除自家）。
/// 只操作 3D 表现层（对象池），不修改 NormalGameStateManager 对局数据。
/// </summary>
public partial class Game3DManager : MonoBehaviour
{
    private static readonly int[] RandomTableStandardTiles =
    {
        11, 12, 13, 14, 15, 16, 17, 18, 19,
        21, 22, 23, 24, 25, 26, 27, 28, 29,
        31, 32, 33, 34, 35, 36, 37, 38, 39,
        41, 42, 43, 44, 45, 46, 47
    };

    /// <summary>随机生成整桌：每家副露 + 手牌合计 13 张，另有随机牌河。</summary>
    public void GenerateRandomTable()
    {
        // 对局/观战进行中（含回主菜单挂后台）禁止清空牌桌生成随机桌面，
        // 否则会破坏正在运行的对局 3D 表现。
        if (GameSessionGuard.BlockIfExclusiveSession("生成随机桌面")) return;
        if (MahjongObjectPool.Instance == null)
        {
            Debug.LogWarning("GenerateRandomTable: MahjongObjectPool 不存在");
            return;
        }
        if (Card3DHoverManager.Instance == null)
        {
            Debug.LogWarning("GenerateRandomTable: Card3DHoverManager 不存在，无法注册牌张");
            return;
        }

        StopAllRunningAnimations();
        Clear3DTile();

        Dictionary<int, int> counts = new Dictionary<int, int>();
        foreach (int tileId in RandomTableStandardTiles) counts[tileId] = 4;

        System.Random rng = new System.Random();
        foreach (string position in new[] { "self", "left", "top", "right" })
        {
            PosPanel3D panel = GetPosPanel(position);
            if (panel == null) continue;

            // 1) 先抽副露（吃/碰/杠），并扣减牌堆
            List<List<int>> meldMasks = new List<List<int>>();
            int meldTileCount = 0;
            int meldCount = rng.Next(1, 4); // 每家 1~3 组副露
            for (int i = 0; i < meldCount; i++)
            {
                List<int> mask = BuildRandomMeldMask(counts, rng);
                if (mask == null || mask.Count < 2) break;
                meldMasks.Add(mask);
                meldTileCount += MeldTileCount(mask);
            }

            // 2) 剩余张数补齐到手牌，使 副露 + 手牌 = 13
            int closedCount = 13 - meldTileCount;
            if (closedCount < 1) closedCount = 1;
            List<int> closedHand = DrawRandomTiles(counts, rng, closedCount);

            // 3) 渲染：手牌（自家明牌立姿，他家暗牌）+ 副露 + 牌河
            LayRandomHand(position, panel, closedHand);
            LayRandomMelds(position, panel, meldMasks);
            LayRandomRiver(position, panel, counts, rng);
        }

        Debug.Log("GenerateRandomTable: 随机桌面已生成（副露+手牌=13 张/家）");
    }

    /// <summary>
    /// 手牌：自家以明牌立姿沿自家手牌区排列；左/上/右家为立起暗牌（牌背朝外）。
    /// </summary>
    private void LayRandomHand(string playerPosition, PosPanel3D panel, List<int> closedHand)
    {
        Transform cardsPosition = panel.cardsPosition;
        if (cardsPosition == null || closedHand == null || closedHand.Count == 0) return;

        if (playerPosition == "self")
        {
            // 自家：明牌立姿，按牌面排序，沿 RightDirection 排列
            closedHand.Sort(TileIdOrder.Comparer);
            Vector3 direction = RightDirection.normalized;
            Quaternion rotation = SelfHandStandingRotation();
            for (int i = 0; i < closedHand.Count; i++)
            {
                Vector3 spawn = cardsPosition.position + cardsPosition.childCount * cardWidth * direction;
                GameObject cardObj = MahjongObjectPool.Instance.Spawn(closedHand[i], spawn, rotation);
                if (cardObj == null) continue;
                cardObj.transform.SetParent(cardsPosition, worldPositionStays: true);
            }
            return;
        }

        Quaternion backRotation;
        Vector3 backDirection;
        if (playerPosition == "left") { backRotation = Quaternion.Euler(0, 90, 0); backDirection = BackDirection; }
        else if (playerPosition == "top") { backRotation = Quaternion.Euler(0, 180, 0); backDirection = LeftDirection; }
        else if (playerPosition == "right") { backRotation = Quaternion.Euler(0, 270, 0); backDirection = FrontDirection; }
        else return;

        for (int i = 0; i < closedHand.Count; i++)
        {
            Vector3 spawn = cardsPosition.position + cardsPosition.childCount * cardWidth * backDirection.normalized;
            GameObject cardObj = MahjongObjectPool.Instance.SpawnBlankTile(spawn, backRotation);
            if (cardObj == null) continue;
            cardObj.transform.SetParent(cardsPosition, worldPositionStays: true);
        }
    }

    /// <summary>按掩码摆副露，方向位与真实对局一致（0 竖 1 横 2 暗）。</summary>
    private void LayRandomMelds(string playerPosition, PosPanel3D panel, List<List<int>> meldMasks)
    {
        Transform[] parents = panel.combination3DObjects;
        if (parents == null || parents.Length == 0 || panel.combinationsPosition == null) return;
        if (meldMasks == null || meldMasks.Count == 0) return;

        Quaternion rotation;
        Vector3 setDirection;
        Vector3 jiagangDirection;
        if (playerPosition == "self")
        {
            rotation = Quaternion.Euler(90, 0, 180);
            setDirection = LeftDirection;
            jiagangDirection = FrontDirection;
        }
        else if (playerPosition == "left")
        {
            rotation = Quaternion.Euler(90, 0, 90);
            setDirection = FrontDirection;
            jiagangDirection = RightDirection;
        }
        else if (playerPosition == "top")
        {
            rotation = Quaternion.Euler(90, 0, 0);
            setDirection = RightDirection;
            jiagangDirection = BackDirection;
        }
        else
        {
            rotation = Quaternion.Euler(90, 0, 270);
            setDirection = BackDirection;
            jiagangDirection = LeftDirection;
        }

        Vector3 cursor = panel.combinationsPosition.position;
        float acrossGroupLastSlot = 0f;
        float groupGap = ConfigManager.Instance != null && ConfigManager.Instance.MeldSpacingEnabled
            ? cardWidth * CombinationGroupGapFactor
            : 0f;

        for (int meldIndex = 0; meldIndex < meldMasks.Count; meldIndex++)
        {
            List<int> mask = meldMasks[meldIndex];
            Transform setParent = parents[Mathf.Min(meldIndex, parents.Length - 1)];

            List<int> tileList = new List<int>();
            List<int> signList = new List<int>();
            for (int i = 0; i + 1 < mask.Count; i += 2)
            {
                signList.Add(mask[i]);
                tileList.Add(mask[i + 1]);
            }
            tileList.Reverse();
            signList.Reverse();

            float prevSlotWidth = 0f;
            bool hasPrevInGroup = false;
            float lastPlacedSlot = 0f;

            for (int i = 0; i < tileList.Count; i++)
            {
                int sign = signList[i];
                if (sign == 3 || sign == 4) continue;

                Quaternion tileRotation = rotation;
                float slotWidth = CombinationSlotWidth(sign, cardWidth, cardHeight);
                if (sign == 1) tileRotation = Quaternion.Euler(0, -90, 0) * rotation;

                float advance;
                if (!hasPrevInGroup)
                {
                    advance = acrossGroupLastSlot > 0f
                        ? 0.5f * (acrossGroupLastSlot + slotWidth) + groupGap
                        : slotWidth;
                }
                else
                {
                    advance = 0.5f * (prevSlotWidth + slotWidth);
                }

                cursor += setDirection.normalized * advance;
                Vector3 tilePosition = cursor;
                if (sign == 1) tilePosition += (-jiagangDirection) * 0.5f * (cardHeight - cardWidth);

                prevSlotWidth = slotWidth;
                hasPrevInGroup = true;
                lastPlacedSlot = slotWidth;

                int tileId = tileList[i];
                GameObject cardObj = MahjongObjectPool.Instance.Spawn(tileId, tilePosition, tileRotation);
                if (cardObj == null)
                {
                    Debug.LogWarning($"GenerateRandomTable: 无法从对象池获取牌 {tileId}");
                    continue;
                }
                Card3DHoverManager.Instance.RegisterCard(cardObj, tileId);
                cardObj.transform.SetParent(setParent, worldPositionStays: true);
                MahjongObjectPool.Instance.RefreshTileCollider(cardObj);
                Tile3D tile3D = cardObj.GetComponent<Tile3D>();
                tile3D?.ApplyCombinationPeekState(tileId, sign);
            }

            if (lastPlacedSlot > 0f) acrossGroupLastSlot = lastPlacedSlot;
        }
    }

    /// <summary>随机牌河：6~14 张，复用对局弃牌布局（每行 6 张、可多行）。</summary>
    private void LayRandomRiver(string playerPosition, PosPanel3D panel, Dictionary<int, int> counts, System.Random rng)
    {
        if (panel.discardsPosition == null) return;

        List<int> available = new List<int>();
        foreach (KeyValuePair<int, int> kv in counts)
        {
            if (kv.Value > 0) available.Add(kv.Key);
        }

        int discardCount = rng.Next(6, 15);
        for (int i = 0; i < discardCount && available.Count > 0; i++)
        {
            int index = rng.Next(0, available.Count);
            int tileId = available[index];
            Set3DTile(tileId, panel.discardsPosition, "DiscardWithoutAnimation", playerPosition);
            counts[tileId]--;
            if (counts[tileId] <= 0) available.RemoveAt(index);
        }
    }

    private static int MeldTileCount(List<int> mask)
    {
        if (mask == null) return 0;
        int count = 0;
        for (int i = 0; i + 1 < mask.Count; i += 2)
        {
            if (mask[i] == 3 || mask[i] == 4) continue;
            count++;
        }
        return count;
    }

    private static List<int> DrawRandomTiles(Dictionary<int, int> counts, System.Random rng, int n)
    {
        List<int> result = new List<int>();
        for (int i = 0; i < n; i++)
        {
            List<int> available = new List<int>();
            foreach (KeyValuePair<int, int> kv in counts)
            {
                if (kv.Value > 0) available.Add(kv.Key);
            }
            if (available.Count == 0) break;
            int tileId = available[rng.Next(0, available.Count)];
            result.Add(tileId);
            counts[tileId]--;
        }
        return result;
    }

    /// <summary>从剩余牌堆构造一组副露掩码 [方向位, 牌id, ...]，并扣减牌堆。</summary>
    private static List<int> BuildRandomMeldMask(Dictionary<int, int> counts, System.Random rng)
    {
        List<int> available = new List<int>();
        foreach (KeyValuePair<int, int> kv in counts)
        {
            if (kv.Value > 0) available.Add(kv.Key);
        }
        if (available.Count == 0) return null;

        int roll = rng.Next(0, 100);
        int tileId = available[rng.Next(0, available.Count)];

        // 吃：同花色连续三张（40%）
        if (roll >= 60 && TryTakeChi(counts, rng, out List<int> chi))
        {
            return new List<int> { 0, chi[0], 1, chi[1], 0, chi[2] };
        }

        // 杠：四张同牌（15%）
        if (roll >= 40 && CanTakeN(counts, tileId, 4))
        {
            TakeN(counts, tileId, 4);
            bool concealed = rng.Next(0, 2) == 0;
            return concealed
                ? new List<int> { 2, tileId, 2, tileId, 2, tileId, 2, tileId }
                : new List<int> { 1, tileId, 0, tileId, 0, tileId, 0, tileId };
        }

        // 碰：三张同牌（默认）
        if (CanTakeN(counts, tileId, 3))
        {
            TakeN(counts, tileId, 3);
            return new List<int> { 1, tileId, 0, tileId, 0, tileId };
        }

        // 兜底：换一张能凑碰的牌
        foreach (int candidate in available)
        {
            if (CanTakeN(counts, candidate, 3))
            {
                TakeN(counts, candidate, 3);
                return new List<int> { 1, candidate, 0, candidate, 0, candidate };
            }
        }
        return null;
    }

    private static bool TryTakeChi(Dictionary<int, int> counts, System.Random rng, out List<int> chiTiles)
    {
        chiTiles = null;
        List<int> candidates = new List<int>();
        int[] suitBases = { 10, 20, 30 };
        foreach (int baseSuit in suitBases)
        {
            for (int startNumber = 1; startNumber <= 7; startNumber++)
            {
                int t1 = baseSuit + startNumber;
                int t2 = baseSuit + startNumber + 1;
                int t3 = baseSuit + startNumber + 2;
                if (CanTakeN(counts, t1, 1) && CanTakeN(counts, t2, 1) && CanTakeN(counts, t3, 1))
                {
                    candidates.Add(baseSuit * 10 + startNumber);
                }
            }
        }
        if (candidates.Count == 0) return false;

        int pick = candidates[rng.Next(0, candidates.Count)];
        int suit = pick / 10;
        int chiStart = pick % 10;
        chiTiles = new List<int> { suit + chiStart, suit + chiStart + 1, suit + chiStart + 2 };
        TakeN(counts, chiTiles[0], 1);
        TakeN(counts, chiTiles[1], 1);
        TakeN(counts, chiTiles[2], 1);
        return true;
    }

    private static bool CanTakeN(Dictionary<int, int> counts, int tileId, int n)
    {
        return counts.TryGetValue(tileId, out int c) && c >= n;
    }

    private static void TakeN(Dictionary<int, int> counts, int tileId, int n)
    {
        counts[tileId] = counts[tileId] - n;
    }
}
