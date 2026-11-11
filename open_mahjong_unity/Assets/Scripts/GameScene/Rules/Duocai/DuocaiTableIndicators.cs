using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class DuocaiTableIndicators {
    public static DuocaiInfo DisplayedInfo {get;private set;}
    public static PlayerInfo[] Players {get;private set;}
    private static int handVersion;
    public static string SeatOf(int index) {
        var record=GameRecordManager.Instance;
        return record!=null&&record.gameObject.activeSelf&&record.gameRecord!=null
            ? record.indexToPosition.TryGetValue(index,out var seat)?seat:null : TableMirror.Current.SeatOf(index);
    }
    public static RoundIndicatorDisplay FieldState(DuocaiInfo info)=>info==null?new RoundIndicatorDisplay():new RoundIndicatorDisplay {
        Tiles=info.indicators??Array.Empty<int>(),Caption=$"第{info.hand_number}副 · {(info.deck==0?"红":"蓝")}牌组"+(info.variant=="riichi"?"":"\n待选赖子"),CaptionAboveTiles=info.variant=="riichi",
        Counter=info.variant=="riichi"?$"{info.honba}本场 · {info.riichi_sticks}供托":ReserveCaption(info)
    };
    private static string ReserveCaption(DuocaiInfo info)=>info.reserves?.Length==2
        ?$"红背{info.reserves[1]} · 蓝背{info.reserves[0]}":"";
    public static int[] CaptureHandBackState(string seat)=>Players?.FirstOrDefault(p=>SeatOf(p.player_index)==seat)?.duocai_hand_backs?.ToArray();
    public static Action CaptureHandBacks(string seat) {
        var info=DisplayedInfo;
        var player=Players?.FirstOrDefault(p=>SeatOf(p.player_index)==seat);
        if(info==null||player?.duocai_hand_backs==null)return null;
        int deck=info.deck,hand=info.hand_number;
        int version=handVersion;
        var backs=(int[])player.duocai_hand_backs.Clone();
        return ()=> {
            if(version==handVersion&&DisplayedInfo?.deck==deck&&DisplayedInfo.hand_number==hand)
                Game3DManager.Instance?.ApplyDuocaiHandBacks(seat,deck,backs);
        };
    }
    public static void Refresh(DuocaiInfo info,PlayerInfo[] players=null) {
        if(info==null||DisplayedInfo==null||info.deck!=DisplayedInfo.deck||info.hand_number!=DisplayedInfo.hand_number)handVersion++;
        DisplayedInfo=info;if(players!=null||info==null)Players=players;
        RoundPanel.Instance?.RefreshRuleIndicators(FieldState(info));
        var manager=Game3DManager.Instance;if(manager==null)return;
        var host=manager.GetComponent<DuocaiTableOverlay>()??manager.gameObject.AddComponent<DuocaiTableOverlay>();
        host.Dirty=true;
    }
}

public sealed class DuocaiTableOverlay : MonoBehaviour {
    public bool Dirty;
    private bool backsPending;
    private void LateUpdate() {
        var manager=Game3DManager.Instance;if(manager==null)return;
        if(Dirty) {
            manager.RefreshDuocaiWall(DuocaiTableIndicators.DisplayedInfo);
            if(GameCanvas.Instance?.HandCardsContainer!=null)
                foreach(var card in GameCanvas.Instance.HandCardsContainer.GetComponentsInChildren<TileCard>(false))card.RefreshRuleBack();
        }
        if(Dirty||backsPending)backsPending=manager.RefreshDuocaiBacks(DuocaiTableIndicators.DisplayedInfo,DuocaiTableIndicators.Players);
        Dirty=false;
    }
    private void OnDestroy(){if(Game3DManager.Instance!=null)Game3DManager.Instance.RefreshDuocaiWall(null);}
}

