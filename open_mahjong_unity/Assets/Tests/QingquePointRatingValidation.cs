#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Opt-in checks of saved matching controls and the real end-panel path.</summary>
public static class QingquePointRatingValidation {
    static T Field<T>(object owner,string name) => (T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);
    static T Find<T>() where T:Component => Resources.FindObjectsOfTypeAll<T>().First(c=>c.gameObject.scene.IsValid()&&!c.GetComponentsInParent<Transform>(true).Any(t=>t.name=="FeedbackExamples"));
    static void Click(UnityEngine.UI.Button button) {
        if(!button||!button.gameObject.activeInHierarchy||!button.IsInteractable())throw new InvalidOperationException("Button is not available for clicking");
        Canvas.ForceUpdateCanvases();var rect=(RectTransform)button.transform;
        var e=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center)),button=PointerEventData.InputButton.Left};
        var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(e,hits);
        if(hits.Count==0||ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject)!=button.gameObject)throw new InvalidOperationException("Button is blocked: "+button.name);
        ExecuteEvents.Execute(button.gameObject,e,ExecuteEvents.pointerClickHandler);
    }
    public static IEnumerator Run(string outputDirectory) {
        if(!Application.isPlaying)throw new InvalidOperationException("Play Mode required");
        Directory.CreateDirectory(outputDirectory);
        var checks=new List<string>();
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);checks.Add(message);}
        WindowsManager.Instance.SwitchWindow("menu");
        yield return new WaitForSecondsRealtime(.5f);
        Find<Salasasa.Home.FourSideHeader>().ButtonFor("ranked").button.onClick.Invoke();
        var lobby=Find<MatchLobbyView>();
        lobby.SelectRule(Array.IndexOf(RankedRules.Ids,"qingque"));
        yield return new WaitForSecondsRealtime(.3f);
        var entries=Field<MatchButton[]>(lobby,"entries").Where(c=>c.RuleId=="qingque").ToArray();
        Check(entries.Length==2&&entries.All(c=>c.gameObject.activeInHierarchy&&c.IsAvailable),"Both baked Qingque cards are visible and available");
        Check(entries.Select(c=>c.QueueType).OrderBy(s=>s).SequenceEqual(new[]{"qingque_elo_quanzhuang","qingque_elo_unlimited"}),"Card identifiers match server queues");
        Canvas.ForceUpdateCanvases();
        Check(entries.SelectMany(c=>c.GetComponentsInChildren<TMP_Text>()).All(t=>!t.isTextOverflowing),"Qingque card labels fit their baked layout");
        ScreenCapture.CaptureScreenshot(Path.Combine(outputDirectory,"matching.png"));
        yield return new WaitForSecondsRealtime(.3f);
        var help=Field<GameObject>(lobby,"eloHelp").GetComponent<RankedEloHelp>();
        var policy=Field<RankPolicyPanel>(help,"policyPanel");
        Check(entries.All(card=>Field<RankPolicyPanel>(card,"policyPanel")==policy)&&!policy.gameObject.activeInHierarchy,"Header and both Qingque cards share the closed production policy panel");
        Click(help.GetComponent<UnityEngine.UI.Button>());
        yield return null;
        var description=Field<GameObject>(policy,"eloContent").GetComponent<TMP_Text>();
        Canvas.ForceUpdateCanvases();description.ForceMeshUpdate();
        Check(policy.gameObject.activeInHierarchy&&description.gameObject.activeSelf&&!Field<GameObject>(policy,"policyContent").activeSelf&&description.text==PlatformText.Get(RankedRules.QingqueEloDescription)&&!description.isTextOverflowing,"Header click shows the current Qingque explanation within its authored layout");
        var visibleText=new string(description.text.Replace("<b>","").Replace("</b>","").Where(c=>!char.IsControl(c)).ToArray());
        Check(description.font.HasCharacters(visibleText,out uint[] missing,true,false),"All Qingque explanation characters exist in the current font");
        var popupCanvas=policy.GetComponentInParent<Canvas>();
        Check(popupCanvas&&popupCanvas.rootCanvas.name=="OverlayCanvas","Elo explanation uses the production overlay canvas");
        ScreenCapture.CaptureScreenshot(Path.Combine(outputDirectory,"formula.png"));
        yield return new WaitForSecondsRealtime(.3f);
        Click(Field<UnityEngine.UI.Button>(policy,"closeButton"));
        yield return null;
        Check(!policy.gameObject.activeInHierarchy,"Close button dismisses the header explanation");
        foreach(var card in entries){
            Click(Field<UnityEngine.UI.Button>(card,"infoButton"));
            yield return null;
            Check(policy.gameObject.activeInHierarchy&&Field<GameObject>(policy,"eloContent").activeSelf&&description.text==PlatformText.Get(RankedRules.QingqueEloDescription),"Card click preserves Qingque points and stop-after-hand explanation: "+card.QueueType);
            Click(Field<UnityEngine.UI.Button>(policy,"closeButton"));
            yield return null;
        }
        GameHost.Current.SwitchWindow("game");
        yield return new WaitForSecondsRealtime(.3f);
        GameSceneUIManager.Instance.ClearTemporaryPanels();
        var menu=ExitButtonManager.Instance;
        menu.HideAll();
        var state=new UnlimitedRoundInfo{enabled=true,withdrawn_players=new List<UnlimitedRoundPlayer>()};
        var panel=UnlimitedRoundPanel.Instance;
        panel.Initialize(new GameInfo{room_rule="qingque",room_type="match",unlimited_rounds=true,unlimited_info=state},true);
        var button=Field<Button>(menu,"unlimitedEndButton");
        Check(button.gameObject.activeInHierarchy&&button.interactable,"Ranked unlimited games expose stop-after-hand control");
        state.finishing=true;state.revision++;
        panel.ApplyState(state);
        Check(!button.interactable&&Field<GameObject>(panel,"noticeRoot").activeSelf,"Frozen settlement disables return and displays status after disconnect");
        panel.ResetState();
        var end=EndGamePanel.Instance;
        var rows=new Dictionary<string,Dictionary<string,object>>();
        rows["0"]=new Dictionary<string,object>{["user_id"]=PlayerSession.Current.UserId,["username"]=PlayerSession.Current.Username??"测试玩家",
            ["rank"]=1,["score"]=1600,["pt"]=0,["rank_before"]="",["rank_after"]="",["score_before"]=0,["score_after"]=0,
            ["rating_rule"]="qingque",["rating_system"]="elo",["elo_before"]=1500,["elo_after"]=1532,["elo_delta"]=32,["rating_pt"]=0,["rating_games"]=1,
            ["rating_completed_hands"]=16,["rating_net_points"]=1600,["rating_average_points"]=100,["rating_base_delta"]=32,["rating_protection_bonus"]=0};
        rows=JsonConvert.DeserializeObject<Dictionary<string,Dictionary<string,object>>>(JsonConvert.SerializeObject(rows));
        end.ShowGameEndPanel("","","",rows);
        Check(Field<EndGamePanel.RankDisplay[]>(end,"rankDisplays")[0].pt.text=="+32.00 Elo","Final ranking rows show Elo instead of zero PT");
        Field<Button>(end,"goHomeButton").onClick.Invoke();
        yield return new WaitForSecondsRealtime(.3f);
        var rating=RankChangePanel.Instance;
        var details=Field<TMP_Text>(rating,"ratingDetailsText");
        Canvas.ForceUpdateCanvases();details.ForceMeshUpdate();
        Check(rating.gameObject.activeInHierarchy&&Field<TMP_Text>(rating,"ptChangeText").text.Contains("+32"),"Normal settlement displays uncapped Elo change");
        Check(details.gameObject.activeSelf&&details.text.Contains("1600")&&details.text.Contains("16局")&&details.text.Contains("+100.00")&&!details.isTextOverflowing,"Settlement displays net points, actual hands, mean, performance and protection");
        Check(PlayerInfoStatsFormatter.ModeCaption("qingque","unlimited/4_rank",true).Contains("无固定时限"),"Statistics identify unlimited matches separately");
        ScreenCapture.CaptureScreenshot(Path.Combine(outputDirectory,"settlement.png"));
        yield return new WaitForSecondsRealtime(.3f);
        File.WriteAllText(Path.Combine(outputDirectory,"point-rating-validation.json"),JsonConvert.SerializeObject(new{passed=checks.Count,checks,Screen.width,Screen.height},Formatting.Indented));
        Debug.Log("Qingque point rating validation passed: "+checks.Count);
    }
}
#endif
