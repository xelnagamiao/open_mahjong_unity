#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class NewChineseExchangeValidation {
    public static IEnumerator Run(Action<bool,string> check) {
        var canvas = GameCanvas.Instance;
        var manager = NormalGameStateManager.Instance;
        GameInfo fixture = SichuanPresentationValidation.Fixture(NewChineseGameState.SubRule, 0);
        fixture.room_rule = NewChineseGameState.RuleId;
        fixture.gamestate_id = "local-new-chinese-exchange-validation";
        fixture.new_chinese = new NewChineseInfo { phase = "waiting_exchange" };
        fixture.action_tick = 100;
        manager.InitializeGame(true, "", fixture);
        while (canvas.IsChangeHandCardProcessing) yield return null;
        Response Ask() => new Response { game_info = fixture, ask_hand_action_info = new AskHandActionGBInfo {
            action_tick = fixture.action_tick, action_list = new[]{"exchange_three", "pass"}, player_index = 0, remaining_time = 20
        }};
        var state = NewChineseGameState.Active;
        state.HandleMessage("broadcast_hand_action", Ask());
        T Element<T>(string name) => (T)typeof(GameCanvas).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(canvas);
        var confirm = Element<Button>("dingqueWanButton");
        var pass = Element<Button>("dingqueBingButton");
        check(pass.isActiveAndEnabled && !confirm.isActiveAndEnabled, "新中式起始可不换，选满才能确认");
        var cards = Element<Transform>("handCardsContainer").GetComponentsInChildren<TileCard>();
        var chosen = new[]{cards.First(c => c.tileId/10 == 1), cards.First(c => c.tileId/10 == 2), cards.Last(c => c.tileId/10 == 2)};
        foreach (var card in chosen) canvas.TryHandleXueliuTileClick(card);
        state.HandleMessage("broadcast_hand_action", Ask());
        check(chosen.All(c => c.GetComponent<HoverEventTrigger>().SbrRaised) && pass.isActiveAndEnabled &&
            confirm.isActiveAndEnabled, "新中式混合花色可换，重复选牌消息应保留选择");
        canvas.TryHandleXueliuTileClick(chosen[0]);
        check(!confirm.isActiveAndEnabled && pass.isActiveAndEnabled, "取消一张后应允许继续改选或不换");
        canvas.HideNewChineseExchange();
        for (int mask = 0; mask < 16; mask++) {
            var choices = Enumerable.Range(0, 4).Select(i => (mask & (1 << i)) != 0).ToArray();
            int count = choices.Count(v => v);
            var info = new NewChineseInfo { phase = "showing_exchange_choices", exchange_choices = choices,
                exchange_duration_ms = count < 2 ? 1000 : 3750, exchange_remaining_ms = count < 2 ? 1000 : 3750,
                exchange_outgoing = choices[0] ? chosen.Select(c => c.tileId).ToArray() : null };
            canvas.ShowNewChineseExchangeChoices(info, manager.indexToPosition);
            var root = GameObject.Find("NewChineseExchangeChoices");
            check(root && !root.transform.Find("SbrExchangeDirection") &&
                Enumerable.Range(0,4).All(i => root.transform.Find($"ExchangeChoice_{i}/Choice").GetComponent<TMP_Text>().text ==
                    (choices[i] ? "换" : "不换")), $"换牌前应先公示四家选择 mask={mask}");
            canvas.FinishNewChineseExchangeChoices();
            if (count < 2) continue;
            info.exchange_remaining_ms = 1600;
            canvas.ShowNewChineseExchangeChoices(info, manager.indexToPosition);
            yield return null;
            check(XueliuExchangePresentationValidation.HasExchangeArrows(GameObject.Find("NewChineseExchangeChoices").transform,
                count == 2 ? 2 : 1), $"换牌箭头与参与人数不符 mask={mask}");
            var models = Game3DManager.Instance.transform.Cast<Transform>()
                .FirstOrDefault(t => t.name == "SbrExchangeModels" && t.gameObject.activeInHierarchy);
            check(Game3DManager.Instance.HasPendingRecordHandAnimations, "自动回放应等桌面传牌结束");
            check(models && models.childCount == count && Enumerable.Range(0,4).All(i =>
                (models.Find($"From_{i}") != null) == choices[i]), $"仅参与者在桌面传牌 mask={mask}");
            foreach (Transform group in models) {
                var tiles = group.GetComponentsInChildren<Tile3D>();
                check(tiles.Length == 3 && (group.name == "From_0" ?
                    tiles.Select(t => t.GetTileId()).SequenceEqual(info.exchange_outgoing) :
                    tiles.All(t => t.GetTileId() == ConfigManager.BlankFaceImageId)), $"参与者的牌面可见性错误 mask={mask}");
            }
            canvas.ShowNewChineseExchangeChoices(info, manager.indexToPosition);
            check(models.gameObject.activeInHierarchy, "重复公示使传牌从头播放");
            canvas.FinishNewChineseExchangeChoices();
            check(!models.gameObject.activeInHierarchy, "离开公示后桌面应已清理");
            if (count == 2) {
                var seats = Enumerable.Range(0, 4).Where(i => choices[i]).ToArray();
                var starts = new Vector2[2];
                var headings = new Vector3[2];
                for (int step = 0; step <= 4; step++) {
                    info.exchange_remaining_ms = 1650 - 225 * step;
                    canvas.ShowNewChineseExchangeChoices(info, manager.indexToPosition);
                    var view = GameObject.Find("NewChineseExchangeChoices").transform;
                    var table = Game3DManager.Instance.transform.Cast<Transform>()
                        .First(t => t.name == "SbrExchangeModels" && t.gameObject.activeInHierarchy);
                    var reference = view.Find("SbrExchangeDirection");
                    for (int i = 0; i < 2; i++) {
                        Vector2 point = reference.InverseTransformPoint(view.Find($"ExchangeFrom_{seats[i]}").position);
                        Vector3 heading = table.Find($"From_{seats[i]}/Tile_1").right;
                        if (step == 0) { starts[i] = point; headings[i] = heading; }
                        else check(Vector2.Distance(point, Vector2.Lerp(starts[i], starts[1-i], step / 4f)) < 1f &&
                            Vector3.Angle(heading, headings[i]) < .1f, $"两家互换轨迹或朝向错误 mask={mask} step={step}");
                    }
                    canvas.FinishNewChineseExchangeChoices();
                    yield return null;
                }
            }
        }
        fixture.action_tick++;
        fixture.new_chinese = new NewChineseInfo { phase = "waiting_exchange", exchange_outgoing = Array.Empty<int>() };
        manager.InitializeGame(true, "", fixture);
        state.HandleMessage("broadcast_hand_action", Ask());
        check(canvas.IsSbrChoosingOrWaiting && !confirm.isActiveAndEnabled && !pass.isActiveAndEnabled,
            "不换后重连不应可以重复选牌");
        fixture.action_tick++;
        fixture.new_chinese = new NewChineseInfo { phase = "receiving_exchange", exchange_remaining_ms = 500,
            exchange_incoming = fixture.players_info[0].hand_tiles.Take(3).ToArray() };
        manager.InitializeGame(true, "", fixture);
        var stale = new GameInfo { action_tick = fixture.action_tick - 1, new_chinese = new NewChineseInfo {
            phase = "showing_exchange_choices", exchange_choices = new[]{true,true,true,true},
            exchange_duration_ms = 3750, exchange_remaining_ms = 3500 } };
        state.HandleMessage("do_action", new Response {game_info=stale,
            do_action_info=new DoActionInfo {action_list=new[]{"new_chinese_exchange_choices"}}});
        check(state.Info.phase == "receiving_exchange" && !GameObject.Find("NewChineseExchangeChoices"),
            "收牌后补发旧报文不应重播公示");
        yield return new WaitForSecondsRealtime(.8f);
        check(!canvas.IsTableExchangeAnimating, "恢复收牌阶段后不应继续阻塞回放及操作");
    }
}
#endif
