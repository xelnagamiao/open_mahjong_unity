from .test_xueliu_protocol import sim_table, table, mid_win
from .record_sim.flags import resolve_record_flags
from .unity_sim import client


def test_retained_duplicate_claim_survives_soft_expiry_until_the_next_action(monkeypatch):
    now = 100.0
    monkeypatch.setattr(client, "monotonic", lambda: now)
    sim = sim_table()
    start = table("sichuan/doubles_duplicate")
    start["game_info"]["is_duplicate"] = True
    sim.apply(start)

    def ask(tick, mode, seconds, actions=("peng", "pass"), **extra):
        return sim.apply({"type": "gamestate/sichuan/ask_other_action", "ask_other_action_info": {
            "action_tick": tick, "cut_tile": 25, "action_list": actions, "claim_wait_mode": mode,
            "claim_seconds_remaining": seconds, "remaining_time": 20, **extra}})

    assert ask(9, "real_time", 5)["ActionButtonSim"]["visible"] is False
    assert ask(11, "real_time", 4.75)["TimerSim"]["total_seconds"] == 4.75
    now += 2
    assert ask(11, "real_time", 5)["TimerSim"]["total_seconds"] == 2.75
    now += 3
    assert sim.snapshot()["TimerSim"]["waiting_draw"]
    assert ask(11, "waiting_draw", 0)["GsmSim"]["allowActionList"] == ["peng", "pass"]
    assert ask(11, "real_time", 5)["TimerSim"]["waiting_draw"]
    sim.apply({"type": "gamestate/sichuan/do_action",
               "do_action_info": {"action_tick": 12, "action_player": 1, "action_list": ["deal_tile"]}})
    for stale_tick in (11, 12):
        state = ask(stale_tick, "waiting_draw", 0)
        assert not state["ActionButtonSim"]["visible"]
        assert not state["GsmSim"]["IsSelfActionRequired"]
    assert ask(13, "real_time", 5)["TimerSim"]["running"]
    assert not ask(13, "waiting_draw", 0, actions=[])["ActionButtonSim"]["visible"]
    assert not ask(13, "real_time", 5)["ActionButtonSim"]["visible"]

    start["type"] = "gamestate/sichuan/reconnect"
    start["game_info"]["action_tick"] = 13
    sim.apply(start)
    state = ask(13, "waiting_draw", 0)
    assert state["TimerSim"]["waiting_draw"] and state["ActionButtonSim"]["visible"]
    sim.apply({"type": "gamestate/sichuan/do_action",
               "do_action_info": {"action_tick": 13, "action_player": 1, "action_list": ["deal_tile"]}})
    assert sim.action_button["visible"] and sim.timer["waiting_draw"]
    assert ask(14, "real_time", 5, is_tactical_recheck=True)["TimerSim"]["total_seconds"] == 20
    ask(15, "waiting_draw", 0, actions=[])
    assert not ask(15, "real_time", 5)["ActionButtonSim"]["visible"]
    start["game_info"]["is_duplicate"] = False
    sim.apply(start)
    assert ask(15, "real_time", 5)["TimerSim"]["total_seconds"] == 25


def test_two_stage_exchange_restores_committed_and_pending_view():
    sim=sim_table()
    sim.apply(table('sichuan/xueliu_doubles'))
    phase=dict(stage=1,active_players=[0,1],teammate_suit=0,selection_done=False)
    def ask(actions,tick):
        sim.apply(dict(type='gamestate/sichuan/broadcast_hand_action',ask_hand_action_info=dict(
            action_list=actions,player_index=0,remaining_time=8,step_remaining=0,action_tick=tick,xueliu_exchange=phase.copy())))
    ask(['xueliu_exchange_three'],11)
    assert sim.opening['three_tiles'] and not sim.action_button['visible']
    phase.update(stage=2,active_players=[2,3],selection_done=True)
    ask([],12)
    assert not sim.opening['three_tiles'] and not sim.gsm['IsSelfActionRequired']
    assert sim.opening['team_exchange']['selection_done']
    assert sim.gsm['LastAskActionTick']==12
    phase.update(active_players=[0,1],selection_done=False,teammate_suit=2)
    ask(['xueliu_exchange_three'],13)
    assert sim.opening['three_tiles'] and sim.opening['team_exchange']['teammate_suit']==2


def test_new_mode_repeated_hu_stays_active_and_does_not_infer_dingque():
    sim=sim_table()
    sim.apply(table('sichuan/xueliu_doubles'))
    sim.apply(dict(type='gamestate/sichuan/do_action',do_action_info=dict(action_list=['deal_tile'],action_player=0,deal_tile=29)))
    sim.apply(mid_win(score_changes=[6,-3,0,-3],player_to_score={0:6,1:-3,2:0,3:-3}))
    assert not sim.end_result['show_result']
    assert sim.game3d['self']['flowers']==[29]
    assert not resolve_record_flags({'rule':'sichuan','sub_rule':'sichuan/xueliu_doubles'}).infers_dingque_from_discards


def test_exchange_animation_restores_without_exposing_selection_actions():
    sim=sim_table()
    sim.apply(table('sichuan/xueliu_doubles'))
    phase=dict(stage=1,active_players=[2,3],selection_done=False,teammate_suit=1,
               outgoing_suit=0,animating=True,animation_seconds=.6)
    sim.apply(dict(type='gamestate/sichuan/broadcast_hand_action',ask_hand_action_info=dict(
        action_list=[],player_index=0,remaining_time=0,step_remaining=0,action_tick=21,xueliu_exchange=phase)))
    assert not sim.opening['three_tiles'] and not sim.gsm['IsSelfActionRequired']
    assert sim.opening['team_exchange']['animating']
    assert sim.opening['team_exchange']['animation_seconds']==.6
    phase=dict(stage=2,active_players=[0,1],selection_done=False,teammate_suit=1,
               outgoing_suit=0,animating=False,animation_seconds=0)
    sim.apply(dict(type='gamestate/sichuan/broadcast_hand_action',ask_hand_action_info=dict(
        action_list=['xueliu_exchange_three'],player_index=0,remaining_time=10,step_remaining=0,action_tick=22,xueliu_exchange=phase)))
    assert sim.opening['three_tiles'] and sim.gsm['IsSelfActionRequired']
