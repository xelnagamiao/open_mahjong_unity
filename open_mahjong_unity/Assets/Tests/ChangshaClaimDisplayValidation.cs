using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;

// 隔离原生场景显式运行，使用正式操作栏、牌面、对象池和光圈。
public static class ChangshaClaimDisplayValidation {
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    public static int Checks { get; private set; }
    private static void Check(bool ok, string message) {
        if (!ok) throw new Exception("ChangshaClaimDisplay: " + message);
        Checks++;
    }
    private static T Field<T>(object owner, string field) => (T)owner.GetType().GetField(field, Fields).GetValue(owner);

    public static IEnumerator Run(GameCanvas canvas, Game3DManager manager) {
        Checks = 0;
        GameSession.Current.SubRule = ChangshaGameState.ExchangeSixSubRule;
        var active = (ChangshaGameState)RuleRegistry.SetCurrent("changsha", ChangshaGameState.ExchangeSixSubRule);
        var property = typeof(ChangshaGameState).GetProperty("ExchangeSixSnapshot");
        string[] positions = {"self", "left", "top", "right"};
        int[] tiles = {23, 22, 33, 39};
        var snapshot = new ChangshaExchangeSixState {
            phase = "open_kong_public", public_state = new ChangshaExchangeSixPublic {
                current_player_index = 0, kong_tiles = tiles,
                pending_kong = new ChangshaExchangeSixPendingKong {player_index = 0, river_start = 2}
            }, private_state = new ChangshaExchangeSixPrivate {action_list = Array.Empty<string>(), open_hand_pass_hint = true}
        };
        // 同一份全桌公开状态分别从四个座位观察，非开杠者也必须看到完整牌面。
        for (int viewer = 0; viewer < 4; viewer++) {
            TableMirror.Current.IndexToPosition.Clear();
            for (int seat = 0; seat < 4; seat++) TableMirror.Current.IndexToPosition[seat] = positions[(seat - viewer + 4) % 4];
            property.SetValue(active, snapshot);
            canvas.ShowExchangeSixState(snapshot, false);
            yield return null; yield return null;
            Check(Field<GameObject>(canvas, "exchangeSixPanel").activeInHierarchy, "every seat sees the kong display");
            var cards = Field<Transform>(canvas, "exchangeSixPublicCards").GetComponentsInChildren<StaticCard>();
            Check(cards.Select(card => card.TileId).SequenceEqual(tiles), "all four public kong tiles retain order");
            Check(Field<TMP_Text>(canvas, "exchangeSixStatus").text.Contains("待鸣牌"), "pending state is explicit");
            var popupBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(canvas.transform, Field<GameObject>(canvas, "exchangeSixPanel").transform);
            var viewport = ((RectTransform)canvas.transform).rect;
            Check(popupBounds.min.x >= viewport.xMin - 1 && popupBounds.max.x <= viewport.xMax + 1, "public cards stay inside the viewport");
            var notice = Field<TMP_Text>(canvas, "exchangeSixPrivateNotice");
            var noticeBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(canvas.transform, notice.transform);
            Check(popupBounds.min.y >= noticeBounds.max.y - 1, "pending cards do not cover the private pass-hu notice");
        }
        TableMirror.Current.IndexToPosition.Clear();
        for (int seat = 0; seat < 4; seat++) TableMirror.Current.IndexToPosition[seat] = positions[seat];
        // 空牌河、已有两张和即将跨行时，预览也必须分成四个槽位，回放用自己的座位映射。
        foreach (int start in new[] {0, 2, 5}) {
            string source = start == 0 ? "left" : start == 2 ? "top" : "right";
            for (int index = 0; index < start; index++)
                manager.Change3DTile("SetDiscardWithoutAnimation", 11 + index, 0, source, true, null);
            while (manager.HasPendingRecordTableAnimations) yield return null;
            snapshot.public_state.pending_kong.river_start = start;
            manager.SyncChangshaExchangeClaims(snapshot, new Dictionary<int, string> {{0, source}});
            yield return null; yield return null; yield return null;
            var previews = Field<List<GameObject>>(manager, "exchangeSixKongPreviews");
            Check(previews.Select(tile => tile.transform.position).Distinct().Count() == 4, "four previews use distinct river slots");
            float spacing = Field<float>(manager, "widthSpacing");
            int pair = start == 5 ? 1 : 0;
            Check(Mathf.Abs(Vector3.Distance(previews[pair].transform.position, previews[pair + 1].transform.position) - spacing) < .001f,
                "virtual river slots retain actual tile spacing");
            Check(Vector3.Distance(previews[0].transform.position, manager.GetPosPanel(source).discardsPosition.position) < spacing * 10,
                "preview uses the supplied record seat rather than live seat zero");
            manager.ClearChangshaExchangeClaims();
        }
        snapshot.phase = "claims";
        snapshot.public_state.pending_kong = null;
        snapshot.public_state.claim_tiles = new[] {33};
        snapshot.private_state.action_list = new[] {"peng", "pass"};
        snapshot.private_state.targets = new Dictionary<string, int[]> {{"peng", new[] {33}}};
        canvas.ShowExchangeSixState(snapshot, false);
        yield return null; yield return null;
        Check(Field<GameObject>(canvas, "exchangeSixPanel").activeInHierarchy, "ordinary claims expose the discard");
        Check(Field<Transform>(canvas, "exchangeSixPublicCards").GetComponentsInChildren<StaticCard>().Single().TileId == 33,
            "stale kong tiles cannot replace the actual claim tile");
        Check(canvas.GetComponentsInChildren<TMP_Text>().Any(label => label.text == "碰 三条"), "real action button identifies its target");
        Check(ChangshaExchangeSixActions.TileLabel(23) == "三筒" && ChangshaExchangeSixActions.TileLabel(33) == "三条",
            "tile names match the authoritative 21 dots and 31 bamboo mapping");
        var promptBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(canvas.transform, Field<GameObject>(canvas, "exchangeSixPanel").transform);
        var buttonsBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(canvas.transform, canvas.ActionButtonContainer);
        Check(promptBounds.min.y >= buttonsBounds.max.y - 1, "claim cards do not overlap action buttons");

        snapshot.private_state.action_list = new[] {"hu_first", "pass"};
        snapshot.private_state.targets = new Dictionary<string, int[]> {{"hu_first", new[] {33, 39}}};
        var words = ChangshaExchangeSixActions.DisplayWords(snapshot);
        Check(words.Count == 2 && words[0].StartsWith("changsha_exchange_action:hu_first:"),
            "batch hu stays one action rather than selecting just one winning tile");

        snapshot.phase = "open_kong_public";
        snapshot.private_state.action_list = Array.Empty<string>();
        snapshot.public_state.pending_kong = new ChangshaExchangeSixPendingKong {player_index = 0, river_start = 0};
        manager.SyncChangshaExchangeClaims(snapshot);
        yield return null; yield return null; yield return null;
        Check(Field<List<GameObject>>(manager, "exchangeSixKongPreviews").Count == 4, "pre-claim preview shows four actual meshes");
        var glows = Field<List<ClaimTileGlow>>(manager, "exchangeSixKongGlows");
        Check(glows.Count == 4 && glows.All(glow => glow.gameObject.activeInHierarchy), "the whole batch has pending claim glows");

        foreach (int tile in tiles) manager.Change3DTile("SetDiscardWithoutAnimation", tile, 0, "self", true, null);
        while (manager.HasPendingRecordTableAnimations) yield return null;
        snapshot.public_state.pending_kong.discarded = true;
        manager.SyncChangshaExchangeClaims(snapshot);
        yield return null; yield return null; yield return null;
        var river = manager.GetPosPanel("self").discardsPosition;
        Check(Field<List<GameObject>>(manager, "exchangeSixKongPreviews").Count == 0, "real river replaces preview without duplicates");
        Check(river.GetComponentsInChildren<Tile3D>().Select(tile => tile.GetTileId()).SequenceEqual(tiles), "all batch tiles occupy the opener river");
        glows = Field<List<ClaimTileGlow>>(manager, "exchangeSixKongGlows");
        Check(glows.All(glow => Field<Renderer>(glow, "targetRenderer").transform.IsChildOf(river)), "pending glows follow actual river tiles");
        manager.gameObject.SetActive(false);
        Check(Field<List<ClaimTileGlow>>(manager, "exchangeSixKongGlows").Count == 0, "hiding the table clears owned pending objects");
        manager.gameObject.SetActive(true);
        snapshot.public_state.pending_kong = null;
        snapshot.public_state.kong_tiles = Array.Empty<int>();
        snapshot.phase = "playing";
        manager.SyncChangshaExchangeClaims(snapshot);
        canvas.ShowExchangeSixState(snapshot, false);
        yield return null;
        Check(Field<List<ClaimTileGlow>>(manager, "exchangeSixKongGlows").Count == 0, "no claims clears the pending appearance");
        Check(river.GetComponentsInChildren<Tile3D>().Length == 4, "finalization keeps ordinary river tiles");
        yield return VerifyRecordClaims(canvas, manager, tiles);
        canvas.HideExchangeSixState();
        manager.ClearChangshaExchangeClaims();
        Debug.Log("CHANGSHA_CLAIM_DISPLAY_PASSED checks=" + Checks);
    }

