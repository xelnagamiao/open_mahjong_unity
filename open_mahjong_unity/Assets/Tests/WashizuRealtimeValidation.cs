#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;

public static class WashizuRealtimeValidation {
    public static object Run() {
        var checks = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        var logical = new List<int> { 11, 2019, 2019, 22 };
        var order = new WashizuHandOrder(logical);
        Check(order.Snapshot(1, true).tiles.SequenceEqual(new[] { 11, 2019, 2019, 22 }), "initial dealt order preserved");
        Check(order.Reorder(new[] {22,2019,11,2019}, -1, false), "interleave glass and opaque");
        Check(logical.SequenceEqual(new[] {11,2019,2019,22}), "logical draw-last hand never reordered");
        Check(order.Snapshot(1, false).tiles.SequenceEqual(new[] {0,2019,0,2019}), "opponent opaque values masked in custom positions");
        var keys = order.Snapshot(1, true).keys;
        Check(!order.Reorder(new[] {22,19,11,2019}, -1, false), "reject lost transparency identity");
        Check(order.Snapshot(1, true).keys.SequenceEqual(keys), "rejected order atomic");
        logical.Add(2019); order.Reconcile(logical, true);
        var drawn = order.Snapshot(1, true);
        Check(drawn.draw_pinned && drawn.keys.Last() == drawn.drawn_key, "new draw gets independent slot and key");
        Check(!order.Reorder(new[] {2019,22,2019,11,2019}, 0, true), "pinned draw cannot occupy middle slot");
        Check(order.Reorder(new[] {2019,22,2019,11,2019}, 0, false), "draw can merge at left");
        Check(order.Snapshot(1,true).keys[0] == drawn.drawn_key, "duplicate draw retains physical identity");
        Check(!order.Snapshot(1,true).draw_pinned && logical.Last()==2019, "merged draw retains cut semantics");
        logical.Remove(11); order.Reconcile(logical, false);
        Check(order.Snapshot(1,true).tiles.SequenceEqual(new[]{2019,22,2019,2019}), "discard preserves surviving relative order");
        logical.Remove(2019); logical.Remove(2019); order.Reconcile(logical, false);
        Check(order.Snapshot(1,true).tiles.SequenceEqual(new[]{22,2019}), "meld consumes first matching copies and preserves survivors");
        logical.Add(2041); order.Reconcile(logical, true);
        Check(order.Snapshot(1,true).tiles.SequenceEqual(new[]{22,2019,2041}), "replacement appended without sorting");
        var duplicate = new WashizuHandOrder(new[]{2019,11,2019,22});
        duplicate.Reconcile(new[]{2019,11,2019,22,2019},true);
        duplicate.Reorder(new[]{2019,22,2019,11,2019},0,false);
        int drawnKey = duplicate.Snapshot(1,true).drawn_key;
        Check(duplicate.NoteCut(2019,true)==0,"merged duplicate draw removed from its actual position");
        duplicate.Reconcile(new[]{2019,11,2019,22},false);
        Check(duplicate.Snapshot(1,true).tiles.SequenceEqual(new[]{22,2019,11,2019}),"moqie does not move another same-face tile");
        Check(!duplicate.Snapshot(1,true).keys.Contains(drawnKey),"draw physical key removed");
        var selected = duplicate.Snapshot(1,true);
        Check(duplicate.NoteCut(2019,false,selected.keys[3])==3,"record selected same-face physical copy");
        duplicate.Reconcile(new[]{2019,11,22},false);
        Check(duplicate.Snapshot(1,true).tiles.SequenceEqual(new[]{22,2019,11}),"specific hand cut preserves other duplicate positions");
        var same = new WashizuHandOrder(new[]{2019,2019,11});
        var sameLayout = same.Snapshot(1,true);
        Check(same.Reorder(sameLayout.tiles,-1,false,new[]{sameLayout.keys[1],sameLayout.keys[0],sameLayout.keys[2]}),"identical copies can exchange exact physical positions");
        Check(same.Snapshot(1,true).keys[0]==sameLayout.keys[1],"physical order stored even when visible faces equal");
        Check(WashizuHandOrder.OrderSubset(new[]{2019,22},new[]{22,2041,2019}).SequenceEqual(new[]{22,2019}), "settlement excludes winning tile but retains mixed order");
        Check(GameRecordJsonDecoder.ResolveRecordActingPlayerIndex(new List<string>{"wo","2","[]","-1","F"},"wo",0)==2, "record reorder actor independent of active turn");
        Check(WashizuTile.IsWashizu(WashizuTile.RealtimeSubRule) && WashizuTile.UsesTransparentHands(WashizuTile.RealtimeSubRule), "new variant inherits washizu glass and pool rules");
        Check(PlayerInfoRuleCatalog.SubRule("riichi_washizu_realtime")==WashizuTile.RealtimeSubRule, "independent statistics category");
        Check(new WashizuHandOrder(logical, true).Snapshot(1,true).tiles.SequenceEqual(new[]{2019,2041,22}), "legacy grouped records retain original physical keys");
        Check(WashizuTile.IsRealtimeSubRule(GuobiaoGameState.TransparentRealtimeSubRule)
            && WashizuTile.UsesTransparentHands(GuobiaoGameState.TransparentRealtimeSubRule)
            && !WashizuTile.IsWashizu(GuobiaoGameState.TransparentRealtimeSubRule), "MCR realtime shares order and glass without Riichi pool rules");
        Check(PlayerInfoRuleCatalog.SubRule("guobiao_transparent_realtime")==GuobiaoGameState.TransparentRealtimeSubRule, "MCR realtime independent statistics category");
        return new { passed=checks.Count, checks };
    }
}
#endif
