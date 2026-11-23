#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class XueliuExchangePresentationValidation {
    private static Vector2[] ArrowVertices(Transform root) {
        var arrows = root.Find("SbrExchangeDirection/Arrows")?.GetComponent<SbrExchangeGraphic>();
        if (!arrows) return Array.Empty<Vector2>();
        var stream = new List<UIVertex>();
        using (var helper = new VertexHelper()) {
            typeof(SbrExchangeGraphic).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new[] {typeof(VertexHelper)}, null)
                .Invoke(arrows, new object[] { helper });
            helper.GetUIVertexStream(stream);
        }
        return stream.Where(v => v.color.a == 255).Select(v => (Vector2)v.position).ToArray();
    }

    internal static bool HasExchangeArrows(Transform root, int direction) {
        var arrow = root.Find("SbrExchangeDirection/Arrows");
        if (!arrow) return false;
        if (direction == 2) {
            var image = arrow.GetComponent<Image>();
            return image && image.sprite && image.sprite == Resources.Load<Sprite>("TableExchangeOpposite") &&
                image.rectTransform.sizeDelta == new Vector2(278, 184) && !image.raycastTarget;
        }
        Vector2[] tips = direction == 3
                ? new[]{new Vector2(-45,77), new Vector2(-125,-13), new Vector2(50,-85), new Vector2(126,9)}
                : new[]{new Vector2(-118,25), new Vector2(-69,-79), new Vector2(123,-29), new Vector2(68,69)};
        var vertices = ArrowVertices(root);
        return tips.All(tip => vertices.Any(v => Vector2.Distance(tip, v) < 1f));
    }

    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Fields).GetValue(owner);
    private static RectTransform Root() => UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None)
        .FirstOrDefault(t=>t.name=="XueliuExchange");
    private static Image Light(int seat) => Field<Image>(BoardCanvas.Instance,
        "player_"+NormalGameStateManager.Instance.indexToPosition[seat]+"_current_image");

    private static Transform Models(string name) => Game3DManager.Instance.transform.Cast<Transform>()
        .FirstOrDefault(t => t.name == name && t.gameObject.activeInHierarchy);

    private static Vector2 PacketPoint(Transform root, string reference, string packet) =>
        root.Find(reference).InverseTransformPoint(root.Find(packet).position);

    private static Vector3[] BadgeCorners(SbrExchangeSuitBadge badge, bool inTileSpace = false) {
        var plate = badge.GetComponentInChildren<SpriteRenderer>(true);
        var bounds = plate.localBounds;
        var points = new[] {bounds.min, bounds.max, bounds.center + new Vector3(-bounds.extents.x, bounds.extents.y, 0)};
        for (int i = 0; i < points.Length; i++) {
            points[i] = plate.transform.TransformPoint(points[i]);
            if (inTileSpace) points[i] = badge.transform.parent.InverseTransformPoint(points[i]);
        }
        return points;
    }

    private static bool SameCorners(Vector3[] a, Vector3[] b) =>
        a.Length == b.Length && Enumerable.Range(0, a.Length).All(i => Vector3.Distance(a[i], b[i]) < .01f);

    private static bool TagOutsidePacket(SbrExchangeSuitBadge badge, bool right) {
        var camera = BoardCanvas.Instance.GetComponent<Canvas>().worldCamera ?? Camera.main;
        var plate = badge.GetComponentInChildren<SpriteRenderer>(true);
        float tagX = camera.WorldToScreenPoint(plate.bounds.center).x;
        foreach (Transform tile in badge.transform.parent.parent) {
            var body = tile.GetComponent<MeshFilter>() ?? tile.GetComponentInChildren<MeshFilter>();
            Bounds box = body.sharedMesh.bounds;
            for (int i = 0; i < 8; i++) {
                Vector3 p = new Vector3((i & 1) == 0 ? box.min.x : box.max.x,
                    (i & 2) == 0 ? box.min.y : box.max.y, (i & 4) == 0 ? box.min.z : box.max.z);
                float tileX = camera.WorldToScreenPoint(body.transform.TransformPoint(p)).x;
                if (right ? tagX <= tileX : tagX >= tileX) return false;
            }
        }
        return true;
    }

    private static bool AtSeatFront(int seat, Vector2 point) {
        switch (NormalGameStateManager.Instance.indexToPosition[seat]) {
            case "self": return Mathf.Abs(point.x) < 2f && point.y < -100f;
            case "left": return Mathf.Abs(point.y) < 12f && point.x < -110f;
            case "right": return Mathf.Abs(point.y) < 12f && point.x > 110f;
            case "top": return Mathf.Abs(point.x) < 2f && point.y > 70f;
            default: return false;
        }
    }

    public static IEnumerator CheckBloodBattleDirection(GameCanvas canvas, int direction,
            Action<bool, string> check, Func<int> nextTick) {
        var battle = SichuanPresentationValidation.Fixture(SichuanLobby.SbrExchange, 0);
        NormalGameStateManager.Instance.InitializeGame(true, "血战换三张", battle);
        while (canvas.IsChangeHandCardProcessing) yield return null;
        if (direction == 1) {
            canvas.ShowXueliuThrowThreeSelection(10, true, sbr: true);
            var confirm = (RectTransform)Field<Button>(canvas, "dingqueWanButton").transform;
            check(!confirm.gameObject.activeInHierarchy, "未选满三张时应隐藏换牌确认按钮");
            var suit = canvas.GetMainHandCardsOrdered().GroupBy(c => c.tileId / 10)
                .OrderByDescending(g => g.Count()).First().ToArray();
            var picks = new[]{suit[suit.Length - 1], suit[0], suit[suit.Length / 2]};
            foreach (var card in picks) check(canvas.TryHandleXueliuTileClick(card), "血战选牌失败");
            Canvas.ForceUpdateCanvases();
            var actions = (RectTransform)canvas.ActionButtonHost;
            check(confirm.gameObject.activeInHierarchy && Vector2.Distance(confirm.TransformPoint(confirm.rect.max),
                actions.TransformPoint(actions.rect.max)) < 1f, "换牌确认按钮应贴齐常规操作栏右端");
            var submitted = picks.Select(c => c.tileId).ToArray();
            var expected = picks.OrderBy(c => c.handSortIndex).Select(c => c.tileId).ToArray();
            var standing = new Quaternion[4];
            var anchors = new Vector2[4];
            foreach (float elapsed in new[]{0f, .22f, .49f, .76f, 1.25f}) {
                canvas.ShowXueliuExchangePresentation(new AskHandActionGBInfo {
                    action_tick = nextTick(), player_index = 0, xueliu_exchange = new XueliuExchangeInfo {
                        animating = true, dice = new[]{2,5}, outgoing_tiles = submitted,
                        animation_seconds = 2.35f - elapsed
                    }
                }, sbr: true);
                var root = Root();
                var models = Models("SbrExchangeModels");
                check(models.GetComponentsInChildren<Tile3D>().Length == 12, "四组立牌应在桌面同时出现");
                check(models.Find("From_0").GetComponentsInChildren<Tile3D>().Select(tile => tile.GetTileId())
                    .SequenceEqual(expected), "本家桌面立牌应与所交手牌一致");
                check(picks.All(c => c.GetComponent<CanvasGroup>().alpha == 0), "交牌时选中手牌应立即隐藏");
                for (int seat = 0; seat < 4; seat++) {
                    var packet = models.Find($"From_{seat}");
                    var anchor = ((RectTransform)root.Find($"ExchangeFrom_{seat}")).anchoredPosition;
                    if (elapsed == 0f) {
                        standing[seat] = packet.GetChild(1).rotation;
                        anchors[seat] = anchor;
                    }
                    float tilt = Quaternion.Angle(standing[seat], packet.GetChild(1).rotation);
                    float target = elapsed <= .22f ? 0f : elapsed >= .76f ? 90f : 45f;
                    check(Mathf.Abs(tilt - target) < 1f && Vector2.Distance(anchors[seat], anchor) < .1f,
                        "盖牌应在固定桌位绕底边翻转");
                    if (seat != 0) check(packet.GetComponentsInChildren<Tile3D>().All(tile =>
                        tile.GetTileId() == ConfigManager.BlankFaceImageId), "立牌时应隐藏他家点数");
                }
            }
            canvas.ClearXueliuExchangePresentation();
            yield return null;
            canvas.ShowDingqueSelection();
            var suits = Field<GameObject>(canvas, "dingqueSelectionPanel").transform;
            check(confirm.parent == suits && Field<Button>(canvas, "dingqueBingButton").transform.parent == suits &&
                Field<Button>(canvas, "dingqueTiaoButton").gameObject.activeInHierarchy, "换牌后定缺按钮应回到原面板");
            canvas.HideDingqueSelection();
        }
        foreach (float remaining in new[]{2.75f, 2f, 1.65f, 1.25f, .625f, .02f}) {
            canvas.ShowXueliuExchangePresentation(new AskHandActionGBInfo{
                action_tick=nextTick(), player_index=0, xueliu_exchange=new XueliuExchangeInfo{
                    direction=direction, active_players=new[]{0,1,2,3}, animating=true,
                    animation_seconds=remaining, outgoing_suit=1, teammate_suit=2}}, sbr: true);
            var packets=Root();
            var world = Game3DManager.Instance.transform.Find("SbrExchangeModels");
            check(world && world.childCount == 4 && world.Cast<Transform>().All(p => p.childCount == 3),
                "四家应各有三张桌面模型");
            check(world.GetComponentsInChildren<Tile3D>().All(t => t.GetTileId() == ConfigManager.BlankFaceImageId),
                "没有换出面值的视角不应显示真实牌面");
            var subtitle = packets.Find("SbrExchangeDirection").GetComponentsInChildren<TMP_Text>();
            check(subtitle.Any(t => t.text.Contains(direction == 2 ? "对家" : "时针")), "交换方向未显示");
            var anchors = packets.Cast<RectTransform>().Where(p => p.name.StartsWith("ExchangeFrom_")).ToArray();
            check(anchors.Length == 4, "缺少换牌座位");
            if (direction != 2)
                check(anchors.All(a => anchors.All(b => a == b ||
                    Vector2.Distance(a.anchoredPosition, b.anchoredPosition) > 25f)), "相邻席换牌时牌组应分开");
            canvas.ClearXueliuExchangePresentation();
            yield return null;
        }
        var previousPoints = new Vector2[4];
        var startPoints = new Vector2[4];
        var startFacing = new Vector3[4];
        var distances = new float[4, 16];
        for (int step = 0; step <= 16; step++) {
            float elapsed = Mathf.Lerp(1.10f, 2.00f, step / 16f);
            canvas.ShowXueliuExchangePresentation(new AskHandActionGBInfo {
                action_tick = nextTick(), player_index = 0, xueliu_exchange = new XueliuExchangeInfo {
                    animating = true, direction = direction, active_players = new[]{0,1,2,3},
                    animation_seconds = 2.75f - elapsed
                }
            }, sbr: true);
            var root = Root();
            if (step == 0) check(HasExchangeArrows(root, direction), "血战换牌箭头的位置或指向错误");
            for (int seat = 0; seat < 4; seat++) {
                Vector3 facing = Models("SbrExchangeModels").Find($"From_{seat}/Tile_1").right;
                if (step == 0) startFacing[seat] = facing;
                float yaw = Vector3.Angle(startFacing[seat], facing);
                if (direction == 2) check(yaw < 1f, "对家交换中牌组发生自转");
                else if (step == 8) check(yaw > 25f && yaw < 65f, "相邻席传到半途时牌组朝向错误");
                else if (step == 16) check(Mathf.Abs(yaw - 90f) < 1f, "相邻席抵达时应转向九十度");
                Vector2 point = ((RectTransform)root.Find("ExchangeFrom_" + seat)).anchoredPosition;
                if (step == 0) startPoints[seat] = point;
                if (direction == 2) {
                    bool vertical = NormalGameStateManager.Instance.indexToPosition[seat] == "self" ||
                        NormalGameStateManager.Instance.indexToPosition[seat] == "top";
                    float sideways = vertical ? point.x - startPoints[seat].x : point.y - startPoints[seat].y;
                    check(Mathf.Abs(sideways) < .1f, "对家交换应沿本席轴线直穿桌心");
                }
                if (step > 0) distances[seat, step - 1] = Vector2.Distance(previousPoints[seat], point);
                previousPoints[seat] = point;
            }
            canvas.ClearXueliuExchangePresentation();
            yield return null;
        }
        for (int seat = 0; seat < 4; seat++) {
            var steps = Enumerable.Range(0, 16).Select(step => distances[seat, step]).ToArray();
            check(steps.Min() > 0f && steps.Max() < steps.Min() * 1.025f,
                $"换牌未匀速 direction={direction} seat={seat} min={steps.Min()} max={steps.Max()}");
        }
    }

    public static IEnumerator Run(Action<string> completed, string screenshots) {
        if (!Application.isPlaying || !string.IsNullOrEmpty(UserDataManager.Instance.GamestateId))
            throw new InvalidOperationException("需要空闲 Play Mode");
        int checks=0;
        void Check(bool ok,string message) { if (!ok) throw new Exception(message); checks++; }
        SbrExchangeSuitBadge CheckBadge(Transform models, int packetSeat, int suit) {
            var badges = models.GetComponentsInChildren<SbrExchangeSuitBadge>(true);
            Check(badges.Length == 1 && badges[0].Suit == suit, "换牌花色未按座位权限标记");
            Check(badges[0].transform.parent == models.Find($"From_{packetSeat}/Tile_1"), "花色没有直接锁在本队的中间一张牌");
            return badges[0];
        }
        var canvas=GameCanvas.Instance;
        Check(!string.IsNullOrEmpty(SichuanDoublesValidation.ShowAndValidateRoom()),"血战房间布局");
        Check(!string.IsNullOrEmpty(SichuanDoublesValidation.ShowAndValidateRoom("sichuan/xueliu_doubles")),"血流房间布局");
        yield return new WaitForSecondsRealtime(.5f);
        UnityEngine.ScreenCapture.CaptureScreenshot(screenshots+"/room.png");
        yield return null;
        int tick=100;
        var originalStyle = SbrExchangeSuitBadge.PreviewStyle;
        try {
            for (int round=1;round<=4;round++) for (int self=0;self<4;self++) {
                SbrExchangeSuitBadge.PreviewStyle = round % 2 == 0 ? SbrExchangeSuitBadge.Style.SideTag : originalStyle;
                bool duplicate = round > 2;
                string subRule = duplicate ? SichuanLobby.XueliuDuplicateSubRule : SichuanLobby.XueliuDoublesSubRule;
                var fixture=SichuanPresentationValidation.Fixture(subRule,self,round);
                NormalGameStateManager.Instance.InitializeGame(true,"local exchange validation",fixture);
                while(canvas.IsChangeHandCardProcessing) yield return null;
                foreach (var panel in UnityEngine.Object.FindObjectsByType<GamePlayerPanel>(FindObjectsSortMode.None)) {
                    if (panel.SichuanTeamId < 0) continue;
                    Color color = Field<Image>(panel,"sichuanTeamBackground").color;
                    Check(panel.SichuanTeamId == 0 ? color.b > color.r * 3 : color.r > color.g * 3,
                        "队伍底色必须为蓝/红，不能是橙色");
                }
                int first=round-1;
                int[][] groups={new[]{first,(first+1)%4},new[]{(first+2)%4,(first+3)%4}};
                var firstArrivals = new Dictionary<int,Vector2>();
                var ownTiles = fixture.players_info[self].hand_tiles.Where(id => id / 10 == 2).Take(3).ToArray();
                Vector3[] firstBadgeCorners = null;
                for (int stage=1;stage<=2;stage++) {
                    bool active=groups[stage-1].Contains(self);
                    bool tagRight = groups[stage-1].Any(seat => NormalGameStateManager.Instance.indexToPosition[seat] == "left");
                    Vector2? submittedPoint = null;
                    Vector3[] submittedCorners = null;
                    var phase=new XueliuExchangeInfo{stage=stage,active_players=groups[stage-1],
                        teammate_suit=stage==2 && active ? 1 : 0, selection_done=stage==2 && !active,
                        outgoing_suit=stage==2 && !active ? 2 : 0, outgoing_tiles=stage==2 && !active ? ownTiles : null};
                    var info=new AskHandActionGBInfo{action_tick=++tick,player_index=self,remaining_time=10,
                        action_list=active ? new[]{"xueliu_exchange_three"} : Array.Empty<string>(),xueliu_exchange=phase,remain_tiles=55};
                    void Deliver() => SichuanGameState.Active.HandleMessage("broadcast_hand_action",new Response{ask_hand_action_info=info});
                    Deliver();
                    if (stage == 2) {
                        var parkedBadge = CheckBadge(Models("ParkedExchangeModels"), active ? self : (self + 2) % 4, active ? 1 : 2);
                        Check(SameCorners(firstBadgeCorners, BadgeCorners(parkedBadge)), "第一拨到达后重建驻牌时花色徽标跳位或翻转");
                        if (parkedBadge.BadgeStyle == SbrExchangeSuitBadge.Style.SideTag)
                            Check(TagOutsidePacket(parkedBadge, !tagRight), "驻牌挂签没有保留第一拨的牌包外侧");
                    }
                    if (duplicate && stage == 2)
                        foreach (var arrival in firstArrivals) {
                            var atRest = PacketPoint(Root(), "ParkedExchange", $"ParkedAt_{arrival.Key}");
                            Check(Vector2.Distance(arrival.Value, atRest) < 1f, "第一拨收牌转为等候选牌时位置跳变");
                        }
                    for(int seat=0;seat<4;seat++) Check(Light(seat).gameObject.activeSelf==groups[stage-1].Contains(seat),"闪烁组或视角映射错误");
                    float alpha=Light(groups[stage-1][0]).color.a;
                    yield return new WaitForSecondsRealtime(.24f);
                    Check(Mathf.Abs(alpha-Light(groups[stage-1][0]).color.a)>.01f,"两家未持续闪烁");
                    Check(Mathf.Approximately(Light(groups[stage-1][0]).color.a,Light(groups[stage-1][1]).color.a),"两家闪烁不同步");
                    TMP_Text tip=Field<TMP_Text>(canvas,"dingqueTipText");
                    Check(!tip.text.Contains("先手") && !tip.text.Contains("与对家互换"),"多余选牌文本仍在");
                    RectTransform teamHud = Field<RectTransform>(canvas, "sbrHudReference");
                    Check(teamHud.Find("Title").GetComponent<TMP_Text>().text == "换牌中", "双打选牌和等待均应显示换牌标题");
                    string expected = active
                        ? stage == 1 ? "本家先换，请选择换给队友的手牌" : "请选择换给队友的手牌"
                        : stage == 1 ? "队友先换，等待队友选择换出手牌" : "等待队友选择换出手牌";
                    Check(Field<TMP_Text>(canvas, "sbrPromptText").text == expected, "换牌说明没有按座位和拨次提示");
                    string detail = Field<TMP_Text>(canvas, "sbrSelectionText").text;
                    Check(detail == (active ? "已选 0/3 张" : ""), "双打选牌只显示当前进度");
                    if(active) {
                        phase.selection_done=true;
                        phase.outgoing_tiles=ownTiles;
                        info.action_list=Array.Empty<string>();
                        Deliver();
                        Check(Field<TMP_Text>(canvas, "sbrPromptText").text == "已选好换给队友的手牌",
                            "确认选牌或恢复已提交状态后仍提示选择手牌");
                        Check(Light(groups[stage-1][0]).gameObject.activeSelf && Light(groups[stage-1][1]).gameObject.activeSelf,"一人提交后提前灭灯");
                        Check(!Field<Button>(canvas,"dingqueWanButton").gameObject.activeInHierarchy,"已提交仍能再次提交");
                        if (duplicate) {
                            var waiting = Models("DuplicateSubmittedModels");
                            Check(waiting && waiting.childCount == 1 && waiting.Find($"From_{self}"), "确认后只能展示本席已交的牌包");
                            var committedBadge = CheckBadge(waiting, self, 2);
                            submittedCorners = BadgeCorners(committedBadge);
                            if (committedBadge.BadgeStyle == SbrExchangeSuitBadge.Style.SideTag)
                                Check(TagOutsidePacket(committedBadge, tagRight), "交牌候场时挂签没有避开同拨另一出牌者");
                            var apart = waiting.GetComponentsInChildren<Tile3D>();
                            Check(apart.Length == 3 && apart.All(tile => tile.GetTileId() == ConfigManager.BlankFaceImageId),
                                "复式提交后门前三张须扣放");
                            Vector2 point = PacketPoint(Root(), "DuplicateLocalSubmission", $"SubmittedAt_{self}");
                            submittedPoint = point;
                            Check(Mathf.Abs(point.x) < 2f && (stage == 1 ? AtSeatFront(self, point) : point.y > -90f && point.y < -50f),
                                "复式已交牌没有置于门前或第二拨与第一拨同排");
                            if (stage == 2) {
                                var shade = (RectTransform)Field<RectTransform>(canvas, "sbrHudReference").Find("Shade");
                                var packet = Root().Find($"SubmittedAt_{self}");
                                Check(shade.InverseTransformPoint(packet.position).y < shade.rect.yMin - 16f,
                                    "复式第二拨已交的牌被等待提示遮挡");
                            }
                            Check(canvas.GetMainHandCardsOrdered().Count(tile => tile.TryGetComponent(out CanvasGroup group) && group.alpha < .01f) == 3,
                                "复式已交的三张同时出现在本家手中和桌面");
                        }
                    }
                    if(round==1 && self==2 && stage==2) {
                        UnityEngine.ScreenCapture.CaptureScreenshot(screenshots+"/second-pair.png");
                        yield return null;
                    }
                    phase.animating=true;
                    phase.outgoing_suit=active || stage==2 ? 2 : 0;
                    phase.teammate_suit=!active || stage==2 ? 1 : 0;
                    info.action_list=Array.Empty<string>();
                    phase.direction=2;
                    phase.outgoing_tiles=phase.outgoing_suit == 0 ? null : ownTiles;
                    phase.animation_seconds=.625f;
                    info.action_tick=++tick;
                    Deliver();
                    RectTransform root=Root();
                    Check(HasExchangeArrows(root, 2), "两拨队友传牌应使用对家互换箭头");
                    var anchors=root.Cast<RectTransform>().Where(t=>t.name.StartsWith("ExchangeFrom_")).ToArray();
                    Check(anchors.Length==2 && Vector2.Distance(anchors[0].anchoredPosition, anchors[1].anchoredPosition)>25,
                        $"两队牌包在途中重叠 round={round}, self={self}, stage={stage}");
                    phase.animation_seconds=2f;
                    info.action_tick=++tick;
                    Deliver();
                    root=Root();
                    var models=Models("SbrExchangeModels");
                    Check(models && models.childCount==2 && groups[stage-1].All(seat=>models.Find($"From_{seat}")),"必须只播放当前两人的换牌");
                    var flyingBadge = CheckBadge(models, active ? self : (self+2)%4, active ? 2 : 1);
                    Check(flyingBadge.gameObject.activeSelf == (duplicate && active), "立着的牌提前显示或本家已扣放的牌遗失了徽标");
                    Vector3[] localCorners = BadgeCorners(flyingBadge, inTileSpace:true);
                    if (submittedCorners != null)
                        Check(SameCorners(submittedCorners, BadgeCorners(flyingBadge)), "已交牌进入飞行动画时花色徽标跳位或翻转");
                    if (stage == 2)
                        CheckBadge(Models("ParkedExchangeModels"), active ? self : (self+2)%4, active ? 1 : 2);
                    Check(models.Cast<Transform>().All(packet=>packet.childCount==3),"换牌动画并非两组三张");
                    Check(models.Cast<Transform>().Where(p=>p.name!=$"From_{self}").SelectMany(p=>p.GetComponentsInChildren<Tile3D>())
                        .All(t=>t.GetTileId()==ConfigManager.BlankFaceImageId),"换牌泄露其他玩家的数字");
                    Check(root.Find("SbrExchangeDirection/Direction").GetComponent<TMP_Text>().text ==
                        (active ? "本家换给队友三张牌" : "队友传来三张万牌"), "传牌时队友花色或收发视角错误");
                    Check(!Field<RectTransform>(canvas, "sbrHudRoot"), "飞牌开始后残留选牌提示");
                    Check((Models("ParkedExchangeModels")!=null)==(stage==2),"第一拨牌未留在对家桌前");
                    if (submittedPoint.HasValue) {
                        Vector2 departing = PacketPoint(root, "SbrExchangeDirection", $"ExchangeFrom_{self}");
                        Check(Vector2.Distance(submittedPoint.Value, departing) < 1f, "复式扣放后起步位置跳变");
                    }
                    Check(root.GetComponentsInChildren<Graphic>().All(g=>!g.Raycast(
                        RectTransformUtility.WorldToScreenPoint(null,g.rectTransform.position),null)),"动画拦截了输入");
                    Check(!Field<GameObject>(canvas,"dingqueSelectionPanel").activeSelf,"动画中仍可选牌");
                    for(int seat=0;seat<4;seat++) Check(!Light(seat).gameObject.activeSelf,"动画期间提前亮起下一组");
                    Deliver();
                    Check(Root()==root,"同一动画消息重复播放");
                    yield return new WaitForSecondsRealtime(.55f);
                    Check(SameCorners(localCorners, BadgeCorners(flyingBadge, inTileSpace:true)), "翻牌时徽标相对牌身滑动");
                    var track=(RectTransform)root.Find($"ExchangeFrom_{groups[stage-1][0]}");
                    Vector2 middle=track.anchoredPosition;
                    if(round==1 && self==2 && stage==1) UnityEngine.ScreenCapture.CaptureScreenshot(screenshots+"/exchange.png");
                    yield return new WaitForSecondsRealtime(1.6f);
                    Check(SameCorners(localCorners, BadgeCorners(flyingBadge, inTileSpace:true)), "传牌时徽标相对牌身滑动");
                    Check(flyingBadge.gameObject.activeInHierarchy, "扣牌抵达时未显示授权徽标");
                    if (flyingBadge.BadgeStyle == SbrExchangeSuitBadge.Style.SideTag)
                        Check(TagOutsidePacket(flyingBadge, tagRight), "飞行挂签没有落在同拨另一出牌者反侧的包外");
                    if (stage == 1) firstBadgeCorners = BadgeCorners(flyingBadge);
                    Check(!canvas.IsXueliuExchangeAnimating,"换牌动画未结束");
                    Check(Vector2.Distance(middle,track.anchoredPosition)>40,"换牌未抵达对家");
                    if (duplicate)
                        foreach (int seat in groups[stage-1]) {
                            int receiver = (seat + 2) % 4;
                            Vector2 arrival = PacketPoint(root, "SbrExchangeDirection", $"ExchangeFrom_{seat}");
                            Check(AtSeatFront(receiver, arrival), "复式换入牌没有停在接收席正前方");
                            if (stage == 1) firstArrivals[receiver] = arrival;
                        }
                }
            }
            for (int face = 1; face <= 6; face++) {
                var roll = new AskHandActionGBInfo {
                    action_tick = ++tick, player_index = 0, xueliu_exchange = new XueliuExchangeInfo {
                        animating = true, dice = new[]{face, 7 - face}, animation_seconds = .15f
                    }
                };
                canvas.ShowXueliuExchangePresentation(roll, sbr: true);
                var root = Root();
                var red = root.Find("SbrExchangeDice/SbrRedDie").GetComponent<SbrExchangeDie>();
                var white = root.Find("SbrExchangeDice/SbrWhiteDie").GetComponent<SbrExchangeDie>();
                Check(red.TopFace == face && white.TopFace == 7 - face, "重连时骰子顶面与服务端结果不符");
                Check(root.Find("SbrExchangeDirection") == null, "骰子停驻时提前显示换牌方向");
                canvas.ShowXueliuExchangePresentation(roll, sbr: true);
                Check(Root() == root, "重复报文重掷了骰子");
                canvas.ClearXueliuExchangePresentation();
                Check(Root() == null, "关闭后骰子仍留在桌面");
                yield return null;
            }
            for (int direction=1; direction<=3; direction++) {
                var solo = SichuanPresentationValidation.Fixture("sichuan/xueliu_exchange", direction);
                NormalGameStateManager.Instance.InitializeGame(true, "solo table exchange", solo);
                while (canvas.IsChangeHandCardProcessing) yield return null;
                var outgoing = solo.players_info[direction].hand_tiles.Take(3).ToArray();
                var ask = new AskHandActionGBInfo {action_tick=++tick, player_index=direction,
                    action_list=new[]{"xueliu_exchange_three"}, remaining_time=10};
                SichuanGameState.Active.HandleMessage("broadcast_hand_action",new Response{ask_hand_action_info=ask});
                Check(canvas.IsSbrChoosingOrWaiting, "血流普通未使用同花三张选牌");
                ask.action_tick = ++tick;
                ask.action_list = Array.Empty<string>();
                ask.xueliu_exchange = new XueliuExchangeInfo {animating=true, direction=direction,
                    active_players=new[]{0,1,2,3}, outgoing_tiles=outgoing, animation_seconds=2.7f};
                SichuanGameState.Active.HandleMessage("broadcast_hand_action",new Response{ask_hand_action_info=ask});
                Check(!Root().Find("SbrExchangeDice") && Models("SbrExchangeModels")?.childCount==4, "血流四家随机传牌不掷骰");
                Check(Models("SbrExchangeModels").GetComponentsInChildren<SbrExchangeSuitBadge>(true).Length == 0,
                    "普通血流换三张误用了双打花色标记");
                Check(HasExchangeArrows(Root(), direction), "血流换牌箭头与血战相同方向不一致");
                Check(Models("SbrExchangeModels").Find($"From_{direction}").GetComponentsInChildren<Tile3D>()
                    .Select(t=>t.GetTileId()).SequenceEqual(outgoing), "血流本人交牌与实际选择不符");
                canvas.ClearXueliuExchangePresentation();
                yield return null;
                yield return CheckBloodBattleDirection(canvas, direction, Check, () => ++tick);
            }
            // 动画中重连及离开，旧组不能继续闪烁或残留卡片。
            NormalGameStateManager.Instance.InitializeGame(true,"exchange restore",SichuanPresentationValidation.Fixture(SichuanLobby.XueliuDoublesSubRule,3));
            while(canvas.IsChangeHandCardProcessing) yield return null;
            var reconnect=new AskHandActionGBInfo{action_tick=++tick,player_index=3,action_list=Array.Empty<string>(),
                xueliu_exchange=new XueliuExchangeInfo{stage=1,active_players=new[]{0,1},animating=true,animation_seconds=.3f,teammate_suit=1}};
            canvas.ShowXueliuExchangePresentation(reconnect);
            yield return new WaitForSecondsRealtime(.4f);
            Check(!canvas.IsXueliuExchangeAnimating,"重连重播了完整动画");
            CheckBadge(Models("SbrExchangeModels"), 1, 1);
            canvas.ClearXueliuExchangePresentation();
            Check(Root()==null,"结束残留换牌卡片");
            reconnect.action_tick=++tick;
            reconnect.xueliu_exchange.animation_seconds=1.4f;
            canvas.ShowXueliuExchangePresentation(reconnect);
            canvas.ShowXueliuTeamExchange(reconnect);
            var next=new AskHandActionGBInfo{action_tick=++tick,player_index=3,remaining_time=10,
                action_list=new[]{"xueliu_exchange_three"},xueliu_exchange=new XueliuExchangeInfo{
                    stage=2,active_players=new[]{2,3},teammate_suit=1}};
            Check(!canvas.ShowXueliuExchangePresentation(next),"动画未完成就开放下一组");
            Check(!Field<GameObject>(canvas,"dingqueSelectionPanel").activeSelf,"延迟网络期间提前出现选牌");
            yield return new WaitForSecondsRealtime(1.4f);
            Check(canvas.IsSbrChoosingOrWaiting && !Field<GameObject>(canvas,"dingqueSelectionPanel").activeSelf,
                "动画结束后未恢复选牌或未选满便显示确认");
            Check(Light(2).gameObject.activeSelf && Light(3).gameObject.activeSelf,"动画后未切到后一组");
            Check(Models("ParkedExchangeModels")?.childCount==2,"后手缺少第一拨的两包桌牌");
            // 等待第二组选牌时，收到的花色牌仍随中心盘移动，不能固定在旧屏幕坐标。
            RectTransform incoming=(RectTransform)Root().Find("ParkedAt_3");
            Vector2 previous=incoming.anchoredPosition;
            var receivedBadge = CheckBadge(Models("ParkedExchangeModels"), 3, 1);
            Vector3[] localBadge = BadgeCorners(receivedBadge, inTileSpace:true);
            Vector3[] previousBadge = BadgeCorners(receivedBadge);
            Vector3 oldBoardPosition=BoardCanvas.Instance.transform.position;
            try {
                BoardCanvas.Instance.transform.position+=new Vector3(12,0,0);
                yield return null;
                Canvas.ForceUpdateCanvases();
                Check(incoming.anchoredPosition.x>previous.x+10,"等待中的换入牌没有跟随中心盘位置");
                Check(!SameCorners(previousBadge, BadgeCorners(receivedBadge)) &&
                    SameCorners(localBadge, BadgeCorners(receivedBadge, inTileSpace:true)), "牌桌移动时徽标未锁住换牌实物");
            } finally { BoardCanvas.Instance.transform.position=oldBoardPosition; }
            bool wasSpectator = GameSession.Current.IsRealtimeSpectator;
            try {
                GameSession.Current.IsRealtimeSpectator = true;
                Canvas.ForceUpdateCanvases();
                Check(!receivedBadge || !receivedBadge.gameObject.activeInHierarchy, "进入观战后遗留了当前座位的花色");
                reconnect.action_tick = ++tick;
                canvas.ShowXueliuExchangePresentation(reconnect);
                Check(Models("SbrExchangeModels").GetComponentsInChildren<SbrExchangeSuitBadge>(true).Length == 0,
                    "观战误用了牌局的私有花色");
            } finally {
                GameSession.Current.IsRealtimeSpectator = wasSpectator;
                canvas.ClearXueliuExchangePresentation();
            }
            reconnect.action_tick=++tick;
            reconnect.player_index=2;
            canvas.ShowXueliuExchangePresentation(reconnect);
            Check(Models("SbrExchangeModels").GetComponentsInChildren<SbrExchangeSuitBadge>(true).Length == 0,
                "给其他座位的换牌消息生成了花色标记");
            canvas.ClearXueliuExchangePresentation();
            reconnect.action_tick=++tick;
            reconnect.player_index=3;
            canvas.ShowXueliuExchangePresentation(reconnect);
            canvas.gameObject.SetActive(false);
            Check(Root()==null && !canvas.IsXueliuExchangeAnimating,"关闭界面残留动画");
            Check(!UnityEngine.Object.FindAnyObjectByType<SbrExchangeSuitBadge>(), "退出后桌面残留了花色徽标");
            canvas.gameObject.SetActive(true);
            foreach (string sub in new[]{SichuanLobby.SbrExchange,"sichuan/xueliu_exchange",SichuanLobby.XueliuDoublesSubRule,SichuanLobby.XueliuDuplicateSubRule}) {
                var swapped = SichuanPresentationValidation.Fixture(sub, 0);
                swapped.action_tick = ++tick;
                swapped.sbr_exchange_direction = 2;
                swapped.players_info[0].sbr_received_tiles = swapped.players_info[0].hand_tiles.Take(3).ToArray();
                NormalGameStateManager.Instance.InitializeGame(true, "exchange receipt", swapped);
                var receipt = Root();
                if (SichuanLobby.IsXueliuDoubles(sub))
                    Check(receipt.Find("SbrExchangeDirection/Direction").GetComponent<TMP_Text>().text ==
                        "已收到队友的三张牌", "双打收牌结束时应显示收牌提示");
                SichuanGameState.Active.HandleMessage("broadcast_hand_action", new Response {
                    ask_hand_action_info = new AskHandActionGBInfo { action_tick = tick, player_index = 0,
                        xueliu_exchange = new XueliuExchangeInfo { animating = true, cards_face_down = true,
                            direction = 2, active_players = new[]{0,1,2,3}, animation_seconds = .1f } }
                });
                Check(receipt && Root() == receipt && !canvas.IsXueliuExchangeAnimating, "旧传牌报文覆盖了收牌快照");
                int Raised() => canvas.GetComponentsInChildren<HoverEventTrigger>().Count(card => card.SbrRaised);
                if (sub != SichuanLobby.SbrExchange) {
                    yield return new WaitForSecondsRealtime(.95f);
                    Check(Raised() == 3 && !Field<GameObject>(canvas, "dingqueSelectionPanel").activeSelf, "定缺询问到达前换入的三张牌必须保持抬起");
                }
                SichuanGameState.Active.HandleMessage("ask_dingque", new Response {
                    ask_hand_action_info = new AskHandActionGBInfo { action_tick = ++tick, player_index = 0, remaining_time = 10 }
                });
                yield return new WaitUntil(() => Field<GameObject>(canvas, "dingqueSelectionPanel").activeSelf);
                Check(Raised() == 3, "定缺提示亮起时三张换入牌应一同开始落下");
                yield return new WaitForSecondsRealtime(.25f);
                Check(Raised() == 0 && Field<GameObject>(canvas, "dingqueSelectionPanel").activeSelf, "三张牌收拢后定缺输入仍应可用");
                canvas.HideDingqueSelection();
            }
            yield return NewChineseExchangeValidation.Run(Check);
            completed($"Xueliu exchange: {checks} runtime checks passed");
        } finally {
            SbrExchangeSuitBadge.PreviewStyle = originalStyle;
            canvas.ClearXueliuExchangePresentation();
            canvas.HideXueliuThrowThreeSelection();
        }
    }
}
#endif
