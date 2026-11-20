using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class Game3DManager {
    private sealed class DuocaiDeclaredKong {
        public GameObject tile,hidden3D,hidden2D;
        public uint lease,hiddenLease;
        public Transform hidden3DParent,hidden2DParent;
        public int tileId,meldIndex,ticket;
        public int[] mask;
        public bool fromDraw,hideHand,pending=true;
    }
    private readonly Dictionary<string,DuocaiDeclaredKong> duocaiDeclaredKongs=new Dictionary<string,DuocaiDeclaredKong>();
    private int duocaiDeclarationTicket;
    private Transform duocaiDeclaredRoot;

    private static bool TryMeldClaimedPosition(Transform group,IList<int> reversedSigns,out Vector3 position) {
        position=Vector3.zero;if(group==null)return false;
        int child=0;
        foreach(int sign in reversedSigns) {
            if(sign==3||sign==4||MeldStackLayout.IsStack(sign))continue;
            if(sign==1) {
                if(child>=group.childCount)return false;
                position=group.GetChild(child).position;return true;
            }
            child++;
        }
        return false;
    }
    private GameObject DuocaiDeclaredKongSource(string seat,int expectedTileId) {
        if(string.IsNullOrEmpty(seat)||!duocaiDeclaredKongs.TryGetValue(seat,out var value)||value.tile==null)return null;
        var tile=MahjongObjectPool.GetTile3D(value.tile);
        return tile!=null&&tile.PoolLeaseVersion==value.lease&&value.tile.activeInHierarchy
            &&value.tile.transform.parent==duocaiDeclaredRoot&&tile.GetTileId()==expectedTileId?value.tile:null;
    }
    public bool TryGetDuocaiDeclaredKong(string seat,out int tileId,out bool fromDraw,out bool handHidden) {
        tileId=0;fromDraw=handHidden=false;
        if(!duocaiDeclaredKongs.TryGetValue(seat,out var value)||DuocaiDeclaredKongSource(seat,value.tileId)==null)return false;
        tileId=value.tileId;fromDraw=value.fromDraw;
        handHidden=value.hidden3D!=null&&value.hidden3D.transform.parent==value.hidden3DParent&&!value.hidden3D.activeSelf
            &&MahjongObjectPool.GetTile3D(value.hidden3D)?.PoolLeaseVersion==value.hiddenLease
            ||value.hidden2D!=null&&value.hidden2D.transform.parent==value.hidden2DParent&&!value.hidden2D.activeSelf;
        return true;
    }
    public void ClearDuocaiDeclaredKongs() {
        foreach(string seat in duocaiDeclaredKongs.Keys.ToArray())ClearDuocaiDeclaredKong(seat);
    }
    public void ClearDuocaiDeclaredKong(string seat,bool restoreHand=true) {
        if(!duocaiDeclaredKongs.TryGetValue(seat,out var value))return;
        value.pending=false;
        if(restoreHand) {
            if(value.hidden3D!=null&&value.hidden3D.transform.parent==value.hidden3DParent
                &&MahjongObjectPool.GetTile3D(value.hidden3D)?.PoolLeaseVersion==value.hiddenLease)value.hidden3D.SetActive(true);
            if(value.hidden2D!=null&&value.hidden2D.transform.parent==value.hidden2DParent&&RuleRegistry.Current?.RuleId=="duocai")value.hidden2D.SetActive(true);
            duocaiDeclaredKongs.Remove(seat);
        }
        if(value.tile==null)return;
        var state=MahjongObjectPool.GetTile3D(value.tile);
        if(state==null||state.PoolLeaseVersion!=value.lease||value.tile.transform.parent!=duocaiDeclaredRoot)return;
        ClearLastJiagangIfMatches(seat,value.tile);
        MahjongObjectPool.Instance.Return(-1,value.tile);
        value.tile=null;
    }
    public void ShowDuocaiDeclaredKong(string seat,int meldIndex,int tileId,int[] previousMask,bool fromDraw=false,bool hideHand=true) {
        ClearDuocaiDeclaredKong(seat);
        var value=new DuocaiDeclaredKong{tileId=tileId,meldIndex=meldIndex,mask=previousMask==null?null:(int[])previousMask.Clone(),
            fromDraw=fromDraw,hideHand=hideHand,ticket=++duocaiDeclarationTicket};
        duocaiDeclaredKongs[seat]=value;
        if(HasPendingHandAnimWork(seat))EnqueueHandAnimOp(seat,new HandAnimOp{Kind=HandAnimOpKind.DuocaiDeclareKong,RemoveCount=value.ticket});
        else RenderDuocaiDeclaredKong(seat,value.ticket);
    }
    private void RenderDuocaiDeclaredKong(string seat,int ticket) {
        if(!duocaiDeclaredKongs.TryGetValue(seat,out var value)||!value.pending||value.ticket!=ticket)return;
        int tileId=value.tileId,meldIndex=value.meldIndex;var previousMask=value.mask;
        var panel=GetPosPanel(seat);
        if(panel==null||tileId<=0||meldIndex<0||meldIndex>=panel.combination3DObjects.Length)return;
        var group=panel.combination3DObjects[meldIndex];
        if(group==null||group.childCount==0)return;
        Vector3 inward=seat=="self"?FrontDirection:seat=="left"?RightDirection:seat=="top"?BackDirection:LeftDirection;
        Vector3 along=seat=="self"?LeftDirection:seat=="left"?FrontDirection:seat=="top"?RightDirection:BackDirection;
        Quaternion rotation=RiverTileWorldRotation(seat);
        var signs=previousMask?.Where((_,i)=>i%2==0).Reverse().ToArray();
        Vector3 position;
        if(previousMask?.Length==6&&TryMeldClaimedPosition(group,signs,out position)) {
            rotation=Quaternion.Euler(0,-90,0)*rotation;position+=inward*cardWidth;
        } else {
            bool left=DuocaiTableIndicators.DisplayedInfo?.variant=="riichi";
            Vector3 direction=left?along:-along;
            position=group.Cast<Transform>().OrderByDescending(v=>Vector3.Dot(v.position,direction)).First().position+direction*cardWidth;
        }
        if(duocaiDeclaredRoot==null){duocaiDeclaredRoot=new GameObject("DuocaiDeclaredKongs").transform;duocaiDeclaredRoot.SetParent(transform,false);}
        var obj=MahjongObjectPool.Instance.SpawnPresentationTile(tileId,PlaceTileOnTable(position,rotation),rotation);
        if(obj==null)return;
        obj.name="DuocaiDeclaredKong_"+seat;obj.transform.SetParent(duocaiDeclaredRoot,true);
        var tile=MahjongObjectPool.GetTile3D(obj);tile.SetHoverPeekAllowed(false);
        value.tile=obj;value.lease=tile.PoolLeaseVersion;
        if(value.hideHand)HideDuocaiDeclaredHand(seat,panel,value);
        RegisterLastJiagang(seat,obj,tileId);
    }
    private void HideDuocaiDeclaredHand(string seat,PosPanel3D panel,DuocaiDeclaredKong value) {
        Transform row=IsRecordShowCardsModeActive()&&seat!="self"?panel.ShowCardsPosition:panel.cardsPosition;
        if(row!=null&&row.childCount>0) {
            Transform chosen=null;
            if(value.fromDraw)chosen=row.GetChild(row.childCount-1);
            else {
                foreach(Transform child in row) {
                    var state=MahjongObjectPool.GetTile3D(child.gameObject);
                    if(state!=null&&!state.isRecordDrawSlotPinned&&state.GetTileId()==value.tileId){chosen=child;break;}
                }
                if(chosen==null&&DuocaiTileVisual.TryBack(value.tileId,out int back))foreach(Transform child in row) {
                    var state=MahjongObjectPool.GetTile3D(child.gameObject);
                    if(state!=null&&!state.isRecordDrawSlotPinned&&state.GetTileId()<10&&state.RuleBackColor.HasValue&&DuocaiBackOf(state)==back){chosen=child;break;}
                }
            }
            if(chosen!=null) {
                value.hidden3D=chosen.gameObject;value.hidden3DParent=row;value.hiddenLease=MahjongObjectPool.GetTile3D(chosen.gameObject).PoolLeaseVersion;
                chosen.gameObject.SetActive(false);
            }
        }
        if(seat=="self"&&GameCanvas.Instance?.HandCardsContainer!=null) {
            var row2D=GameCanvas.Instance.HandCardsContainer;
            var card=value.fromDraw?GameCanvas.Instance.GetDrawTile():row2D.Cast<Transform>().Select(t=>t.GetComponent<TileCard>()).FirstOrDefault(t=>t!=null&&t.tileId==value.tileId&&!t.currentGetTile);
            if(card!=null&&card.tileId==value.tileId){value.hidden2D=card.gameObject;value.hidden2DParent=row2D;card.gameObject.SetActive(false);}
        }
    }
    private bool TryRemoveDuocaiDeclaredHand(Transform row,int count,int tileId,string seat) {
        if(count!=1||!duocaiDeclaredKongs.TryGetValue(seat,out var value)||value.tileId!=tileId||value.hidden3D==null)return false;
        var hidden=value.hidden3D;
        if(hidden.transform.parent!=row||MahjongObjectPool.GetTile3D(hidden)?.PoolLeaseVersion!=value.hiddenLease)return false;
        SetLastRemovePos(seat,hidden.transform.position);value.hidden3D=null;
        MahjongObjectPool.Instance.Return(-1,hidden);return true;
    }
    public void PlayDuocaiKong(string seat,int meldIndex,int[] mask,int[][] previousMasks,int tileId,bool fromDraw) {
        ClearDuocaiDeclaredKong(seat,restoreHand:false);
        if(!IsHandAnimPlayer(seat))return;
        bool upgrade=previousMasks[meldIndex].Length==6&&mask.Length==8&&GameRecordMeldCodec.ExtractTileByFlag(mask,3).HasValue;
        IEnumerator visual=upgrade?ActionAnimationCoroutine(seat,"jiagang",(int[])mask.Clone(),false,meldIndex)
            :ReplaceMeldGroupCoroutine(seat,meldIndex,(int[])mask.Clone(),previousMasks);
        StartCoroutine(TrackRecordHandAnimation(visual,seat));
        _ankanPendingDrawByPlayer[seat]=true;
        EnqueueJiagangHandWork(seat,tileId,fromDraw);
    }
    private IEnumerator CloseDuocaiAnkan(IReadOnlyList<(Tile3D tile,uint lease,int sign)> tiles,int generation) {
        yield return new WaitForSeconds(.6f);
        if(generation!=_handAnimationGeneration)yield break;
        foreach(var item in tiles)if(item.tile!=null&&item.tile.PoolLeaseVersion==item.lease)
            item.tile.ApplyCombinationPeekState(item.tile.GetTileId(),item.sign);
    }
}
