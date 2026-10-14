"""Source examples, physical conservation, and adversarial state transitions."""
import asyncio
from collections import Counter
import copy
import pytest

from .FuzhouGameState import FuzhouGameState
from .state_machine import Phase as P, StateMachine
from .actions import claim_actions, declaration_cuts, kong_tiles
from .bot import choose_action
from ...game_calculation.fuzhou import rules as book

WIN = [11,12,13,14,15,16,21,22,23,31,32,33,37,37,37,29,29]


def state_with(hand=None, *, jin=39):
    state = FuzhouGameState(calculation_service=object())
    state.initialize_round()
    state.jin = jin
    for p in state.player_list:
        p.hand_tiles = []
        p.has_draw_slot = False
    state.player_list[0].hand_tiles = list(WIN if hand is None else hand)
    state.player_list[0].has_draw_slot = len(state.player_list[0].hand_tiles)%3==2
    available = list(book.PHYSICAL)
    available.remove(jin)
    for tile in state.player_list[0].hand_tiles:
        available.remove(tile)
    for p in state.player_list[1:]:
        for _ in range(16):
            tile = next(t for t in available if t in book.NUMBERS and t!=jin)
            available.remove(tile)
            p.hand_tiles.append(tile)
    state.tiles_list = available
    state.machine.transition(P.OPENING)
    state.machine.transition(P.TURN)
    return state


def act(state, responses):
    return state.apply_action_results(state.live_pending_window,responses)


def open_turn(state,index=0):
    return state.open_action_window(state.begin_turn(index))


def test_initial_deal_and_seed_reproducibility():
    s = FuzhouGameState(calculation_service=object())
    s.initialize_round()
    assert [len(p.hand_tiles) for p in s.player_list]==[17,16,16,16]
    assert len(s.tiles_list)==79
    assert s.physical_inventory()==Counter(book.PHYSICAL)
    wall = list(book.PHYSICAL)
    s2 = FuzhouGameState(calculation_service=object())
    s2.dealer_index = 2
    s2.initialize_round(wall=wall)
    assert [len(p.hand_tiles) for p in s2.player_list]==[16,16,17,16]
    assert s2.player_list[2].hand_tiles[:4]==wall[:4]
    with pytest.raises(ValueError):
        FuzhouGameState(calculation_service=object()).initialize_round(wall=wall[:-1])


def test_opening_replacement_alternates_after_nonflower_but_chains_flower():
    s = FuzhouGameState(calculation_service=object())
    s.initialize_round()
    # This fixture tests the transition order separately from complete-wall tests.
    for p in s.player_list:
        p.hand_tiles = [11,12,13]*5+[29]
    s.player_list[0].hand_tiles += [41,42]
    s.player_list[1].hand_tiles += [43]
    s.tiles_list = [11]*45+[26,25,44]
    w = s.open_action_window(s.opening_window())
    assert w["player"]==0
    act(s,{0:dict(action_type="buhua")})
    assert s.live_pending_window["player"]==0
    assert s.replacement_pending==(0,44)
    act(s,{0:dict(action_type="buhua")})
    assert s.live_pending_window["player"]==1
    act(s,{1:dict(action_type="buhua")})
    assert s.live_pending_window["player"]==0
    assert s.player_list[0].huapai_list==[41,44]
    assert 42 in s.player_list[0].hand_tiles


@pytest.mark.parametrize("seed",range(12))
def test_full_wall_rounds_conserve_all_physical_tiles(seed):
    data = FuzhouGameState._default_room_data()
    data["random_seed"] = seed+11
    s = FuzhouGameState(room_data=data,calculation_service=object())
    s.initialize_round()
    s.open_action_window(s.opening_window())
    seen = set()
    for _ in range(250):
        assert s.physical_inventory()==Counter(book.PHYSICAL)
        seen.add(s.machine.phase)
        if s.machine.phase==P.END:
            break
        responses = {i:choose_action(s,i,a) for i,a in s.live_pending_window["actions"].items() if a}
        act(s,responses)
    assert s.machine.phase==P.END
    assert sum(s.round_changes)==0
    assert P.REPLACEMENT in seen and P.OPENING in seen


def test_gold_cannot_be_discard_claimed_or_melded():
    s = state_with([39]*3+[11,12,13,14,15,16,21,22,23,31,32,33,29,29])
    assert not any(claim_actions(s,0,39).values())
    assert 39 not in kong_tiles(s,0,"angang")
    assert s.can_three_gold(0)
    s.player_list[0].discarded_gold = True
    assert not s.can_win(0,"discard",29)


