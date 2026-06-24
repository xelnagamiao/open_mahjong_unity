using System.Collections.Generic;
using System.Linq;

namespace Riichi {
    /// <summary>
    /// 立直麻将役种检测。
    /// 依据一般型（4 面子+雀头）的拆解，或七对子/国士无双结构，枚举所有成立的役，
    /// 返回 (役名, han) 列表，以及是否役满等元信息。
    /// 逻辑参照 Python mahjong 库的 yaku_list 实现。
    /// </summary>
    public class RiichiYakuDetector {
        public struct YakuEntry {
            public string Name;
            public int Han;
            public bool IsYakuman;
            public int YakumanMultiplier;
        }

        public class DetectResult {
            public List<YakuEntry> Yaku = new List<YakuEntry>();
            public int YakumanMultiplier;
            public bool IsClosed;
            public RiichiWaitType WaitType;
            public bool HasYaku;
        }

        private readonly RiichiHandContext _ctx;

        public RiichiYakuDetector(RiichiHandContext ctx) {
            _ctx = ctx ?? new RiichiHandContext();
        }

        public DetectResult Detect(List<RiichiSet> sets, int winTile, HandShape shape) {
            var result = new DetectResult();
            result.IsClosed = sets.All(s => !s.Opened);

            if (shape == HandShape.Kokushi) {
                DetectKokushi(sets, winTile, result);
                Finalize(result);
                return result;
            }
            if (shape == HandShape.Chiitoitsu) {
                DetectChiitoitsu(sets, winTile, result);
                Finalize(result);
                return result;
            }

            result.WaitType = DetermineWait(sets, winTile);

            DetectYakuman(sets, winTile, result);
            if (result.YakumanMultiplier > 0) {
                Finalize(result);
                return result;
            }

            DetectNormalYaku(sets, winTile, result);
            DetectContextYaku(result);
            DetectDora(sets, result);

            Finalize(result);
            return result;
        }

        private void Finalize(DetectResult result) {
            if (result.YakumanMultiplier > 0) {
                result.HasYaku = true;
                return;
            }
            int nonBonusHan = 0;
            foreach (var y in result.Yaku) {
                if (y.Name != "宝牌" && y.Name != "赤宝牌" && y.Name != "里宝牌") {
                    nonBonusHan += y.Han;
                }
            }
            result.HasYaku = nonBonusHan > 0;
        }

        // ---------- 国士 / 七对 ----------

        private void DetectKokushi(List<RiichiSet> sets, int winTile, DetectResult result) {
            // sets 非一般型时不复用，这里只做回填。调用方会区分 shape；下面从传入的 winTile 判断十三面。
            var pairTile = sets.FirstOrDefault(s => s.Type == RiichiSetType.Pair)?.Tile ?? -1;
            bool thirteenWait = pairTile != -1 && pairTile == RiichiTileUtil.Normalize(winTile);
            int mult = thirteenWait ? 2 : 1;
            result.Yaku.Add(new YakuEntry { Name = thirteenWait ? "国士无双十三面" : "国士无双", IsYakuman = true, YakumanMultiplier = mult });
            result.YakumanMultiplier = mult;
        }

        private void DetectChiitoitsu(List<RiichiSet> sets, int winTile, DetectResult result) {
            result.Yaku.Add(new YakuEntry { Name = "七对子", Han = 2 });
            DetectCommonOnFlatTiles(CollectAllTiles(sets), winTile, result, isChiitoitsu: true);
            DetectContextYaku(result);
            DetectDora(sets, result);
        }

        // ---------- 一般型役 ----------

