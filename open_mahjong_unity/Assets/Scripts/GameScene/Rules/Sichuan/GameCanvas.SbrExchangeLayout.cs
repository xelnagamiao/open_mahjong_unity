using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class GameCanvas {
    private const float SbrExchangeSeconds = 2.75f;
    private RectTransform sbrHudRoot, sbrHudReference;
    private RectTransform exchangeButtonRow;
    private TMP_Text sbrPromptText, sbrSelectionText;

    private static RectTransform SbrNode(Transform parent, string name, Vector2 size) {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.gameObject.layer = parent.gameObject.layer;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = size;
        return rect;
    }

    private static TMP_Text SbrLabel(RectTransform parent, string name, string content, Vector2 size,
            Vector2 location, float fontSize, Color color, FontStyles style = FontStyles.Normal, TMP_FontAsset face = null) {
        RectTransform rect = SbrNode(parent, name, size);
        rect.anchoredPosition = location;
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = face ? face : PlatformTypography.ChineseRegular;
        label.fontSharedMaterial = label.font.material;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.fontWeight = FontWeight.Regular;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        TableText.Set(label, content);
        return label;
    }

    private static SbrExchangeGraphic SbrGraphic(RectTransform parent, string name, Vector2 size,
            Vector2 location, SbrExchangeGraphic.Shape shape) {
        RectTransform rect = SbrNode(parent, name, size);
        rect.anchoredPosition = location;
        var graphic = rect.gameObject.AddComponent<SbrExchangeGraphic>();
        graphic.shape = shape;
        graphic.raycastTarget = false;
        return graphic;
    }

    private static float SbrReferenceScale(RectTransform rect) =>
        Mathf.Min(rect.rect.width / 747f, rect.rect.height / 420f);

    private static void PlaceSbrReference(RectTransform reference, RectTransform root) {
        if (!reference || !root) return;
        reference.localScale = Vector3.one * SbrReferenceScale(root);
        if (BoardCanvas.Instance && BoardCanvas.Instance.TryGetExchangePoints(root, "self", out Vector2 center, out _))
            reference.anchoredPosition = center;
    }

    private void UpdateSbrHudPosition() {
        PlaceSbrReference(sbrHudReference, sbrHudRoot);
        if (exchangeButtonRow && ActionButtonHost is RectTransform host)
            exchangeButtonRow.position = host.TransformPoint(host.rect.max);
    }

    private void MountExchangeButtons() {
        if (!dingqueSelectionPanel || !dingqueWanButton || !dingqueBingButton ||
                !(ActionButtonHost is RectTransform host)) return;
        exchangeButtonRow = SbrNode(dingqueSelectionPanel.transform, "ExchangeButtons", host.rect.size);
        exchangeButtonRow.pivot = Vector2.one;
        exchangeButtonRow.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var layout = exchangeButtonRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperRight;
        layout.childControlWidth = layout.childControlHeight = false;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        if (host.TryGetComponent(out HorizontalLayoutGroup actions)) {
            layout.padding = actions.padding;
            layout.spacing = actions.spacing;
        }
        dingqueBingButton.transform.SetParent(exchangeButtonRow, false);
        dingqueWanButton.transform.SetParent(exchangeButtonRow, false);
    }

    private void SetTableExchangeHud(string prompt, string selection, bool team = false) {
        if (!sbrHudRoot) {
            Canvas canvas = GetComponentInParent<Canvas>()?.rootCanvas;
            if (!canvas || !isActiveAndEnabled) return;
            sbrHudRoot = SbrNode(canvas.transform, "SbrExchangeHud", Vector2.zero);
            sbrHudRoot.anchorMin = Vector2.zero;
            sbrHudRoot.anchorMax = Vector2.one;
            sbrHudRoot.offsetMin = sbrHudRoot.offsetMax = Vector2.zero;
            sbrHudReference = SbrNode(sbrHudRoot, "Reference", new Vector2(747, 420));
            bool showCount = team && selection.Length > 0;
            float hudY = team ? (showCount ? -7f : 0f) : -82f;
            SbrGraphic(sbrHudReference, "Shade", new Vector2(team ? 236f : 256f, showCount ? 68f : 53f), new Vector2(0, hudY),
                SbrExchangeGraphic.Shape.Shade);
            if (team)
                SbrLabel(sbrHudReference, "Title", "换牌中", new Vector2(180, 30), new Vector2(0, 9), 21,
                    new Color(1f, .84f, .29f), FontStyles.Normal, TableHudTypography.Emphasis);
            sbrPromptText = SbrLabel(sbrHudReference, "Prompt", prompt, new Vector2(248, 19),
                new Vector2(0, team ? -13f : hudY + 10), 13, team ? Color.white : new Color(.47f, .89f, 1f));
            sbrSelectionText = SbrLabel(sbrHudReference, "SbrExchangeSelection", "", new Vector2(232, 19),
                new Vector2(0, team ? -30f : hudY - 10), team ? 11f : 13f, Color.white);
            MountExchangeButtons();
            Canvas.willRenderCanvases += UpdateSbrHudPosition;
            UpdateSbrHudPosition();
        }
        if (sbrPromptText.text != prompt) TableText.Set(sbrPromptText, prompt);
        if (sbrSelectionText.text != selection) TableText.Set(sbrSelectionText, selection);
    }

    private void ShowSbrExchangeHud(int selected, bool waiting) {
        bool bind = !sbrHudRoot;
        string prompt = _newChineseExchange ? "请选择任意三张手牌交换，或不换" : "请选择三张同花色的手牌交换";
        string detail = waiting ? exchangeWaitHint ?? "等待其他玩家…" : $"已选 {selected}/3 张";
        if (xueliuTeamSelection != null) {
            prompt = waiting ? "已选好换给队友的手牌" :
                xueliuTeamSelection.stage == 1 ? "本家先换，请选择换给队友的手牌" : "请选择换给队友的手牌";
        }
        SetTableExchangeHud(prompt, detail, team: xueliuTeamSelection != null);
        if (bind) {
            if (dingqueWanButton) {
                SetDingqueButtonLabel(dingqueWanButton, "确认");
                dingqueWanButton.onClick.RemoveAllListeners();
                dingqueWanButton.onClick.AddListener(SubmitXueliuThrowThree);
            }
            if (dingqueBingButton) {
                dingqueBingButton.gameObject.SetActive(_newChineseExchange);
                if (_newChineseExchange) {
                    SetDingqueButtonLabel(dingqueBingButton, "不换");
                    dingqueBingButton.onClick.RemoveAllListeners();
                    dingqueBingButton.onClick.AddListener(PassNewChineseExchange);
                }
            }
            if (dingqueTiaoButton) dingqueTiaoButton.gameObject.SetActive(false);
            if (dingqueCountdownText) dingqueCountdownText.text = string.Empty;
            SetDingqueTip(string.Empty);
        }
        if (dingqueWanButton) dingqueWanButton.gameObject.SetActive(!waiting && selected == 3);
        if (dingqueSelectionPanel) dingqueSelectionPanel.SetActive(!waiting && (_newChineseExchange || selected == 3));
    }

    private void ClearSbrExchangeHud() {
        Canvas.willRenderCanvases -= UpdateSbrHudPosition;
        if (exchangeButtonRow) {
            dingqueWanButton.transform.SetParent(dingqueSelectionPanel.transform, false);
            dingqueBingButton.transform.SetParent(dingqueSelectionPanel.transform, false);
            exchangeButtonRow.gameObject.SetActive(false);
            Destroy(exchangeButtonRow.gameObject);
            exchangeButtonRow = null;
        }
        if (sbrHudRoot) {
            if (dingqueSelectionPanel) dingqueSelectionPanel.SetActive(false);
            sbrHudRoot.gameObject.SetActive(false);
            Destroy(sbrHudRoot.gameObject);
        }
        sbrHudRoot = sbrHudReference = null;
    }

    private RectTransform CreateSbrDirection(bool opposite, bool clockwise, string subtitle = null, float arrowAngle = 0f) {
        var reference = SbrNode(exchangePresentationRoot, "SbrExchangeDirection", new Vector2(747, 420));
        reference.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
        var size = new Vector2(278, 184);
        if (opposite) {
            var rect = SbrNode(reference, "Arrows", size);
            rect.localRotation = Quaternion.Euler(0, 0, arrowAngle);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = Resources.Load<Sprite>("TableExchangeOpposite");
            image.preserveAspect = true;
            image.raycastTarget = false;
        } else {
            var arrows = SbrGraphic(reference, "Arrows", size, Vector2.zero, SbrExchangeGraphic.Shape.Arrows);
            arrows.clockwise = clockwise;
        }
        SbrGraphic(reference, "Shade", new Vector2(202, 52), Vector2.zero, SbrExchangeGraphic.Shape.Shade);
        SbrLabel(reference, "Title", "换牌中", new Vector2(180, 30), new Vector2(0, 9), 21,
            new Color(1f, .84f, .29f), FontStyles.Normal, TableHudTypography.Emphasis);
        subtitle ??= opposite ? "本局与对家交换" : clockwise ? "本局按顺时针方向交换" : "本局按逆时针方向交换";
        SbrLabel(reference, "Direction", subtitle, new Vector2(194, 22), new Vector2(0, -13), 13, Color.white);
        PlaceSbrReference(reference, exchangePresentationRoot);
        return reference;
    }

    private sealed class SbrPacket {
        public RectTransform anchor;
        public Transform[] tiles;
        public TableExchangeRoute route;
        public int modelSeat;
        public float[] pathLengths;
        public SbrExchangeSuitBadge badge;
    }

    // Clockwise world yaw: self, left, across, right.
    private static int SbrSeat(string name) => name == "left" ? 1 : name == "top" ? 2 : name == "right" ? 3 : 0;
    private const float SbrPacketDownSeconds = .85f;
    private static readonly int[] SbrSeats = { 0, 1, 2, 3 };
    private static float SbrRollToBack(float t) => Mathf.SmoothStep(0f, 1f,
        Mathf.InverseLerp(.22f, .76f, t));

    private IEnumerator AnimateSbrTableExchange(AskHandActionGBInfo info, Dictionary<int, string> positions) {
        var phase = info.xueliu_exchange;
        int[] senders = phase.active_players ?? System.Array.Empty<int>();
        var flight = AnimateTableExchange(positions, senders, System.Array.ConvertAll(senders, s => (s + phase.direction) % 4),
            null, phase.animation_seconds, phase.cards_face_down ? SbrExchangeSeconds - SbrPacketDownSeconds : SbrExchangeSeconds,
            info.player_index, sbrOutgoingTiles, cardsFaceDown: phase.cards_face_down);
        while (flight.MoveNext()) yield return flight.Current;
        exchangeFlight = null;
    }

    private static TableExchangeRoute[] GetTableExchangeRoutes(IReadOnlyDictionary<int, string> positions,
            int[] senders, int[] recipients) {
        if (positions == null || senders == null || recipients == null || recipients.Length != senders.Length)
            return System.Array.Empty<TableExchangeRoute>();
        var routes = new List<TableExchangeRoute>(senders.Length);
        for (int i = 0; i < senders.Length; i++) {
            if (positions.TryGetValue(senders[i], out string from) && positions.TryGetValue(recipients[i], out string to))
                routes.Add(new TableExchangeRoute(senders[i], recipients[i], SbrSeat(from), SbrSeat(to)));
        }
        return routes.ToArray();
    }

    private static bool IsStraightExchange(TableExchangeRoute[] routes) =>
        System.Array.TrueForAll(routes, r => r.turns == 2) ||
        (routes.Length == 2 && routes[0].from != routes[0].to &&
         routes[0].from == routes[1].to && routes[0].to == routes[1].from);

    private IEnumerator AnimateTableExchange(IReadOnlyDictionary<int, string> positions, int[] senders, int[] recipients,
            string subtitle, float remaining, float duration, int localPlayer = -1, int[] localOutgoing = null,
            int tileCount = 3, bool cardsFaceDown = false, bool parkedAtDeparture = false,
            bool localAlreadyDown = false, AskHandActionGBInfo teamInfo = null) {
        var routes = GetTableExchangeRoutes(positions, senders, recipients);
        if (routes.Length == 0 || !exchangePresentationRoot || duration <= 0 || tileCount < 1) yield break;
        bool straight = IsStraightExchange(routes);
        bool clockwise = System.Array.Exists(routes, r => r.turns == 1);
        float arrowAngle = straight && routes[0].turns != 2
            ? Vector2.SignedAngle(Vector2.up, SeatFront(routes[0].to) - SeatFront(routes[0].from)) : 0f;
        var direction = CreateSbrDirection(straight, clockwise, subtitle, arrowAngle);
        var group = direction.GetComponent<CanvasGroup>();
        var packets = CreateTablePackets(routes, positions, direction, tileCount, localPlayer, localOutgoing);
        if (packets == null) yield break;
        AttachTeamExchangeBadges(packets, teamInfo, positions);
        packets.inFrontOfReceipt = parkedAtDeparture;
        packets.localAlreadyDown = localAlreadyDown;
        float start = cardsFaceDown ? SbrPacketDownSeconds : 0;
        float elapsed = Mathf.Clamp(duration - remaining, 0, duration);
        do {
            float t = Mathf.Lerp(start, SbrExchangeSeconds, elapsed / duration);
            group.alpha = Mathf.InverseLerp(start, start + .14f, t);
            packets.Place(SbrRollToBack(t), Mathf.InverseLerp(1.10f, 2.00f, t));
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        } while (elapsed < duration);
        packets.Place(1, 1);
        if (teamInfo != null) packets.owner.FollowBoard();
    }

    private SbrPacketLayout CreateTablePackets(TableExchangeRoute[] routes, IReadOnlyDictionary<int, string> positions,
            RectTransform reference, int tileCount, int localPlayer, int[] localOutgoing,
            bool handsAlreadyReduced = false, bool parked = false) {
        var table = Game3DManager.Instance;
        var pool = MahjongObjectPool.Instance;
        if (!table || !pool || positions == null || routes.Length == 0) return null;
        var owner = exchangePresentationRoot.gameObject.AddComponent<SbrExchangeModelOwner>();
        owner.world = new GameObject(parked ? "ParkedExchangeModels" : "SbrExchangeModels");
        owner.world.transform.SetParent(table.transform, false);
        var packets = new List<SbrPacket>(routes.Length);
        foreach (var route in routes) {
            string from = positions[route.sender];
            int modelSeat = parked ? route.recipient : route.sender;
            string anchorName = parked ? $"ParkedAt_{modelSeat}" : $"ExchangeFrom_{modelSeat}";
            var packet = new SbrPacket { route = route, modelSeat = modelSeat, tiles = new Transform[tileCount],
                anchor = SbrNode(exchangePresentationRoot, anchorName, Vector2.zero) };
            if (!handsAlreadyReduced && from != "self") owner.HideHandEnd(table.GetPosPanel(from)?.cardsPosition, tileCount);
            Transform world = new GameObject($"From_{modelSeat}").transform;
            world.SetParent(owner.world.transform, false);
            bool known = !GameSession.Current.IsRealtimeSpectator && from == "self" &&
                route.sender == localPlayer && localOutgoing?.Length == tileCount;
            for (int i = 0; i < tileCount; i++) {
                int id = known ? localOutgoing[i] : ConfigManager.BlankFaceImageId;
                GameObject tile = pool.CreatePresentationTile(id);
                if (!tile) continue;
                tile.name = $"Tile_{i}";
                TableWorldObjectMount.AttachPreservingWorld(tile.transform, world);
                Card3DHoverManager.Instance?.ResetAndUnregisterCard(tile);
                MahjongObjectPool.GetTile3D(tile)?.SetHoverPeekAllowed(false);
                foreach (var collider in tile.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                packet.tiles[i] = tile.transform;
            }
            packets.Add(packet);
        }
        var boardCanvas = BoardCanvas.Instance?.GetComponent<Canvas>();
        Camera tableCamera = boardCanvas && boardCanvas.worldCamera ? boardCanvas.worldCamera : Camera.main;
        Canvas overlay = exchangePresentationRoot.GetComponentInParent<Canvas>()?.rootCanvas;
        Camera uiCamera = overlay && overlay.renderMode != RenderMode.ScreenSpaceOverlay ? overlay.worldCamera : null;
        var layout = new SbrPacketLayout { table = table, owner = owner, tableCamera = tableCamera, uiCamera = uiCamera,
            root = exchangePresentationRoot, reference = reference, packets = packets,
            straight = IsStraightExchange(routes), clockwise = System.Array.Exists(routes, r => r.turns == 1) };
        owner.redraw = layout.Redraw;
        if (parked) owner.FollowBoard();
        return layout;
    }

    private sealed class SbrPacketLayout {
        public Game3DManager table;
        public SbrExchangeModelOwner owner;
        public Camera tableCamera, uiCamera;
        public RectTransform root, reference;
        public List<SbrPacket> packets;
        public bool straight, clockwise, inFrontOfReceipt, localAlreadyDown;
        private float roll, travel;
        private int badgeViewer = -1;

        public void AttachBadge(int modelSeat, int suit, bool extendRight) {
            if (suit < 1 || suit > 3) return;
            var packet = packets.Find(p => p.modelSeat == modelSeat);
            if (packet != null && packet.tiles.Length == 3 && packet.tiles[1]) {
                packet.badge = SbrExchangeSuitBadge.Attach(packet.tiles[1], suit, extendRight);
                badgeViewer = GameSession.Current.SelfIndex;
            }
        }

        public void Place(float roll, float travel) {
            this.roll = roll;
            this.travel = travel;
            Redraw();
        }

        private Vector2 PathPosition(TableExchangeRoute route, int turns, float t) {
            if (!straight && route.from != route.to) return SeatFront(route.from + turns * t);
            Vector2 start = SeatFront(route.from);
            if (inFrontOfReceipt) start = Vector2.Lerp(start, new Vector2(-.62f, 6.65f), .43f);
            return route.from == route.to ? start : Vector2.Lerp(start, SeatFront(route.to), t);
        }

        private float PathParameter(SbrPacket packet, int turns, float progress) {
            if (straight || progress <= 0f || progress >= 1f || packet.route.from == packet.route.to) return progress;
            const int segments = 96;
            if (packet.pathLengths == null) {
                var lengths = new float[segments + 1];
                Vector2 previous = PathPosition(packet.route, turns, 0f);
                for (int i = 1; i <= segments; i++) {
                    Vector2 next = PathPosition(packet.route, turns, i / (float)segments);
                    lengths[i] = lengths[i - 1] + Vector2.Distance(previous, next);
                    previous = next;
                }
                packet.pathLengths = lengths;
            }
            var path = packet.pathLengths;
            float distance = progress * path[segments];
            int index = System.Array.BinarySearch(path, distance);
            if (index >= 0) return index / (float)segments;
            int end = ~index;
            return (end - 1 + Mathf.InverseLerp(path[end - 1], path[end], distance)) / segments;
        }

        public void Redraw() {
            PlaceSbrReference(reference, root);
            if (!tableCamera || !root || !reference) return;
            Vector2 center = RectTransformUtility.WorldToScreenPoint(uiCamera, reference.position);
            Vector2 unit = RectTransformUtility.WorldToScreenPoint(uiCamera, reference.TransformPoint(Vector3.right)) - center;
            float pixel = unit.magnitude;
            float width = table.SbrWorldWidth(tableCamera, center + Vector2.down * (125f * pixel), 25.3f * pixel);
            if (badgeViewer >= 0 && !HasPrivateTeamExchangeView(badgeViewer)) {
                foreach (var p in packets) if (p.badge) {
                    p.badge.gameObject.SetActive(false);
                    Object.Destroy(p.badge.gameObject);
                    p.badge = null;
                }
                badgeViewer = -1;
            }
            foreach (var packet in packets) {
                var route = packet.route;
                int turns = route.turns;
                if (!straight && turns == 2 && !clockwise) turns = -2;
                float t = PathParameter(packet, turns, travel);
                float tileRoll = localAlreadyDown && route.from == 0 && !GameSession.Current.IsRealtimeSpectator ? 1f : roll;
                float angle = (route.from + (straight ? 0f : turns * t)) * 90f;
                Vector2 screen = center + PathPosition(route, turns, t) * pixel;
                float near = straight ? Mathf.Lerp(Mathf.Cos(route.from * Mathf.PI / 2f), Mathf.Cos(route.to * Mathf.PI / 2f), t)
                    : Mathf.Cos(angle * Mathf.Deg2Rad);
                float side = straight ? Mathf.Lerp(route.from % 2, route.to % 2, t) : 1f - near * near;
                float size = .94f - .089f * side - .01f * near;
                float stretch = 1f + (near < 0 ? .065f : .012f) * Mathf.Abs(near) + .02f * side;
                table.PlaceSbrPacket(packet.tiles, tableCamera, screen, width * size, angle, tileRoll, route.from == 0, stretch);
                if (packet.badge) packet.badge.SetRoll(tileRoll);
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen,
                        uiCamera, out Vector2 anchor)) packet.anchor.anchoredPosition = anchor;
            }
        }
    }

    private static Vector2 SeatFront(float seat) {
        float rad = seat * Mathf.PI / 2f;
        float side = Mathf.Sin(rad), depth = Mathf.Cos(rad);
        float perspective = 1f - .137f * depth;
        return new Vector2(-.62f + .82f * side - side * 174.7f / perspective, 6.65f - depth * 119.9f / perspective);
    }

}

