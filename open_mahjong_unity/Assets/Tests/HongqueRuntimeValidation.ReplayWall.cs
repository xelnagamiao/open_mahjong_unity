#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed partial class HongqueRuntimeValidation {
    IEnumerator RunReplayWallPreview(GameRecordManager record, JObject fixture) {
        Application.logMessageReceived += Log;
        currentCase = "replay-wall";
        List<int> Wall() => (List<int>)typeof(GameRecordManager).GetField("currentTilesList", Hidden).GetValue(record);
        List<int> Slots() => (List<int>)typeof(GameRecordManager).GetField("currentOriginalIndices", Hidden).GetValue(record);
        List<int> Forecast() {
            var indices = new List<int>();
            RecordChongHintCalculator.ComputeMoqieZimoDrawOriginalIndices(record, indices);
            return indices;
        }
        string Snapshot() => JsonConvert.SerializeObject(new { wall=Wall(), slots=Slots(), forecast=Forecast(),
            reserved=record.RecordReservedWallIndices().OrderBy(i=>i).ToArray() });
        var view=(GameObject)typeof(GameRecordManager).GetField("tileListView",Hidden).GetValue(record);
        record.GotoAction(0); yield return Idle();
        if (!view.activeSelf) typeof(GameRecordManager).GetMethod("ShowTileList",Hidden).Invoke(record,null);
        yield return Idle();
        Check(record.RecordReservedWallIndices().Count==18,"sanma wall displays eight replacements and ten indicators");
        var section=(Transform)typeof(GameRecordManager).GetField("last14TilesContainer",Hidden).GetValue(record);
        Check(section.GetComponentsInChildren<StaticCard>().Length==18,"actual wall UI contains all eighteen special slots");
        Check(Forecast().SequenceEqual(new[]{0,3,6,9,12,15}),"opening prediction follows three seats");
        var removed=new List<int>();
        int beforeWin=(int)fixture["beforeWin"];
        var round=record.gameRecord.gameRound.rounds[record.currentRoundIndex];
        for (int node=0;node<beforeWin;node++) {
            record.GotoAction(node); yield return Idle();
            int[] previous=Slots().ToArray();
            record.AdvanceToNextAction(); yield return Idle();
            int reached=record.currentNode;
            string sequential=Snapshot();
            Check(reached>node,"sequential action advances "+node);
            string action=round.actionTicks[node][0];
            if(action=="nd" || action=="gd") removed.AddRange(previous.Except(Slots()));
            record.GotoAction(reached); yield return Idle();
            Check(Snapshot()==sequential,"wall and prediction agree after seek "+node+" "+action);
            Check(Slots().Select(i=>round.tilesList[i]).SequenceEqual(Wall()),"physical wall slots match tiles "+node);
        }
        record.GotoAction(beforeWin); yield return Idle();
        Check(removed.SequenceEqual(fixture["replacementSlots"].ToObject<int[]>()),"all eight replacements follow server physical slots",removed);
        Check(Wall().SequenceEqual(fixture["remainingBeforeWin"].ToObject<int[]>()),"remaining wall agrees with completed server game");
        ScreenCapture.CaptureScreenshot(Path.Combine(folder,"wall-after-eight.png"));
        yield return new WaitForEndOfFrame(); yield return null;
        record.AdvanceToNextAction(); yield return Idle();
        float until=Time.realtimeSinceStartup+20;
        while(!EndResultPanel.Instance.gameObject.activeInHierarchy && Time.realtimeSinceStartup<until) yield return null;
        Check(EndResultPanel.Instance.gameObject.activeInHierarchy,"eight replacements replay reaches actual winning panel");
        typeof(GameRecordManager).GetMethod("ClearRecordRoundEndPanels",Hidden).Invoke(record,null);
        record.GotoAction((int?)fixture["finalNode"]??0); yield return Idle();
        record.FocusTileListScrollOnWallSection();
        yield return new WaitForSecondsRealtime(1);
        ScreenCapture.CaptureScreenshot(Path.Combine(folder,"preview-wall.png"));
        yield return new WaitForEndOfFrame(); yield return null;
        Application.logMessageReceived -= Log;
    }
}
#endif
