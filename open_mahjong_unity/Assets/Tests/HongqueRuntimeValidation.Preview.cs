#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed partial class HongqueRuntimeValidation {
    /// <summary>Load a server-generated replay fixture for interactive and visual regression.</summary>
    IEnumerator RunPreview() {
        Time.timeScale = 1;
        yield return null;
        var fixture = JObject.Parse(File.ReadAllText(Path.Combine(folder, "preview.json")));
        var players = fixture["players"].ToObject<PlayerRecordInfo[]>();
        int seat = (int?)fixture["seat"] ?? 1;
        int round = (int?)fixture["round"] ?? 1;
        int beforeWin = (int)fixture["beforeWin"];
        UserDataManager.Instance.SetUserInfo("虹雀规则预览", "fixture-only", players[seat].user_id, false);
        NormalGameStateManager.Instance.ResetForExit();
        GameHost.Current.SwitchWindow("game");
        var record = GameRecordManager.Instance;
        record.ResetForSessionEnd();
        record.LoadRecord(fixture["record"].ToString(), players);
        GameHost.Current.SwitchWindow("recordscene");
        record.SwitchRecordPerspectiveToOriginalPlayerIndex(seat);
        record.JumpToSharePosition(round, beforeWin);
        yield return Idle();
        yield return new WaitForSecondsRealtime(1);
        ScreenCapture.CaptureScreenshot(Path.Combine(folder, "preview-table.png"));
        // CaptureScreenshot renders at the end of the frame; advance only
        // after that frame, so the table capture cannot include the next action.
        yield return new WaitForEndOfFrame();
        yield return null;
        record.AdvanceToNextAction();
        float until = Time.realtimeSinceStartup + 20;
        while (!EndResultPanel.Instance.gameObject.activeInHierarchy && Time.realtimeSinceStartup < until)
            yield return null;
        Check(EndResultPanel.Instance.gameObject.activeInHierarchy, "preview reaches winning panel");
        yield return new WaitForSecondsRealtime(1);
        ScreenCapture.CaptureScreenshot(Path.Combine(folder, "preview-result.png"));
        yield return new WaitForSecondsRealtime(1);
        record.GotoAction(beforeWin);
        yield return Idle();
    }
}
#endif