internal sealed class SbrExchangeModelOwner : MonoBehaviour {
    internal GameObject world;
    internal System.Action redraw;
    private readonly List<(Tile3D tile, uint lease, Transform hand)> hidden = new List<(Tile3D, uint, Transform)>(9);

    internal void FollowBoard() => Canvas.willRenderCanvases += Redraw;
    private void Redraw() => redraw?.Invoke();

    internal void HideHandEnd(Transform hand, int tileCount) {
        if (!hand) return;
        int count = 0;
        for (int i = hand.childCount - 1; i >= 0 && count < tileCount; i--) {
            Transform item = hand.GetChild(i);
            if (!item.gameObject.activeSelf || !item.TryGetComponent(out Tile3D tile)) continue;
            hidden.Add((tile, tile.PoolLeaseVersion, hand));
            item.gameObject.SetActive(false);
            count++;
        }
    }

    internal void Clear() {
        Canvas.willRenderCanvases -= Redraw;
        redraw = null;
        foreach (var item in hidden)
            if (item.tile && item.tile.PoolLeaseVersion == item.lease && item.tile.transform.parent == item.hand)
                item.tile.gameObject.SetActive(true);
        hidden.Clear();
        if (!world) return;
        world.SetActive(false);
        Destroy(world);
        world = null;
    }
    private void OnRectTransformDimensionsChange() => redraw?.Invoke();
    private void OnDisable() => Clear();
}

