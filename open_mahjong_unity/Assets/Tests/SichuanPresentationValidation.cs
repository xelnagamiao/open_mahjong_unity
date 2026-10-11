#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>在空闲的 MainScene Play Mode 中运行；纯本地协议夹具，不创建网络房间。</summary>
public static class SichuanPresentationValidation {
    private static readonly int[] Hand = {11,12,13,14,15,16,21,22,23,26,27,28,25};
    private static readonly Dictionary<int,int> Suits = new Dictionary<int,int>{{0,1},{1,2},{2,3},{3,1}};

    public static GameInfo Fixture(string sub, int self = 3, int round = 1) {
        var players = new PlayerInfo[4];
        for (int i=0; i<4; i++) players[i] = new PlayerInfo {
            player_index=i, original_player_index=i, user_id=i==self ? UserDataManager.Instance.UserId : 2,
            username=i==self ? "定缺与手牌验证" : "验证机器人", hand_tiles_count=13,
            hand_tiles=i==self ? (int[])Hand.Clone() : null, discard_tiles=Array.Empty<int>(),
            discard_origin_tiles=Array.Empty<int>(), combination_tiles=Array.Empty<string>(),
            combination_mask=Array.Empty<int[]>(), huapai_list=Array.Empty<int>(),
            score_history=Array.Empty<string>(), round_number_history=Array.Empty<int>(), tag_list=Array.Empty<string>(),
            title_used=1, character_used=1, profile_used=1, voice_used=1, team_id=i%2,
            has_won=false, post_hu_lock=false, win_count=0, is_hu=false
        };
        return new GameInfo {
            gamestate_id="local-sichuan-presentation-validation", room_id=0, room_type="custom",
            room_rule="sichuan", sub_rule=sub, current_round=round, max_round=1, tile_count=55,
            current_player_index=0, dealer_index=0, players_info=players, hepai_limit=0,
            player_entry_order=players.Select(p=>p.user_id).ToArray(),
            sichuan_teams=SichuanLobby.IsTeamMode(sub) ? new SichuanTeamsInfo {
                team_by_player_index=new[] {0,1,0,1}, settled_scores=new Dictionary<int,int>{{0,0},{1,0}},
                round_history=new List<Dictionary<string,object>>()
            } : null
        };
    }

    private static IEnumerator IdleHand() {
        float deadline=Time.realtimeSinceStartup+5;
        do {
            if (Time.realtimeSinceStartup>deadline) throw new Exception("手牌队列未能结束");
            yield return null;
        } while (GameCanvas.Instance.IsChangeHandCardProcessing || GameCanvas.Instance.IsHandReflowAnimating);
    }

    private static ShowResultInfo Win(int winner, bool selfDraw) => new ShowResultInfo {
        hepai_player_index=winner, hu_class=selfDraw ? "hu_self" : "hu_first", hepai_tile=25,
        hepai_player_hand=Hand.Concat(new[]{25}).ToArray(), hepai_player_combination_mask=Array.Empty<int[]>(),
        hepai_player_huapai=Array.Empty<int>(), hu_fan=new[]{"基本胡"},
        defer_score_settlement=true, round_continues=true, next_status="round_continue", action_tick=10,
        player_to_score=new Dictionary<int,int>{{0,0},{1,0},{2,0},{3,0}}
    };

    private static TileCard[] Cards() => GameCanvas.Instance.GetMainHandCardsOrdered().ToArray();

