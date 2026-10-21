using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

// 仅在隔离场景主动运行，检查真实手牌队列、对象身份、位置和摸牌标记。
public static class ChangshaExchangeHandValidation {
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    public static IEnumerator Run(GameCanvas canvas) {
        var hand = (Transform)typeof(GameCanvas).GetField("handCardsContainer", Flags).GetValue(canvas);
        var sync = typeof(GameCanvas).GetMethod("SyncExchangeSixHand", Flags);
        var pending = typeof(GameCanvas).GetField("exchangeSixHandSync", Flags);
        int checks = 0;
        void Check(bool ok, string label) {
            if (!ok) throw new Exception("ChangshaExchangeHand: " + label);
            checks++;
        }
        IEnumerator WaitForHand() {
            int frames = 0;
            while (canvas.IsChangeHandCardProcessing || pending.GetValue(canvas) != null) {
                if (++frames > 600) throw new Exception("ChangshaExchangeHand: hand queue did not finish");
                yield return null;
            }
        }
        var tiles = new[] {11,12,13,14,15,16,17,21,22,23,24,31,33};
        var snapshot = new ChangshaExchangeSixState {
            phase = "hand", action_tick = 100,
            public_state = new ChangshaExchangeSixPublic(),
            private_state = new ChangshaExchangeSixPrivate {hand_tiles = tiles.Concat(new[] {18}).ToArray(), has_draw_slot = true}
        };
        canvas.HideExchangeSixState();
        canvas.ChangeHandCards("SyncHandCards", 0, tiles, null);
        yield return WaitForHand();
        var main = canvas.GetMainHandCardsOrdered();
        main.Reverse();
        for (int i = 0; i < main.Count; i++) main[i].handSortIndex = i;
        canvas.LayoutHandCardsFromCurrentOrder();
        var ids = main.Select(card => card.GetInstanceID()).ToArray();
        var positions = main.Select(card => ((RectTransform)card.transform).anchoredPosition).ToArray();
        canvas.ChangeHandCards("GetCard", 18, null, null);
        yield return WaitForHand();
        var draw = canvas.GetDrawTile();
        yield return canvas.StartCoroutine((IEnumerator)sync.Invoke(canvas, new object[] {snapshot, false}));
        Check(ids.SequenceEqual(canvas.GetMainHandCardsOrdered().Select(card => card.GetInstanceID())), "draw keeps manual order and original card objects");
        Check(main.Select((card, i) => Vector2.Distance(((RectTransform)card.transform).anchoredPosition, positions[i]) < .01f).All(value => value), "draw snapshot leaves main-hand positions unchanged");
        Check(canvas.GetDrawTile() == draw && draw.tileId == 18 && draw.isDrawSlotPinned, "draw slot retains the actual drawn card");

        canvas.ShowExchangeSixState(snapshot, true);
        yield return WaitForHand();
        Check(ids.SequenceEqual(canvas.GetMainHandCardsOrdered().Select(card => card.GetInstanceID())), "real snapshot display does not rebuild matching cards");
        Check(pending.GetValue(canvas) == null, "no-op snapshot coroutine clears its handle");
        draw.isDrawSlotPinned = false;
        canvas.LayoutHandCardsFromCurrentOrder();
        canvas.ShowExchangeSixState(snapshot, false);
        yield return WaitForHand();
        Check(canvas.GetDrawTile() == draw && !draw.isDrawSlotPinned, "snapshot respects a drawn card dragged into the main hand");

        canvas.HideExchangeSixState();
        canvas.ChangeHandCards("SyncHandCards", 0, tiles, null);
        yield return WaitForHand();
        var preserved = canvas.GetMainHandCardsOrdered().Select(card => card.GetInstanceID()).ToArray();
        yield return canvas.StartCoroutine((IEnumerator)sync.Invoke(canvas, new object[] {snapshot, false}));
        Check(preserved.All(id => hand.GetComponentsInChildren<TileCard>().Any(card => card.GetInstanceID() == id)), "missing draw correction retains all thirteen original cards");
        Check(hand.GetComponentsInChildren<TileCard>().Length == 14 && canvas.GetDrawTile()?.tileId == 18, "reconnect adds only the missing drawn tile");

        canvas.ShowExchangeSixState(snapshot, false);
        yield return WaitForHand();
        snapshot = new ChangshaExchangeSixState {
            phase = "playing", action_tick = 101,
            public_state = new ChangshaExchangeSixPublic {completed_exchange_rounds = 1},
            private_state = snapshot.private_state
        };
        canvas.ShowExchangeSixState(snapshot, true);
        yield return WaitForHand();
        Check(!hand.GetComponentsInChildren<TileCard>().Any(card => preserved.Contains(card.GetInstanceID())), "completed exchange still rebuilds the exchanged hand");
        Check(pending.GetValue(canvas) == null, "completed exchange does not leave receipt confirmation waiting");
        Debug.Log("CHANGSHA_DRAW_HAND_NATIVE_PASSED checks=" + checks);
    }
}
