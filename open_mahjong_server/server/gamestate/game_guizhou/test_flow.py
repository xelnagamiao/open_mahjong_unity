import asyncio
from collections import Counter
from copy import deepcopy

import pytest

from .GuizhouGameState import GuizhouGameState
from .actions import claim_actions, ready_cuts
from .bot import choose_action
from .state_machine import Phase as P, StateMachine, TRANSITIONS
from ...game_calculation.guizhou.rules import TILES, meld_tiles, RULE_VERSION

PLAIN=[11,12,13,14,15,16,21,22,23,34,35,36,28,28]
PUNGS=[11,11,11,22,22,22,33,33,33,38,38,38,29,29]


def state(seed=2):
    room=GuizhouGameState._default_room_data()
    room.update(random_seed=seed,allow_spectator=False,claim_protection=False,bot_speed="instant")
    return GuizhouGameState(room_data=room,calculation_service=object())


def start(s):
    s.start_game_recording()
    s.initialize_round()
    s.start_round_recording()
    return s.open_action_window(s.opening_window())


def physical(s):
    result=Counter(s.tiles_list)
    for p in s.player_list:
        result.update(p.hand_tiles)
        result.update(p.discard_tiles)
        result.update(p.won_tiles)
        for meld in p.combination_tiles:
            result.update(meld_tiles(meld))
    return result


def act(s,actor,action,**fields):
    return s.apply_action_results(s.live_pending_window,{actor:dict(action_type=action,**fields)})


def configured_turn(hand=PLAIN, index=0):
    s=state()
    s.initialize_round()
    for p in s.player_list:
        p.hand_tiles=[12,13,14,16,18,19,21,23,25,27,32,35,39]
        p.discard_count=1
        p.initial_quads.clear()
    s.discard_log=[(0,17)]
    s.player_list[index].hand_tiles=list(hand)
    s.player_list[index].has_draw_slot=True
    s.open_action_window(s.begin_turn(index))
    return s


@pytest.mark.parametrize("seed", [1,2,7,19])
def test_offline_hand_conserves_108_tiles_scores_and_replay(seed):
    s=state(seed)
    window=start(s)
    expected=Counter({t:4 for t in TILES})
    assert physical(s)==expected
    for step in range(350):
        if s.machine.phase==P.END:
            break
        choices={i:choose_action(s,i,a) for i,a in window["actions"].items() if a}
        window=s.apply_action_results(window,choices)
        assert physical(s)==expected
    else:
        pytest.fail("Finite wall failed to terminate")
    s.apply_deferred_score_changes()
    scores=[p.score for p in s.player_list]
    s.apply_deferred_score_changes()
    assert scores==[p.score for p in s.player_list] and sum(scores)==0
    s.machine.transition(P.READY)
    s.finalize_round_recording()
    assert s.game_record["game_title"]["rule_version"]==RULE_VERSION
    ticks=s.game_record["game_round"]["round_index_1"]["action_ticks"]
    assert ticks[-1]==["end"] and ticks[-2][:2]==["guizhou","state"]


def test_hard_ready_precedes_dealer_and_locks_hand():
    s=state()
    s.initialize_round()
    for i in (1,2,3):
        s.player_list[i].hand_tiles=PLAIN[:-1]
    window=s.open_action_window(s.opening_window())
    assert window["actions"][0]==[]
    s.apply_action_results(window,{1:{"action_type":"baoting_initial"},2:{"action_type":"pass"},3:{"action_type":"pass"}})
    assert s.player_list[1].ready_kind=="hard_ready"
    assert s.machine.phase==P.TURN
    assert "guizhou_ready" not in claim_actions(s,0,11)[1]
    s.machine.transition(P.RESPONSE)
    s.open_action_window(s.draw_for(1))
    assert set(s.action_dict[1]) <= {"hu_self","cut"}
    with pytest.raises(ValueError):
        act(s,1,"cut",TileId=11,cutClass=False,cutIndex=0)


def test_soft_ready_two_step_only_before_first_discard():
    s=configured_turn()
    p=s.player_list[0]
    p.discard_count=0
    s.open_action_window(s.begin_turn(0))
    assert 28 in ready_cuts(s,0)
    act(s,0,"guizhou_ready")
    assert p.ready_pending
    act(s,0,"guizhou_ready_cancel")
    assert not p.ready_pending and not p.ready_kind
    act(s,0,"guizhou_ready")
    with pytest.raises(ValueError):
        act(s,0,"cut",TileId=19)
    act(s,0,"cut",TileId=28,cutClass=True)
    assert p.ready_kind=="soft_ready" and p.discard_riichi_flags[-1]
    assert not ready_cuts(s,0)