public partial class Game3DManager {
    private readonly Dictionary<int,GameObject> duocaiWall=new Dictionary<int,GameObject>();
    private readonly Dictionary<int,(int face,int back,string seat,int stack,int layer)> duocaiWallViews=new Dictionary<int,(int,int,string,int,int)>();
    private Transform duocaiWallRoot;
    internal void RefreshDuocaiWall(DuocaiInfo info) {
        if(MahjongObjectPool.Instance==null)return;
        var active=new HashSet<int>(info?.wall?.Where(v=>!v.removed).Select(v=>v.position)??Enumerable.Empty<int>());
        foreach(int position in duocaiWall.Keys.Where(k=>!active.Contains(k)).ToArray()) {
            if(duocaiWall[position]!=null) {
                SetWallTileScale(duocaiWall[position].transform,1f);
                MahjongObjectPool.Instance.Return(-1,duocaiWall[position]);
            }
            duocaiWall.Remove(position);
            duocaiWallViews.Remove(position);
        }
        if(info?.wall==null)return;
        if(duocaiWallRoot==null){duocaiWallRoot=new GameObject("DuocaiWall").transform;duocaiWallRoot.SetParent(transform,false);}
        foreach(var tile in info.wall.Where(v=>!v.removed)) {
            string seat=DuocaiTableIndicators.SeatOf(tile.side);var panel=GetPosPanel(seat);if(panel?.cardsPosition==null||panel.discardsPosition==null)continue;
            bool created=!duocaiWall.TryGetValue(tile.position,out var obj)||obj==null;
            bool known=duocaiWallViews.TryGetValue(tile.position,out var previous);
            int face=tile.tile??0;
            bool moved=created||!known||previous.seat!=seat||previous.stack!=tile.stack||previous.layer!=tile.layer;
            if(created) {
                var rotation=RiverTileWorldRotation(seat);
                Vector3 position=WallSlotPosition(seat,tile.stack-8.5f);
                obj=MahjongObjectPool.Instance.SpawnPresentationTile(tile.tile??0,position,rotation);
                if(obj==null)continue;obj.name="DuocaiWall_"+tile.position;obj.transform.SetParent(duocaiWallRoot,true);duocaiWall[tile.position]=obj;
            } else if(!known||previous.face!=face)MahjongObjectPool.Instance.SetPresentationFace(obj,face);
            if(moved) {
                var rotation=RiverTileWorldRotation(seat);
                SetWallTileScale(obj.transform,TableWallScale);
                Vector3 position=KeepMovingTileAboveTable(obj.transform,WallSlotPosition(seat,tile.stack-8.5f),rotation);
                obj.transform.SetPositionAndRotation(position,rotation);
                var renderer=obj.GetComponentInChildren<Renderer>();
                if(tile.layer==0&&renderer!=null)obj.transform.position+=Vector3.up*renderer.bounds.size.y;
            }
            var state=MahjongObjectPool.GetTile3D(obj);
            if(created||!known||previous.face!=face)state.SetConcealedFaceDown(!tile.tile.HasValue);
            if(created||!known||previous.back!=tile.back)state.SetRuleBackColor(DuocaiTileVisual.BackColor(tile.back));
            if(created) {
                state.SetHoverPeekAllowed(false);
                foreach(var collider in obj.GetComponentsInChildren<Collider>())collider.enabled=false;
            }
            duocaiWallViews[tile.position]=(face,tile.back,seat,tile.stack,tile.layer);
        }
    }
    internal void ApplyDuocaiHandBacks(string seat,int deck,int[] backs) {
        var panel=GetPosPanel(seat);if(panel==null)return;
        var cards=panel.cardsPosition?.GetComponentsInChildren<Tile3D>(false)??Array.Empty<Tile3D>();
        bool mapped=backs?.Length==cards.Length;
        var remaining=new int[2];
        if(mapped)foreach(int back in backs)if(back>=0&&back<2)remaining[back]++;
        for(int i=0;i<cards.Length;i++) {
            int id=cards[i].GetTileId();
            if(id>=10)cards[i].SetRuleBackColor(DuocaiTileVisual.BackColor((deck+(DuocaiTiles.UnusualBack(id)?1:0))%2));
            if(mapped&&cards[i].RuleBackColor.HasValue)remaining[DuocaiBackOf(cards[i])]--;
        }
        if(mapped)for(int i=0;i<cards.Length;i++)if(!cards[i].RuleBackColor.HasValue) {
            int back=remaining[backs[i]]>0?backs[i]:remaining[0]>0?0:1;
            cards[i].SetRuleBackColor(DuocaiTileVisual.BackColor(back));remaining[back]--;
        }
        if(panel.ShowCardsPosition!=null)
            foreach(var tile in panel.ShowCardsPosition.GetComponentsInChildren<Tile3D>(false))
                tile.SetRuleBackColor(DuocaiTileVisual.BackColor((deck+(DuocaiTiles.UnusualBack(tile.GetTileId())?1:0))%2));
    }
    private static int DuocaiBackOf(Tile3D tile)=>tile.RuleBackColor.Value.r>tile.RuleBackColor.Value.b?0:1;

