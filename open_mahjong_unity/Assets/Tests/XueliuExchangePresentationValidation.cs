#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class XueliuExchangePresentationValidation {
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Fields).GetValue(owner);
    private static RectTransform Root() => UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None)
        .FirstOrDefault(t=>t.name=="XueliuExchange");
    private static Image Light(int seat) => Field<Image>(BoardCanvas.Instance,
        "player_"+NormalGameStateManager.Instance.indexToPosition[seat]+"_current_image");

    private static Vector2 ProjectedBoardCenter(RectTransform root) {
        Vector3 world = (Light(0).transform.position + Light(1).transform.position
            + Light(2).transform.position + Light(3).transform.position) * .25f;
        Canvas board = BoardCanvas.Instance.GetComponent<Canvas>();
        Canvas overlay = root.GetComponentInParent<Canvas>();
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(board.worldCamera, world);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen,
            overlay.renderMode == RenderMode.ScreenSpaceOverlay ? null : overlay.worldCamera, out Vector2 local);
        return local;
    }

    public static IEnumerator Run(Action<string> completed, string screenshots) {
        if (!Application.isPlaying || !string.IsNullOrEmpty(UserDataManager.Instance.GamestateId))
            throw new InvalidOperationException("需要空闲 Play Mode");
        int checks=0;
        void Check(bool ok,string message) { if (!ok) throw new Exception(message); checks++; }
        var canvas=GameCanvas.Instance;
        Check(!string.IsNullOrEmpty(SichuanDoublesValidation.ShowAndValidateRoom()),"血战房间布局");
        Check(!string.IsNullOrEmpty(SichuanDoublesValidation.ShowAndValidateRoom("sichuan/xueliu_doubles")),"血流房间布局");
        yield return new WaitForSecondsRealtime(.5f);
        UnityEngine.ScreenCapture.CaptureScreenshot(screenshots+"/room.png");
        yield return null;
        int tick=100;
        try {
            for (int round=1;round<=4;round++) for (int self=0;self<4;self++) {
                var fixture=SichuanPresentationValidation.Fixture("sichuan/xueliu_doubles",self,round);
                NormalGameStateManager.Instance.InitializeGame(true,"local exchange validation",fixture);
                while(canvas.IsChangeHandCardProcessing) yield return null;
                foreach (var panel in UnityEngine.Object.FindObjectsByType<GamePlayerPanel>(FindObjectsSortMode.None)) {
                    if (panel.SichuanTeamId < 0) continue;
                    Color color = Field<Image>(panel,"sichuanTeamBackground").color;
                    Check(panel.SichuanTeamId == 0 ? color.b > color.r * 3 : color.r > color.g * 3,
                        "队伍底色必须为蓝/红，不能是橙色");
                }
                int first=round-1;
                int[][] groups={new[]{first,(first+1)%4},new[]{(first+2)%4,(first+3)%4}};
                for (int stage=1;stage<=2;stage++) {
                    bool active=groups[stage-1].Contains(self);
                    var phase=new XueliuExchangeInfo{stage=stage,active_players=groups[stage-1],
                        teammate_suit=stage==2 && active ? 1 : 0, selection_done=stage==2 && !active};
                    var info=new AskHandActionGBInfo{action_tick=++tick,player_index=self,remaining_time=10,
                        action_list=active ? new[]{"xueliu_exchange_three"} : Array.Empty<string>(),xueliu_exchange=phase,remain_tiles=55};
                    void Deliver() => SichuanGameState.Active.HandleMessage("broadcast_hand_action",new Response{ask_hand_action_info=info});
                    Deliver();
                    for(int seat=0;seat<4;seat++) Check(Light(seat).gameObject.activeSelf==groups[stage-1].Contains(seat),"闪烁组或视角映射错误");
                    float alpha=Light(groups[stage-1][0]).color.a;
                    yield return new WaitForSecondsRealtime(.24f);
                    Check(Mathf.Abs(alpha-Light(groups[stage-1][0]).color.a)>.01f,"两家未持续闪烁");
                    Check(Mathf.Approximately(Light(groups[stage-1][0]).color.a,Light(groups[stage-1][1]).color.a),"两家闪烁不同步");
                    TMP_Text tip=Field<TMP_Text>(canvas,"dingqueTipText");
                    Check(!tip.text.Contains("先手") && !tip.text.Contains("与对家互换"),"多余选牌文本仍在");
                    if(active) {
                        phase.selection_done=true;
                        info.action_list=Array.Empty<string>();
                        Deliver();
                        Check(Light(groups[stage-1][0]).gameObject.activeSelf && Light(groups[stage-1][1]).gameObject.activeSelf,"一人提交后提前灭灯");
                        Check(!Field<Button>(canvas,"dingqueWanButton").gameObject.activeInHierarchy,"已提交仍能再次提交");
                    }
                    if(round==1 && self==2 && stage==2) {
                        UnityEngine.ScreenCapture.CaptureScreenshot(screenshots+"/second-pair.png");
                        yield return null;
                    }
                    phase.animating=true;
                    phase.animation_seconds=1.4f;
                    phase.outgoing_suit=active ? 2 : 0;
                    phase.teammate_suit=active ? 0 : 1;
                    info.action_list=Array.Empty<string>();
                    // 从汇合停留阶段开始，确定性检查坐标，避免截图/低帧率越过中间帧。
                    phase.animation_seconds=.9f;
                    info.action_tick=++tick;
                    Deliver();
                    RectTransform root=Root();
                    Vector2 firstCenter=root.GetChild(0).GetComponent<RectTransform>().anchoredPosition;
                    Vector2 secondCenter=root.GetChild(1).GetComponent<RectTransform>().anchoredPosition;
                    Check(Vector2.Distance((firstCenter+secondCenter)*.5f,ProjectedBoardCenter(root))<3f,
                        $"飞牌汇合点偏离了实际中心盘 round={round}, self={self}, stage={stage}");
                    phase.animation_seconds=1.4f;
                    info.action_tick=++tick;
                    Deliver();
                    root=Root();
                    Check(root!=null && root.childCount==2,"必须只播放当前两人的换牌");
                    Check(root.GetComponentsInChildren<Image>().Length==6,"换牌动画并非两组三张");
                    Check(root.GetComponentsInChildren<TMP_Text>().Length==3,"本队花色未显示或泄露对手花色");
                    Check(root.GetComponentsInChildren<Graphic>().All(g=>!g.Raycast(
                        RectTransformUtility.WorldToScreenPoint(null,g.rectTransform.position),null)),"动画拦截了输入");
                    Check(!Field<GameObject>(canvas,"dingqueSelectionPanel").activeSelf,"动画中仍可选牌");
                    for(int seat=0;seat<4;seat++) Check(!Light(seat).gameObject.activeSelf,"动画期间提前亮起下一组");
                    Deliver();
                    Check(Root()==root,"同一动画消息重复播放");
                    yield return new WaitForSecondsRealtime(.42f);
                    Vector2 middle=root.GetChild(0).GetComponent<RectTransform>().anchoredPosition;
                    if(round==1 && self==2 && stage==1) UnityEngine.ScreenCapture.CaptureScreenshot(screenshots+"/exchange.png");
                    yield return new WaitForSecondsRealtime(.95f);
                    Check(!canvas.IsXueliuExchangeAnimating,"换牌动画未结束");
                    Check(Vector2.Distance(middle,root.GetChild(0).GetComponent<RectTransform>().anchoredPosition)>40,"换牌没有从中心移动到对家");
                }
            }
            // 动画中重连及离开，旧组不能继续闪烁或残留卡片。
            var reconnect=new AskHandActionGBInfo{action_tick=++tick,player_index=3,action_list=Array.Empty<string>(),
                xueliu_exchange=new XueliuExchangeInfo{stage=1,active_players=new[]{0,1},animating=true,animation_seconds=.3f,teammate_suit=1}};
            canvas.ShowXueliuExchangePresentation(reconnect);
            yield return new WaitForSecondsRealtime(.4f);
            Check(!canvas.IsXueliuExchangeAnimating,"重连重播了完整动画");
            canvas.ClearXueliuExchangePresentation();
            Check(Root()==null,"结束残留换牌卡片");
            reconnect.action_tick=++tick;
            reconnect.xueliu_exchange.animation_seconds=1.4f;
            canvas.ShowXueliuExchangePresentation(reconnect);
            canvas.ShowXueliuTeamExchange(reconnect);
            var next=new AskHandActionGBInfo{action_tick=++tick,player_index=3,remaining_time=10,
                action_list=new[]{"xueliu_exchange_three"},xueliu_exchange=new XueliuExchangeInfo{
                    stage=2,active_players=new[]{2,3},teammate_suit=1}};
            Check(!canvas.ShowXueliuExchangePresentation(next),"动画未完成就开放下一组");
            Check(!Field<GameObject>(canvas,"dingqueSelectionPanel").activeSelf,"延迟网络期间提前出现选牌");
            yield return new WaitForSecondsRealtime(1.4f);
            Check(Field<GameObject>(canvas,"dingqueSelectionPanel").activeSelf,"动画结束后未恢复选牌");
            Check(Light(2).gameObject.activeSelf && Light(3).gameObject.activeSelf,"动画后未切到后一组");
            Check(Root().GetComponentsInChildren<TMP_Text>().Length==3,"后手缺少持久花色牌");
            // 等待第二组选牌时，收到的花色牌仍随中心盘移动，不能固定在旧屏幕坐标。
            RectTransform incoming=Root().GetChild(0).GetComponent<RectTransform>();
            Vector2 previous=incoming.anchoredPosition;
            Vector3 oldBoardPosition=BoardCanvas.Instance.transform.position;
            try {
                BoardCanvas.Instance.transform.position+=new Vector3(12,0,0);
                yield return null;
                Canvas.ForceUpdateCanvases();
                Check(incoming.anchoredPosition.x>previous.x+10,"等待中的换入牌没有跟随中心盘位置");
            } finally { BoardCanvas.Instance.transform.position=oldBoardPosition; }
            canvas.ClearXueliuExchangePresentation();
            reconnect.action_tick=++tick;
            canvas.ShowXueliuExchangePresentation(reconnect);
            canvas.gameObject.SetActive(false);
            Check(Root()==null && !canvas.IsXueliuExchangeAnimating,"关闭界面残留动画");
            canvas.gameObject.SetActive(true);
            completed($"Xueliu exchange: {checks} runtime checks passed");
        } finally {
            canvas.ClearXueliuExchangePresentation();
            canvas.HideXueliuThrowThreeSelection();
        }
    }
}
#endif
