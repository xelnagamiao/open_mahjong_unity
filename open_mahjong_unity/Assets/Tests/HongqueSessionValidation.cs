#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

/// <summary>虹雀观战、重连和和牌展示契约；不切换 Play Mode 或修改活动场景。</summary>
public static class HongqueSessionValidation {
    public static string Run() {
        var checks = new List<string>();
        void Check(bool value, string label) {
            if (!value) throw new InvalidOperationException("Hongque session: " + label);
            checks.Add(label);
        }
        const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        var registry = (Dictionary<string, RuleManifest>)typeof(RuleRegistry)
            .GetField("ManifestsByRule", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        registry.TryGetValue("hongque", out var previous);
        bool wasSpectating = GameSession.Current.IsRealtimeSpectator;
        try {
            typeof(HongqueRuleBootstrap).GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            var manifest = RuleRegistry.Resolve("hongque", "hongque/v1.6");
            Check((bool)manifest.CreateRoomDefaults[CreateRoomKeys.AllowSpectator], "create-room spectator switch is available and enabled by default");
            var hand = new[] { 1005, 1125, 1135 };
            var ron = HepaiRevealDirector.BuildRequestCore("self", "hu", hand, null, "hongque", "right");
            Check(ron.WinTileMode == HepaiWinTilePresentMode.GuobiaoRonTravelFromRiver, "ron uses the river tile instead of spawning another winning tile");
            var tsumo = HepaiRevealDirector.BuildRequestCore("self", "hu_self", hand, null, "hongque", "right");
            Check(tsumo.WinTileMode == HepaiWinTilePresentMode.TsumoTravel, "self draw retains its draw-slot presentation");
            Check(ron.HepaiTile == 1135 && hand.SequenceEqual(new[]{1005,1125,1135}), "presentation preserves complete scoring hand and winning tile identity");
            Check(EndResultPanel.SortSettlementHand(hand).SequenceEqual(new[]{1005,1135,1125}), "winning panel still displays wrap groups continuously");
            Check(MatchQueueDisplayText.GetQueueTitle("hongque_elo_banzhuang").Contains("8局"), "eight-hand matchmaking title");
            Check(MatchQueueDisplayText.GetQueueTitle("hongque_elo_quanzhuang").Contains("16局"), "sixteen-hand matchmaking title");
            for (int seat=0; seat<4; seat++) {
                var game = new HongqueGameState();
                var state = new HongqueStateInfo {
                    you=seat, phase="turn", current_player=seat, hand=new[]{"AX5","GX5","GY5"},
                    players=Enumerable.Range(0,4).Select(i=>new HongquePlayerInfo {
                        index=i,user_id=101+i,username="P"+i,hand_count=3
                    }).ToArray()
                };
                typeof(HongqueGameState).GetField("state",hidden).SetValue(game,state);
                GameSession.Current.IsRealtimeSpectator = true;
                var draw = new HongqueEventInfo {type="draw",player=seat,tile="GY5"};
                var info=(GameInfo)typeof(HongqueGameState).GetMethod("BuildGameInfo",hidden).Invoke(game,new object[]{draw});
                Check(info.view_player_index==seat && info.self_hand_tiles.SequenceEqual(new[]{1005,1125}), "spectator seat and pending draw are restored once: "+seat);
                Check(info.players_info.Where(p=>p.player_index!=seat).All(p=>p.hand_tiles.Length==0), "opponent hand identities stay hidden: "+seat);
                Check(game.TryCutTile(1005) && game.TryChooseAction(HongqueActionWords.Win), "spectator actions are consumed without sending: "+seat);
                state.phase="round_end";
                Check(game.TryConfirmRoundResult(), "spectator cannot send ready via fallback channel: "+seat);
            }
            return JsonConvert.SerializeObject(new {passed=checks.Count,checks}, Formatting.Indented);
        } finally {
            GameSession.Current.IsRealtimeSpectator=wasSpectating;
            if (previous==null) registry.Remove("hongque"); else registry["hongque"]=previous;
        }
    }
}
#endif
