using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class DuocaiTableIndicators {
    public static DuocaiInfo DisplayedInfo {get;private set;}
    public static PlayerInfo[] Players {get;private set;}
    public static string SeatOf(int index) {
        var record=GameRecordManager.Instance;
        return record!=null&&record.gameObject.activeSelf&&record.gameRecord!=null
            ? record.indexToPosition.TryGetValue(index,out var seat)?seat:null : TableMirror.Current.SeatOf(index);
    }
    public static RoundIndicatorDisplay FieldState(DuocaiInfo info)=>info==null?new RoundIndicatorDisplay():new RoundIndicatorDisplay {
        Tiles=info.indicators??Array.Empty<int>(),Caption=$"第{info.hand_number}副 · {(info.deck==0?"红":"蓝")}牌组",CaptionAboveTiles=info.variant=="riichi",
        Counter=info.variant=="riichi"?$"{info.honba}本场 · {info.riichi_sticks}供托":$"待选赖子 {string.Join(" / ",info.reserves??Array.Empty<int>())}"
    };
    public static void Refresh(DuocaiInfo info,PlayerInfo[] players=null) {
        DisplayedInfo=info;if(players!=null||info==null)Players=players;
        RoundPanel.Instance?.RefreshRuleIndicators(FieldState(info));
        var manager=Game3DManager.Instance;if(manager==null)return;
        var host=manager.GetComponent<DuocaiTableOverlay>()??manager.gameObject.AddComponent<DuocaiTableOverlay>();
        host.Dirty=true;
    }
}

public sealed class DuocaiTableOverlay : MonoBehaviour {
    public bool Dirty;
    private void LateUpdate() {
        var manager=Game3DManager.Instance;if(manager==null||manager.HasPendingRecordTableAnimations)return;
        if(Dirty){manager.RefreshDuocaiWall(DuocaiTableIndicators.DisplayedInfo);Dirty=false;}
        manager.RefreshDuocaiBacks(DuocaiTableIndicators.DisplayedInfo,DuocaiTableIndicators.Players);
    }
    private void OnDestroy(){if(Game3DManager.Instance!=null)Game3DManager.Instance.RefreshDuocaiWall(null);}
}

public partial class Game3DManager {
    // Approved plan E uses the shared R120 / 0.82 live-wall layout.
    private readonly Dictionary<int,GameObject> duocaiWall=new Dictionary<int,GameObject>();
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
        }
        if(info?.wall==null)return;
        if(duocaiWallRoot==null){duocaiWallRoot=new GameObject("DuocaiWall").transform;duocaiWallRoot.SetParent(transform,false);}
        foreach(var tile in info.wall.Where(v=>!v.removed)) {
            string seat=DuocaiTableIndicators.SeatOf(tile.side);var panel=GetPosPanel(seat);if(panel?.cardsPosition==null||panel.discardsPosition==null)continue;
            var rotation=RiverTileWorldRotation(seat);
            Vector3 position=WallSlotPosition(seat,tile.stack-8.5f);
            if(!duocaiWall.TryGetValue(tile.position,out var obj)||obj==null) {
                obj=MahjongObjectPool.Instance.SpawnPresentationTile(tile.tile??0,position,rotation);
                if(obj==null)continue;obj.name="DuocaiWall_"+tile.position;obj.transform.SetParent(duocaiWallRoot,true);duocaiWall[tile.position]=obj;
            } else MahjongObjectPool.Instance.SetPresentationFace(obj,tile.tile??0);
            SetWallTileScale(obj.transform,TableWallScale);
            position=KeepMovingTileAboveTable(obj.transform,position,rotation);
            obj.transform.SetPositionAndRotation(position,rotation);
            var renderer=obj.GetComponentInChildren<Renderer>();
            if(tile.layer==0&&renderer!=null)obj.transform.position+=Vector3.up*renderer.bounds.size.y;
            var state=MahjongObjectPool.GetTile3D(obj);state.SetConcealedFaceDown(!tile.tile.HasValue);state.SetHoverPeekAllowed(false);state.SetRuleBackColor(DuocaiTileVisual.BackColor(tile.back));
            foreach(var collider in obj.GetComponentsInChildren<Collider>())collider.enabled=false;
        }
    }
    internal void RefreshDuocaiBacks(DuocaiInfo info,PlayerInfo[] players) {
        if(info==null||players==null)return;
        foreach(var player in players) {
            string seat=DuocaiTableIndicators.SeatOf(player.player_index);var panel=GetPosPanel(seat);if(panel==null)continue;
            var cards=panel.cardsPosition?.GetComponentsInChildren<Tile3D>(false)??Array.Empty<Tile3D>();
            if(player.duocai_hand_backs?.Length==cards.Length)for(int i=0;i<cards.Length;i++)cards[i].SetRuleBackColor(DuocaiTileVisual.BackColor(player.duocai_hand_backs[i]));
            if(panel.combination3DObjects!=null)for(int i=0;i<panel.combination3DObjects.Length;i++) {
                var group=panel.combination3DObjects[i];if(group==null)continue;
                var tiles=group.GetComponentsInChildren<Tile3D>(false);
                if(player.duocai_meld_backs?.Length>i&&player.duocai_meld_backs[i].Length==tiles.Length)
                    for(int j=0;j<tiles.Length;j++)tiles[j].SetRuleBackColor(DuocaiTileVisual.BackColor(player.duocai_meld_backs[i][j]));
            }
            var river=panel.discardsPosition?.GetComponentsInChildren<Tile3D>(false)??Array.Empty<Tile3D>();
            foreach(var tile in river)tile.SetRuleBackColor(DuocaiTileVisual.BackColor((info.deck+(DuocaiTiles.UnusualBack(tile.GetTileId())?1:0))%2));
        }
    }
}
