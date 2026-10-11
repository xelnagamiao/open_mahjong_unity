"""Opening order is presentation input; scoring and draw slots remain authoritative."""
from collections import Counter
import pytest

from ..game_hangzhou.HangzhouGameState import HangzhouGameState
from ..game_guizhou.GuizhouGameState import GuizhouGameState
from ..game_yixing.YixingGameState import YixingGameState
from ..game_wenzhou.WenzhouGameState import WenzhouGameState
from ..game_hongkong.HongKongGameState import HongKongGameState

CASES = [(HangzhouGameState,None,13),(GuizhouGameState,None,13),
         (YixingGameState,None,13),(WenzhouGameState,None,16),
         (HongKongGameState,'hongkong/qingzhang',13),
         (HongKongGameState,'hongkong/new13_gametower',13),
         (HongKongGameState,'hongkong/new13_lianhuise',13),
         (HongKongGameState,'hongkong/qingzhang_lianhuise',13),
         (HongKongGameState,'hongkong/new16',16)]

def opening_state(cls,profile):
    room=cls._default_room_data()
    if profile is not None: room['sub_rule']=profile
    room['random_seed']=20261011
    state=cls(room_data=room,calculation_service=object())
    state.master_seed=20261011
    state.initialize_round()
    return state

@pytest.mark.parametrize('cls,profile,size',CASES)
def test_opening_preserves_unsorted_deal_in_viewer_safe_payload(cls,profile,size):
    state=opening_state(cls,profile)
    before=[list(p.hand_tiles) for p in state.player_list]
    assert all(len(hand) in (size,size+1) for hand in before)
    assert any(hand!=sorted(hand) for hand in before)
    for viewer in range(4):
        payload=state.build_game_start_payload(viewer)
        for player in payload['game_info']['players_info']:
            if player['player_index']==viewer:
                assert player['hand_tiles']==before[viewer]
            else: assert not player.get('hand_tiles')
    assert before==[p.hand_tiles for p in state.player_list]

@pytest.mark.parametrize('cls,profile,size',CASES)
def test_opening_order_change_does_not_sort_wall_or_move_draw_slot(cls,profile,size):
    state=opening_state(cls,profile)
    wall=list(state.tiles_list)
    hands=[list(p.hand_tiles) for p in state.player_list]
    second=opening_state(cls,profile)
    assert wall==second.tiles_list and hands==[p.hand_tiles for p in second.player_list]
    for player in state.player_list:
        if player.has_draw_slot:
            assert len(player.hand_tiles)==size+1
            assert player.hand_tiles[-1]==second.player_list[player.player_index].hand_tiles[-1]
    physical=Counter(wall)
    physical.update(t for hand in hands for t in hand)
    if cls is WenzhouGameState: physical[state.indicator]+=1
    assert max(physical.values())<=4