        private void DetectYakuman(List<RiichiSet> sets, int winTile, DetectResult result) {
            var triplets = sets.Where(s => s.Type == RiichiSetType.Pon || s.Type == RiichiSetType.Kan).ToList();
            var pair = sets.FirstOrDefault(s => s.Type == RiichiSetType.Pair);
            var allTiles = CollectAllTiles(sets);

            int dragonTriplets = triplets.Count(s => RiichiTileUtil.IsDragon(s.Tile));
            int windTriplets = triplets.Count(s => RiichiTileUtil.IsWind(s.Tile));
            bool pairIsDragon = pair != null && RiichiTileUtil.IsDragon(pair.Tile);
            bool pairIsWind = pair != null && RiichiTileUtil.IsWind(pair.Tile);

            int mult = 0;

            if (dragonTriplets == 3) {
                result.Yaku.Add(new YakuEntry { Name = "大三元", IsYakuman = true, YakumanMultiplier = 1 });
                mult += 1;
            }
            if (windTriplets == 4) {
                result.Yaku.Add(new YakuEntry { Name = "大四喜", IsYakuman = true, YakumanMultiplier = 2 });
                mult += 2;
            } else if (windTriplets == 3 && pairIsWind) {
                result.Yaku.Add(new YakuEntry { Name = "小四喜", IsYakuman = true, YakumanMultiplier = 1 });
                mult += 1;
            }
            if (allTiles.All(RiichiTileUtil.IsHonor)) {
                result.Yaku.Add(new YakuEntry { Name = "字一色", IsYakuman = true, YakumanMultiplier = 1 });
                mult += 1;
            }
            if (allTiles.All(RiichiTileUtil.IsGreen)) {
                result.Yaku.Add(new YakuEntry { Name = "绿一色", IsYakuman = true, YakumanMultiplier = 1 });
                mult += 1;
            }
            if (allTiles.All(RiichiTileUtil.IsTerminal)) {
                result.Yaku.Add(new YakuEntry { Name = "清老头", IsYakuman = true, YakumanMultiplier = 1 });
                mult += 1;
            }
            if (IsSuuankou(sets, winTile, out bool suuankouTanki)) {
                int m = suuankouTanki ? 2 : 1;
                result.Yaku.Add(new YakuEntry { Name = suuankouTanki ? "四暗刻单骑" : "四暗刻", IsYakuman = true, YakumanMultiplier = m });
                mult += m;
            }
            if (sets.Count(s => s.Type == RiichiSetType.Kan) == 4) {
                result.Yaku.Add(new YakuEntry { Name = "四杠子", IsYakuman = true, YakumanMultiplier = 1 });
                mult += 1;
            }
            if (IsChuurenPoutou(sets, winTile, result.IsClosed, out bool daburu)) {
                int m = daburu ? 2 : 1;
                result.Yaku.Add(new YakuEntry { Name = daburu ? "纯正九莲宝灯" : "九莲宝灯", IsYakuman = true, YakumanMultiplier = m });
                mult += m;
            }
            if (_ctx.IsTenhou) {
                result.Yaku.Add(new YakuEntry { Name = "天和", IsYakuman = true, YakumanMultiplier = 1 });
                mult += 1;
            }
            if (_ctx.IsChiihou) {
                result.Yaku.Add(new YakuEntry { Name = "地和", IsYakuman = true, YakumanMultiplier = 1 });
                mult += 1;
            }

            result.YakumanMultiplier = mult;
        }

