#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Opt-in client regression: --verify-hongzhong-client &lt;folder&gt; [live|replay].
/// Live reads runtime-fixtures.json and runtime-settlement-race-fixtures.json;
/// replay reads saved records and oracles under network/.
/// All reports and screenshots stay in the supplied folder. Ordinary launches do nothing.
/// </summary>
public sealed class HongzhongRuntimeValidation : MonoBehaviour {
    private const string Argument = "--verify-hongzhong-client";
    private const string SummaryFile = "hongzhong-client-validation.json";
    private string folder;
    private string mode;
    private string resultPath;
    private string phase;
    private double startedAt;
    private double deadline;

    private static bool TryReadArguments(string[] args, out string folder, out string mode, out string error) {
        folder = null; mode = "live"; error = null;
        int index = args == null ? -1 : Array.IndexOf(args, Argument);
        if (index < 0) return false;
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("-", StringComparison.Ordinal)) {
            error = Argument + " requires an input/output folder."; return false;
        }
        try { folder = Path.GetFullPath(args[index + 1]); }
        catch (Exception failure) { error = "Invalid validation folder: " + failure.Message; return false; }
        if (index + 2 < args.Length && !args[index + 2].StartsWith("-", StringComparison.Ordinal)) mode = args[index + 2];
        if (mode != "live" && mode != "replay") {
            error = "Validation mode must be live or replay."; return false;
        }
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Launch() {
        if (!TryReadArguments(Environment.GetCommandLineArgs(), out string folder, out string mode, out string error)) {
            if (error == null) return;
            LaunchFailed(folder, mode, new ArgumentException(error)); return;
        }
        try {
            var runner = new GameObject("HongzhongClientValidation").AddComponent<HongzhongRuntimeValidation>();
            DontDestroyOnLoad(runner.gameObject);
            runner.Configure(folder, mode);
            runner.StartCoroutine(runner.Guard(runner.Boot()));
        } catch (Exception failure) { LaunchFailed(folder, mode, failure); }
    }

    private static void LaunchFailed(string folder, string mode, Exception error) {
        if (folder != null) {
            try {
                Directory.CreateDirectory(folder);
                AppendFailure(Path.Combine(folder, SummaryFile), "launch", mode, error, 2, new List<string>());
            } catch (Exception reportError) { Debug.LogError(reportError); }
        }
        Debug.LogError(error);
        Application.Quit(2);
    }

    private void Configure(string inputFolder, string selectedMode) {
        folder = inputFolder; mode = selectedMode;
        Application.runInBackground = true;
        resultPath = Path.Combine(folder, mode == "replay" ? "unity-replay-results.json" : "unity-runtime-results.json");
        Directory.CreateDirectory(folder);
        foreach (string path in new[] { resultPath, Path.Combine(folder, SummaryFile) }) {
            if (File.Exists(path)) File.Move(path, path + ".previous-" + DateTime.UtcNow.Ticks + "-" + Guid.NewGuid().ToString("N"));
        }
        startedAt = Time.realtimeSinceStartupAsDouble;
        phase = "waiting_for_scene"; deadline = startedAt + 30;
        WriteSummary("running", null);
    }

