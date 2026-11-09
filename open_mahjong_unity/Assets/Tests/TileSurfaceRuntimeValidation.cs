#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Salasasa.Home;
using TMPro;
using UnityEngine;

/// <summary>Opt-in validation in MainScene using complete server-generated replays.</summary>
public sealed class TileSurfaceRuntimeValidation : MonoBehaviour {
    private static string folder;
    private readonly List<string> checks = new List<string>(), errors = new List<string>();
    private readonly Dictionary<string,int?> preferences = new Dictionary<string,int?>();
    private ConfigManager config;
    private bool labels, dora;
    private AppLanguage language;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Configure() {
        folder=null;
        var args=Environment.GetCommandLineArgs(); int index=Array.IndexOf(args,"-tile-surface-validation");
        if(index>=0 && index+1<args.Length) folder=args[index+1];
#if UNITY_EDITOR
        if(folder==null) folder=UnityEditor.SessionState.GetString("TileSurfaceValidationFolder","");
#endif
        if(string.IsNullOrEmpty(folder)) return;
        ConfigManager.BuildForSteam=false; Application.runInBackground=true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Launch() {
        if(string.IsNullOrEmpty(folder)) return;
        NetworkManager.Instance.enabled=false;
        foreach(var chat in Resources.FindObjectsOfTypeAll<ChatManager>()) chat.enabled=false;
        new GameObject(nameof(TileSurfaceRuntimeValidation)).AddComponent<TileSurfaceRuntimeValidation>();
    }
    private void Log(string message,string stack,LogType kind) {
        if(kind==LogType.Error||kind==LogType.Exception) errors.Add(message+"\n"+stack);
    }
    private IEnumerator Start() {
        Directory.CreateDirectory(folder); config=ConfigManager.Instance;
        labels=config.TileCornerLabelsEnabled; dora=config.DoraHintsEnabled; language=ConfigManager.CurrentLanguage;
        foreach(string key in new[]{"AppLanguage",ConfigManager.TileCornerLabelsKey,ConfigManager.DoraHintsKey,ConfigManager.TileCornerIntroKey,SettingsStyleCollection.StylePreference})
            preferences[key]=PlayerPrefs.HasKey(key)?PlayerPrefs.GetInt(key):(int?)null;
        Application.logMessageReceived+=Log;
        var stack=new Stack<IEnumerator>(); stack.Push(Verify()); Exception failure=null;
        while(stack.Count>0) {
            object yielded=null; bool more=false;
            try { more=stack.Peek().MoveNext(); if(more) yielded=stack.Peek().Current; }
            catch(Exception error) {failure=error;break;}
            if(!more) stack.Pop(); else if(yielded is IEnumerator nested) stack.Push(nested); else yield return yielded;
        }
        Application.logMessageReceived-=Log;
        bool success=failure==null&&errors.Count==0;
        if(!success) { config.SetDoraHintsEnabled(dora); config.SetTileCornerLabelsEnabled(labels);config.SetLanguageMode((int)language); }
        foreach(var pref in preferences) {if(pref.Value.HasValue) PlayerPrefs.SetInt(pref.Key,pref.Value.Value);else PlayerPrefs.DeleteKey(pref.Key);}
        PlayerPrefs.Save();
        File.WriteAllText(Path.Combine(folder,"runtime-results.json"),JsonConvert.SerializeObject(new{success,count=checks.Count,checks,errors,exception=failure?.ToString()},Formatting.Indented));
        if(failure!=null) Debug.LogException(failure);
        else if(success) Debug.Log("Tile surface validation passed: "+checks.Count);
    }
    private void Check(bool value,string name) {if(!value) throw new InvalidOperationException(name);checks.Add(name);}
    private IEnumerator Settle() {yield return new WaitForSecondsRealtime(.5f);Canvas.ForceUpdateCanvases();yield return null;}
    private IEnumerator Capture(string name) {
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(folder,name+".png"));
        yield return new WaitForEndOfFrame();yield return null;
    }
    private IEnumerator Load(JObject fixture,int node) {
        var players=fixture["players"].ToObject<PlayerRecordInfo[]>();
        typeof(UserDataManager).GetProperty("UserId").SetValue(UserDataManager.Instance,players[0].user_id);
        NormalGameStateManager.Instance.ResetForExit();
        WindowsManager.Instance.SwitchWindow("game");yield return null;
        var record=GameRecordManager.Instance;record.ResetForSessionEnd();
        record.LoadRecord(fixture["record"].ToString(),players);
        WindowsManager.Instance.SwitchWindow("recordscene");
        record.SwitchRecordPerspectiveToOriginalPlayerIndex(0);record.GotoAction(node);yield return Settle();
    }
    private void CheckFaces(string stage) {
        foreach(var card in GameCanvas.Instance.HandCardsContainer.GetComponentsInChildren<TileCard>())
            Check((card.GetComponent<DoraTileSheen>()?.IsVisible==true)==RiichiDoraHints.IsDora(card.tileId),stage+" hand "+card.tileId);
        var visibleId=typeof(Tile3D).GetField("currentVisibleFaceTileId",BindingFlags.NonPublic|BindingFlags.Instance);
        foreach(var tile in FindObjectsByType<Tile3D>(UnityEngine.FindObjectsSortMode.None)) {
            int visible=(int)visibleId.GetValue(tile);
            bool expected=!tile.IsConcealedFaceDown&&RiichiDoraHints.IsDora(visible);
            Check((tile.GetComponentInChildren<DoraTileSheen>()?.IsVisible==true)==expected,stage+" visible 3D "+visible);
            foreach(var label in tile.GetComponentsInChildren<TextMeshPro>()) {
                var face=label.transform.parent.GetComponent<Renderer>();
                Check(label.transform.localScale.x*label.fontSize<face.localBounds.size.x*5,
                    stage+" pooled 3D glyph has bounded scale "+label.text);
            }
        }
    }
    private IEnumerator Verify() {
        yield return null;
        config.SetLanguageMode((int)AppLanguage.English);config.SetTileCornerLabelsEnabled(true);
        WindowsManager.Instance.SwitchWindow("config");yield return Settle();
        var settings=FindObjectsByType<SettingsStyleCollection>(UnityEngine.FindObjectsSortMode.None).Single();settings.SelectCategory(2);
        foreach(int theme in new[]{0,2,1}) {
            settings.SelectVariant(theme);
            var row=settings.controls.Single(f=>f.key=="DoraHintsRow");
            row.root.GetComponentInParent<SettingsBakedPage>().GetComponent<SettingsPagePresentation>().EnsureVisible(row.root);
            row.options[1].onClick.Invoke();yield return null;
            Check(config.DoraHintsEnabled&&row.dropdown.value==1&&PlayerPrefs.GetInt(ConfigManager.DoraHintsKey)==1,"theme "+theme+" enables and persists dora highlights");
            Check(row.options[1].GetComponent<SettingsShapeGraphic>().color==row.choiceStates[1].fill
                &&row.options[0].GetComponent<SettingsShapeGraphic>().color==row.choiceStates[0].fill,
                "theme "+theme+" visibly selects On");
            row.options[0].onClick.Invoke();yield return null;
            Check(!config.DoraHintsEnabled&&row.dropdown.value==0,"theme "+theme+" disables highlights");
            Check(row.options[0].GetComponent<SettingsShapeGraphic>().color==row.choiceStates[1].fill
                &&row.options[1].GetComponent<SettingsShapeGraphic>().color==row.choiceStates[0].fill,
                "theme "+theme+" visibly selects Off");
            Check(row.label.text=="Dora highlights","theme "+theme+" English setting name");
        }
        settings.controls.Single(f=>f.key=="DoraHintsRow").options[1].onClick.Invoke();
        yield return Settle();yield return Capture("settings-english");
        var kan=JObject.Parse(File.ReadAllText(Path.Combine(folder,"kan-replay.json")));
        yield return Load(kan,0);
        var record=GameRecordManager.Instance;
        Check(record.IsVisibleRecordDora(23)&&!record.IsVisibleRecordDora(25),"initial public indicator, no future kan dora");
        CheckFaces("initial");
        record.GotoAction(12);yield return Settle();
        Check(record.IsVisibleRecordDora(25),"kan dora appears at its actual replay tick");CheckFaces("kan");
        record.GotoAction(0);yield return Settle();
        Check(!record.IsVisibleRecordDora(25),"rewinding removes later kan dora");CheckFaces("rewind");
        record.GotoAction(21);yield return Settle();CheckFaces("four kongs");
        Check(FindObjectsByType<Tile3D>(UnityEngine.FindObjectsSortMode.None).Any(t=>t.IsConcealedFaceDown),"real replay contains face-down kong tiles");
        yield return Capture("sanma-kongs");
        config.SetDoraHintsEnabled(false);yield return Settle();
        Check(FindObjectsByType<DoraTileSheen>(UnityEngine.FindObjectsSortMode.None).All(s=>!s.IsVisible),"turning setting off clears all existing surfaces");
        Check(FindObjectsByType<UnityEngine.UI.Image>(UnityEngine.FindObjectsSortMode.None).All(i=>i.material.shader.name!="Mahjong/DoraSheenUI"),
            "turning setting off restores original face materials");
        config.SetDoraHintsEnabled(true);

        var sample=JObject.Parse(File.ReadAllText(Path.Combine(folder,"table-replay.json")));
        yield return Load(sample,60);
        var withDora=record.recordPlayer_to_info.Values.First(p=>p.tileList.Any(RiichiDoraHints.IsDora));
        record.SwitchRecordPerspectiveToOriginalPlayerIndex(withDora.originalPlayerIndex);yield return Settle();
        CheckFaces("table");
        var hand=GameCanvas.Instance.HandCardsContainer.GetComponentsInChildren<TileCard>();
        var shiny=hand.First(c=>RiichiDoraHints.IsDora(c.tileId));
        Check(shiny.FaceArtwork.material.shader.name=="Mahjong/DoraSheenUI"
            && shiny.BadgeBody.material==shiny.FaceArtwork.material,"body and artwork share the calibrated face shader");
        var overlay=shiny.transform.Find("TileFaceOverlay");var surface=overlay.Find("DoraSheen");
        var label=overlay.Find("TileCornerLabel").GetComponent<TMP_Text>();
        Check(label.rectTransform.anchoredPosition.x<=-7 && label.rectTransform.anchoredPosition.y<=-10,"hand indices are inset from the top rim");
        var hover=shiny.GetComponent<HoverEventTrigger>();
        Vector3 relation=shiny.BadgeBody.transform.InverseTransformPoint(surface.position);
        hover.SetArmedLift(true);yield return Settle();
        Check(Vector3.Distance(relation,shiny.BadgeBody.transform.InverseTransformPoint(surface.position))<.001f,"gloss and labels travel with lifted tile");
        yield return Capture("hand-hover");hover.SetArmedLift(false);yield return Settle();
        var corners=new Vector3[4];GameCanvas.Instance.HandCardsContainerRect.GetWorldCorners(corners);
        var canvas=GameCanvas.Instance.GetComponent<Canvas>().rootCanvas;
        // Letterboxing changes the native root to ScreenSpaceCamera. Project through
        // that camera before comparing with its viewport, rather than using world Y.
        var uiCamera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
        float screenBottom=RectTransformUtility.WorldToScreenPoint(uiCamera,corners[0]).y;
        float bottom=screenBottom-canvas.pixelRect.yMin;
        float referenceBottom=bottom/canvas.scaleFactor;
        File.WriteAllText(Path.Combine(folder,"bottom-layout.json"),JsonConvert.SerializeObject(new {
            pixelMargin=bottom,referenceMargin=referenceBottom,screenBottom,
            viewportBottom=canvas.pixelRect.yMin,canvas.scaleFactor,renderMode=canvas.renderMode.ToString(),Screen.width,Screen.height
        }));
        Check(referenceBottom>=12 && referenceBottom<=20,"hand bottom margin within game viewport: "+referenceBottom);
        Check(Resources.Load<Shader>("Materials/Tiles/DoraSheen").isSupported,"shared sheen shader supported by target renderer");
        yield return Capture("table-wave-a");yield return new WaitForSecondsRealtime(.7f);yield return Capture("table-wave-b");
        if(Environment.GetCommandLineArgs().Contains("-dora-sweep-capture")) {
            var frames=new List<object>();
            var faceRect=(RectTransform)surface;
            faceRect.GetWorldCorners(corners);
            File.WriteAllText(Path.Combine(folder,"sweep-face.json"),JsonConvert.SerializeObject(new {
                tile=shiny.tileId,corners=corners.Select(p=> {
                    var point=RectTransformUtility.WorldToScreenPoint(uiCamera,p);
                    return new {x=point.x,y=point.y};
                }).ToArray(),
                Screen.width,Screen.height
            },Formatting.Indented));
            for(int frame=0;frame<120;frame++) {
                yield return new WaitForEndOfFrame();
                frames.Add(new {frame,gameTime=Time.time,realtime=Time.realtimeSinceStartup});
                ScreenCapture.CaptureScreenshot(Path.Combine(folder,"sweep-"+frame.ToString("D2")+".png"));
                yield return new WaitForSecondsRealtime(.025f);
            }
            File.WriteAllText(Path.Combine(folder,"sweep-times.json"),JsonConvert.SerializeObject(frames,Formatting.Indented));
        }
        config.SetLanguageMode((int)AppLanguage.SimplifiedChinese);yield return Settle();yield return Capture("table-chinese");
        Check(record.currentNode==60,"preview retains real operable replay position");
    }
}
#endif