def test_plain_ron_requires_any_committed_kong():
    s=configured_turn(index=1)
    p=s.player_list[1]
    p.hand_tiles=PLAIN[:-1]
    p.has_draw_slot=False
    assert not s.can_win(1,"discard",28,payer=0)
    s.hot_discard=True
    assert s.can_win(1,"discard",28,payer=0)
    assert claim_actions(s,0,28)[1]==["hu"]
    s.hot_discard=False
    assert s.can_win(1,"rob_kong",28,payer=0)


def test_missed_win_and_declared_permanent_water():
    s=configured_turn(index=1)
    p=s.player_list[1]
    p.hand_tiles=PUNGS[:-1]
    p.has_draw_slot=False
    s.open_action_window(s._window(P.RESPONSE,0,29,claim_actions(s,0,29)))
    s._remember_responses(s.live_pending_window,{1:{"action_type":"pass"}})
    assert not s.can_win(1,"discard",29,payer=0)
    s.hot_discard=True
    assert s.can_win(1,"discard",29,payer=0)
    p.ready_kind="soft_ready"
    s._remember_responses(s.live_pending_window,{1:{"action_type":"pass"}})
    assert not s.can_win(1,"rob_kong",29,payer=0)
    p.hand_tiles.append(29)
    p.has_draw_slot=True
    assert s.can_win(1,"self_draw",29)


def test_only_self_discard_chicken_gets_passed_pung_exception():
    s=configured_turn()
    p=s.player_list[1]
    p.hand_tiles=[31,31,11,11,28,28,12,14,16,18,23,25,39]
    p.discarded_since_action={31,11}
    assert "peng" in claim_actions(s,0,31)[1]
    assert "peng" not in claim_actions(s,0,11)[1]
    p.passed_pungs={31}
    assert "peng" not in claim_actions(s,0,31)[1]


def test_last_tile_may_be_pung_but_no_kong():
    s=configured_turn()
    s.tiles_list=[]
    p=s.player_list[1]
    p.hand_tiles=[31,31,31,11,12,14,16,18,23,25,33,35,39]
    assert "peng" in claim_actions(s,0,31)[1]
    assert "gang" not in claim_actions(s,0,31)[1]
    s.player_list[0].hand_tiles=[11]*4+[12,13,14,21,22,23,31,32,33,39]
    s.open_action_window(s.begin_turn(0))
    assert "angang" not in s.action_dict[0]


@pytest.mark.parametrize("fresh,hanbao", [(True,False),(False,True)])
def test_concealed_kong_fresh_or_hanbao_commits_after_response(fresh,hanbao):
    hand=[11]*4+[12,13,14,21,22,23,31,32,33,39]
    if fresh:
        hand.remove(11); hand.append(11)
    s=configured_turn(hand)
    before=list(s.player_list[0].hand_tiles)
    act(s,0,"angang",target_tile=11)
    assert s.machine.phase==P.KONG and s.player_list[0].hand_tiles==before
    s.apply_action_results(s.live_pending_window,{})
    assert s.kongs[-1].hanbao==hanbao
    assert s.player_list[0].combination_tiles==["G11"]
    assert s.player_list[0].after_kong


def test_invalid_or_stale_action_does_not_mutate_state():
    s=configured_turn()
    window=deepcopy(s.live_pending_window)
    before=physical(s)
    with pytest.raises(ValueError):
        s.apply_action_results(window,{0:{"action_type":"hu_self"}})
    with pytest.raises(ValueError):
        act(s,0,"angang",target_tile=11)
    assert physical(s)==before


def test_early_network_response_is_retained_and_duplicate_rejected():
    async def run():
        s=configured_turn()
        await s.submit_action(0,"cut",TileId=28,cutClass=True,cutIndex=13)
        with pytest.raises(ValueError):
            await s.submit_action(0,"hu_self")
        result=await s.wait_action(timeout=0)
        assert result[0]["TileId"]==28
    asyncio.run(run())


def test_privacy_and_reveal_after_hand_end():
    s=state()
    start(s)
    for viewer in range(4):
        payload=s.build_game_start_payload(viewer)
        assert all((p["hand_tiles"] is not None)==(i==viewer) for i,p in enumerate(payload["game_info"]["players_info"]))
    s.machine.transition(P.TURN)
    s.end_draw()
    assert all(p["hand_tiles"] is not None for p in s.restore_payloads(0)[0]["game_info"]["players_info"])


def test_all_phase_edges_are_explicit():
    for source in P:
        for target in P:
            machine=StateMachine()
            machine.phase=source
            if source==target or target in TRANSITIONS[source]:
                machine.transition(target)
                assert machine.phase==target
            else:
                with pytest.raises(RuntimeError):
                    machine.transition(target)
