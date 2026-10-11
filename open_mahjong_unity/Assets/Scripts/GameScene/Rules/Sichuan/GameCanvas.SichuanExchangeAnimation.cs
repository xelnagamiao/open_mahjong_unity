using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class GameCanvas {
    private const float ExchangeFlightSeconds = 1.25f;
    private RectTransform exchangePresentationRoot;
    private Coroutine exchangeFlight;
    private int exchangePresentationTick = -1;
    private AskHandActionGBInfo pendingExchangeAsk;
    private float pendingExchangeAskAt;
    private RectTransform incomingExchangePacket;
    private string incomingExchangeSeat;
    public bool IsXueliuExchangeAnimating => exchangeFlight != null;

    public bool ShowXueliuExchangePresentation(AskHandActionGBInfo info) {
        XueliuExchangeInfo phase = info?.xueliu_exchange;
        if (phase == null) return true;
        if (!phase.animating && IsXueliuExchangeAnimating) {
            // 网络批量送达时，下一组询问也须等本机飞牌完成才开放输入。
            pendingExchangeAsk = info;
            pendingExchangeAskAt = Time.realtimeSinceStartup;
            BoardCanvas.Instance?.ShowCurrentPlayer(null, info.remain_tiles);
            return false;
        }
        var positions = NormalGameStateManager.Instance?.indexToPosition;
        if (phase.animating) {
            BoardCanvas.Instance?.ClearExchangePlayers();
            BoardCanvas.Instance?.ShowCurrentPlayer(null, info.remain_tiles);
            if (exchangePresentationTick == info.action_tick) return true;
            pendingExchangeAsk = null;
            ClearXueliuExchangeCards();
            exchangePresentationTick = info.action_tick;
            if (CreateExchangeRoot()) exchangeFlight = StartCoroutine(AnimateExchangeGroup(info, positions));
        } else {
            ClearXueliuExchangeCards();
            exchangePresentationTick = -1;
            BoardCanvas.Instance?.ShowExchangePlayers(phase.active_players, positions);
            // 第二组选牌期间保留换入的花色牌；重连也直接恢复到这个位置。
            if (phase.stage == 2 && phase.teammate_suit > 0 && positions != null
                && positions.TryGetValue(info.player_index, out string seat) && CreateExchangeRoot()) {
                incomingExchangePacket = CreateExchangePacket(phase.teammate_suit, "IncomingExchange");
                incomingExchangeSeat = seat;
                UpdateIncomingExchangePosition();
            }
        }
        return true;
    }

    private bool CreateExchangeRoot() {
        Canvas canvas = GetComponentInParent<Canvas>()?.rootCanvas;
        if (!canvas || !isActiveAndEnabled) return false;
        var root = new GameObject("XueliuExchange", typeof(RectTransform), typeof(CanvasGroup));
        root.layer = canvas.gameObject.layer;
        exchangePresentationRoot = root.GetComponent<RectTransform>();
        exchangePresentationRoot.SetParent(canvas.transform, false);
        exchangePresentationRoot.anchorMin = Vector2.zero;
        exchangePresentationRoot.anchorMax = Vector2.one;
        exchangePresentationRoot.offsetMin = exchangePresentationRoot.offsetMax = Vector2.zero;
        var group = root.GetComponent<CanvasGroup>();
        group.blocksRaycasts = group.interactable = false;
        Canvas.willRenderCanvases += UpdateIncomingExchangePosition;
        return true;
    }

    private Vector2 ExchangeSeatPoint(string seat) {
        if (!BoardCanvas.Instance || !BoardCanvas.Instance.TryGetExchangePoints(exchangePresentationRoot, seat,
            out Vector2 center, out Vector2 edge)) return Vector2.zero;
        Vector2 direction = (edge - center).normalized;
        float halfPacket = seat == "self" || seat == "top" ? 40f : 89f;
        // 从中心盘对应边缘向外留出半包牌及间隔；不依赖屏幕长宽比例。
        return center + (edge - center) * 1.25f + direction * (halfPacket + 24f);
    }

    private Vector2 ExchangeCenterPoint() {
        return BoardCanvas.Instance && BoardCanvas.Instance.TryGetExchangePoints(exchangePresentationRoot, "self",
            out Vector2 center, out _) ? center : Vector2.zero;
    }

    private void UpdateIncomingExchangePosition() {
        if (incomingExchangePacket && exchangePresentationRoot)
            incomingExchangePacket.anchoredPosition = ExchangeSeatPoint(incomingExchangeSeat);
    }

    private RectTransform CreateExchangePacket(int suit, string name) {
        var packet = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        packet.SetParent(exchangePresentationRoot, false);
        packet.anchorMin = packet.anchorMax = packet.pivot = new Vector2(.5f,.5f);
        packet.sizeDelta = new Vector2(178, 80);
        for (int i=0; i<3; i++) {
            var card = new GameObject("ExchangeCard", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            card.transform.SetParent(packet, false);
            card.raycastTarget = false;
            card.color = suit > 0 ? new Color(.97f,.96f,.9f) : new Color(.12f,.42f,.34f);
            card.rectTransform.sizeDelta = new Vector2(54,76);
            card.rectTransform.anchoredPosition = new Vector2((i-1)*60,0);
            if (suit < 1 || suit > 3 || dingqueTipText == null) continue;
            TMP_Text label = Instantiate(dingqueTipText, card.transform, false);
            label.name = "ExchangeSuit";
            label.text = DingqueSuitLabels[suit];
            label.fontSize = 38;
            label.enableAutoSizing = false;
            label.alignment = TextAlignmentOptions.Center;
            label.color = suit == 1 ? new Color(.8f,.16f,.14f) : suit == 2 ? new Color(.15f,.35f,.8f) : new Color(.1f,.5f,.25f);
            label.raycastTarget = false;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            label.gameObject.SetActive(true);
        }
        return packet;
    }

    private IEnumerator AnimateExchangeGroup(AskHandActionGBInfo info, Dictionary<int,string> positions) {
        var packets = new List<RectTransform>();
        var starts = new List<string>();
        var ends = new List<string>();
        XueliuExchangeInfo phase = info.xueliu_exchange;
        int viewer = info.player_index;
        foreach (int sender in phase.active_players ?? System.Array.Empty<int>()) {
            if (positions == null || !positions.TryGetValue(sender, out string from)
                || !positions.TryGetValue((sender+2)%4, out string to)) continue;
            int suit = sender == viewer ? phase.outgoing_suit : (sender+2)%4 == viewer ? phase.teammate_suit : 0;
            packets.Add(CreateExchangePacket(suit, "ExchangeFrom_"+sender));
            starts.Add(from);
            ends.Add(to);
        }
        float elapsed = Mathf.Clamp(ExchangeFlightSeconds - phase.animation_seconds, 0, ExchangeFlightSeconds-.01f);
        while (elapsed < ExchangeFlightSeconds) {
            for (int i=0;i<packets.Count;i++) {
                // 全程使用实际中心盘投影；窗口缩放、视角变化时也沿中心盘移动。
                Vector2 center = ExchangeCenterPoint() + new Vector2(0, i == 0 ? -52 : 52);
                float arrive = Mathf.SmoothStep(0,1,Mathf.Clamp01(elapsed/.3f));
                float depart = Mathf.SmoothStep(0,1,Mathf.Clamp01((elapsed-.65f)/.6f));
                packets[i].anchoredPosition = elapsed < .65f
                    ? Vector2.Lerp(ExchangeSeatPoint(starts[i]),center,arrive)
                    : Vector2.Lerp(center,ExchangeSeatPoint(ends[i]),depart);
            }
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        for (int i=0;i<packets.Count;i++) packets[i].anchoredPosition = ExchangeSeatPoint(ends[i]);
        exchangeFlight = null;
        if (pendingExchangeAsk != null) {
            AskHandActionGBInfo next = pendingExchangeAsk;
            pendingExchangeAsk = null;
            next.remaining_time = Mathf.Max(1, next.remaining_time-Mathf.FloorToInt(Time.realtimeSinceStartup-pendingExchangeAskAt));
            ShowXueliuExchangePresentation(next);
            ShowXueliuTeamExchange(next);
        }
    }

    private void ClearXueliuExchangeCards() {
        Canvas.willRenderCanvases -= UpdateIncomingExchangePosition;
        incomingExchangePacket = null;
        incomingExchangeSeat = null;
        if (exchangeFlight != null) StopCoroutine(exchangeFlight);
        exchangeFlight = null;
        if (exchangePresentationRoot != null) {
            exchangePresentationRoot.gameObject.SetActive(false);
            Destroy(exchangePresentationRoot.gameObject);
            exchangePresentationRoot = null;
        }
    }

    public void ClearXueliuExchangePresentation() {
        pendingExchangeAsk = null;
        ClearXueliuExchangeCards();
        exchangePresentationTick = -1;
        BoardCanvas.Instance?.ClearExchangePlayers();
    }
}
