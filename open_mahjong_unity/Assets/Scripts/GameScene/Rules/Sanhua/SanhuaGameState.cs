using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class SanhuaGameState : TurnBasedGameState {
    public const string SubRule="new_chinese/sanhua";
    internal const string Description="散花天才麻将是一群热血少年的原创四人麻将变体，使用136 张无花标准麻将牌，融合皮子‑癞子‑红中特殊杠体系、配对差异化算分、报清机制、多重牌型，是一套规则密度很高、策略博弈极强的自定义麻将玩法。";
    internal static SanhuaGameState Active => RuleRegistry.ActiveGameState as SanhuaGameState;
    internal SanhuaInfo Info {get; private set;}
    internal static SanhuaInfo Context(bool replay) => replay ? GameRecordManager.Instance?.SanhuaRecordInfo : Active?.Info;

    public override bool TryCutTile(int tileId) {
        if (!IsSingleKongTile(tileId)) return false;
        // A tile can still have its old selectable flag while the action window closes.
        if (IsActive && !Session.IsRealtimeSpectator && GameRecordManager.Instance?.IsSpectating != true
            && CanCutTile(tileId))
            GameCanvas.Instance.ChooseAction("angang", tileId);
        return true;
    }

    public override bool CanCutTile(int tileId) {
        if (IsSingleKongTile(tileId))
            return Clock.AllowActionList.Contains("angang") && Clock.IsKongCandidate("angang", tileId);
        return base.CanCutTile(tileId);
    }

    private bool IsSingleKongTile(int tileId) =>
        tileId == 45 || Info?.skin == tileId || Info?.joker == tileId;
    internal static readonly Dictionary<string,double> Fans = new Dictionary<string,double> {
        {"小屁和",0},{"杠上开花",1},{"硬和",1},{"七对",3},{"豪华七对",4},{"双豪华七对",5},{"三豪华七对",6},
        {"碰碰和",3},{"清一色",3},{"将一色",3},{"全求人",3},{"十三幺",10},{"明杠",1},{"暗杠",2},{"补杠",1},
        {"红中杠",1},{"癞子杠",2},{"皮子杠",1},{"开口番",0.5}
    };
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void Register() {
        RuleNameDictionary.WholeName[SubRule]="散花天才麻将";
        RuleNameDictionary.ShortName[SubRule]="散花天才";
        ActionWords.Register(new ActionWordSpec {Word="baoqing",Label=_=>"报清",BlocksAutoCut=true});
        RuleRegistry.RegisterSubRule(SubRule,new RuleManifest {
            RuleId=NewChineseGameState.RuleId,DefaultSubRule=SubRule,DisplayName="散花天才麻将",OutboundChannel=NewChineseGameState.RuleId,
            GameStateFactory=()=>new SanhuaGameState(),BuildRoom=NewChineseRoomBuilder.Build,
            ReplayFactory=r=>r.CreateSanhuaReplayModule(),RulebookKey="custom",
            RulebookPath=(s,c)=>"/rulebook/custom?sub_rule=new_chinese%2Fsanhua",
            OpeningHandHasDrawSlot=i=>i?.sanhua?.self_has_draw_slot==true,
            FirstTenTileDiscardRow = _ => 4,
            HasFlowerReplacement=false,DefaultHepaiLimit=4,FaceDownAnkan=true,RecordDangerUsesWaitHint=true,
            RonWinTileTravelsFromRiver=true,SupportsRobbedAddedKongSource=true,
            Tingpai=SanhuaHand.Waiting,DescribeWaitingTile=SanhuaHand.Describe,
            TileBadgeText=t=>Display?.joker==t?"癞":Display?.skin==t?"皮":null,
            HandJokerTiles=_=>Display==null?Array.Empty<int>():new[]{Display.joker},
            RecordHandJokerTiles=r=>r?.sanhua==null?Array.Empty<int>():new[]{r.sanhua.joker},
            LiveVisibleIndicators=()=>Array.Empty<int>(),
            RoundIndicators=i=>Indicators(i?.sanhua), MaxRoundInfoText=i=>$"{i.max_round}副",
            RoundSupplementText=i=>"4番起和",
            RoundName=n=>$"第{n}副",RoundStatusText=i=>$"第{i.current_round}副",MaxRoundText=n=>$"{n}副",
            FanValueText=(r,n)=>Fans.TryGetValue(n,out double f)?$"{f:0.#}番":"",
            ScoreboardFanText=q=>$"{q.HuScore}点",ScoreboardMainFanRank=(r,n)=>Fans.TryGetValue(n,out double f)?f:0,
            SettlementTotal=q=>new SettlementTotalDisplay {FanText=$"{q.WinnerPointDelta ?? q.HuScore}点"},
            ActionCaption=a=>a=="angang"||a=="buhua"?"杠":null,ActionVoice=a=>a=="buhua"?"gang":a=="hu_self"?"hu":null,
            StatsFanNames=Fans.Keys.ToDictionary(k=>k,k=>k)
        });
    }
    internal static RoundIndicatorDisplay Indicators(SanhuaInfo info) {
        if (info == null || info.indicator <= 0) return RoundIndicatorDisplay.Pending;
        var tiles = new List<RoundIndicatorTile> { new RoundIndicatorTile(info.indicator, "指") };
        if (info.indicator != info.skin) tiles.Add(new RoundIndicatorTile(info.skin, "皮"));
        tiles.Add(new RoundIndicatorTile(info.joker, "赖"));
        return new RoundIndicatorDisplay(tiles);
    }
    private static SanhuaInfo Display {
        get {
            var record = GameRecordManager.Instance;
            return record && record.ShouldShowRecordTips() ? record.SanhuaRecordInfo : Active?.Info;
        }
    }

    /// <summary>
    /// 返回某个供牌者因报清而承担的锁清方向。
    /// liable 按报清者记录供牌者，因此要反查，才能在供牌者的头像面板上显示。
    /// </summary>
    internal static string LockedClearText(SanhuaInfo info, int supplier) {
        if (info?.liable == null || info.declared == null || supplier < 0 || supplier >= 4) return null;
        bool LockedBy(int offset) {
            int seat = (supplier + offset) % 4;
            return seat < info.declared.Length && seat < info.liable.Length
                && info.declared[seat] && info.liable[seat] == supplier;
        }
        string directions = LockedBy(3) ? "上" : "";
        if (LockedBy(1)) directions += directions.Length > 0 ? "/下" : "下";
        if (LockedBy(2)) directions += directions.Length > 0 ? "/对" : "对";
        return directions.Length > 0 ? "被锁清：" + directions : null;
    }

    public override bool HandleMessage(string suffix, Response response) {
        int? previousIndicator = Info?.indicator;
        if(response.game_info?.sanhua!=null) Info=response.game_info.sanhua;
        bool handled=base.HandleMessage(suffix,response);
        if (Info?.indicator != previousIndicator)
            RoundPanel.Instance?.RefreshIndicators(SubRule, Indicators(Info));
        GameCanvas.Instance?.ShowSanhuaFans(Info,Mirror.IndexToPosition);
        return handled;
    }

    public override void OnPlayerTagsRefreshed() {
        base.OnPlayerTagsRefreshed();
        GameCanvas.Instance?.ShowSanhuaFans(Info, Mirror.IndexToPosition);
    }

    protected override void OnDoAction(Response r) {
        if(r.do_action_info?.action_list?.Contains("sanhua_rob")==true && r.game_info?.self_hand_tiles!=null) {
            Manager.InitializeGame(true,"",r.game_info);
            return;
        }
        base.OnDoAction(r);
    }
    internal List<(int,List<int>)> Kongs(List<int> hand) {
        var list=new List<(int,List<int>)>();
        foreach (var group in hand.GroupBy(tile => tile)) {
            int tile = group.Key;
            bool single = IsSingleKongTile(tile);
            if ((single || group.Count() == 4) && Clock.IsKongCandidate("angang", tile))
                list.Add((tile, Enumerable.Repeat(tile, single ? 1 : 4).ToList()));
        }
        return list;
    }
}

public partial class GameCanvas {
    internal void ShowSanhuaFans(SanhuaInfo info,IReadOnlyDictionary<int,string> positions) {
        if(positions==null) return;
        foreach(var seat in positions) {
            var panel=GetPlayerPanelByPosition(seat.Value);
            bool valid=seat.Key>=0 && seat.Key<(info?.table_fans?.Length ?? 0);
            panel?.SetPlayerVariable(GamePlayerPanel.VariableLangyong,valid?$"{info.table_fans[seat.Key]:0.#}番":null);
            bool declared = seat.Key >= 0 && seat.Key < (info?.declared?.Length ?? 0) && info.declared[seat.Key];
            panel?.SetPlayerVariable(GamePlayerPanel.VariableDeclaredReady,declared?"报清":null);
            panel?.SetPlayerVariable(GamePlayerPanel.VariableChengbao, SanhuaGameState.LockedClearText(info, seat.Key));
        }
    }
}
