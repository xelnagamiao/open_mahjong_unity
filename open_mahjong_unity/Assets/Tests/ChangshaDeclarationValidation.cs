using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

// 在隔离场景主动运行；使用正式声明入口、协程和牌面组件，不向服务器发送请求。
public static class ChangshaDeclarationValidation {
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    public static int Checks { get; private set; }
    private static void Check(bool ok, string message) {
        if (!ok) throw new Exception("ChangshaDeclaration: " + message);
        Checks++;
    }
    public static IEnumerator Run(GameCanvas canvas) {
        Checks = 0;
        Check(canvas.transform.lossyScale.x > .01f && ((RectTransform)canvas.transform).rect.width > 500,
            "native fixture must expose a visible game canvas");
        GameSession.Current.SubRule = ChangshaGameState.ExchangeSixSubRule;
        TableMirror.Current.IndexToPosition.Clear();
        string[] seats = {"self", "left", "top", "right"};
        for (int i = 0; i < 4; i++) TableMirror.Current.IndexToPosition[i] = seats[i];
        var state = (ChangshaGameState)RuleRegistry.SetCurrent("changsha", ChangshaGameState.ExchangeSixSubRule);
        var property = typeof(ChangshaGameState).GetProperty("ExchangeSixSnapshot");
        var snapshot = new ChangshaExchangeSixState {
            phase = "hand", public_state = new ChangshaExchangeSixPublic(),
            private_state = new ChangshaExchangeSixPrivate {action_list = Array.Empty<string>()}
        };
        property.SetValue(state, snapshot);
        Check(state.CanCutTile(11), "ordinary waiting keeps cards bright and available for arranging");
        canvas.ChangeHandCards("SyncHandCards", 0, new[] {11,12,13,14,15,16,17,21,22,23,24,31,33}, null);
        while (canvas.IsChangeHandCardProcessing) yield return null;
        NormalGameStateManager.Instance.allowActionList = new List<string>();
        canvas.RefreshHandTileSelectability();
        var handCards = canvas.HandCardsContainer.GetComponentsInChildren<TileCard>();
        Check(handCards.Length == 13 && handCards.All(card => card.IsSelectableForCut()), "actual waiting hand remains available for arranging");
        Check(handCards.All(card => ((UnityEngine.UI.Image)typeof(TileCard).GetField("tileImage", Flags).GetValue(card)).color.r > .99f), "actual waiting hand uses bright card colors");
        Check(!NormalGameStateManager.Instance.allowActionList.Contains("cut"), "bright waiting cards do not grant a discard action");
        snapshot.private_state.action_list = new[] {"cut"};
        snapshot.private_state.cut_tiles = new[] {18};
        Check(!state.CanCutTile(11) && state.CanCutTile(18), "actual turn respects authoritative cut list");
        snapshot.private_state.action_list = Array.Empty<string>();
        snapshot.private_state.local_hint_context = JObject.Parse("{context:{reported_ready:true}}");
        Check(!state.CanCutTile(11), "reported-ready hand remains locked while waiting");
        snapshot.private_state.local_hint_context = JObject.Parse("{context:{locked_patterns:{}}}");
        Check(!state.CanCutTile(11), "kong-locked hand remains locked while waiting");
        snapshot.phase = "exchange";
        snapshot.private_state.action_list = new[] {"changsha_exchange_six"};
        Check(state.CanCutTile(11), "exchange selection remains available");

        canvas.HideExchangeSixState();
        var views = (Dictionary<string, GameObject>)typeof(GameCanvas).GetField("changshaDeclarationViews", Flags).GetValue(canvas);
        var queues = (IDictionary)typeof(GameCanvas).GetField("changshaDeclarations", Flags).GetValue(canvas);
        var baseline = new ChangshaExchangeSixState {public_state = new ChangshaExchangeSixPublic()};
        var declared = new ChangshaExchangeSixState {public_state = new ChangshaExchangeSixPublic {
            open_hand_seats = new[] {0}, reported_ready_seats = new[] {0}
        }};
        canvas.ShowChangshaDeclarationChanges(null, declared);
        Check(queues.Count == 0, "reconnect does not replay historical declarations");
        declared.restoring = true;
        canvas.ShowChangshaDeclarationChanges(baseline, declared);
        Check(queues.Count == 0, "restoring over an older snapshot does not replay missed declarations");
        declared.restoring = false;
        canvas.ShowChangshaDeclarationChanges(baseline, declared);
        yield return null; yield return null;
        Check(views["self"].GetComponent<TMP_Text>().text == "明牌", "open-hand declaration appears at the action position");
        canvas.ShowChangshaDeclarationChanges(declared, declared);
        yield return new WaitForSecondsRealtime(1f);
        Check(views["self"].GetComponent<TMP_Text>().text == "明牌", "notification is not overwritten by the next declaration");
        yield return new WaitForSecondsRealtime(1.7f);
        Check(views["self"].GetComponent<TMP_Text>().text == "报听", "second declaration plays in order");
        yield return new WaitForSecondsRealtime(2.7f);
        Check(views.Count == 0, "repeated snapshot does not add duplicate notifications");

        // 四喜走真实结算入口，紧随的声明不能吞掉四张牌展示。
        typeof(ChangshaGameState).GetMethod("PresentSettlement", Flags).Invoke(state, new object[] {
            new SettlementEnvelope {HuClass = "initial_hu", WinnerIndex = 1, WinnerHand = new[] {22,22,22,22}}
        });
        canvas.QueueChangshaDeclaration("left", "明牌");
        yield return null; yield return null;
        var fourJoy = views["four_joy"];
        canvas.QueueChangshaDeclaration("right", "四喜", new[] {33,33,33,33});
        Check(fourJoy.GetComponent<TMP_Text>().text.Contains("四喜"), "four-joy result is announced explicitly");
        Check(fourJoy.GetComponentsInChildren<StaticCard>().Length == 4, "four-joy notification contains all four cards");
        canvas.HideExchangeSixState(keepDeclarations: true);
        Check(views.ContainsKey("four_joy"), "settlement keeps an unfinished four-joy notification visible");
        canvas.ShowActionDisplay("left", "peng", "changsha");
        yield return new WaitForSecondsRealtime(3f);
        Check(fourJoy != null && fourJoy.activeInHierarchy, "fast next meld does not remove four-joy notification");
        Check(fourJoy.transform.parent == canvas.transform, "four joy has a separate visible desk popup");
        yield return new WaitForSecondsRealtime(1.6f);
        Check(views["four_joy"].GetComponent<TMP_Text>().text.Contains("右侧玩家"), "simultaneous four joys play one at a time");
        canvas.HideExchangeSixState();
        yield return null;
        Check(views.Count == 0 && queues.Count == 0 && fourJoy == null, "exit clears visible and pending notifications");
        Debug.Log("CHANGSHA_DECLARATION_VALIDATION_PASSED checks=" + Checks);
    }
}
