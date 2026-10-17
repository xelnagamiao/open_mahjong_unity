#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

/// <summary>从后台 Pipeline 显式运行。穷举合法牌组与全部非空花色集合，不依赖活动场景。</summary>
public static class HongqueDisplayOrderValidation {
    public static string Run(bool checkStandardTiles = true) {
        int checks = 0, groups = 0, hands = 0, changed = 0;
        void Check(bool value, string label) {
            if (!value) throw new InvalidOperationException("HongqueDisplayOrder: " + label);
            checks++;
        }
        int[] Ids(params string[] codes) => codes.Select(HongqueTileVisual.FromCode).ToArray();
        int Colour(int id) => (id - 1000) / 10;
        int Mod(int n) => (n % 14 + 14) % 14;
        bool Continuous(int[] ids) {
            int colourStep = Mod(Colour(ids[1]) - Colour(ids[0]));
            int numberStep = ids[1] % 10 - ids[0] % 10;
            if (!(new[] {0, 1, 2, 12, 13}).Contains(colourStep)
                || Math.Abs(numberStep) > 4 || (colourStep == 0 && numberStep == 0)) return false;
            return Enumerable.Range(1, ids.Length - 1).All(i =>
                Mod(Colour(ids[i]) - Colour(ids[i-1])) == colourStep
                && ids[i] % 10 - ids[i-1] % 10 == numberStep);
        }
        var universe = new Dictionary<string, int[]>();
        void Add(int[] ids) {
            int[] sorted = ids.OrderBy(id => id).ToArray();
            universe[string.Join(",", sorted)] = sorted;
        }
        for (int c = 0; c < 14; c++) for (int n = 1; n <= 9; n++) {
            foreach (int dc in new[] {1, 2}) for (int len = 3; len <= (dc == 1 ? 14 : 7); len++)
                Add(Enumerable.Range(0,len).Select(i => 1000+Mod(c+dc*i)*10+n).ToArray());
            for (int dn = -4; dn <= 4; dn++) if (dn != 0)
                for (int dc = 0; dc <= 2; dc++) for (int len = 3; len <= 9; len++) {
                    int end = n+dn*(len-1); if (end < 1 || end > 9) break;
                    Add(Enumerable.Range(0,len).Select(i => 1000+Mod(c+dc*i)*10+n+dn*i).ToArray());
                }
        }
        Check(universe.Count == 4787, "authoritative legal group count");
        foreach (int[] input in universe.Values) {
            int[] source = (int[])input.Clone();
            int[] ordered = HongqueDisplayOrder.Meld(source);
            var tick = new List<string> { "p", input[0].ToString(), "0" };
            tick.AddRange(input.Skip(1).Select(id => id.ToString()));
            var removed = GameRecordMeldCodec.ResolveHandTiles(tick, "p", input[0]);
            Check(removed.SequenceEqual(input.Skip(1)), "record keeps every hand tile in long claim");
            int[] claimMask = GameRecordMeldCodec.BuildMingpaiMask("p", input[0], removed, "left");
            Check(HongqueDisplayOrder.MeldMask(claimMask).Where((_,i)=>i%2==1).SequenceEqual(ordered), "long claim replay display matches meld");
            Check(Continuous(ordered), "adjacent colour/number progression " + string.Join(",",input));
            Check(input.SequenceEqual(source) && ordered.OrderBy(x=>x).SequenceEqual(input), "no mutation or lost tiles");
            Check(HongqueDisplayOrder.Meld(ordered).SequenceEqual(ordered), "idempotent meld");
            Check(HongqueDisplayOrder.Meld(input.Reverse()).SequenceEqual(ordered), "input permutation independent");
            Check(HongqueDisplayOrder.Meld(input.Skip(1).Concat(input.Take(1))).SequenceEqual(ordered), "rotated input independent");
            Check(HongqueDisplayOrder.SettlementHand(input).SequenceEqual(ordered), "complete hand group agrees");
            int[] mask = input.SelectMany((id,i) => new[] {i == 0 ? 1 : i == input.Length-1 ? 3 : 0,id}).ToArray();
            int[] display = HongqueDisplayOrder.MeldMask(mask);
            Check(GameRecordMeldCodec.ExtractTileByFlag(display,1) == input[0]
                && GameRecordMeldCodec.ExtractTileByFlag(display,3) == input[input.Length-1], "source/added identity follows tile");
            var slots = SettlementMeldLayoutBuilder.Build(new[]{mask})[0];
            Check(slots.Select(s=>s.TileId).SequenceEqual(ordered)
                && slots.All(s=>!s.Sideways && !s.StackedTileId.HasValue), "settlement order and vertical extension");
            if (!input.SequenceEqual(ordered)) changed++;
            groups++;
        }
        for (int subset = 1; subset < (1 << 14); subset++) {
            int[] source = Enumerable.Range(0,14).Where(c => (subset & (1<<c)) != 0)
                .Select(c => 1000+c*10+c%9+1).Reverse().ToArray();
            int[] playingHand = (int[])source.Clone(); TileIdOrder.SortHand(playingHand);
            Check(playingHand.SequenceEqual(source.OrderBy(x=>x)), "playing hand retains table order for every colour subset");
            int[] actual = EndResultPanel.SortSettlementHand(source);
            Check(actual.OrderBy(x=>x).SequenceEqual(source.OrderBy(x=>x)), "hand conserves tiles");
            Check(HongqueDisplayOrder.SettlementHand(actual).SequenceEqual(actual), "hand idempotent");
            Check(HongqueDisplayOrder.SettlementHand(source.Reverse()).SequenceEqual(actual), "hand independent of incoming order");
            if (actual.Length > 1 && HongqueScoring.ClassifyMeld(actual.ToList()) == null) {
                int[] colours = actual.Select(Colour).OrderBy(x=>x).ToArray();
                int maxGap = Enumerable.Range(0,colours.Length).Max(i => Mod(colours[(i+1)%colours.Length]-colours[i]));
                int occupied = 0;
                for (int i=1;i<actual.Length;i++) {
                    int d = Mod(Colour(actual[i])-Colour(actual[i-1])); occupied += Math.Min(d,14-d);
                }
                Check(occupied == 14-maxGap, "hand uses shortest occupied colour arc");
            }
            hands++;
        }
        Check(HongqueDisplayOrder.Meld(Ids("AX5","GX5","GY5")).SequenceEqual(Ids("AX5","GY5","GX5")), "reported red-purple-wrap case");
        Check(HongqueDisplayOrder.SettlementHand(Ids("AX6","FX2","GY4","AX1","GX5")).SequenceEqual(Ids("AX1","AX6","GY4","GX5","FX2")), "same colour numbers stay ascending");
        foreach (int[] malformed in new[]{new[]{0,1005,101,1135,0,1125},new[]{0,1005,2,1135,0,1125},new[]{0,1005,0,1005,0,1125},new[]{0,1005,0,1125,0}})
            Check(HongqueDisplayOrder.MeldMask(malformed).SequenceEqual(malformed), "no reinterpretation of malformed/hidden/stacked mask");
        if (checkStandardTiles) {
            int[] normal = {205,25,12,11,45,47}; int[] expected=(int[])normal.Clone(); Array.Sort(expected,TileIdOrder.Comparer);
            TileIdOrder.SortHand(normal); Check(normal.SequenceEqual(expected), "standard tiles/red five unchanged");
        }
        var masks = new List<int[]> {Ids("AX5","GX5","GY5").SelectMany(id=>new[]{0,id}).ToArray(), Ids("CX1","CX2","CX3").SelectMany(id=>new[]{0,id}).ToArray()};
        var combinations = new List<string>{"k1005","s1041"}; int[] untouched=(int[])masks[1].Clone();
        var extended = HongqueDisplayOrder.ExtendRecordedMeld(masks,combinations,0,HongqueTileVisual.FromCode("AY5"));
        Check(masks[1].SequenceEqual(untouched) && extended.Length==8 && extended.Where((_,i)=>i%2==0).All(s=>s==0), "extend explicit non-last group");
        Check(Continuous(extended.Where((_,i)=>i%2==1).ToArray()), "extension reorders whole group");
        bool rejected=false; try { HongqueDisplayOrder.ExtendRecordedMeld(masks,combinations,1,HongqueTileVisual.FromCode("GX9")); } catch(InvalidOperationException) {rejected=true;}
        Check(rejected && masks[1].SequenceEqual(untouched), "invalid target rejected atomically");
        return JsonConvert.SerializeObject(new {passed=checks,groups,hands,changed});
    }
}
#endif
