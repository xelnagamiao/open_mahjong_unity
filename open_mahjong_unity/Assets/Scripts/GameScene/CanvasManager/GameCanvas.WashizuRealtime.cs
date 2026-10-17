using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class GameCanvas {
    private WashizuHandLayout washizuSelfLayout;
    private Coroutine washizuSelfSync;
    private bool washizuOrderDirty, washizuAwaitingOrder, washizuApplySelfLayout, washizuAutoArrangePending;
    private int washizuOrderRequest;
    private float washizuOrderSentAt, washizuRetryAfter;
    private string washizuSyncGame;
    private readonly Dictionary<TileCard, int> washizuSelfKeys = new Dictionary<TileCard, int>();

    public void PrepareWashizuRealtimeHand(GameInfo info) {
        ResetWashizuRealtimeHand();
        AutoAction.Instance?.PrepareRealtimeHandOrder(info);
        if (!WashizuTile.IsRealtimeSubRule(info.sub_rule)) return;
        washizuSyncGame = info.gamestate_id;
        if (info.washizu_layouts != null && info.washizu_layouts.TryGetValue(GameSession.Current.SelfIndex, out var layout)) {
            washizuSelfLayout = layout;
            washizuApplySelfLayout = true;
            washizuAutoArrangePending = AutoAction.Instance?.IsAutoArrangeHandCards == true;
            EnsureWashizuSelfSync();
        }
    }

    public void RequestWashizuAutoArrange() {
        if (!WashizuTile.IsRealtimeHandOrder || IsHandRecordPlayback()) return;
        washizuAutoArrangePending = true;
        EnsureWashizuSelfSync();
    }

    private void ResetWashizuRealtimeHand() {
        if (washizuSelfSync != null) StopCoroutine(washizuSelfSync);
        washizuSelfSync = null;
        washizuSelfLayout = null;
        washizuSelfKeys.Clear();
        washizuSyncGame = null;
        washizuOrderDirty = washizuAwaitingOrder = washizuApplySelfLayout = washizuAutoArrangePending = false;
        washizuRetryAfter = 0;
    }

    public void PublishWashizuHandOrder() {
        if (!WashizuTile.IsRealtimeHandOrder || GameSession.Current.IsRealtimeSpectator
            || IsHandRecordPlayback() || washizuSelfLayout == null) return;
        washizuOrderDirty = true;
        // 自动理牌与拖拽使用同一条物理牌顺序同步；不让尚未应用的旧快照覆盖本地操作。
        if (BindWashizuSelfKeys(washizuSelfLayout, CurrentWashizuCards())) washizuApplySelfLayout = false;
        EnsureWashizuSelfSync();
    }

    public void ReceiveWashizuSelfLayout(WashizuHandLayout layout, Response acknowledgement = null) {
        if (layout?.tiles == null || layout.keys == null || layout.tiles.Length != layout.keys.Length) return;
        bool pendingLocalOrder = washizuOrderDirty || washizuAwaitingOrder;
        bool acknowledged = acknowledgement?.hand_order_request_id == washizuOrderRequest;
        if (acknowledged) washizuAwaitingOrder = false;
        bool newer = washizuSelfLayout == null || layout.round_index > washizuSelfLayout.round_index
            || (layout.round_index == washizuSelfLayout.round_index && layout.revision > washizuSelfLayout.revision);
        if (newer) {
            bool handChanged = washizuSelfLayout == null || layout.round_index != washizuSelfLayout.round_index
                || !SameWashizuTiles(washizuSelfLayout.tiles, layout.tiles);
            washizuSelfLayout = layout;
            if (handChanged) {
                washizuAutoArrangePending = true;
                // A draw/discard may cross an in-flight drag. The existing 2D
                // hand mutation preserves survivors, so rebase that local order
                // on the new revision instead of snapping back to the old order.
                washizuOrderDirty = pendingLocalOrder;
                washizuApplySelfLayout = !pendingLocalOrder;
            } else if (GameSession.Current.IsRealtimeSpectator) {
                washizuApplySelfLayout = true;
            }
        }
        if (acknowledged && !acknowledgement.success) {
            // Rebase a rapid drag on the latest revision. Gameplay changes use
            // the authoritative surviving hand before another order is sent.
            if (SameWashizuTiles(CurrentWashizuCards().Select(c => c.tileId), washizuSelfLayout.tiles))
                washizuOrderDirty = true;
            else washizuApplySelfLayout = true;
            washizuRetryAfter = Time.unscaledTime + .25f;
        }
        EnsureWashizuSelfSync();
    }

    private void EnsureWashizuSelfSync() {
        if (washizuSelfSync == null && isActiveAndEnabled)
            washizuSelfSync = StartCoroutine(SyncWashizuSelfHand());
    }

    private List<TileCard> CurrentWashizuCards() {
        var cards = GetMainHandCardsOrdered();
        var draw = GetPinnedDrawTile();
        if (draw != null) cards.Add(draw);
        return cards;
    }

    private static bool SameWashizuTiles(IEnumerable<int> first, IEnumerable<int> second) =>
        first.OrderBy(t => t).SequenceEqual(second.OrderBy(t => t));

    private IEnumerator SyncWashizuSelfHand() {
        yield return null;
        while (washizuSelfLayout != null && WashizuTile.IsRealtimeHandOrder
            && washizuSyncGame == UserDataManager.Instance.GamestateId) {
            if (!IsChangeHandCardProcessing && !IsHandReflowAnimating && !HandCardDragController.SuppressPointerHover) {
                if (washizuApplySelfLayout && TryApplyWashizuSelfLayout()) washizuApplySelfLayout = false;
                if (washizuAutoArrangePending && !washizuApplySelfLayout) {
                    washizuAutoArrangePending = false;
                    if (AutoAction.Instance?.IsAutoArrangeHandCards == true && !IsHandRecordPlayback()) {
                        SortMainHandByTileIdIfNeeded();
                        // 先完成本次移动，再发送稳定的排列；点击发生在摸牌/补花中也不会丢失。
                        yield return null;
                        continue;
                    }
                }
                if (washizuAwaitingOrder && Time.unscaledTime - washizuOrderSentAt > 5f) {
                    washizuAwaitingOrder = false;
                    washizuOrderDirty = true;
                }
                if (washizuOrderDirty && !washizuAwaitingOrder && !washizuApplySelfLayout
                    && Time.unscaledTime >= washizuRetryAfter && GameSession.Current.IsGameActive
                    && !GameSession.Current.IsRealtimeSpectator && !IsHandRecordPlayback()) {
                    var cards = CurrentWashizuCards();
                    int[] ids = cards.Select(c => c.tileId).ToArray();
                    if (SameWashizuTiles(ids, washizuSelfLayout.tiles) && BindWashizuSelfKeys(washizuSelfLayout, cards)) {
                        int drawIndex = cards.FindIndex(c => c.currentGetTile);
                        bool pinned = drawIndex >= 0 && cards[drawIndex].isDrawSlotPinned;
                        washizuAwaitingOrder = true;
                        washizuOrderDirty = false;
                        washizuOrderSentAt = Time.unscaledTime;
                        GameStateNetworkManager.Instance.SendWashizuHandOrder(ids, cards.Select(c => washizuSelfKeys[c]).ToArray(), drawIndex, pinned,
                            washizuSelfLayout.round_index, washizuSelfLayout.revision, ++washizuOrderRequest);
                    }
                }
            }
            yield return null;
        }
        washizuSelfSync = null;
    }

    public void ApplyWashizuRecordOrder(WashizuHandLayout layout) {
        if (layout != null) StartCoroutine(ApplyWashizuRecordOrderWhenIdle());
    }

    private IEnumerator ApplyWashizuRecordOrderWhenIdle() {
        yield return null;
        while (IsChangeHandCardProcessing || IsHandReflowAnimating) yield return null;
        if (IsHandRecordPlayback()) TryApplyWashizuSelfLayout(GameRecordManager.Instance.GetWashizuRecordLayout("self", true));
    }

    private bool TryApplyWashizuSelfLayout(WashizuHandLayout layout = null) {
        layout = layout ?? washizuSelfLayout;
        if (layout == null) return false;
        var available = CurrentWashizuCards();
        if (!SameWashizuTiles(available.Select(c => c.tileId), layout.tiles)) return false;
        if (!BindWashizuSelfKeys(layout, available)) return false;
        int drawn = Array.IndexOf(layout.keys, layout.drawn_key);
        TileCard draw = drawn >= 0
            ? available.First(c => washizuSelfKeys[c] == layout.drawn_key)
            : null;
        if (draw != null) available.Remove(draw);
        var main = new List<TileCard>();
        for (int i = 0; i < layout.tiles.Length; i++) {
            TileCard card = i == drawn ? draw : available.First(c => washizuSelfKeys[c] == layout.keys[i]);
            available.Remove(card);
            card.currentGetTile = i == drawn;
            card.isDrawSlotPinned = i == drawn && layout.draw_pinned;
            if (!card.isDrawSlotPinned) main.Add(card);
        }
        TileCard pinned = layout.draw_pinned ? draw : null;
        ApplyHandLayoutPositions(BuildHandLayoutPositions(main, pinned, null), main, pinned);
        isArranged = true;
        return true;
    }

    private bool BindWashizuSelfKeys(WashizuHandLayout layout, List<TileCard> cards) {
        if (!SameWashizuTiles(cards.Select(c => c.tileId), layout.tiles)) return false;
        var faces = new Dictionary<int, int>();
        for (int i=0;i<layout.keys.Length;i++) faces[layout.keys[i]] = layout.tiles[i];
        foreach (var entry in washizuSelfKeys.ToArray()) {
            if (entry.Key == null || !cards.Contains(entry.Key) || !faces.TryGetValue(entry.Value, out int face)
                || entry.Key.tileId != face) washizuSelfKeys.Remove(entry.Key);
        }
        if (layout.drawn_key != 0 && !washizuSelfKeys.ContainsValue(layout.drawn_key)) {
            var draw = cards.FirstOrDefault(c => c.currentGetTile && c.tileId == faces[layout.drawn_key])
                ?? cards.LastOrDefault(c => !washizuSelfKeys.ContainsKey(c) && c.tileId == faces[layout.drawn_key]);
            if (draw != null) washizuSelfKeys[draw] = layout.drawn_key;
        }
        foreach (var card in cards) {
            if (washizuSelfKeys.ContainsKey(card)) continue;
            int key = faces.Keys.First(k => faces[k] == card.tileId && !washizuSelfKeys.ContainsValue(k));
            washizuSelfKeys[card] = key;
        }
        return true;
    }
}