    public static IEnumerator Run(Action<string> completed) {
        if (!Application.isPlaying || !string.IsNullOrEmpty(UserDataManager.Instance.GamestateId))
            throw new InvalidOperationException("需要无在线对局的 Play Mode");
        int checks=0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        GameCanvas canvas=null;
        bool originalArrange=true;
        try {
            foreach (string sub in new[]{"sichuan/standard","sichuan/doubles","sichuan/xueliu","sichuan/xueliu_exchange","sichuan/xueliu_doubles"}) {
                GameInfo fixture=Fixture(sub);
                NormalGameStateManager.Instance.InitializeGame(true,"local validation",fixture);
                yield return IdleHand();
                if (canvas==null) {
                    canvas=GameCanvas.Instance;
                    originalArrange=AutoAction.Instance.IsAutoArrangeHandCards;
                }
                AutoAction.Instance.SetAutoArrangeHandCards(false);
                var before=Cards().Reverse().ToArray();
                for (int i=0;i<before.Length;i++) {before[i].handSortIndex=i;before[i].transform.SetSiblingIndex(i);}
                canvas.LayoutHandCardsFromCurrentOrder();
                canvas.SetHandArranged(true);
                var positions=before.Select(c=>((RectTransform)c.transform).anchoredPosition).ToArray();
                SichuanGameState.Active.HandleMessage("show_result",new Response{show_result_info=Win(0,true)});
                if (SichuanLobby.IsXueliu(sub)) {
                    // 连续胡后的重复快照，服务器顺序与玩家手排顺序不同。
                    SichuanGameState.Active.HandleMessage("xueliu_continue",new Response{game_info=fixture});
                    SichuanGameState.Active.HandleMessage("xueliu_continue",new Response{game_info=fixture});
                } else {
                    yield return new WaitForSecondsRealtime(2);
                    SichuanGameState.Active.TryResumeAfterContinue();
                    TurnClock.Current.LastAskHandPlayerIndex=3;
                    TurnClock.Current.BeginHandAsk("top",0,1);
                }
                yield return IdleHand();
                Check(Cards().SequenceEqual(before),sub+" 他家胡后手牌对象/顺序改变");
                Check(Cards().Select(c=>((RectTransform)c.transform).anchoredPosition).SequenceEqual(positions),sub+" 他家胡后强制理牌");

                if (SichuanLobby.IsXueliu(sub)) {
                    var drag=HandCardDragController.Instance;
                    var pointer=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) {
                        button=UnityEngine.EventSystems.PointerEventData.InputButton.Left,
                        position=RectTransformUtility.WorldToScreenPoint(null,before[0].transform.position)
                    };
                    drag.OnPointerDown(before[0],pointer);
                    var press=typeof(HandCardDragController).GetField("pendingPress",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                    try {
                        Check((bool)press.GetValue(drag),sub+" 未开始真实手牌按压");
                        Check(!canvas.ChangeHandCardsCoroutine("SyncHandCardsPreservingOrder",0,Hand,null).MoveNext(),sub+" 不变快照启动理牌协程");
                        Check((bool)press.GetValue(drag),sub+" 不变快照中止按压/拖拽");
                    } finally { drag.AbortForHandChange("validation cleanup"); }
                }

                // 已知同牌自摸，主列的同牌不能误删；队列之后紧接服务端续打快照和下次摸牌。
                TableMirror.Current.SelfHandTiles.Add(25);
                canvas.ChangeHandCards("GetCard",25,null,null);
                yield return IdleHand();
                TileCard drawn=canvas.GetPinnedDrawTile();
                Check(drawn!=null && drawn.tileId==25,sub+" 摸牌张未独立显示");
                if (SichuanLobby.IsXueliu(sub)) {
                    SichuanGameState.Active.HandleMessage("xueliu_continue",new Response{game_info=WithHand(fixture,Hand.Concat(new[]{25}).ToArray())});
                    yield return IdleHand();
                    Check(canvas.GetPinnedDrawTile()==drawn,sub+" 不变快照改变摸牌区");
                }
                var win=Win(3,true); win.action_tick=20;
                SichuanGameState.Active.HandleMessage("show_result",new Response{show_result_info=win});
                yield return new WaitForSecondsRealtime(.8f);
                if (SichuanLobby.IsXueliu(sub))
                    SichuanGameState.Active.HandleMessage("xueliu_continue",new Response{game_info=WithHand(fixture,Hand)});
                yield return IdleHand();
                Check(Cards().SequenceEqual(before),sub+" 自摸误删主列同牌或重排");
                Check(canvas.GetPinnedDrawTile()==null,sub+" 和牌张残留");
                Check(TableMirror.Current.SelfHandTiles.OrderBy(x=>x).SequenceEqual(Hand.OrderBy(x=>x)),sub+" 和牌后模型牌数错误");
                if (SichuanLobby.IsXueliu(sub)) {
                    canvas.ChangeHandCards("GetCard",29,null,null);
                    yield return IdleHand();
                    Check(Cards().SequenceEqual(before) && canvas.GetPinnedDrawTile()?.tileId==29,sub+" 续打后摸牌丢失");
                    canvas.ChangeHandCards("SyncHandCardsPreservingOrder",0,Hand.Take(12).Concat(new[]{35}).ToArray(),null);
                    yield return IdleHand();
                    Check(Cards().Select(c=>c.tileId).OrderBy(x=>x).SequenceEqual(Hand.Take(12).Concat(new[]{35}).OrderBy(x=>x)),sub+" 差额同步牌数错误");
                    Check(Cards().Take(12).SequenceEqual(before.Where(c=>c!=null && c.tileId!=25)),sub+" 差额同步破坏已有顺序");
                }
            }

            for (int round=1;round<=4;round++) {
                GameInfo fixture=Fixture("sichuan/xueliu_doubles",round-1,round);
                NormalGameStateManager.Instance.InitializeGame(true,"local validation",fixture);
                yield return IdleHand();
                Check(!canvas.IsDingqueRevealPlaying,"开局未确认时播放定缺");
                var response=new Response{show_result_info=new ShowResultInfo{player_to_dingque=SuitedCopy(),action_tick=3}};
                SichuanGameState.Active.HandleMessage("dingque_done",response);
                var root=UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None)
                    .FirstOrDefault(r=>r.name=="DingqueReveal");
                Check(root!=null,"定缺动画根缺失");
                Check(root.childCount==4,"血流双打定缺必须同时展示四家");
                for (int seat=0;seat<4;seat++) Check(root.Find("DingqueCard_"+seat)!=null,"定缺卡缺失");
                Check(root.GetComponentsInChildren<Graphic>().All(g=>!g.raycastTarget),"动画卡片挡住输入");
                SichuanGameState.Active.HandleMessage("dingque_done",response);
                Check(root!=null && root.childCount==4,"重复定缺消息重启动画");
                yield return new WaitForSecondsRealtime(1.15f);
                Check(!canvas.IsDingqueRevealPlaying,"双打定缺动画未清理");
                foreach(var p in LivePanels()) Check(p.DingqueAnchor.gameObject.activeSelf,"定缺卡未落到头像");
                Check(!string.IsNullOrEmpty(XueliuDoublesValidation.ValidateTeamBadges()),"队色底板验证失败");
            }
            GameInfo regular=Fixture("sichuan/standard");
            NormalGameStateManager.Instance.InitializeGame(true,"local validation",regular);
            yield return IdleHand();
            SichuanGameState.Active.HandleMessage("dingque_done",new Response{show_result_info=new ShowResultInfo{player_to_dingque=SuitedCopy()}});
            Check(UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None).Count(r=>r.name.StartsWith("DingqueCard_"))==4,"普通血战未同时展示四张卡");
            yield return new WaitForSecondsRealtime(1.1f);
            Check(!canvas.IsDingqueRevealPlaying,"普通定缺未结束");
            foreach(var p in LivePanels()) Check(p.SichuanTeamId==-1,"切回普通规则仍留队色");
            // 动画中重连：旧卡立刻清除，头像从已确认快照恢复，不重放。
            canvas.PlayDingqueReveal(SuitedCopy());
            foreach(var p in regular.players_info) p.dingque_suit=SuitedCopy()[p.player_index];
            NormalGameStateManager.Instance.InitializeGame(true,"local reconnect",regular);
            yield return IdleHand();
            Check(!canvas.IsDingqueRevealPlaying,"重连留下旧定缺动画");
            foreach(var p in LivePanels()) Check(p.DingqueAnchor.gameObject.activeSelf,"重连未恢复定缺");
            canvas.PlayDingqueReveal(SuitedCopy());
            canvas.gameObject.SetActive(false);
            Check(!canvas.IsDingqueRevealPlaying,"隐藏界面留下定缺卡片");
            canvas.gameObject.SetActive(true);
            completed($"Sichuan presentation: {checks} runtime checks passed");
        } finally {
            canvas?.CancelDingqueReveal();
            AutoAction.Instance?.SetAutoArrangeHandCards(originalArrange);
        }
    }

    private static GameInfo WithHand(GameInfo fixture,int[] hand) {
        GameInfo copy=Newtonsoft.Json.JsonConvert.DeserializeObject<GameInfo>(Newtonsoft.Json.JsonConvert.SerializeObject(fixture));
        var self=copy.players_info.First(p=>p.hand_tiles!=null);
        self.hand_tiles=(int[])hand.Clone(); self.hand_tiles_count=hand.Length;
        return copy;
    }
    private static Dictionary<int,int> SuitedCopy() => new Dictionary<int,int>(Suits);
    private static GamePlayerPanel[] LivePanels() => UnityEngine.Object.FindObjectsByType<GamePlayerPanel>(FindObjectsSortMode.None);
}
#endif