        private void DetectNormalYaku(List<RiichiSet> sets, int winTile, DetectResult result) {
            var triplets = sets.Where(s => s.Type == RiichiSetType.Pon || s.Type == RiichiSetType.Kan).ToList();
            var sequences = sets.Where(s => s.Type == RiichiSetType.Chi).ToList();
            var pair = sets.FirstOrDefault(s => s.Type == RiichiSetType.Pair);
            var allTiles = CollectAllTiles(sets);

            // 役牌
            if (pair == null) return;
            var yakuhaiList = new List<string>();
            foreach (var t in triplets) {
                if (t.Tile == RiichiTileUtil.Haku) yakuhaiList.Add("役牌·白");
                else if (t.Tile == RiichiTileUtil.Hatsu) yakuhaiList.Add("役牌·发");
                else if (t.Tile == RiichiTileUtil.Chun) yakuhaiList.Add("役牌·中");
                if (t.Tile == _ctx.PlayerWind) yakuhaiList.Add("自风");
                if (t.Tile == _ctx.RoundWind && _ctx.RoundWind != _ctx.PlayerWind) yakuhaiList.Add("场风");
            }
            foreach (var name in yakuhaiList) {
                result.Yaku.Add(new YakuEntry { Name = name, Han = 1 });
            }

            // 断幺九
            if (allTiles.All(t => !RiichiTileUtil.IsYaojiu(t))) {
                bool allowed = _ctx.HasOpenTanyao || result.IsClosed;
                if (allowed) result.Yaku.Add(new YakuEntry { Name = "断幺九", Han = 1 });
            }

            // 平和
            if (result.IsClosed && sequences.Count == 4 && result.WaitType == RiichiWaitType.Ryanmen) {
                bool pairIsYakuhai = IsYakuhaiTile(pair.Tile);
                if (!pairIsYakuhai) {
                    result.Yaku.Add(new YakuEntry { Name = "平和", Han = 1 });
                }
            }

            // 一杯口 / 二杯口
            if (result.IsClosed && sequences.Count >= 2) {
                int peikouCount = CountIdenticalSequencePairs(sequences);
                if (peikouCount >= 2) {
                    result.Yaku.Add(new YakuEntry { Name = "二杯口", Han = 3 });
                } else if (peikouCount == 1) {
                    result.Yaku.Add(new YakuEntry { Name = "一杯口", Han = 1 });
                }
            }

            // 三色同顺
            if (HasSanshokuDoujun(sequences)) {
                result.Yaku.Add(new YakuEntry { Name = "三色同顺", Han = result.IsClosed ? 2 : 1 });
            }
            // 三色同刻
            if (HasSanshokuDoukou(triplets)) {
                result.Yaku.Add(new YakuEntry { Name = "三色同刻", Han = 2 });
            }
            // 一气通贯
            if (HasIttsu(sequences)) {
                result.Yaku.Add(new YakuEntry { Name = "一气通贯", Han = result.IsClosed ? 2 : 1 });
            }

            // 对对和
            bool allTripletsLike = triplets.Count == 4 && pair != null;
            if (allTripletsLike) result.Yaku.Add(new YakuEntry { Name = "对对和", Han = 2 });

            // 三暗刻
            int concealedTriplets = triplets.Count(s => !s.Opened);
            // 荣和完成的刻子视为明刻；IsSuuankou 的同款判断——这里的 Opened 是由拆解得来的 K/G 默认闭合
            // 如果当前刻子包含 winTile 且是荣和（非自摸），视为明刻
            if (!_ctx.IsTsumo) {
                var ronTriplet = triplets.FirstOrDefault(s => !s.Opened && s.Tile == RiichiTileUtil.Normalize(winTile));
                if (ronTriplet != null) concealedTriplets--;
            }
            if (concealedTriplets == 3 && !allTripletsLike) {
                result.Yaku.Add(new YakuEntry { Name = "三暗刻", Han = 2 });
            } else if (concealedTriplets == 3 && allTripletsLike) {
                // 对对和+三暗刻可同时存在
                result.Yaku.Add(new YakuEntry { Name = "三暗刻", Han = 2 });
            }

            // 三杠子
            int kanCount = sets.Count(s => s.Type == RiichiSetType.Kan);
            if (kanCount == 3) result.Yaku.Add(new YakuEntry { Name = "三杠子", Han = 2 });

            // 小三元
            int dragonTriplets = triplets.Count(s => RiichiTileUtil.IsDragon(s.Tile));
            if (dragonTriplets == 2 && RiichiTileUtil.IsDragon(pair.Tile)) {
                result.Yaku.Add(new YakuEntry { Name = "小三元", Han = 2 });
            }

            // 混老头
            if (allTiles.All(RiichiTileUtil.IsYaojiu) && allTiles.Any(RiichiTileUtil.IsHonor)) {
                result.Yaku.Add(new YakuEntry { Name = "混老头", Han = 2 });
            }

            // 混全带幺九 / 纯全带幺九
            bool allSetsHaveYaojiu = sets.All(s => s.ContainsYaojiu());
            bool allSetsHaveTerminal = sets.All(s => s.ContainsTerminal());
            bool hasHonor = allTiles.Any(RiichiTileUtil.IsHonor);
            bool hasSequence = sequences.Count > 0;
            if (allSetsHaveYaojiu && hasSequence) {
                if (!hasHonor && allSetsHaveTerminal) {
                    result.Yaku.Add(new YakuEntry { Name = "纯全带幺九", Han = result.IsClosed ? 3 : 2 });
                } else if (hasHonor) {
                    result.Yaku.Add(new YakuEntry { Name = "混全带幺九", Han = result.IsClosed ? 2 : 1 });
                }
            }

            // 混一色 / 清一色
            var suits = allTiles.Where(t => !RiichiTileUtil.IsHonor(t)).Select(t => RiichiTileUtil.Suit(t)).Distinct().ToList();
            bool singleSuit = suits.Count == 1;
            if (singleSuit && !hasHonor) {
                result.Yaku.Add(new YakuEntry { Name = "清一色", Han = result.IsClosed ? 6 : 5 });
            } else if (singleSuit && hasHonor) {
                result.Yaku.Add(new YakuEntry { Name = "混一色", Han = result.IsClosed ? 3 : 2 });
            } else if (allTiles.All(RiichiTileUtil.IsHonor)) {
                // 字一色已在役满中处理；这里不额外计。
            }
        }