    private static IEnumerator VerifyRecordClaims(GameCanvas canvas, Game3DManager manager, int[] tiles) {
        var record = GameRecordManager.Instance;
        var previousRecord = record.gameRecord;
        var previousSeats = new Dictionary<int, string>(record.indexToPosition);
        var previousPlayers = Field<List<GameRecordManager.RecordPlayer>>(record, "recordPlayerList");
        var previousInfo = new Dictionary<string, GameRecordManager.RecordPlayer>(record.recordPlayer_to_info);
        typeof(GameRecordManager).GetProperty("gameRecord").SetValue(record, new GameRecord {
            gameTitle = new Dictionary<string, object> {{"sub_rule", ChangshaGameState.ExchangeSixSubRule}}
        });
        record.indexToPosition.Clear(); record.recordPlayer_to_info.Clear();
        var players = new List<GameRecordManager.RecordPlayer>();
        string[] seats = {"right", "self", "left", "top"};
        for (int seat = 0; seat < 4; seat++) {
            record.indexToPosition[seat] = seats[seat];
            var player = new GameRecordManager.RecordPlayer {playerIndex = seat};
            if (seat == 0) player.discardTiles.AddRange(new[] {11, 12, 13, 14, 15});
            players.Add(player); record.recordPlayer_to_info[seats[seat]] = player;
        }
        typeof(GameRecordManager).GetField("recordPlayerList", Fields).SetValue(record, players);
        var publicTiles = typeof(GameRecordManager).GetField("exchangeSixRecordPublicTiles", Fields);
        var refresh = typeof(GameRecordManager).GetMethod("RefreshExchangeSixRecordPresentation", Fields);
        int previousPlayer = record.currentPlayerIndex;
        record.currentPlayerIndex = 0;
        publicTiles.SetValue(record, tiles);
        refresh.Invoke(record, null);
        yield return null; yield return null; yield return null;
        var displayed = Field<ChangshaExchangeSixState>(canvas, "exchangeSixState");
        Check(displayed.public_state.pending_kong?.river_start == 5, "record refresh supplies the pending river offset");
        Check(Field<GameObject>(canvas, "exchangeSixPanel").activeInHierarchy, "record refresh keeps public kong cards visible");
        Check(Field<List<GameObject>>(manager, "exchangeSixKongPreviews").Count == 4, "record refresh renders the batch at its own seat");
        publicTiles.SetValue(record, Array.Empty<int>());
        refresh.Invoke(record, null);
        yield return null;
        Check(Field<List<GameObject>>(manager, "exchangeSixKongPreviews").Count == 0, "next record node clears pending previews");
        typeof(GameRecordManager).GetProperty("gameRecord").SetValue(record, previousRecord);
        typeof(GameRecordManager).GetField("recordPlayerList", Fields).SetValue(record, previousPlayers);
        record.indexToPosition.Clear(); foreach (var seat in previousSeats) record.indexToPosition[seat.Key] = seat.Value;
        record.recordPlayer_to_info.Clear(); foreach (var player in previousInfo) record.recordPlayer_to_info[player.Key] = player.Value;
        record.currentPlayerIndex = previousPlayer;
    }
}