def test_sanjin_is_optional_with_a_larger_normal_win():
    s = state_with(WIN[:12]+[39]*3+WIN[15:])
    w = open_turn(s)
    assert "sanjin" in w["actions"][0] and "hu_self" in w["actions"][0]
    assert s.score_win(0,"self_draw",29)["points"]>s.score_win(0,"self_draw",special="three_gold")["points"]


def test_normal_win_settles_once_and_draw_repeats():
    s = state_with()
    open_turn(s)
    act(s,{0:dict(action_type="hu_self")})
    assert s.round_changes==[120,-40,-40,-40]
    s.apply_deferred_score_changes()
    s.apply_deferred_score_changes()
    assert [p.score for p in s.player_list]==[120,-40,-40,-40]
    assert s.next_dealer_streak==1
    with pytest.raises(ValueError):
        s.settle_win(0,"self_draw",29)
    t = state_with()
    t.end_draw()
    assert t.round_changes==[0]*4 and t.next_dealer_streak==1


def test_last_four_do_not_discard_or_replace_flower():
    s = state_with(WIN[:-1])
    s.tiles_list = [41,12,14,15]+[19]*18
    s.machine.transition(P.RESPONSE)
    s.open_action_window(s.draw_for(0))
    assert s.machine.phase==P.SEA
    assert s.player_list[0].hand_tiles[-1]==41
    assert s.live_pending_window["actions"][0]==["pass"]
    for index in range(4):
        assert s.live_pending_window["player"]==index
        act(s,{index:dict(action_type="pass")})
    assert s.machine.phase==P.END
    assert len(s.tiles_list)==18
    assert all(not p.discard_tiles for p in s.player_list)


def test_first_youjin_blocks_ron_but_not_other_players_melds():
    s = state_with()
    s.youjin_owner = 0
    s.player_list[1].hand_tiles = [22,22,23,24]+[31]*12
    assert "peng" in claim_actions(s,0,22)[1]
    s.double_youjin_owner = 0
    assert not any(claim_actions(s,0,22).values())


def test_double_youjin_gives_three_other_draws_then_owner_only():
    s = state_with()
    s.double_youjin_owner = 2
    s.double_youjin_remaining = [3,0,1]
    assert [s.next_turn_index(i) for i in (2,3,0,1,2,2)]==[3,0,1,2,2,2]


def test_locked_discard_rejects_hand_identity_even_when_same_face():
    s = state_with(WIN[:-2]+[39,39])
    p = s.player_list[0]
    p.youjin,p.locked_waits = 1,{39}
    open_turn(s)
    before = copy.deepcopy(p.hand_tiles)
    with pytest.raises(ValueError):
        act(s,{0:dict(action_type="cut",TileId=39,cutIndex=15,cutClass=False)})
    assert p.hand_tiles==before
    act(s,{0:dict(action_type="cut",TileId=39,cutIndex=16,cutClass=True)})
    assert p.hand_tiles==before[:-1]


def test_stale_window_and_extra_response_do_not_mutate_state():
    s = state_with()
    w = open_turn(s)
    before = s.physical_inventory()
    with pytest.raises(ValueError):
        s.apply_action_results(dict(w),{0:dict(action_type="hu_self")})
    with pytest.raises(ValueError):
        act(s,{0:dict(action_type="hu_self"),1:dict(action_type="pass")})
    assert s.physical_inventory()==before and s.machine.phase==P.TURN


def test_private_snapshot_and_declared_public_state():
    s = state_with()
    p = s.player_list[0]
    p.golden_middle=True
    p.locked_waits={39}
    own = s.build_game_start_payload(0)["game_info"]
    other = s.build_game_start_payload(1)["game_info"]
    assert own["players_info"][0]["hand_tiles"]==p.hand_tiles
    assert other["players_info"][0]["hand_tiles"] is None
    assert other["fuzhou_info"]["declarations"][0]["golden_middle"]
    assert "locked_waits" not in other["fuzhou_info"]["declarations"][0]


def test_action_arriving_before_wait_is_not_lost():
    async def scenario():
        s = state_with()
        s.step_time=5
        open_turn(s)
        await s.submit_action(0,"hu_self")
        with pytest.raises(ValueError):
            await s.submit_action(0,"hu_self")
        responses = await s.wait_action()
        assert responses[0]["action_type"]=="hu_self"
    asyncio.run(scenario())


def test_illegal_state_jump_is_rejected():
    machine = StateMachine()
    with pytest.raises(RuntimeError):
        machine.transition(P.SEA)
