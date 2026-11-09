"""Every Sichuan board starts with the configured bank, including no-opening doubles."""
from copy import deepcopy

import pytest

from .SichuanGameState import SichuanGameState
from .DuplicateGameState import SbrDuplicateGameState, XueliuDuplicateGameState
from .boardcast import _base_game_info, _build_players_info
from .test_duplicate import pair
from ..verifier.host import build_db_manager, build_game_server, build_room_data
from ...game_calculation.sichuan.duplicate_rules import SBR_DUPLICATE, XUELIU_DUPLICATE, team_by_seat


@pytest.mark.parametrize('seconds', [0, 7, 65, 86400])
@pytest.mark.parametrize('sub_rule,engine', [
    ('sichuan/standard', SichuanGameState),
    ('sichuan/doubles', SichuanGameState),
    (SBR_DUPLICATE, SbrDuplicateGameState),
    (XUELIU_DUPLICATE, XueliuDuplicateGameState),
])
@pytest.mark.parametrize('table', ['open', 'closed'])
def test_each_board_restores_room_bank_and_opening_payload(seconds, sub_rule, engine, table):
    db = build_db_manager()
    server = build_game_server(db, messages=[])
    room = build_room_data(seed=1, room_id='clock-test')
    room.update(room_rule='sichuan', sub_rule=sub_rule, round_timer=seconds, step_timer=6,
                team_by_seat=team_by_seat(table))
    if sub_rule in (SBR_DUPLICATE, XUELIU_DUPLICATE):
        match, _ = pair(sub_rule, count=4)
        server.gamestate_manager.sichuan_duplicate_matches = {'clock-test': match}
        room.update(duplicate_match_id='clock-test', duplicate_table=table,
                    duplicate_key='test-key', duplicate_game_id='test-game')
    state = engine(server, room, server.calculation_service, db, 'clock-test')
    for seat, player in enumerate(state.player_list):
        player.player_index = player.original_player_index = seat
    original_room = deepcopy(room)
    for board in range(1, 4):
        state.current_round = board
        for seat, player in enumerate(state.player_list):
            player.remaining_time = min(seconds, seat)
        state._reset_round_state()
        assert [p.remaining_time for p in state.player_list] == [seconds] * 4
        assert state.round_time == seconds and state.step_time == 6
        assert _base_game_info(state)['round_time'] == seconds
        for player in state.player_list:
            payload = _build_players_info(state, player.user_id)
            assert all(p['remaining_time'] == seconds for p in payload)
    assert room == original_room
