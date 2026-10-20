#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Linq;
using UnityEngine;

public sealed partial class HongqueRuntimeValidation {
    static Vector3 RowDirection(string seat) => seat == "left" ? Vector3.back : seat == "top" ? Vector3.left : Vector3.forward;

    void Check3DRow(Transform row, string seat, int expectedCount, bool hasDraw, bool visible = false, bool requireDrawSlot = true) {
        Vector3 direction = RowDirection(seat);
        var tiles = row.Cast<Transform>().OrderBy(tile => Vector3.Dot(tile.position, direction)).ToArray();
        Check(tiles.Length == expectedCount, "draw layout count " + seat, new { expectedCount, actual = tiles.Length });
        float step = (float)typeof(Game3DManager).GetField("handStep", Hidden).GetValue(Game3DManager.Instance);
        int mainCount = tiles.Length - (hasDraw ? 1 : 0);
        float[] gaps = tiles.Skip(1).Select((tile, i) => Vector3.Dot(tile.position - tiles[i].position, direction)).ToArray();
        Check(gaps.Take(Math.Max(0, mainCount - 1)).All(gap => Math.Abs(gap - step) < .02f),
            "main row has no holes " + seat, gaps);
        Check(gaps.All(gap => gap > step * .5f), "draws never overlap " + seat, gaps);
        // 牌谱隐藏牌面跳转时既有设计将整手铺成主列；增量摸牌与展开模式保留独立槽。
        if (hasDraw && tiles.Length > 1 && requireDrawSlot) Check(gaps.Last() > step, "only newest draw is separated " + seat, gaps);
        if (visible) Check(row.GetComponentsInChildren<Tile3D>().Count(tile => tile.isRecordDrawSlotPinned) == (hasDraw ? 1 : 0),
            "revealed row has one draw marker " + seat);
    }

    void Check2DRow(int expectedCount, int? winningDraw = null) {
        var cards = GameCanvas.Instance.HandCardsContainer.GetComponentsInChildren<TileCard>();
        Check(cards.Length == expectedCount, "2D draw preserves every tile");
        var marked = cards.Where(card => card.isDrawSlotPinned).ToArray();
        Check(marked.Length == 1 && cards.Count(card => card.currentGetTile) == 1, "2D has one physical and semantic draw slot");
        if (winningDraw.HasValue) Check(marked.Length == 1 && marked[0].tileId == winningDraw.Value, "newest 2D draw owns the slot");
        var x = cards.Where(card => !card.isDrawSlotPinned).Select(card => ((RectTransform)card.transform).anchoredPosition.x).OrderBy(v => v).ToArray();
        var gaps = x.Skip(1).Select((v, i) => v - x[i]).ToArray();
        Check(gaps.Length < 2 || gaps.All(gap => gap > 0 && Math.Abs(gap - gaps[0]) < .02f), "2D main row is contiguous", gaps);
    }

    void CheckDrawLayout(HongqueStateInfo state) {
        if (state == null || state.phase != "turn") return;
        var gsm = NormalGameStateManager.Instance;
        foreach (var player in state.players) {
            string seat = gsm.indexToPosition[player.index];
            bool draw = state.pending_draw?.player == player.index;
            if (seat == "self") {
                if (draw) Check2DRow(state.hand.Length, HongqueTileVisual.FromCode(state.pending_draw.tile));
            } else Check3DRow(Game3DManager.Instance.GetPosPanel(seat).cardsPosition, seat, player.hand_count, draw);
        }
    }

    IEnumerator ProbeDrawLayout() {
        var manager = Game3DManager.Instance;
        int discardedTile = 1131;
        foreach (string seat in new[] { "left", "top", "right" }) {
            Transform row = manager.GetPosPanel(seat).cardsPosition;
            int count = row.childCount;
            // 同帧连续到达会让旧协程在 yield 之前缓存相同落点。
            manager.Change3DTile("GetCard", 0, 0, seat, false, null);
            manager.Change3DTile("GetCard", 0, 0, seat, false, null);
            yield return Idle();
            Check3DRow(row, seat, count + 2, true);
            // 收拢尚未结束就补第三张，随后手切；旧动画不能把已移出的牌拉回。
            manager.Change3DTile("GetCard", 0, 0, seat, false, null);
            yield return null;
            manager.Change3DTile("Discard", discardedTile++, 0, seat, false, null);
            yield return Idle();
            Check3DRow(row, seat, count + 2, false);
            // 虹雀加牌入副露后立即连续补摸，沿同座队列执行。
            manager.RemoveHongqueKongHandTile(seat, 1002);
            manager.Change3DTile("GetCard", 0, 0, seat, false, null);
            manager.Change3DTile("GetCard", 0, 0, seat, false, null);
            yield return Idle();
            Check3DRow(row, seat, count + 3, true);
        }
        int selfCount = GameCanvas.Instance.HandCardsContainer.childCount;
        int[] oldOrder = GameCanvas.Instance.HandCardsContainer.GetComponentsInChildren<TileCard>()
            .OrderBy(card => card.handSortIndex).Select(card => card.tileId).ToArray();
        GameCanvas.Instance.ChangeHandCards("GetCard", 1121, null, null);
        GameCanvas.Instance.ChangeHandCards("GetCard", 1122, null, null);
        yield return Idle(); yield return new WaitForSeconds(.2f);
        Check2DRow(selfCount + 2, 1122);
        if (!AutoAction.Instance.IsAutoArrangeHandCards) {
            int[] mainOrder = GameCanvas.Instance.HandCardsContainer.GetComponentsInChildren<TileCard>()
                .Where(card => !card.isDrawSlotPinned).OrderBy(card => card.handSortIndex).Select(card => card.tileId).ToArray();
            Check(mainOrder.SequenceEqual(oldOrder.Concat(new[] { 1121 })), "manual hand order survives consecutive draws");
        }
        GameCanvas.Instance.ChangeHandCards("RemoveGetCard", 1122, null, null);
        yield return Idle();
        Check(GameCanvas.Instance.HandCardsContainer.childCount == selfCount + 1, "supplement then discard removes only the new draw");
    }

    // 仅诊断国标开局补花共用的收拢时序，不改变 TurnClock 或补花结束逻辑。
    IEnumerator ProbeOpeningConsolidation() {
        var manager = Game3DManager.Instance;
        Transform row = manager.GetPosPanel("left").cardsPosition;
        int count = row.childCount;
        manager.Change3DTile("GetCard", 0, 0, "left", false, null);
        manager.CheckAndRearrangeAllPlayersHandCards("self");
        yield return Idle();
        Check3DRow(row, "left", count + 1, true);
        var clock = new TurnClock();
        clock.BeginHandAsk("top", 0, 2);
        yield return Idle();
        Check3DRow(row, "left", count + 1, true);
        clock.BeginHandAsk("top", 0, 2);
        yield return Idle();
        Check3DRow(row, "left", count + 1, true);
        // 换人后检查已存在的牌才会收拢；上面的三个遗漏条件均保留现状。
        clock.BeginHandAsk("right", 0, 1);
        yield return new WaitForSeconds(.6f);
        yield return Idle();
        Check3DRow(row, "left", count + 1, false);
    }
}
#endif
