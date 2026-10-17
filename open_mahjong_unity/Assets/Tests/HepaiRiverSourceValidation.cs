#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>从打开场景复用实际牌面配置，在预览场景验证单家/多家点和的实体牌归属。</summary>
public static class HepaiRiverSourceValidation {
    public static string Run() {
        var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var root=new GameObject("Hongque physical ron verification");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
        var hidden=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        object Get(object o,string n)=>o.GetType().GetField(n,hidden).GetValue(o);
        void Set(object o,string n,object v)=>o.GetType().GetField(n,hidden).SetValue(o,v);
        object Call(object o,string n,params object[] args)=>o.GetType().GetMethod(n,hidden).Invoke(o,args);
        var source=UnityEngine.Object.FindFirstObjectByType<Game3DManager>(FindObjectsInactive.Include);
        var sourcePool=UnityEngine.Object.FindFirstObjectByType<MahjongObjectPool>(FindObjectsInactive.Include);
        var poolBefore=MahjongObjectPool.Instance;
        var hoverProperty=typeof(Card3DHoverManager).GetProperty("Instance");
        var hoverBefore=hoverProperty.GetValue(null);
        var sharedMaterial=Resources.Load<Material>(CardBackManager.MaterialResourcePath);
        var materialBefore=new Material(sharedMaterial);
        var outcomes=new List<object>();
        try {
         var manager=root.AddComponent<Game3DManager>(); UnityEditor.EditorUtility.CopySerialized(source,manager);
         var pool=root.AddComponent<MahjongObjectPool>(); UnityEditor.EditorUtility.CopySerialized(sourcePool,pool);
         Set(pool,"poolDictionary",new Dictionary<int,Queue<GameObject>>());
         Call(pool,"CacheAllSprites",Get(pool,"cardAtlas"));pool.InitializePool();MahjongObjectPool.Instance=pool;
         var hover=root.AddComponent<Card3DHoverManager>();hoverProperty.SetValue(null,hover);
         var panels=new Dictionary<string,PosPanel3D>();
         foreach(string seat in new[]{"self","left","top","right"}){
           var node=new GameObject(seat);node.transform.SetParent(root.transform);
           var panel=node.AddComponent<PosPanel3D>();
           panel.cardsPosition=new GameObject("Hand").transform;panel.cardsPosition.SetParent(node.transform);
           panel.discardsPosition=new GameObject("River").transform;panel.discardsPosition.SetParent(node.transform);
           panel.combinationsPosition=new GameObject("Melds").transform;panel.combinationsPosition.SetParent(node.transform);
           panel.ShowCardsPosition=new GameObject("RecordHand").transform;panel.ShowCardsPosition.SetParent(node.transform);
           Set(manager,seat+"PosPanel",panel);panels[seat]=panel;
         }
         foreach(string winner in new[]{"self","left","top","right"}){
           var river=panels[winner=="right"?"left":"right"].discardsPosition;
           string from=winner=="right"?"left":"right";
           Call(manager,"Set3DTile",1135,river,"DiscardWithoutAnimation",from,false,false,false);
           var original=river.GetChild(0).gameObject;
           var request=new HepaiPresentationRequest{WinnerPosition=winner,DiscardPlayerPosition=from,HuClass="hu",HepaiPlayerHand=new[]{1005,1125,1135},HepaiTile=1135,WinTileMode=HepaiWinTilePresentMode.GuobiaoRonTravelFromRiver};
           var hand=panels[winner].cardsPosition;
           var steps=new Stack<System.Collections.IEnumerator>();steps.Push(manager.PlayHepaiHandReveal(request));int frames=0;
           // Check the physical handoff before the shared timed interpolation; EditMode has deltaTime=0.
           while(steps.Count>0 && original.transform.parent!=hand){if(++frames>10000)throw new Exception("Coroutine did not finish");var e=steps.Peek();if(!e.MoveNext()){steps.Pop();continue;}if(e.Current is System.Collections.IEnumerator nested)steps.Push(nested);}
           bool okay=hand.childCount==3&&river.childCount==0&&original.transform.parent==hand&&hand.GetChild(2).gameObject==original;
           if(!okay)throw new Exception("Ron did not move the same physical river tile: "+winner);
           outcomes.Add(new{winner,handCount=hand.childCount,riverCount=river.childCount,samePhysicalTile=true,coroutineSteps=frames});
           Call(manager,"ClearHandCardsPosition",hand);
         }
         var multi=new[]{
           new HepaiPresentationRequest{WinnerPosition="self",DiscardPlayerPosition="right",HuClass="hu",HepaiPlayerHand=new[]{1005,1125,1135},WinTileMode=HepaiWinTilePresentMode.GuobiaoRonTravelFromRiver},
           new HepaiPresentationRequest{WinnerPosition="left",DiscardPlayerPosition="right",HuClass="hu",HepaiPlayerHand=new[]{1041,1042,1135},WinTileMode=HepaiWinTilePresentMode.GuobiaoRonTravelFromRiver},
           new HepaiPresentationRequest{WinnerPosition="top",DiscardPlayerPosition="right",HuClass="hu",HepaiPlayerHand=new[]{1061,1062,1135},WinTileMode=HepaiWinTilePresentMode.GuobiaoRonTravelFromRiver}
         };
         var pending=(Dictionary<string,int>)Get(manager,"_recordHandAnimationsByPlayer");
         foreach(int winnerCount in new[]{2,3}) {
           var requests=multi.Take(winnerCount).ToArray();
           var reveal=manager.PlayMultipleHepaiHandReveals(requests);
           pending["right"]=1;
           if(!reveal.MoveNext()||reveal.Current!=null||requests.Any(r=>panels[r.WinnerPosition].cardsPosition.childCount!=0))
             throw new Exception("Multiple ron must wait for the pending discard before revealing hands");
           Call(manager,"Set3DTile",1135,panels["right"].discardsPosition,"DiscardWithoutAnimation","right",false,false,false);
           var shared=panels["right"].discardsPosition.GetChild(0).gameObject;
           pending["right"]=0;reveal.MoveNext();
           bool multiOkay=panels["right"].discardsPosition.childCount==0
             &&requests.All(r=>panels[r.WinnerPosition].cardsPosition.childCount==3)
             &&panels["self"].cardsPosition.GetChild(2).gameObject==shared
             &&requests.Skip(1).All(r=>panels[r.WinnerPosition].cardsPosition.GetChild(2).gameObject!=shared);
           if(!multiOkay)throw new Exception("Multiple ron must consume one physical source and only copy it for subsequent winners");
           outcomes.Add(new{multiRon=true,winners=winnerCount,waitsForDiscard=true,physicalSources=1,extraPresentationCopies=winnerCount-1,riverCount=0});
           foreach(var request in requests)Call(manager,"ClearHandCardsPosition",panels[request.WinnerPosition].cardsPosition);
         }
         pending["right"]=1;
         var cancelled=manager.PlayMultipleHepaiHandReveals(multi);cancelled.MoveNext();
         Set(manager,"_handAnimationGeneration",(int)Get(manager,"_handAnimationGeneration")+1);
         pending["right"]=0;
         if(cancelled.MoveNext()||multi.Any(r=>panels[r.WinnerPosition].cardsPosition.childCount!=0))
           throw new Exception("A table reset must cancel a waiting multi-ron reveal");
         outcomes.Add(new{cancelledOnTableReset=true});
         foreach(int tileId in new[]{1135,11}) {
           Call(manager,"Set3DTile",tileId,panels["self"].discardsPosition,"DiscardWithoutAnimation","self",false,false,false);
           var original=panels["self"].discardsPosition.GetChild(0).gameObject;
           int winnerIndex=0;
           foreach(string winner in new[]{"left","top","right"}) {
             var row=panels[winner].ShowCardsPosition;
             var request=new HepaiPresentationRequest{WinnerPosition=winner,DiscardPlayerPosition="self",HuClass="hu",HepaiTile=tileId,
               HepaiPlayerHand=new[]{tileId},IsRecordShowCardsExpanded=true,WinTileMode=HepaiWinTilePresentMode.GuobiaoRonTravelFromRiver};
             var routine=(System.Collections.IEnumerator)Call(manager,"CoRecordShowCardsGuobiaoWinTileToDrawSlot",request);
             // The source is assigned before the timed interpolation; do not advance EditMode's zero deltaTime loop.
             routine.MoveNext();
             if(row.childCount!=1)throw new Exception("Expanded replay lost a multi-ron winning tile: "+winner+" / "+tileId);
             var tile=MahjongObjectPool.GetTile3D(row.GetChild(0).gameObject);
             bool physical=winnerIndex++==0;
             if(physical ? row.GetChild(0).gameObject!=original : tile.GetPoolTileId()!=2)
               throw new Exception("Expanded replay must consume one physical river tile and use presentation copies for further winners");
             outcomes.Add(new{expandedReplay=true,tileId,winner,physicalSource=physical,poolTile=tile.GetPoolTileId()});
           }
           foreach(string winner in new[]{"left","top","right"})Call(manager,"ClearHandCardsPosition",panels[winner].ShowCardsPosition);
         }
         return Newtonsoft.Json.JsonConvert.SerializeObject(new{passed=outcomes.Count,cases=outcomes},Newtonsoft.Json.Formatting.Indented);
        } finally {
         MahjongObjectPool.Instance=poolBefore;hoverProperty.SetValue(null,hoverBefore);
         foreach(var pool in root.GetComponents<MahjongObjectPool>()){
          foreach(string n in new[]{"hongqueMaterialCache","customStandardMaterialCache"}){
           var dict=(Dictionary<int,Material>)Get(pool,n);foreach(var m in dict.Values)if(m)UnityEngine.Object.DestroyImmediate(m);dict.Clear();
          }
         }
         UnityEngine.Object.DestroyImmediate(root);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
         sharedMaterial.CopyPropertiesFromMaterial(materialBefore);
         UnityEngine.Object.DestroyImmediate(materialBefore);
        }
    }
}
#endif