        private void DetectContextYaku(DetectResult result) {
            if (_ctx.IsDaburuRiichi) {
                result.Yaku.Add(new YakuEntry { Name = "两立直", Han = 2 });
            } else if (_ctx.IsRiichi) {
                result.Yaku.Add(new YakuEntry { Name = "立直", Han = 1 });
            }
            if (_ctx.IsTsumo && result.IsClosed) result.Yaku.Add(new YakuEntry { Name = "门前清自摸", Han = 1 });
            if (_ctx.IsHaitei) result.Yaku.Add(new YakuEntry { Name = "海底摸月", Han = 1 });
            if (_ctx.IsHoutei) result.Yaku.Add(new YakuEntry { Name = "河底捞鱼", Han = 1 });
            if (_ctx.IsRinshan) result.Yaku.Add(new YakuEntry { Name = "岭上开花", Han = 1 });
            if (_ctx.IsChankan) result.Yaku.Add(new YakuEntry { Name = "抢杠", Han = 1 });
        }

        private void DetectCommonOnFlatTiles(List<int> allTiles, int winTile, DetectResult result, bool isChiitoitsu) {
            bool hasHonor = allTiles.Any(RiichiTileUtil.IsHonor);
            var suits = allTiles.Where(t => !RiichiTileUtil.IsHonor(t)).Select(t => RiichiTileUtil.Suit(t)).Distinct().ToList();
            bool singleSuit = suits.Count == 1;
            if (allTiles.All(t => !RiichiTileUtil.IsYaojiu(t))) {
                bool allowed = _ctx.HasOpenTanyao || result.IsClosed;
                if (allowed) result.Yaku.Add(new YakuEntry { Name = "断幺九", Han = 1 });
            }
            if (isChiitoitsu && allTiles.All(RiichiTileUtil.IsYaojiu) && hasHonor) {
                result.Yaku.Add(new YakuEntry { Name = "混老头", Han = 2 });
            }
            if (singleSuit && !hasHonor) {
                result.Yaku.Add(new YakuEntry { Name = "清一色", Han = result.IsClosed ? 6 : 5 });
            } else if (singleSuit && hasHonor) {
                result.Yaku.Add(new YakuEntry { Name = "混一色", Han = result.IsClosed ? 3 : 2 });
            }
        }

        private void DetectDora(List<RiichiSet> sets, DetectResult result) {
            var allTiles = CollectAllTilesForDora(sets);
            int dora = 0;
            foreach (int ind in _ctx.DoraIndicators) {
                int target = RiichiTileUtil.DoraFromIndicator(ind);
                foreach (int t in allTiles) {
                    if (RiichiTileUtil.Normalize(t) == target) dora++;
                }
            }
            if (dora > 0) result.Yaku.Add(new YakuEntry { Name = "宝牌", Han = dora });

            int aka;
            if (_ctx.AkaCount.HasValue) {
                aka = _ctx.AkaCount.Value;
            } else {
                aka = allTiles.Count(RiichiTileUtil.IsRedFive);
            }
            if (aka > 0) result.Yaku.Add(new YakuEntry { Name = "赤宝牌", Han = aka });

            if (_ctx.IsRiichi || _ctx.IsDaburuRiichi) {
                int ura = 0;
                foreach (int ind in _ctx.UraDoraIndicators) {
                    int target = RiichiTileUtil.DoraFromIndicator(ind);
                    foreach (int t in allTiles) {
                        if (RiichiTileUtil.Normalize(t) == target) ura++;
                    }
                }
                if (ura > 0) result.Yaku.Add(new YakuEntry { Name = "里宝牌", Han = ura });
            }
        }

        // ---------- 工具 ----------

        private bool IsYakuhaiTile(int tile) {
            if (RiichiTileUtil.IsDragon(tile)) return true;
            if (tile == _ctx.PlayerWind) return true;
            if (tile == _ctx.RoundWind) return true;
            return false;
        }

