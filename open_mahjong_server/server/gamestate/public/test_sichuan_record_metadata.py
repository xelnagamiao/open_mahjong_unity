"""Sichuan always supplements from the front, without per-record policy."""
from types import SimpleNamespace

import pytest

from .game_record_manager import build_game_title_data
from ..game_sichuan.SichuanGameState import SichuanGameState
from ..game_sichuan.XueliuGameState import XueliuGameState
from ..verifier.host import build_db_manager, build_game_server, build_room_data


def state(sub_rule, blood_battle=True):
    db = build_db_manager()
    server = build_game_server(db, messages=[])
    room = build_room_data(seed=20261003, room_id='record-kong-source')
    room.update(room_rule='sichuan', sub_rule=sub_rule, blood_battle=blood_battle)
    cls = SichuanGameState if sub_rule == 'sichuan/standard' else XueliuGameState
    return cls(server, room, server.calculation_service, db, 'record-kong-source')


@pytest.mark.parametrize('sub_rule', ['sichuan/standard', 'sichuan/xueliu', 'sichuan/xueliu_exchange'])
@pytest.mark.parametrize('blood_battle', [True, False])
def test_canonical_front_draw_including_inherited_blood_flow(sub_rule, blood_battle):
    game = state(sub_rule, blood_battle)
    title = build_game_title_data(game)
    assert 'sichuan_kong_draw_source' not in title
    assert title['blood_battle'] is (blood_battle if sub_rule == 'sichuan/standard' else False)
    player = game.player_list[0]
    original = list(player.hand_tiles)
    wall = [11, 22, 33]
    for drawn, remaining in [(11, [22, 33]), (22, [33]), (33, [])]:
        player.has_draw_slot = False
        player.get_gang_tile(wall, game)
        original.append(drawn)
        assert player.hand_tiles == original and wall == remaining
        assert player.has_draw_slot is True
    # The game loop decides exhaustion; the unchanged player primitive must not
    # fabricate a tile or mutate a hand from an empty wall.
    with pytest.raises(IndexError):
        player.get_gang_tile(wall, game)
    assert player.hand_tiles == original and wall == []


@pytest.mark.parametrize('rule', ['guobiao', 'riichi', 'hangzhou', 'yixing', 'guangdong', 'wenzhou', 'hongzhong', 'changchun'])
def test_other_record_titles_do_not_gain_sichuan_wall_metadata(rule):
    game = SimpleNamespace(room_rule=rule, room_type='custom', sub_rule=rule + '/standard',
        commitment=0, salt='', max_round=1, open_cuohe=False, tips=True,
        isPlayerSetRandomSeed=False, player_list=[])
    assert 'sichuan_kong_draw_source' not in build_game_title_data(game)
