using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class GameCanvas {
    private RectTransform exchangePresentationRoot;
    private Coroutine exchangeFlight;
    private int exchangePresentationTick = -1;
    private AskHandActionGBInfo pendingExchangeAsk;
    private float pendingExchangeAskAt;
    public bool IsXueliuExchangeAnimating => exchangeFlight != null;
    public bool HasExchangePresentation(int tick) => exchangePresentationRoot && exchangePresentationTick == tick;

    public bool ShowXueliuExchangePresentation(AskHandActionGBInfo info, bool sbr = false) {
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
            if (CreateExchangeRoot()) {
                if (sbr && (phase.dice?.Length == 2 || phase.direction >= 1 && phase.direction <= 3)) {
                    var session = GameSession.Current;
                    bool privateHand = !session.IsRealtimeSpectator && GameRecordManager.Instance?.IsSpectating != true &&
                        info.player_index == session.SelfIndex;
                    if (!sbrFlightHandHidden)
                        BeginSbrTableHand(privateHand ? phase.outgoing_tiles : null, orderByHand: privateHand);
                    exchangeFlight = StartCoroutine(phase.dice?.Length == 2
                        ? AnimateSbrDice(info) : AnimateSbrTableExchange(info, positions));
                } else {
                    bool sends = System.Array.IndexOf(phase.active_players ?? System.Array.Empty<int>(), info.player_index) >= 0;
                    if (sends || phase.stage == 2) BeginSbrTableHand(phase.outgoing_tiles);
                    else HideXueliuThrowThreeSelection();
                    StopTimeRunning();
                    ShowFirstXueliuGroupAtRest(info, positions);
                    exchangeFlight = StartCoroutine(AnimateExchangeGroup(info, positions));
                }
            }
        } else {
            ClearXueliuExchangeCards();
            exchangePresentationTick = -1;
            BoardCanvas.Instance?.ShowExchangePlayers(phase.active_players, positions);
            if (phase.stage == 2 && CreateExchangeRoot()) ShowFirstXueliuGroupAtRest(info, positions);
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
        return true;
    }

    private Vector2 ExchangeSeatPoint(string seat) {
        if (!BoardCanvas.Instance || !BoardCanvas.Instance.TryGetExchangePoints(exchangePresentationRoot, seat,
            out Vector2 center, out Vector2 edge)) return Vector2.zero;
        Vector2 direction = (edge - center).normalized;
        float halfPacket = seat == "self" || seat == "top" ? 31f : 56f;
        // 从中心盘对应边缘向外留出半包牌及间隔；不依赖屏幕长宽比例。
        return center + (edge - center) * 1.25f + direction * (halfPacket + 24f);
    }

    private void ShowFirstXueliuGroupAtRest(AskHandActionGBInfo info, Dictionary<int, string> positions) {
        var phase = info.xueliu_exchange;
        if (phase.stage != 2 || phase.active_players?.Length != 2) return;
        var routes = GetTableExchangeRoutes(positions, phase.active_players.Select(r => (r + 2) % 4).ToArray(), phase.active_players);
        if (!exchangePresentationRoot || routes.Length == 0) return;
        var reference = SbrNode(exchangePresentationRoot, "ParkedExchange", new Vector2(747, 420));
        var parked = CreateTablePackets(routes, positions, reference, 3, -1, null,
            handsAlreadyReduced: true, parked: true);
        if (parked == null) return;
        foreach (int receiver in phase.active_players) {
            int sender = (receiver + 2) % 4;
            if (positions.TryGetValue(sender, out string from) && from != "self")
                parked.owner.HideHandEnd(parked.table.GetPosPanel(from)?.cardsPosition, 3);
        }
        AttachTeamExchangeBadges(parked, info, positions, atReceivingSeats: true);
        parked.Place(1, 1);
    }

    private void ShowDuplicateSubmission(int[] submitted, XueliuExchangeInfo phase) {
        var session = GameSession.Current;
        if (!sbrWaiting || submitted?.Length != 3 || phase == null || (phase.stage != 1 && phase.stage != 2) ||
            session.SubRule != SichuanLobby.XueliuDuplicateSubRule || !HasPrivateTeamExchangeView(session.SelfIndex)) return;
        if (!exchangePresentationRoot && !CreateExchangeRoot()) return;
        if (exchangePresentationRoot.Find("DuplicateLocalSubmission")) return;
        var reference = SbrNode(exchangePresentationRoot, "DuplicateLocalSubmission", new Vector2(747, 420));
        var positions = NormalGameStateManager.Instance.indexToPosition;
        var self = new[] { session.SelfIndex };
        var layout = CreateTablePackets(GetTableExchangeRoutes(positions, self, self), positions, reference,
            3, -1, null, handsAlreadyReduced: true, parked: true);
        if (layout == null) return;
        layout.owner.world.name = "DuplicateSubmittedModels";
        layout.packets[0].anchor.name = "SubmittedAt_" + session.SelfIndex;
        layout.inFrontOfReceipt = phase.stage == 2;
        layout.AttachBadge(session.SelfIndex, SubmittedSuit(submitted), TeamBadgeExtendsRight(phase, positions));
        layout.Place(1, 0);
        if (!sbrFlightHandHidden) StartCoroutine(HideDuplicateSubmissionHand(reference, submitted));
    }

    private IEnumerator HideDuplicateSubmissionHand(RectTransform reference, int[] submitted) {
        while (reference && reference.gameObject.activeInHierarchy && IsChangeHandCardProcessing) yield return null;
        if (reference && reference.gameObject.activeInHierarchy && sbrWaiting && !sbrFlightHandHidden)
            HideOutgoingTableTiles(SelectedTableTiles(submitted), submitted);
    }

    private static string DescribeXueliuTeamSuit(int suit) =>
        suit >= 1 && suit <= 3 ? $"队友传来三张{DingqueSuitLabels[suit]}牌" : null;

    private static bool HasPrivateTeamExchangeView(int viewer) {
        var session = GameSession.Current;
        return viewer >= 0 && viewer < 4 && viewer == session.SelfIndex && SichuanLobby.IsXueliuDoubles(session.SubRule)
            && !session.IsRealtimeSpectator && !IsDingqueSelectionSuppressed();
    }

    private static int SubmittedSuit(int[] tiles) {
        if (tiles?.Length != 3) return 0;
        int suit = tiles[0] / 10;
        return suit >= 1 && suit <= 3 && System.Array.TrueForAll(tiles,
            id => id / 10 == suit && id % 10 >= 1 && id % 10 <= 9) ? suit : 0;
    }

    private static bool TeamBadgeExtendsRight(XueliuExchangeInfo phase, IReadOnlyDictionary<int, string> positions,
            bool atReceivingSeats = false) {
        if (phase.active_players == null) return true;
        foreach (int seat in phase.active_players) {
            int sender = atReceivingSeats ? (seat + 2) % 4 : seat;
            if (positions.TryGetValue(sender, out string side)) {
                if (side == "left") return true;
                if (side == "right") return false;
            }
        }
        return true;
    }

    private static void AttachTeamExchangeBadges(SbrPacketLayout packets, AskHandActionGBInfo info,
            IReadOnlyDictionary<int, string> positions, bool atReceivingSeats = false) {
        if (info == null || !HasPrivateTeamExchangeView(info.player_index)) return;
        var phase = info.xueliu_exchange;
        int self = info.player_index, teammate = (self + 2) % 4;
        int ownSuit = phase.outgoing_suit != 0 ? phase.outgoing_suit : SubmittedSuit(phase.outgoing_tiles);
        bool extendRight = TeamBadgeExtendsRight(phase, positions, atReceivingSeats);
        packets.AttachBadge(atReceivingSeats ? teammate : self, ownSuit, extendRight);
        packets.AttachBadge(atReceivingSeats ? self : teammate, phase.teammate_suit, extendRight);
    }

    private IEnumerator AnimateExchangeGroup(AskHandActionGBInfo info, Dictionary<int,string> positions) {
        var phase = info.xueliu_exchange;
        bool doubles = phase.stage == 1 || phase.stage == 2;
        int direction = doubles ? 2 : phase.direction;
        int[] senders = phase.active_players ?? System.Array.Empty<int>();
        int[] recipients = senders.Select(seat => (seat + direction) % 4).ToArray();
        string subtitle = null;
        if (doubles) {
            var session = GameSession.Current;
            if (IsDingqueSelectionSuppressed() || info.player_index != session.SelfIndex) subtitle = "两家正在向各自的队友传牌";
            else if (System.Array.IndexOf(senders, info.player_index) >= 0) subtitle = "本家换给队友三张牌";
            else subtitle = DescribeXueliuTeamSuit(phase.teammate_suit) ?? "队友换给本家三张牌";
        }
        var flight = AnimateTableExchange(positions, senders, recipients, subtitle, phase.animation_seconds,
            doubles ? 2f : 2.75f, info.player_index, phase.outgoing_tiles,
            parkedAtDeparture: phase.stage == 2,
            localAlreadyDown: doubles && GameSession.Current.SubRule == SichuanLobby.XueliuDuplicateSubRule,
            teamInfo: doubles ? info : null);
        while (flight.MoveNext()) yield return flight.Current;
        exchangeFlight = null;
        if (pendingExchangeAsk != null) {
            AskHandActionGBInfo next = pendingExchangeAsk;
            pendingExchangeAsk = null;
            next.remaining_time = Mathf.Max(1, next.remaining_time-Mathf.FloorToInt(Time.realtimeSinceStartup-pendingExchangeAskAt));
            ShowXueliuExchangePresentation(next);
            var session = GameSession.Current;
            if (!session.IsRealtimeSpectator && !GameRecordManager.Instance.IsSpectating && next.player_index == session.SelfIndex)
                ShowXueliuTeamExchange(next);
        }
    }

    private void ClearXueliuExchangeCards() {
        if (exchangeFlight != null) StopCoroutine(exchangeFlight);
        exchangeFlight = null;
        if (exchangePresentationRoot != null) {
            exchangePresentationRoot.gameObject.SetActive(false);
            Destroy(exchangePresentationRoot.gameObject);
            exchangePresentationRoot = null;
        }
    }

    public void ClearXueliuExchangePresentation() {
        if (IsSbrChoosingOrWaiting) HideXueliuThrowThreeSelection();
        else ResetSbrSelection();
        ClearSbrReceived();
        pendingExchangeAsk = null;
        ClearXueliuExchangeCards();
        exchangePresentationTick = -1;
        BoardCanvas.Instance?.ClearExchangePlayers();
    }
}