        private bool IsSuuankou(List<RiichiSet> sets, int winTile, out bool tanki) {
            tanki = false;
            var triplets = sets.Where(s => s.Type == RiichiSetType.Pon || s.Type == RiichiSetType.Kan).ToList();
            if (triplets.Count != 4) return false;
            if (triplets.Any(s => s.Opened)) return false;
            // 荣和完成的刻子视为明刻 → 不成立四暗刻，除非是单骑
            var pair = sets.FirstOrDefault(s => s.Type == RiichiSetType.Pair);
            if (pair == null) return false;
            int winNorm = RiichiTileUtil.Normalize(winTile);
            if (!_ctx.IsTsumo) {
                // 荣和：只有单骑和才算四暗刻（完成的是雀头而不是刻子）
                if (pair.Tile == winNorm) {
                    tanki = true;
                    return true;
                }
                return false;
            }
            tanki = pair.Tile == winNorm;
            return true;
        }

        private bool IsChuurenPoutou(List<RiichiSet> sets, int winTile, bool isClosed, out bool daburu) {
            daburu = false;
            if (!isClosed) return false;
            var allTiles = CollectAllTiles(sets);
            if (allTiles.Any(RiichiTileUtil.IsHonor)) return false;
            var suits = allTiles.Select(t => RiichiTileUtil.Suit(t)).Distinct().ToList();
            if (suits.Count != 1) return false;
            int suit = suits[0];
            int baseId = 11 + suit * 10;
            // 需要每个数字的张数匹配 1112345678999 模式（+1 张赢牌）
            int[] counts = new int[10];
            foreach (int t in allTiles) {
                int n = RiichiTileUtil.NumberInSuit(t);
                counts[n]++;
            }
            int[] baseCounts = { 0, 3, 1, 1, 1, 1, 1, 1, 1, 3 };
            int winNum = RiichiTileUtil.NumberInSuit(winTile);
            if (RiichiTileUtil.Suit(winTile) != suit) return false;
            // 九面待：若去掉胜牌后恰好为 111345678999 + 2X 的纯正形（即 1-9 各至少 1 张且 1/9 各 3 张）
            int[] without = (int[])counts.Clone();
            without[winNum]--;
            bool pureNineWait = without[1] == 3 && without[9] == 3;
            for (int n = 2; n <= 8 && pureNineWait; n++) {
                if (without[n] < 1) pureNineWait = false;
            }
            if (!pureNineWait) {
                // 普通九莲：整手满足 baseCounts + 1 张任意
                for (int n = 1; n <= 9; n++) {
                    if (counts[n] < baseCounts[n]) return false;
                }
                int total = 0;
                for (int n = 1; n <= 9; n++) total += counts[n];
                if (total != 14) return false;
                return true;
            }
            daburu = true;
            return true;
        }

        private int CountIdenticalSequencePairs(List<RiichiSet> sequences) {
            var counter = new Dictionary<string, int>();
            foreach (var s in sequences) {
                string key = $"{RiichiTileUtil.Suit(s.Tile)}_{s.Tile}";
                counter[key] = counter.TryGetValue(key, out int c) ? c + 1 : 1;
            }
            int pairs = 0;
            foreach (var kv in counter) {
                pairs += kv.Value / 2;
            }
            return pairs;
        }

        private bool HasSanshokuDoujun(List<RiichiSet> sequences) {
            // 同一 number（中张数字相同）在三种花色各出现至少一次
            for (int n = 2; n <= 8; n++) {
                bool m = sequences.Any(s => RiichiTileUtil.Suit(s.Tile) == 0 && RiichiTileUtil.NumberInSuit(s.Tile) == n);
                bool p = sequences.Any(s => RiichiTileUtil.Suit(s.Tile) == 1 && RiichiTileUtil.NumberInSuit(s.Tile) == n);
                bool sou = sequences.Any(s => RiichiTileUtil.Suit(s.Tile) == 2 && RiichiTileUtil.NumberInSuit(s.Tile) == n);
                if (m && p && sou) return true;
            }
            return false;
        }

        private bool HasSanshokuDoukou(List<RiichiSet> triplets) {
            for (int n = 1; n <= 9; n++) {
                bool m = triplets.Any(s => RiichiTileUtil.Suit(s.Tile) == 0 && RiichiTileUtil.NumberInSuit(s.Tile) == n);
                bool p = triplets.Any(s => RiichiTileUtil.Suit(s.Tile) == 1 && RiichiTileUtil.NumberInSuit(s.Tile) == n);
                bool sou = triplets.Any(s => RiichiTileUtil.Suit(s.Tile) == 2 && RiichiTileUtil.NumberInSuit(s.Tile) == n);
                if (m && p && sou) return true;
            }
            return false;
        }

