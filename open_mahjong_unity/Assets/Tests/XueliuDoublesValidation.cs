#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

public static class XueliuDoublesValidation {
    public static string Run() {
        int checks = 0;
        void Check(bool condition, string label) {
            if (!condition) throw new InvalidOperationException(label);
            checks++;
        }
        const string sub = "sichuan/xueliu_doubles";
        Check(SichuanLobby.IsTeamMode(sub) && SichuanLobby.IsXueliu(sub) && SichuanLobby.IsExchange(sub), "双打血流路由");
        Check(!SichuanLobby.IsDoubles(sub), "不应用 SBR 提前结束");
        Check(SichuanLobby.SubRules[3].Key == "sichuan/doubles" && SichuanLobby.SubRules[4].Key == sub, "已有子规则索引稳定");
        Check(ScoreHistorySettlementHelper.ResolveSichuanEndgameRoundLabel(2, false, sub) == SichuanRoundLabel.Liuju, "两人胡后仍到流局");
        var hand = new List<int> {11,12,13,14,15,16,21,22,23,26,27,28,25};
        Check(SichuanTips.Tingpai(new TingpaiQuery { Hand=hand, SubRule=sub }).Contains(25), "十三张血流听牌");
        var hint = SichuanTips.Describe(new WaitHintQuery { HandWithWin=new List<int>(hand) {25}, HepaiTile=25, SubRule=sub, ExcludedSuit=3 });
        Check(hint.Kind==WaitTileHint.KindRon, "换三张番表可胡");
        Check(SichuanTips.Describe(new WaitHintQuery { HandWithWin=new List<int>(hand) {25}, HepaiTile=25, SubRule=sub, ExcludedSuit=1 }).Kind==WaitTileHint.KindNone, "控猪不等于带缺胡牌");
        Check(SichuanFanText.FanTotal(sub,new[] {"基本胡","门清","根"})==6,"番型与根相乘");
        var room = JsonConvert.DeserializeObject<RoomInfo>("{\"room_rule\":\"sichuan\",\"sub_rule\":\"sichuan/xueliu_doubles\",\"seat_list\":[101,102,0,0],\"team_by_seat\":[0,1,0,1]}");
        Check(RoomPanel.DoublesTeamsComplete(room), "重复机器人按座位分队");
        var fields = RoomConfigContainer.BuildDisplayFields(room);
        Check(!fields.Any(f=>new[] {"赛制","换三张","定缺","有效分","盘胜负"}.Contains(f.Key)), "血流房内无额外规则说明");
        room.sub_rule="sichuan/doubles";
        Check(!RoomConfigContainer.BuildDisplayFields(room).Any(f=>new[] {"赛制","定缺 / 顺和","和牌条件","提前结束","盘胜负"}.Contains(f.Key)), "血战房内无额外规则说明");
        var ask = JsonConvert.DeserializeObject<AskHandActionGBInfo>("{\"action_list\":[\"xueliu_exchange_three\"],\"action_tick\":8,\"xueliu_exchange\":{\"stage\":2,\"active_players\":[2,3],\"teammate_suit\":1,\"selection_done\":false}}");
        Check(ask.xueliu_exchange.stage==2 && ask.xueliu_exchange.teammate_suit==1 && ask.action_tick==8,"两阶段询问协议");
        return $"Xueliu doubles: {checks} checks passed";
    }

    public static string ValidateExchangePanel() {
        if (!Application.isPlaying) throw new InvalidOperationException("Play Mode required");
        var canvas = GameCanvas.Instance;
        var flags = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var info = new AskHandActionGBInfo { remaining_time=10, action_tick=1, action_list=new[] {"xueliu_exchange_three"},
            xueliu_exchange=new XueliuExchangeInfo { stage=2, active_players=new[] {2,3}, teammate_suit=1 } };
        canvas.ShowXueliuTeamExchange(info);
        var tip=(TMPro.TMP_Text)typeof(GameCanvas).GetField("dingqueTipText",flags).GetValue(canvas);
        if (!tip.text.Contains("队友换出万牌")) throw new Exception("后手未看到队友花色");
        info.action_list=Array.Empty<string>();
        info.xueliu_exchange.selection_done=true;
        canvas.ShowXueliuTeamExchange(info);
        if (!tip.text.Contains("已提交") || !tip.text.Contains("西、北")) throw new Exception("已提交重连恢复错误");
        var button=(UnityEngine.UI.Button)typeof(GameCanvas).GetField("dingqueWanButton",flags).GetValue(canvas);
        if (button.gameObject.activeSelf) throw new Exception("等待阶段可以重复提交");
        canvas.HideXueliuThrowThreeSelection();
        return "Exchange panel: teammate suit, active seats, committed reconnect and disabled input verified";
    }

    public static string ValidateTeamBadges() {
        if (!Application.isPlaying) throw new InvalidOperationException("Play Mode required");
        var flags = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var panels = UnityEngine.Resources.FindObjectsOfTypeAll<GamePlayerPanel>()
            .Where(p=>p.gameObject.scene.IsValid() && p.gameObject.activeInHierarchy).ToArray();
        if (panels.Length != 4) throw new Exception("Four live player panels required");
        foreach (var panel in panels) {
            int team=panel.SichuanTeamId;
            var background=panel.transform.Find("Username/BackGround").GetComponent<UnityEngine.UI.Image>();
            var dingque=(UnityEngine.UI.Image)typeof(GamePlayerPanel).GetField("playerDingqueImage",flags).GetValue(panel);
            bool wasActive=dingque.gameObject.activeSelf;
            Color dingqueColor=dingque.color;
            Vector3 position=dingque.transform.position;
            int childCount=panel.transform.childCount;
            panel.SetSichuanTeam(-1);
            Color original=background.color;
            panel.SetSichuanTeam(0,true);
            Color blue=background.color;
            panel.SetSichuanTeam(1,false);
            if (background.color==blue || background.color==original) throw new Exception("Team background color missing: "+panel.name);
            if (panel.transform.childCount!=childCount) throw new Exception("Team color added a separate badge");
            if (dingque.gameObject.activeSelf!=wasActive || dingque.color!=dingqueColor || dingque.transform.position!=position)
                throw new Exception("Team background changed dingque: "+panel.name);
            var corners=new Vector3[4];
            background.rectTransform.GetWorldCorners(corners);
            foreach (var corner in corners) {
                var point=RectTransformUtility.WorldToScreenPoint(null,corner);
                if (point.x < 0 || point.x > Screen.width || point.y < 0 || point.y > Screen.height)
                    throw new Exception("Team background outside screen: "+panel.name);
            }
            panel.SetSichuanTeam(-1);
            if (background.color!=original) throw new Exception("Team background not restored outside doubles");
            panel.SetSichuanTeam(team,true);
        }
        return "Four ID backgrounds show team colors, restore on exit, and leave dingque layout unchanged";
    }
}
#endif