    private IEnumerator Boot() {
        while (NetworkManager.Instance == null || GameCanvas.Instance == null || NormalGameStateManager.Instance == null)
            yield return null;
        yield return new WaitForSecondsRealtime(1);
        phase = mode; deadline = Time.realtimeSinceStartupAsDouble + 1200;
        WriteSummary("running", null);
        yield return mode == "replay" ? RunReplay(folder) : RunLive(folder);
        phase = "result";
        if (!File.Exists(resultPath)) throw new InvalidDataException("Validation finished without a result report.");
        var report = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(resultPath));
        string status = (string)report["status"];
        if (status != "completed" && status != "failed") throw new InvalidDataException("Validation did not reach a terminal status: " + status);
        int exitCode = status == "completed" ? 0 : 1;
        WriteSummary(status, exitCode);
        Application.Quit(exitCode);
    }

    private void WriteSummary(string status, int? exitCode) {
        File.WriteAllText(Path.Combine(folder, SummaryFile), Newtonsoft.Json.JsonConvert.SerializeObject(new {
            status, mode, phase, exitCode, inputFolder = folder, resultFile = Path.GetFileName(resultPath),
            elapsedSeconds = Time.realtimeSinceStartupAsDouble - startedAt
        }, Newtonsoft.Json.Formatting.Indented));
    }

    private static void AppendFailure(string path, string phase, string mode, Exception error, int exitCode, List<string> cleanupErrors) {
        var report = new Newtonsoft.Json.Linq.JObject();
        if (File.Exists(path)) {
            try { report = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path)); }
            catch (Exception readError) { report["previousReportReadError"] = readError.ToString(); }
        }
        report["status"] = "failed"; report["phase"] = phase; report["mode"] = mode;
        report["exitCode"] = exitCode; report["error"] = error.ToString(); report["stack"] = error.StackTrace;
        report["cleanupErrors"] = new Newtonsoft.Json.Linq.JArray(cleanupErrors);
        File.WriteAllText(path, report.ToString(Newtonsoft.Json.Formatting.Indented));
    }

    private IEnumerator Guard(IEnumerator routine) {
        var pending = new Stack<IEnumerator>(); pending.Push(routine);
        while (pending.Count > 0) {
            bool next = false; object value = null; Exception failure = null;
            try {
                if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Hongzhong validation timed out during " + phase + ".");
                var iterator = pending.Peek(); next = iterator.MoveNext();
                if (next) value = iterator.Current;
                else { pending.Pop(); (iterator as IDisposable)?.Dispose(); }
            } catch (Exception error) { failure = error; }
            if (failure != null) {
                var cleanupErrors = new List<string>();
                // Preserve probe finally blocks (log subscriptions and audio settings) before recording the failure.
                while (pending.Count > 0) {
                    try { (pending.Pop() as IDisposable)?.Dispose(); }
                    catch (Exception cleanupError) { cleanupErrors.Add(cleanupError.ToString()); }
                }
                int exitCode = failure is TimeoutException ? 2 : 1;
                foreach (string path in new[] { resultPath, Path.Combine(folder, SummaryFile) }) {
                    try { AppendFailure(path, phase, mode, failure, exitCode, cleanupErrors); }
                    catch (Exception reportError) { Debug.LogError(reportError); }
                }
                Debug.LogError(failure);
                Application.Quit(exitCode); yield break;
            }
            if (!next) continue;
            // Drive nested and custom-yield enumerators so exceptions and deadlines remain guarded each frame.
            if (value is IEnumerator nested) pending.Push(nested);
            else yield return value;
        }
    }

    // Generated from check-unity-live-fixtures.cs; SHA-256 b8049d73575fcf9a82a36f930630d36686919bf8bab29ece5d0f1038b59ee06c.
    private IEnumerator RunLive(string folder) {
        var root=Path.GetFullPath(folder)+Path.DirectorySeparatorChar;
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
        var checks=new System.Collections.Generic.List<object>();var errors=new System.Collections.Generic.List<string>();
        int failures=0,finishedRounds=0,finishedRaceViews=0,raceCases=0;bool completed=false;string activeCase="setup";
        void Save(string status)=>System.IO.File.WriteAllText(root+"unity-runtime-results.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{status,finishedRounds,finishedRaceViews,raceCases,failures,checks,errors},Newtonsoft.Json.Formatting.Indented));
        void Check(string name,bool passed,object detail=null){checks.Add(new{activeCase,name,passed,detail});if(!passed)failures++;}
        void Log(string text,string stack,UnityEngine.LogType kind){if(kind==UnityEngine.LogType.Error||kind==UnityEngine.LogType.Exception)errors.Add(text+"\n"+stack);}
        object[] Texts(UnityEngine.GameObject parent){
            return parent.GetComponentsInChildren<TMPro.TMP_Text>().Where(t=>t.enabled&&!string.IsNullOrEmpty(t.text)).Select(t=>{
                t.ForceMeshUpdate();var missing=new System.Collections.Generic.List<int>();
                for(int i=0;i<t.textInfo.characterCount;i++){var c=t.textInfo.characterInfo[i];if(char.IsWhiteSpace(c.character))continue;if(c.textElement==null||(c.textElement.unicode==0x25A1&&c.character!=0x25A1))missing.Add(c.character);}
                Check("rendered glyphs "+t.name,missing.Count==0,new{t.text,font=t.font?.name,missing});
                if(t.text.Contains("红中替代")||t.text.Contains("杠分另计")||t.text.Contains("仅自摸"))
                    Check("new rule text fits "+t.name,!t.isTextOverflowing&&!t.isTextTruncated,new{t.text,t.isTextOverflowing,t.isTextTruncated,width=t.rectTransform.rect.width,height=t.rectTransform.rect.height});
                return (object)new{t.name,t.text,font=t.font?.name,t.fontSize,overflow=t.isTextOverflowing,truncated=t.isTextTruncated};
            }).ToArray();
        }
        
        UnityEngine.Rect SettlementScreenRect(UnityEngine.RectTransform rect){
            var corners=new UnityEngine.Vector3[4];rect.GetWorldCorners(corners);
            var canvas=rect.GetComponentInParent<UnityEngine.Canvas>();var camera=canvas==null||canvas.renderMode==UnityEngine.RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
            var points=corners.Select(p=>UnityEngine.RectTransformUtility.WorldToScreenPoint(camera,p)).ToArray();
            return UnityEngine.Rect.MinMaxRect(points.Min(p=>p.x),points.Min(p=>p.y),points.Max(p=>p.x),points.Max(p=>p.y));
        }
        void CheckHongzhongSettlementLayout(UnityEngine.GameObject panelObject){
            var panel=panelObject.GetComponent<EndResultPanel>();if(panel==null)return;
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            T LayoutField<T>(string name)=>(T)typeof(EndResultPanel).GetField(name,flags).GetValue(panel);
            var label=LayoutField<TMPro.TextMeshProUGUI>("guobiaoAngangCheckText");
            Check("red-center footnote visible",label!=null&&label.gameObject.activeInHierarchy);if(label==null||!label.gameObject.activeInHierarchy)return;
            UnityEngine.Canvas.ForceUpdateCanvases();label.ForceMeshUpdate();var note=SettlementScreenRect(label.rectTransform);
            Check("red-center footnote fits",!label.isTextOverflowing&&!label.isTextTruncated,new{label.text,rect=note.ToString(),label.fontSize});
            Check("red-center footnote inside screen",note.xMin>=0&&note.yMin>=0&&note.xMax<=UnityEngine.Screen.width&&note.yMax<=UnityEngine.Screen.height,note.ToString());
            var bird=LayoutField<UnityEngine.GameObject>("ChangshaBirdPanel");
            var targets=new System.Collections.Generic.List<UnityEngine.RectTransform>();
            if(bird!=null&&bird.activeInHierarchy){targets.AddRange(bird.GetComponentsInChildren<TMPro.TMP_Text>().Select(t=>t.rectTransform));targets.AddRange(bird.GetComponentsInChildren<StaticCard>().Select(c=>c.GetComponent<UnityEngine.RectTransform>()));}
            foreach(string name in new[]{"FanCountTotalPanel","EndTilescontainer"}){var go=LayoutField<UnityEngine.GameObject>(name);if(go!=null&&go.activeInHierarchy)targets.Add(go.GetComponent<UnityEngine.RectTransform>());}
            foreach(var target in targets.Where(t=>t!=null)){var other=SettlementScreenRect(target);Check("red-center footnote avoids "+target.name,!note.Overlaps(other),new{footnote=note.ToString(),other=other.ToString()});}
        }
        
        void Identify(GameInfo info,int fixtureUid){
            var self=info.players_info.First(p=>p.user_id==fixtureUid);self.user_id=PlayerSession.Current.UserId;
            info.player_entry_order=info.players_info.Select(p=>p.user_id).ToArray();
        }
        System.Collections.IEnumerator Idle(){
            double deadline=UnityEngine.Time.realtimeSinceStartupAsDouble+15;
            while(GameCanvas.Instance.IsChangeHandCardProcessing||Game3DManager.Instance.HasPendingRecordTableAnimations){if(UnityEngine.Time.realtimeSinceStartupAsDouble>deadline)throw new System.Exception("Hand queue stalled");yield return null;}
        }
        void CheckFinalControl(string label){
            var control=(UnityEngine.GameObject)typeof(RoundEndPresentation).GetField("selfGameplayControlRoot",flags).GetValue(RoundEndPresentation.Instance);
            Check(label+" self gameplay control hidden",!control.activeSelf&&!GameCanvas.Instance.HandCardsContainer.gameObject.activeInHierarchy);
        }
        void CheckConcealedKongs(string label){
            foreach(var pair in TableMirror.Current.IndexToPosition){
                var player=TableMirror.Current.Info(pair.Value);var panel=Game3DManager.Instance.GetPosPanel(pair.Value);
                for(int k=0;k<player.combination_tiles.Count;k++){
                    if(!player.combination_tiles[k].StartsWith("G"))continue;
                    int[] mask=player.combination_masks[k];
                    Check(label+" concealed kong exposes middle only",mask.Where((v,i)=>i%2==0).SequenceEqual(new[]{2,0,0,2}),mask);
                    var group=panel.combination3DObjects[k];
                    var tiles=group==null?System.Array.Empty<Tile3D>():group.Cast<UnityEngine.Transform>().Select(t=>t.GetComponent<Tile3D>()).ToArray();
                    Check(label+" actual 3D concealed kong has four tiles",tiles.Length==4&&tiles.All(t=>t!=null),new{seat=pair.Key,k,count=tiles.Length});
                    if(tiles.Length!=4||tiles.Any(t=>t==null))continue;
                    int[] expectedIds=mask.Where((v,i)=>i%2==1).Reverse().ToArray();
                    bool[] expectedCovered=new[]{true,false,false,true};
                    Check(label+" actual 3D kong retains physical faces",tiles.Select(t=>t.GetTileId()).SequenceEqual(expectedIds),new{seat=pair.Key,k,actual=tiles.Select(t=>t.GetTileId()).ToArray(),expectedIds});
                    Check(label+" actual 3D kong covers only outer tiles",tiles.Select(t=>t.IsConcealedFaceDown).SequenceEqual(expectedCovered),new{seat=pair.Key,k,covered=tiles.Select(t=>t.IsConcealedFaceDown).ToArray()});
                    for(int i=0;i<4;i++){
                        Check(label+" physical kong tile visible "+i,tiles[i].gameObject.activeInHierarchy&&tiles[i].GetComponentsInChildren<UnityEngine.Renderer>().Any(r=>r.enabled));
                        if(!expectedCovered[i])continue;
                        var mesh=(UnityEngine.Transform)typeof(Tile3D).GetField("faceMeshTransform",flags).GetValue(tiles[i]);
                        bool peek=(bool)typeof(Tile3D).GetField("isPeekFaceUp",flags).GetValue(tiles[i]);
                        var rotation=(UnityEngine.Quaternion)typeof(Tile3D).GetField(peek?"faceUpLocalRotation":"faceDownLocalRotation",flags).GetValue(tiles[i]);
                        Check(label+" actual concealed mesh orientation "+i,mesh!=null&&UnityEngine.Quaternion.Angle(mesh.localRotation,rotation)<.1f,new{seat=pair.Key,k,i,peek});
                        Check(label+" known red-rule kong remains peekable "+i,tiles[i].CanPeekOnHover);
                    }
                }
            }
        }
        System.Collections.IEnumerator Run(){
            UnityEngine.Application.logMessageReceived+=Log;Save("running");
            try{
                WindowsManager.Instance.SwitchWindow("game");
                yield return new UnityEngine.WaitForSecondsRealtime(.5f);
                Check("game window selected for rendered fixture checks",(string)typeof(WindowsManager).GetField("currentWindow",flags).GetValue(WindowsManager.Instance)=="game"&&!LoginPanel.Instance.gameObject.activeInHierarchy);
                var fixtures=Newtonsoft.Json.Linq.JArray.Parse(System.IO.File.ReadAllText(root+"runtime-fixtures.json"));
                var raceViews=Newtonsoft.Json.Linq.JArray.Parse(System.IO.File.ReadAllText(root+"runtime-settlement-race-fixtures.json"));
                Check("one saved nonwinner wire view",raceViews.Count==1&&(string)raceViews[0]["probe_case"]=="race-round-9-opponent");
                var gsm=NormalGameStateManager.Instance;
                foreach(var fixture in fixtures.Concat(raceViews)){
                    int round=(int)fixture["round"],fixtureUid=(int)fixture["viewer_user_id"];
                    bool raceView=fixture["probe_case"]!=null;string artifactTag=(string)fixture["probe_case"]??round.ToString();activeCase=(string)fixture["probe_case"]??("round-"+round);
                    gsm.ResetForExit();var start=fixture["start"].ToObject<Response>();Identify(start.game_info,fixtureUid);
                    gsm.InitializeGame(true,"红中真实状态机协议夹具",start.game_info);yield return new UnityEngine.WaitForSecondsRealtime(.8f);yield return Idle();
                    Check("dedicated state round "+round,RuleRegistry.ActiveGameState is HongzhongGameState);
                    foreach(var card in GameCanvas.Instance.HandCardsContainer.GetComponentsInChildren<TileCard>()){
                        int tile=card.tileId;
                        var badge=card.GetComponentsInChildren<TMPro.TMP_Text>(true).FirstOrDefault(t=>t.name=="RuleTileBadge");
                        Check("physical red badge "+round+" "+tile,tile==45?badge!=null&&badge.gameObject.activeSelf&&badge.text=="癞":badge==null||!badge.gameObject.activeSelf);
                    }
                    if(round==1){Texts(GameCanvas.Instance.gameObject);UnityEngine.ScreenCapture.CaptureScreenshot(root+"runtime-red-hand.png");yield return null;}
                    var events=fixture["events"].ToArray();
                    int lastCut=System.Array.FindLastIndex(events,e=>e["do_action_info"]?["action_list"]?.ToObject<string[]>()?.Contains("cut")==true);
                    bool raceRound=round==9||round==13||round==14;
                    for(int eventIndex=0;eventIndex<events.Length;eventIndex++){
                        var item=events[eventIndex];
                        var response=item.ToObject<Response>();if(!RuleRegistry.TryParseGameStateType(response.type,out _,out string suffix))continue;
                        var scoresBefore=TableMirror.Current.IndexToPosition.ToDictionary(p=>p.Key,p=>TableMirror.Current.Info(p.Value).score);
                        // The source timers are tested over live sockets; give this deterministic render sweep enough time.
                        if(response.ask_hand_action_info!=null)response.ask_hand_action_info.remaining_time=3600;
                        if(response.ask_other_action_info!=null)response.ask_other_action_info.remaining_time=3600;
                        RuleRegistry.ActiveGameState.HandleMessage(suffix,response);
                        // These are unmodified real protocol frames. Deliver the final action batch
                        // and result in one frame so the test cannot accidentally drain the race away.
                        bool burstTail=round==9||((round==13||round==14)&&eventIndex>=lastCut);
                        if(!burstTail){yield return null;yield return Idle();}
                        var changes=response.do_action_info?.gang_score_changes;
                        if(changes!=null&&response.do_action_info.silent!=true){
                            foreach(var seat in TableMirror.Current.IndexToPosition){
                                int delta=changes.TryGetValue(seat.Key,out int value)?value:0;
                                int actual=TableMirror.Current.Info(seat.Value).score;
                                Check("immediate kong/refund score "+round+" "+seat.Key,actual==scoresBefore[seat.Key]+delta,new{before=scoresBefore[seat.Key],delta,actual});
                            }
                        }
                    }
                    if(raceRound){Check("terminal result arrives with actual 3D queue pending",Game3DManager.Instance.HasPendingRecordTableAnimations,new{round,lastCut,eventCount=events.Length});raceCases++;}
                    var end=fixture["result"].ToObject<Response>();RuleRegistry.ActiveGameState.HandleMessage("show_result",end);
                    CheckFinalControl("immediate settlement");
                    bool awaitingMatchEnd=(bool)typeof(NormalGameStateManager).GetField("awaitingMatchEnd",flags).GetValue(gsm);
                    Check("match-end lifecycle preserved before queue drain",awaitingMatchEnd==(end.show_result_info.hu_class=="hu_self"&&end.show_result_info.next_status=="match_end"),awaitingMatchEnd);
                    if(raceRound)Check("queued settlement waits before reveal",typeof(HongzhongGameState).GetField("settlementCoroutine",flags).GetValue(HongzhongGameState.Active)!=null);
                    yield return Idle();yield return new UnityEngine.WaitForSecondsRealtime(end.show_result_info.hu_class=="liuju"?.6f:5.5f);yield return Idle();
                    foreach(var pair in TableMirror.Current.IndexToPosition){
                        var p=TableMirror.Current.Info(pair.Value);var expected=fixture["expected"]["players"].First(t=>(int)t["seat"]==pair.Key);
                        Check("final score "+round+" "+pair.Key,p.score==(int)expected["score"],new{p.score,expected=expected["score"]});
                        Check("net history "+round+" "+pair.Key,p.score_history.SequenceEqual(expected["history"].ToObject<string[]>()),new{actual=p.score_history,expected=expected["history"]});
                        var panel=Game3DManager.Instance.GetPosPanel(pair.Value);
                        var physical=panel.cardsPosition.Cast<UnityEngine.Transform>().Select(t=>t.GetComponent<Tile3D>().GetTileId()).OrderBy(x=>x).ToArray();
                        Check("all hands revealed "+round+" "+pair.Key,physical.SequenceEqual(expected["hand"].ToObject<int[]>().OrderBy(x=>x)),new{physical,expected=expected["hand"]});
                    }
                    CheckConcealedKongs("terminal "+artifactTag);CheckFinalControl("terminal "+artifactTag);
                    var panelObject=end.show_result_info.hu_class=="liuju"?EndLiujuPanel.Instance.gameObject:EndResultPanel.Instance.gameObject;
                    Check("settlement visible "+round,panelObject.activeInHierarchy);
                    CheckHongzhongSettlementLayout(panelObject);
                    var labels=Texts(panelObject);System.IO.File.WriteAllText(root+"settlement-"+artifactTag+"-texts.json",Newtonsoft.Json.JsonConvert.SerializeObject(labels,Newtonsoft.Json.Formatting.Indented));
                    if(new[]{1,6,7,9,10,11,12,14,15,16}.Contains(round)){UnityEngine.ScreenCapture.CaptureScreenshot(root+"settlement-"+artifactTag+".png");yield return null;}
                    var reconnect=fixture["reconnect"].ToObject<Response>();Identify(reconnect.game_info,fixtureUid);
                    gsm.InitializeGame(true,"红中终局重连",reconnect.game_info);yield return new UnityEngine.WaitForSecondsRealtime(.2f);yield return Idle();
                    RuleRegistry.ActiveGameState.HandleMessage("show_result",end);yield return new UnityEngine.WaitForSecondsRealtime(.1f);
                    // A repeated terminal snapshot must never duplicate the net row.
                    RuleRegistry.ActiveGameState.HandleMessage("show_result",end);yield return new UnityEngine.WaitForSecondsRealtime(.1f);
                    foreach(var pair in TableMirror.Current.IndexToPosition){
                        var expected=fixture["expected"]["players"].First(t=>(int)t["seat"]==pair.Key);var p=TableMirror.Current.Info(pair.Value);
                        Check("reconnect and repeated settlement "+round+" "+pair.Key,p.score==(int)expected["score"]&&p.score_history.SequenceEqual(expected["history"].ToObject<string[]>()),new{p.score,history=p.score_history});
                    }
                    Check("no duplicate fan snapshot "+round,TableMirror.Current.RoundSettlementHistory.Count<=round);
                    if(end.show_result_info.hu_class!="liuju"){
                        var snapshot=TableMirror.Current.RoundSettlementHistory.LastOrDefault(s=>s!=null);
                        Check("winner tooltip snapshot exists "+round,snapshot!=null);
                        if(snapshot!=null){
                            var winner=TableMirror.Current.Info(TableMirror.Current.IndexToPosition[snapshot.hepaiPlayerIndex]);
                            int expectedDelta=end.show_result_info.hongzhong_info.round_score_changes[winner.original_player_index];
                            Check("winner tooltip uses whole-hand net "+round,snapshot.winnerScoreDelta==expectedDelta,new{snapshot.winnerScoreDelta,expectedDelta});
                            if(new[]{9,10,12}.Contains(round)){
                                var tooltip=ScoreHistoryFanTooltip.Instance??UnityEngine.Object.FindFirstObjectByType<ScoreHistoryFanTooltip>(UnityEngine.FindObjectsInactive.Include);
                                Check("actual score history tooltip present "+round,tooltip!=null);
                                if(tooltip!=null){
                                    tooltip.Show(snapshot,"hongzhong/mil2024");yield return null;
                                    var summary=(TMPro.TMP_Text)typeof(ScoreHistoryFanTooltip).GetField("scoreSummaryText",flags).GetValue(tooltip);
                                    Check("rendered tooltip includes whole-hand net "+round,summary!=null&&summary.text.Contains("+"+expectedDelta+"分"),summary?.text);
                                    Texts(tooltip.gameObject);UnityEngine.ScreenCapture.CaptureScreenshot(root+"runtime-tooltip-"+artifactTag+".png");yield return null;tooltip.Hide();
                                }
                            }
                        }
                    }
                    if(raceView)finishedRaceViews++;else finishedRounds++;Save("running");
                }
                Check("all sixteen normal fixtures retained",finishedRounds==16,finishedRounds);
                Check("all four pending-queue race cases executed",raceCases==4&&finishedRaceViews==1,new{raceCases,finishedRaceViews});
                var sound=SoundManager.Instance;var source=(UnityEngine.AudioSource)typeof(SoundManager).GetField("audioSource",flags).GetValue(sound);
                int oldVolume=ConfigManager.Instance.VoiceVolume;var volume=typeof(ConfigManager).GetProperty("VoiceVolume");volume.SetValue(ConfigManager.Instance,100);
                try{
                    foreach(int voice in new[]{1,2})foreach(string word in new[]{"peng","gang","angang","jiagang","hu_self"}){
                        sound.PlayActionSound("self",word,voice);float peak=0;var samples=new float[1024];double until=UnityEngine.Time.realtimeSinceStartupAsDouble+.8;
                        while(UnityEngine.Time.realtimeSinceStartupAsDouble<until){source.GetOutputData(samples,0);foreach(float sample in samples)peak=UnityEngine.Mathf.Max(peak,UnityEngine.Mathf.Abs(sample));yield return null;}
                        Check("audible action "+voice+" "+word,peak>0.00001f,peak);
                    }
                }finally{volume.SetValue(ConfigManager.Instance,oldVolume);}
                Check("no runtime exceptions",errors.Count==0,errors);completed=true;
            }finally{UnityEngine.Application.logMessageReceived-=Log;Save(completed&&failures==0?"completed":"failed");}
        }
        yield return Run();
    }

    // Generated from check-unity-replay.cs; SHA-256 7c1fcdf8d24fbaa1e5209fc0f7a5ab3e9ad165c736f6e9424999c3c730a65518.
    private IEnumerator RunReplay(string folder) {
        var root=Path.GetFullPath(folder)+Path.DirectorySeparatorChar;
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
        var failures=new System.Collections.Generic.List<object>();
        int checkedNodes=0,checkedRewinds=0,checkedDrawSlots=0,checkedDrawIdentities=0;
        void Save(string status)=>System.IO.File.WriteAllText(root+"unity-replay-results.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{status,checkedNodes,checkedRewinds,checkedDrawSlots,checkedDrawIdentities,failures},Newtonsoft.Json.Formatting.Indented));
        void Check(bool condition,string label,object detail){if(!condition)failures.Add(new{label,detail});}
        void CheckDrawSlot(GameRecordManager.RecordPlayer p,Newtonsoft.Json.Linq.JToken target,string stem,int round,int node,string traversal){
            bool expectedDraw=(bool)target["has_draw_slot"];
            Check(p.showHandDrawSlotActive==expectedDraw,"draw slot flag",new{stem,round,node,traversal,p.playerIndex,actual=p.showHandDrawSlotActive,expected=expectedDraw});
            checkedDrawSlots++;
            if(expectedDraw){
                int? actualDraw=p.tileList.Count>0?(int?)p.tileList.Last():null;
                int? expectedTile=(int?)target["last_drawn_tile"];
                Check(actualDraw==expectedTile,"draw slot physical tile",new{stem,round,node,traversal,p.playerIndex,actual=actualDraw,expected=expectedTile});
                checkedDrawIdentities++;
            }
        }
        System.Collections.IEnumerator Idle(){
            double deadline=UnityEngine.Time.realtimeSinceStartupAsDouble+15;
            while(GameCanvas.Instance.IsChangeHandCardProcessing){
                if(UnityEngine.Time.realtimeSinceStartupAsDouble>deadline)throw new System.Exception("Replay hand queue stalled");
                yield return null;
            }
        }
        System.Collections.IEnumerator Run(){
            Save("running");
            NormalGameStateManager.Instance.ResetForExit();
            foreach(string stem in new[]{"fixtures","normal"}){
                string detailPath=root+"network/"+stem+"-record-detail.json",oraclePath=root+"network/"+stem+"-replay-oracle.json";
                if(!System.IO.File.Exists(detailPath)||!System.IO.File.Exists(oraclePath))throw new System.Exception("Missing recorded match: "+stem);
                var detail=Newtonsoft.Json.JsonConvert.DeserializeObject<RecordDetail>(System.IO.File.ReadAllText(detailPath));
                var oracle=Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(oraclePath));
                RecordPanel.OpenRecord(detail,true);yield return new UnityEngine.WaitForSecondsRealtime(.7f);yield return Idle();
                var manager=GameRecordManager.Instance;
                var wallField=typeof(GameRecordManager).GetField("currentTilesList",flags);
                var remainMethod=typeof(GameRecordManager).GetMethod("GetRecordRemainTiles",flags);
                foreach(var hand in oracle["rounds"]){
                    int round=(int)hand["round"];
                    manager.GotoSelectRound(round,false);yield return null;yield return Idle();
                    foreach(var expected in hand["nodes"]){
                        int node=(int)expected["node"];
                        manager.GotoAction(node);yield return null;yield return Idle();
                        var wall=(System.Collections.Generic.List<int>)wallField.GetValue(manager);
                        Check(wall.SequenceEqual(expected["wall"].ToObject<int[]>()),"exact wall",new{stem,round,node,wall,expected=expected["wall"]});
                        Check((int)remainMethod.Invoke(manager,null)==(int)expected["wall_count"],"remaining count",new{stem,round,node,actual=remainMethod.Invoke(manager,null),expected=expected["wall_count"]});
                        foreach(var p in manager.recordPlayer_to_info.Values){
                            var target=expected["players"][p.playerIndex];
                            Check(p.score==(int)expected["scores"][p.playerIndex],"score",new{stem,round,node,p.playerIndex,p.score,expected=expected["scores"]});
                            Check(p.tileList.OrderBy(x=>x).SequenceEqual(target["hand"].ToObject<int[]>()),"physical hand",new{stem,round,node,p.playerIndex,actual=p.tileList,expected=target["hand"]});
                            CheckDrawSlot(p,target,stem,round,node,"forward");
                            Check(p.discardTiles.SequenceEqual(target["river"].ToObject<int[]>()),"river",new{stem,round,node,p.playerIndex,actual=p.discardTiles,expected=target["river"]});
                            Check(p.combinationTiles.SequenceEqual(target["melds"].ToObject<string[]>()),"melds",new{stem,round,node,p.playerIndex,actual=p.combinationTiles,expected=target["melds"]});
                        }
                        Check((manager.HongzhongRecordInfo?.bird_tiles??System.Array.Empty<int>()).SequenceEqual(expected["bird_tiles"].ToObject<int[]>()),"bird reveal",new{stem,round,node});
                        checkedNodes++;if(checkedNodes%20==0)Save("running");
                        if(failures.Count>0){Save("failed");yield break;}
                    }
                    int end=manager.gameRecord.gameRound.rounds[round].actionTicks.Count;
                    foreach(int node in new[]{0,end/2,end,0,end}){
                        manager.GotoAction(node);yield return null;yield return Idle();
                        var expected=hand["nodes"][node];
                        Check(((System.Collections.Generic.List<int>)wallField.GetValue(manager)).SequenceEqual(expected["wall"].ToObject<int[]>()),"rewind wall",new{stem,round,node});
                        foreach(var p in manager.recordPlayer_to_info.Values){
                            Check(p.score==(int)expected["scores"][p.playerIndex],"rewind score",new{stem,round,node,p.playerIndex,p.score});
                            CheckDrawSlot(p,expected["players"][p.playerIndex],stem,round,node,"rewind");
                        }
                        checkedRewinds++;
                    }
                    if(round==1||round==9||round==12||round==16){
                        UnityEngine.ScreenCapture.CaptureScreenshot(root+"replay-"+stem+"-"+round+".png");
                        yield return null;
                    }
                }
                var rows=ScoreHistoryRecordSettlementExtractor.ExtractScoreRows(manager.gameRecord);
                Check(rows.Count==oracle["rounds"].Count(),"score history row count",new{stem,actual=rows.Count,expected=oracle["rounds"].Count()});
                for(int original=0;original<4;original++){
                    int net=rows.Sum(row=>row.scoreChangesByOriginal[original]);
                    Check(net==(int)oracle["final_original_scores"][original],"score history net",new{stem,original,actual=net,expected=oracle["final_original_scores"]});
                }
            }
            Save(failures.Count==0?"completed":"failed");
        }
        System.Collections.IEnumerator Safe(){
            var pending=new System.Collections.Generic.Stack<System.Collections.IEnumerator>();pending.Push(Run());
            while(pending.Count>0){
                bool next=false;object value=null;
                try{var iterator=pending.Peek();next=iterator.MoveNext();if(next)value=iterator.Current;else pending.Pop();}
                catch(System.Exception error){
                    failures.Add(new{label="unhandled coroutine exception",error=error.ToString(),stack=error.StackTrace});
                    Save("failed");yield break;
                }
                if(!next)continue;
                // Advance nested Idle enumerators here so their failures cannot bypass this report.
                if(value is System.Collections.IEnumerator nested&&!(value is UnityEngine.CustomYieldInstruction))pending.Push(nested);
                else yield return value;
            }
        }
        yield return Safe();
    }
}
#endif