    private bool TryRemoveDuocaiHandCards(Transform cards,int count,bool drawn,int tileId,int[] mask,string seat,int[] postBacks) {
        if(!DuocaiTileVisual.TryBack(tileId,out int back)||cards.childCount<count)return false;
        var requested=new List<int>();
        if(count==1&&tileId>=10)requested.Add(back);
        else if(mask!=null)for(int i=0;i+1<mask.Length;i+=2)
            if(mask[i]!=1&&!MeldStackLayout.IsStack(mask[i])&&mask[i+1]>=10) {
                DuocaiTileVisual.TryBack(mask[i+1],out int color);requested.Add(color);
            }
        if(requested.Count!=count&&postBacks?.Length==cards.childCount-count) {
            var removed=new int[2];bool complete=true;
            for(int i=0;i<cards.childCount;i++) {
                var state=MahjongObjectPool.GetTile3D(cards.GetChild(i).gameObject);
                if(state==null||!state.RuleBackColor.HasValue){complete=false;break;}
                removed[DuocaiBackOf(state)]++;
            }
            foreach(int color in postBacks)removed[color]--;
            if(complete&&removed[0]>=0&&removed[1]>=0) {
                requested.Clear();
                for(int color=0;color<2;color++)for(int i=0;i<removed[color];i++)requested.Add(color);
            }
        }
        if(requested.Count!=count)return false;
        var selected=new List<Transform>();
        for(int i=0;i<count;i++) {
            Transform chosen=null;
            if(drawn&&count==1)chosen=cards.GetChild(cards.childCount-1);
            else for(int j=0;j<cards.childCount;j++) {
                var child=cards.GetChild(j);var state=MahjongObjectPool.GetTile3D(child.gameObject);
                if(selected.Contains(child)||state==null||count==1&&state.isRecordDrawSlotPinned)continue;
                if(state.RuleBackColor.HasValue&&DuocaiBackOf(state)==requested[i]){chosen=child;break;}
            }
            if(chosen==null)for(int j=0;j<cards.childCount;j++) {
                var child=cards.GetChild(j);var state=MahjongObjectPool.GetTile3D(child.gameObject);
                if(!selected.Contains(child)&&state!=null&&!state.RuleBackColor.HasValue){chosen=child;break;}
            }
            if(chosen==null)return false;
            selected.Add(chosen);
        }
        foreach(var child in selected) {
            SetLastRemovePos(seat,child.position);
            MahjongObjectPool.Instance.Return(-1,child.gameObject);
        }
        return true;
    }
    internal bool RefreshDuocaiBacks(DuocaiInfo info,PlayerInfo[] players) {
        if(info==null||players==null)return false;
        bool pending=false;
        foreach(var player in players) {
            string seat=DuocaiTableIndicators.SeatOf(player.player_index);var panel=GetPosPanel(seat);if(panel==null)continue;
            if(HasPendingRecordStepAnimations(seat))pending=true;
            else ApplyDuocaiHandBacks(seat,info.deck,player.duocai_hand_backs);
            if(panel.combination3DObjects!=null)for(int i=0;i<panel.combination3DObjects.Length;i++) {
                var group=panel.combination3DObjects[i];if(group==null)continue;
                var tiles=group.GetComponentsInChildren<Tile3D>(false);
                for(int j=0;j<tiles.Length;j++) {
                    int id=tiles[j].GetTileId();
                    if(id>=10)tiles[j].SetRuleBackColor(DuocaiTileVisual.BackColor((info.deck+(DuocaiTiles.UnusualBack(id)?1:0))%2));
                    else if(player.duocai_meld_backs?.Length>i&&player.duocai_meld_backs[i].Length==tiles.Length)
                        tiles[j].SetRuleBackColor(DuocaiTileVisual.BackColor(player.duocai_meld_backs[i][tiles.Length-1-j]));
                }
            }
            var river=panel.discardsPosition?.GetComponentsInChildren<Tile3D>(false)??Array.Empty<Tile3D>();
            foreach(var tile in river)tile.SetRuleBackColor(DuocaiTileVisual.BackColor((info.deck+(DuocaiTiles.UnusualBack(tile.GetTileId())?1:0))%2));
        }
        return pending;
    }
}
