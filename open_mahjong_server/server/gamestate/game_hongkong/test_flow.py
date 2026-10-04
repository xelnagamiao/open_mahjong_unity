import asyncio
from collections import Counter
from copy import deepcopy

import pytest

from .HongKongGameState import HongKongGameState
from .state_machine import HongKongPhase as P
from .bot import choose_action, distance
from ...game_calculation.hongkong.models import TILES,FLOWERS,PROFILES
from ...game_calculation.hongkong.solver import parse_meld


def make_state(profile=PROFILES[0],*,flowers=False,seed=1,detailed_config=None):
    room = HongKongGameState._default_room_data()
    room.update(sub_rule=profile,random_seed=seed,claim_protection=False,allow_spectator=False,
                detailed_config={"flowers":flowers} if detailed_config is None else detailed_config,bot_speed="instant")
    return HongKongGameState(room_data=room,calculation_service=object())


def open_round(state):
    if not state.game_record:
        state.start_game_recording()
    state.initialize_round()
    state.start_round_recording()
    state.emit_game_start_payloads()
    return state.open_action_window(state.opening_window())


def physical_tiles(state):
    found = Counter(state.tiles_list)
    found.update(state.won_tiles)
    for p in state.player_list:
        found.update(p.hand_tiles)
        found.update(p.discard_tiles)
        found.update(p.huapai_list)
        for code in p.combination_tiles:
            found.update(parse_meld(code).tiles)
    return found


def assert_conservation(state):
    expected = Counter({t:4 for t in TILES})
    if state.rules.flowers:
        expected.update(FLOWERS)
    assert physical_tiles(state)==expected
    assert sum(p.score for p in state.player_list)==(4800 if state.rules.is_remix else 0)


@pytest.mark.parametrize("profile,flowers",[(PROFILES[0],False),(PROFILES[0],True),(PROFILES[1],False),(PROFILES[2],True)])
def test_seeded_full_hand_preserves_every_physical_tile_and_replay(profile,flowers):
    state = make_state(profile,flowers=flowers,seed=241)
    window = open_round(state)
    assert_conservation(state)
    for _ in range(1000):
        if state.machine.phase==P.END:
            break
        decisions = {i:choose_action(state,i,actions) for i,actions in window["actions"].items() if actions}
        window = state.apply_action_results(window,decisions)
        assert_conservation(state)
    else:
        pytest.fail("The hand did not terminate within its physical wall/action bound")
    state.apply_deferred_score_changes()
    state.machine.transition(P.READY)
    state.finalize_round_recording()
    assert_conservation(state)
    assert state.game_record["game_title"]["rule_version"]==state.rules.version
    ticks = state.game_record["game_round"]["round_index_1"]["action_ticks"]
    assert ticks[-1]==["end"]
    assert ticks[-2][0:2]==["hongkong","state"]
    for view in range(4):
        snapshot = state.build_game_start_payload(view)
        for i,info in enumerate(snapshot["game_info"]["players_info"]):
            if i!=view and not state.player_list[i].is_hu:
                assert info["hand_tiles"] is None


def test_early_queued_response_is_not_discarded_when_wait_begins():
    async def run():
        state = make_state()
        state.step_time = 1
        window = open_round(state)
        index = window["player"]
        decision = choose_action(state,index,window["actions"][index])
        action = decision.pop("action_type")
        await state.submit_action(index,action,**decision)
        with pytest.raises(ValueError):
            await state.submit_action(index,action,**decision)
        responses = await state.wait_action(0.1)
        assert responses[index]["action_type"]==action
        next_window = state.apply_action_results(window,responses)
        before = physical_tiles(state)
        with pytest.raises(ValueError):
            state.apply_action_results(window,responses)
        assert physical_tiles(state)==before and state.live_pending_window is next_window
    asyncio.run(run())


def test_invalid_target_and_extra_seat_are_atomic():
    state = make_state()
    window = open_round(state)
    def snapshot():
        players = [{**vars(p),"record_counter":vars(p.record_counter)} for p in state.player_list]
        return deepcopy((players,state.tiles_list,state.machine.history,state.game_record,state.ledger.snapshot(),state.outbound_payloads))
    before = snapshot()
    with pytest.raises(ValueError):
        state.apply_action_results(window,{0:{"action_type":"cut","TileId":999}})
    assert snapshot()==before
    with pytest.raises(ValueError):
        state.apply_action_results(window,{0:{"action_type":"cut","TileId":state.player_list[0].hand_tiles[-1]},1:{"action_type":"pass"}})
    assert snapshot()==before


def test_bot_structural_distance_has_four_and_five_meld_targets():
    assert distance([11,12,13,21,22,23,31,32,33,41,41,41,45],0,4)==0
    assert distance([11,12,13,14,15,16,21,22,23,31,32,33,41,41,41,45],0,5)==0
    assert distance([45],4,4)==0
    assert distance([45],5,5)==0