internal readonly struct TableExchangeRoute {
    internal readonly int sender, recipient, from, to, turns;

    internal TableExchangeRoute(int sender, int recipient, int from, int to) {
        this.sender = sender;
        this.recipient = recipient;
        this.from = from;
        this.to = to;
        int clockwise = (to - from + 4) % 4;
        turns = clockwise == 3 ? -1 : clockwise;
    }
}

internal sealed class SbrExchangeGraphic : MaskableGraphic {
    internal enum Shape { Shade, Arrows }
    internal Shape shape;
    internal bool clockwise;

    protected override void OnPopulateMesh(VertexHelper vh) {
        vh.Clear();
        Rect rect = rectTransform.rect;
        if (shape == Shape.Shade) {
            Color32 edge = new Color32(10, 20, 26, 0);
            Color32 inner = new Color32(10, 20, 26, 188);
            Band(vh, rect.xMin, rect.xMin * .62f, rect, edge, inner);
            Band(vh, rect.xMin * .62f, rect.xMax * .62f, rect, inner, inner);
            Band(vh, rect.xMax * .62f, rect.xMax, rect, inner, edge);
        } else {
            DrawCornerArrows(vh);
        }
    }

    private static void Band(VertexHelper vh, float x1, float x2, Rect rect, Color32 left, Color32 right) {
        Quad(vh, new Vector2(x1,rect.yMin), new Vector2(x1,rect.yMax), new Vector2(x2,rect.yMax),
            new Vector2(x2,rect.yMin), left, left, right, right);
    }

