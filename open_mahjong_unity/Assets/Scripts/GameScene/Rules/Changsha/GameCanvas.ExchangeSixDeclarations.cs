using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class GameCanvas {
    private readonly Dictionary<string, Queue<(string caption, int[] tiles, string owner)>> changshaDeclarations
        = new Dictionary<string, Queue<(string, int[], string)>>();
    private readonly Dictionary<string, Coroutine> changshaDeclarationPlayers = new Dictionary<string, Coroutine>();
    private readonly Dictionary<string, GameObject> changshaDeclarationViews = new Dictionary<string, GameObject>();

    public void ShowChangshaDeclarationChanges(ChangshaExchangeSixState previous, ChangshaExchangeSixState current) {
        // 首份快照可能来自重连，恢复头像标记即可，不补播已经发生的声明。
        if (previous == null || current.restoring) return;
        foreach (var seat in TableMirror.Current.IndexToPosition) {
            if ((current.public_state.open_hand_seats ?? new int[0]).Contains(seat.Key)
                && !(previous.public_state.open_hand_seats ?? new int[0]).Contains(seat.Key))
                QueueChangshaDeclaration(seat.Value, "明牌");
            if ((current.public_state.reported_ready_seats ?? new int[0]).Contains(seat.Key)
                && !(previous.public_state.reported_ready_seats ?? new int[0]).Contains(seat.Key))
                QueueChangshaDeclaration(seat.Value, "报听");
        }
        if (current.phase == "blind_kong_declared" && previous.phase != current.phase
            && TableMirror.Current.IndexToPosition.TryGetValue(current.public_state.current_player_index, out string opener))
            QueueChangshaDeclaration(opener, "闷杠");
    }

    public void QueueChangshaDeclaration(string seat, string caption, int[] tiles = null) {
        if (GetActionDisplayPos(seat) == null) return;
        // 四喜用全桌共用的展示队列，多家同时四喜也依次看清，不相互盖住。
        string key = tiles != null && tiles.Length > 0 ? "four_joy" : seat;
        if (!changshaDeclarations.TryGetValue(key, out var queue)) {
            queue = new Queue<(string, int[], string)>();
            changshaDeclarations[key] = queue;
        }
        queue.Enqueue((caption, tiles == null ? new int[0] : (int[])tiles.Clone(), seat));
        if (!changshaDeclarationPlayers.TryGetValue(key, out var player) || player == null)
            changshaDeclarationPlayers[key] = StartCoroutine(PlayChangshaDeclarations(key));
    }

    private IEnumerator PlayChangshaDeclarations(string seat) {
        // 确保返回的协程句柄先赋值，空队列结束时才能可靠清理。
        yield return null;
        var queue = changshaDeclarations[seat];
        while (queue.Count > 0) {
            var declaration = queue.Dequeue();
            var anchor = GetActionDisplayPos(declaration.owner);
            var view = Instantiate(ActionDisplayText, anchor);
            view.name = "ChangshaDeclaration";
            changshaDeclarationViews[seat] = view;
            var text = view.GetComponent<TMP_Text>();
            text.text = declaration.caption;
            text.raycastTarget = false;
            // 吃碰杠会清同一锚点的文字；专用提示作为相邻节点，保留相同屏幕位置和字体。
            view.transform.SetParent(anchor.parent, worldPositionStays: true);
            // 与紧随的吃碰杠错开一行；对家的提示向内移动，避免越过屏幕上沿。
            view.transform.localPosition += new Vector3(0, seat == "top" ? -100 : 100, 0);
            if (declaration.tiles.Length > 0) {
                // 四喜单独放在桌面内侧，四家都能看清，不能压在本人的手牌和副露上。
                var popup = (RectTransform)view.transform;
                popup.SetParent(transform, false);
                popup.anchorMin = popup.anchorMax = new Vector2(.5f, .5f);
                popup.pivot = new Vector2(.5f, .5f);
                popup.anchoredPosition = new Vector2(-100, 200);
                popup.sizeDelta = new Vector2(440, 64);
                text.fontSize = 42;
                text.alignment = TextAlignmentOptions.Center;
                text.text = (declaration.owner == "self" ? "你" : declaration.owner == "left" ? "左侧玩家"
                    : declaration.owner == "top" ? "对家" : "右侧玩家") + "：" + declaration.caption;
                var row = new GameObject("FourJoyCards", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                row.transform.SetParent(view.transform, false);
                var rect = (RectTransform)row.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
                rect.pivot = new Vector2(.5f, 1);
                rect.anchoredPosition = new Vector2(0, -8);
                rect.sizeDelta = new Vector2(408, 144);
                var layout = row.GetComponent<HorizontalLayoutGroup>();
                layout.spacing = 8;
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = layout.childForceExpandHeight = false;
                layout.childAlignment = TextAnchor.UpperCenter;
                foreach (int tile in declaration.tiles) {
                    var card = Instantiate(StaticCardPrefab, row.transform);
                    ((RectTransform)card.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 96);
                    card.GetComponent<StaticCard>().SetTileOnlyImage(tile);
                    var size = card.GetComponent<LayoutElement>() ?? card.AddComponent<LayoutElement>();
                    size.preferredWidth = 96; size.preferredHeight = 140;
                    foreach (var image in card.GetComponentsInChildren<Image>()) image.raycastTarget = false;
                }
            }
            // 仅延长展示，不延后权威动作和倒计时；同家多项声明依次完整显示。
            yield return new WaitForSecondsRealtime(declaration.tiles.Length > 0 ? 4f : 2f);
            float elapsed = 0;
            var original = text.color;
            while (view != null && elapsed < .4f) {
                elapsed += Time.unscaledDeltaTime;
                text.color = new Color(original.r, original.g, original.b, 1 - Mathf.Clamp01(elapsed / .4f));
                yield return null;
            }
            if (view != null) Destroy(view);
            changshaDeclarationViews.Remove(seat);
        }
        changshaDeclarationPlayers[seat] = null;
    }

    internal void ClearChangshaDeclarations() {
        foreach (var player in changshaDeclarationPlayers.Values) if (player != null) StopCoroutine(player);
        foreach (var view in changshaDeclarationViews.Values) if (view != null) Destroy(view);
        changshaDeclarationPlayers.Clear();
        changshaDeclarationViews.Clear();
        changshaDeclarations.Clear();
    }
}