        private bool HasIttsu(List<RiichiSet> sequences) {
            for (int suit = 0; suit < 3; suit++) {
                bool s123 = sequences.Any(x => RiichiTileUtil.Suit(x.Tile) == suit && RiichiTileUtil.NumberInSuit(x.Tile) == 2);
                bool s456 = sequences.Any(x => RiichiTileUtil.Suit(x.Tile) == suit && RiichiTileUtil.NumberInSuit(x.Tile) == 5);
                bool s789 = sequences.Any(x => RiichiTileUtil.Suit(x.Tile) == suit && RiichiTileUtil.NumberInSuit(x.Tile) == 8);
                if (s123 && s456 && s789) return true;
            }
            return false;
        }

        private RiichiWaitType DetermineWait(List<RiichiSet> sets, int winTile) {
            int w = RiichiTileUtil.Normalize(winTile);
            // 对每个闭合的、含 winTile 的 set 评估待牌类型，取"最优"（ryanmen > 其余）
            RiichiWaitType best = RiichiWaitType.None;
            foreach (var s in sets) {
                if (s.Opened) continue;
                if (s.Type == RiichiSetType.Pair && s.Tile == w) best = Max(best, RiichiWaitType.Tanki);
                else if (s.Type == RiichiSetType.Pon && s.Tile == w) best = Max(best, RiichiWaitType.Shanpon);
                else if (s.Type == RiichiSetType.Chi) {
                    int mid = s.Tile;
                    if (w == mid) best = Max(best, RiichiWaitType.Kanchan);
                    else if (w == mid - 1) {
                        if (RiichiTileUtil.NumberInSuit(mid + 1) == 9) best = Max(best, RiichiWaitType.Penchan);
                        else best = Max(best, RiichiWaitType.Ryanmen);
                    } else if (w == mid + 1) {
                        if (RiichiTileUtil.NumberInSuit(mid - 1) == 1) best = Max(best, RiichiWaitType.Penchan);
                        else best = Max(best, RiichiWaitType.Ryanmen);
                    }
                }
            }
            return best;
        }

        private static RiichiWaitType Max(RiichiWaitType a, RiichiWaitType b) {
            // Ryanmen 优先（利于平和）；其次 Penchan/Kanchan/Tanki/Shanpon 无优劣差异，但保持顺序
            int RankOf(RiichiWaitType t) {
                switch (t) {
                    case RiichiWaitType.Ryanmen: return 5;
                    case RiichiWaitType.Penchan: return 4;
                    case RiichiWaitType.Kanchan: return 3;
                    case RiichiWaitType.Tanki: return 2;
                    case RiichiWaitType.Shanpon: return 1;
                    default: return 0;
                }
            }
            return RankOf(a) >= RankOf(b) ? a : b;
        }

        public static List<int> CollectAllTiles(List<RiichiSet> sets) {
            var list = new List<int>();
            foreach (var s in sets) {
                foreach (int t in s.Tiles()) list.Add(t);
            }
            return list;
        }

        /// <summary>
        /// 宝牌统计用全牌集合：手牌区 + 副露/暗杠（与服务端 _collect_all_tile_ids 一致）。
        /// </summary>
        private List<int> CollectAllTilesForDora(List<RiichiSet> sets) {
            if (_ctx.WinHandTileIds != null) {
                var allTiles = new List<int>(_ctx.WinHandTileIds);
                if (_ctx.CombinationMasks != null && _ctx.CombinationMasks.Count > 0) {
                    foreach (var mask in _ctx.CombinationMasks) {
                        allTiles.AddRange(RiichiTileUtil.TilesFromMask(mask));
                    }
                } else if (_ctx.OpenCombinationTiles != null) {
                    foreach (var combo in _ctx.OpenCombinationTiles) {
                        allTiles.AddRange(TilesFromCombination(combo));
                    }
                }
                return allTiles;
            }
            return CollectAllTiles(sets);
        }

        internal static List<int> TilesFromCombination(string combo) {
            var list = new List<int>();
            if (string.IsNullOrEmpty(combo) || combo.Length < 2) return list;
            var sets = RiichiSet.ParseCombinationList(new List<string> { combo });
            return CollectAllTiles(sets);
        }
    }

    public enum HandShape { Normal, Chiitoitsu, Kokushi }
}