    private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d,
            Color32 ca, Color32 cb, Color32 cc, Color32 cd) {
        int i = vh.currentVertCount;
        vh.AddVert(a,ca,Vector2.zero); vh.AddVert(b,cb,Vector2.zero);
        vh.AddVert(c,cc,Vector2.zero); vh.AddVert(d,cd,Vector2.zero);
        vh.AddTriangle(i,i+1,i+2); vh.AddTriangle(i,i+2,i+3);
    }

    private void DrawCornerArrows(VertexHelper vh) {
        Arc(vh, new Vector2(-45,77), new Vector2(-70,72), new Vector2(-98,55), new Vector2(-118,25));
        Arc(vh, new Vector2(-125,-13), new Vector2(-122,-38), new Vector2(-102,-62), new Vector2(-69,-79));
        Arc(vh, new Vector2(50,-85), new Vector2(78,-80), new Vector2(102,-59), new Vector2(123,-29));
        Arc(vh, new Vector2(126,9), new Vector2(121,34), new Vector2(100,57), new Vector2(68,69));
    }

    private void Arc(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d) {
        if (clockwise) DrawArrow(vh, d, c, b, a);
        else DrawArrow(vh, a, b, c, d);
    }

    private static void DrawArrow(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d) {
        for (int pass = 0; pass < 3; pass++) {
            float extra = pass == 0 ? 3.6f : pass == 1 ? 1.6f : 0;
            Color32 color = pass == 0 ? new Color32(45, 195, 255, 32) : pass == 1
                ? new Color32(116, 221, 255, 105) : new Color32(239, 254, 255, 255);
            Vector2 previousLeft = a, previousRight = a, normal = Vector2.zero, point = a;
            const int segments = 20;
            for (int i = 0; i <= segments; i++) {
                float t = .81f * i / segments, v = 1f - t;
                point = v*v*v*a + 3*v*v*t*b + 3*v*t*t*c + t*t*t*d;
                Vector2 tangent = (v*v*(b-a) + 2*v*t*(c-b) + t*t*(d-c)).normalized;
                normal = new Vector2(-tangent.y, tangent.x);
                float halfWidth = Mathf.Lerp(.1f, 3.8f, t/.81f) + extra;
                Vector2 left = point + normal * halfWidth, right = point - normal * halfWidth;
                if (i > 0) Quad(vh, previousLeft, left, right, previousRight, color, color, color, color);
                previousLeft = left; previousRight = right;
            }
            int n = vh.currentVertCount;
            float headWidth = 10f + extra;
            vh.AddVert(point + normal*headWidth,color,Vector2.zero);
            vh.AddVert(d + (d-c).normalized*extra,color,Vector2.zero);
            vh.AddVert(point - normal*headWidth,color,Vector2.zero);
            vh.AddTriangle(n,n+1,n+2);
        }
    }

}
