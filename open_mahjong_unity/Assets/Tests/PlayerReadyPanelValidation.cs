#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;

public static class PlayerReadyPanelValidation {
    public static string Run(string protocolPath=null) {
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var sources=Resources.FindObjectsOfTypeAll<GamePlayerPanel>()
            .Where(panel=>panel.gameObject.scene.IsValid()).ToArray();
        if(sources.Length!=4) throw new InvalidOperationException("Four scene player panels required");
        var checks=new List<string>();
        void Check(bool condition,string name) {if(!condition) throw new Exception(name);checks.Add(name);}
        var host=new GameObject("Ready panel validation");
        host.hideFlags=HideFlags.HideAndDontSave;
        host.SetActive(false);
        try {
            var clones=new List<GamePlayerPanel>();
            foreach(var source in sources) {
                var panel=UnityEngine.Object.Instantiate(source,host.transform);
                clones.Add(panel);
                TMP_Text Label(string key) => (TMP_Text)typeof(GamePlayerPanel).GetMethod("FindPlayerVariable",flags).Invoke(panel,new object[]{key});
                var ready=Label(GamePlayerPanel.VariableDeclaredReady);
                Check(ready!=null,source.name+": prebound ready component");
                var offline=(UnityEngine.UI.Image)typeof(GamePlayerPanel).GetField("playerIslossconnPicture",flags).GetValue(panel);
                var bot=(GameObject)typeof(GamePlayerPanel).GetField("playerIsPeidaPicture",flags).GetValue(panel);
                int childCount=panel.GetComponentsInChildren<Transform>(true).Length;
                foreach(string rule in new[]{"changchun","shenyang","jiujiang","taiwan","tuidao","guangdong","shanxi","shanghai","guizhou","hongkong"}) {
                    string label=rule=="shanghai" ? "已敲牌" : "已报听";
                    panel.UpdateTagList(Array.Empty<string>(),rule);
                    Check(!ready.transform.parent.gameObject.activeSelf,source.name+": undeclared hidden "+rule);
                    panel.UpdateTagList(new[]{"declared_ready"},rule);
                    Check(ready.transform.parent.gameObject.activeSelf && ready.text==label,source.name+": declaration shown "+rule);
                    panel.UpdateTagList(new[]{"declared_ready","offline"},rule);
                    Check(ready.transform.parent.gameObject.activeSelf && offline.gameObject.activeSelf,source.name+": disconnected ready remains "+rule);
                    panel.UpdateTagList(new[]{"declared_ready"},rule);
                    Check(ready.transform.parent.gameObject.activeSelf && !offline.gameObject.activeSelf,source.name+": reconnect restores ready "+rule);
                    panel.UpdateTagList(new[]{"declared_ready","declared_ready"},rule);
                    Check(ready.transform.parent.gameObject.activeSelf && panel.GetComponentsInChildren<Transform>(true).Length==childCount,source.name+": duplicate tag reuses component "+rule);
                    panel.UpdateTagList(Array.Empty<string>(),rule);
                    Check(!ready.transform.parent.gameObject.activeSelf,source.name+": rewind or next round clears "+rule);
                }
                panel.UpdateTagList(new[]{"declared_ready","tian_qiao","chengbao_1"},"shanghai");
                Check(ready.text=="已天敲" && Label(GamePlayerPanel.VariableChengbao).text=="承包南",source.name+": Shanghai labels preserved");
                panel.UpdateTagList(new[]{"declared_ready","chengbao_1","langyong_3","peida"},"changchun");
                Check(!Label(GamePlayerPanel.VariableChengbao).transform.parent.gameObject.activeSelf && bot.activeSelf
                    && Label(GamePlayerPanel.VariableLangyong).text=="浪涌点数*3",source.name+": unrelated indicators preserved");
                foreach(string rule in new[]{"riichi","guobiao","sichuan","changchun","taiwan"}) {
                    panel.UpdateTagList(new[]{"riichi","heavenly","earthly","ready"},rule);
                    Check(!ready.transform.parent.gameObject.activeSelf,source.name+": private wait and riichi are not public ready "+rule);
                }
                panel.UpdateTagList(new[]{"declared_ready"},"future_rule");
                Check(ready.transform.parent.gameObject.activeSelf,source.name+": public tag independent of rule allowlist");
                Check("已报听".All(character=>ready.font.HasCharacter(character)),source.name+": static font contains ready label");
                panel.UpdateTagList(null);
                Check(!ready.transform.parent.gameObject.activeSelf,source.name+": null tag reset");
            }
            var canvas=Resources.FindObjectsOfTypeAll<GameCanvas>().Single(c=>c.gameObject.scene.IsValid());
            var positions=new[]{"self","right","top","left"};
            var fields=new[]{"playerSelfPanel","playerRightPanel","playerTopPanel","playerLeftPanel"}
                .Select(name=>typeof(GameCanvas).GetField(name,flags|BindingFlags.Public)).ToArray();
            var saved=fields.Select(field=>field.GetValue(canvas)).ToArray();
            try {
                for(int i=0;i<4;i++) fields[i].SetValue(canvas,clones[i]);
                var tags=new Dictionary<int,string[]>{{0,new[]{"declared_ready"}},{1,Array.Empty<string>()},{2,Array.Empty<string>()},{3,Array.Empty<string>()}};
                if(protocolPath!=null) {
                    var data=Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(protocolPath));
                    var packet=data["messages"]["101"].First(p=>p["refresh_player_tag_list_info"]!=null);
                    tags=packet["refresh_player_tag_list_info"]["player_to_tag_list"].ToObject<Dictionary<int,string[]>>();
                }
                for(int viewer=0;viewer<4;viewer++) {
                    var map=Enumerable.Range(0,4).ToDictionary(seat=>seat,seat=>positions[(seat-viewer+4)%4]);
                    canvas.UpdatePlayerTagList(tags,map,"changchun");
                    for(int seat=0;seat<4;seat++) {
                        var panel=clones[(seat-viewer+4)%4];
                        var ready=(TMP_Text)typeof(GamePlayerPanel).GetMethod("FindPlayerVariable",flags).Invoke(panel,new object[]{GamePlayerPanel.VariableDeclaredReady});
                        Check(ready.transform.parent.gameObject.activeSelf==(seat==0),$"network/replay seat mapping viewer={viewer}, seat={seat}");
                    }
                    canvas.UpdatePlayerTagList(Enumerable.Range(0,4).ToDictionary(seat=>seat,seat=>Array.Empty<string>()),map,"changchun");
                    Check(clones.All(panel=>!((TMP_Text)typeof(GamePlayerPanel).GetMethod("FindPlayerVariable",flags).Invoke(panel,new object[]{GamePlayerPanel.VariableDeclaredReady})).transform.parent.gameObject.activeSelf),"all seat badges clear after replay rewind / round reset "+viewer);
                }
            } finally {
                for(int i=0;i<4;i++) fields[i].SetValue(canvas,saved[i]);
            }
        } finally {UnityEngine.Object.DestroyImmediate(host);}
        return JsonConvert.SerializeObject(new{passed=checks.Count,checks});
    }
}
#endif
