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
        var positionsBySeat = NormalGameStateManager.Instance.indexToPosition;
        snapshot.phase = "exchange_dice";
        snapshot.public_state.exchange_round = snapshot.public_state.completed_exchange_rounds + 1;
        foreach (var (dice, source, expected) in new[] {
            (new[] {4, 6}, 1, "合计10点，本局与下家交换"),
            (new[] {2, 3}, 2, "合计5点，本局与对家交换"),
            (new[] {6, 6}, 3, "合计12点，本局与上家交换")
        }) {
            snapshot.public_state.dice = dice;
            snapshot.public_state.source_offset = source;
            canvas.ShowExchangeSixState(snapshot, false);
            canvas.PlayChangshaRecordTransfer(positionsBySeat, null);
            yield return new WaitForSecondsRealtime(2.1f);
            var prompt = canvas.transform.Find("ChangshaTableNotice/Prompt").GetComponent<TMPro.TMP_Text>();
            Check(prompt.text == expected && prompt.isActiveAndEnabled, "incorrect settled exchange subtitle " + expected);
            Check(GameObject.Find("ChangshaTableDice/RedDie")?.GetComponent<MeshRenderer>() &&
                GameObject.Find("ChangshaTableDice/IvoryDie")?.GetComponent<MeshFilter>().sharedMesh.subMeshCount == 3,
                "exchange uses table mesh dice with recessed pips");
            canvas.StopChangshaRecordTransfer();
        }
#if UNITY_EDITOR
        var normals = new[] {Vector3.zero,Vector3.up,Vector3.forward,Vector3.right,Vector3.left,Vector3.back,Vector3.down};
        snapshot.phase = "deal_dice";
        snapshot.restoring = true;
        snapshot.public_state.small_hu_forbidden_seat = 0;
        for (int i = 1; i <= 6; i++) {
            snapshot.public_state.deal_dice = new[]{i, 7-i};
            canvas.ShowExchangeSixState(snapshot, false);
            var pair = GameObject.Find("ChangshaTableDice").transform;
            Game3DManager.Instance.GetDiceTablePlane(out _, out var up);
            Check(Vector3.Dot(pair.Find("RedDie").TransformDirection(normals[i]),up) > .999f &&
                Vector3.Dot(pair.Find("IvoryDie").TransformDirection(normals[7-i]),up) > .999f, "server dice must face upward");
        }
        var notice = (RectTransform)canvas.transform.Find("ChangshaTableNotice");
        var title = notice.Find("Title").GetComponent<TMPro.TMP_Text>();
        Check(title.text == "合计7点，本家本局禁小胡" && !notice.Find("Prompt").gameObject.activeSelf, "single result line");
        foreach (string line in new[]{"开局掷骰","换牌掷骰","合计0123456789点，本家上下对家本局禁小胡"}) {
            TableText.Set(title, line);
            title.ForceMeshUpdate();
            Check(title.font == TableHudTypography.Emphasis && title.textInfo.characterInfo.Take(title.textInfo.characterCount)
                .All(c => c.fontAsset == title.font), "mixed weights in " + line);
        }
        Check(notice.Find("Shade").GetComponent<SbrExchangeGraphic>().isActiveAndEnabled, "dice result uses the table shade");
        var table = (RectTransform)canvas.transform;
        Check(BoardCanvas.Instance.TryGetExchangePoints(table, "self", out Vector2 center, out _) &&
            Vector2.Distance(table.InverseTransformPoint(notice.position), center) < 1f, "dice result stays at the table center");
        Vector3 initial = notice.position;
        var handPosition = hand.localPosition;
        try {
            hand.localPosition += new Vector3(55, 83, 0);
            yield return null;
            yield return null;
            Check(Vector3.Distance(notice.position, initial) < .1f, "lifting the hand does not move the result");
        } finally {
            hand.localPosition = handPosition;
        }
        snapshot.phase = "exchange_dice";
        snapshot.public_state.completed_exchange_rounds = snapshot.public_state.exchange_round;
        canvas.ShowExchangeSixState(snapshot, false);
        Check(!notice.gameObject.activeInHierarchy && !GameObject.Find("ChangshaTableDice"), "received hand has no intermediate panel");
        snapshot.phase = "exchange_repeat_choice";
        canvas.ShowExchangeSixState(snapshot, false);
        Check(!notice.gameObject.activeInHierarchy, "the next choice has no dice or receipt caption");
        canvas.HideExchangeSixState();
        Check(!notice.gameObject.activeInHierarchy, "leaving the game clears the receipt");
#endif
        Debug.Log("CHANGSHA_DRAW_HAND_NATIVE_PASSED checks=" + checks);
    }
}
