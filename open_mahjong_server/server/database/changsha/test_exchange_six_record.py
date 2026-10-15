import asyncio
from copy import deepcopy

import pytest

from ..shared_record_metrics import analyze_shared_record_for_player
from ..subrule_fans import collect_subrule_fans
from ..guobiao.round_score_utils import extract_player_round_deltas
from ...gamestate.test_room_lifecycle import make_server


def grouped_record():
    # Original 0 sits south. Four-joy dictionaries use originals; payments use seats.
    wins = [dict(winner=1, groups=[dict(fans=['十三将', '报听'], tiles=[35])],
                 payer_details=[dict(payer=0, payment=32)]),
            dict(winner=2, groups=[dict(fans=['全幺九'], tiles=[19])],
                 payer_details=[dict(payer=0, payment=16)])]
    return dict(game_title=dict(rule='changsha', sub_rule='changsha/exchange_six'), game_round={
        'round_index_1': dict(seats=[1, 2, 3, 0], action_ticks=[
            ['changsha_four_joy', 1, 12, [1, 2], {'0': 6, '1': -2, '2': -2, '3': -2}],
            ['c', 35, 'F'], ['changsha_exchange_six_win', wins, [11, 12], 0, 0],
            ['hu_first', 1, 48, ['玩家2 第1组: 十三将 + 报听'], [-50, 38, 14, -2], 35], ['end']])})


def test_grouped_winners_payments_fans_and_rotated_four_joy_agree():
    record = grouped_record()
    before = deepcopy(record)
    rows = [analyze_shared_record_for_player(record, i) for i in range(4)]
    assert [r['win_count'] for r in rows] == [1, 1, 0, 0]
    assert [r['self_draw_count'] for r in rows] == [0, 0, 0, 0]
    assert [r['total_fan_score'] for r in rows] == [32, 16, 0, 0]
    assert rows[3]['deal_in_count'] == 2 and rows[3]['total_fangchong_score'] == 48
    assert [r['total_round_score'] for r in rows] == [38, 14, -2, -50]
    assert [extract_player_round_deltas(record, i)[0] for i in range(4)] == [38, 14, -2, -50]
    assert collect_subrule_fans(record, 0, 'changsha/exchange_six') == {'十三将': 1, '报听': 1, '四喜': 1}
    assert collect_subrule_fans(record, 1, 'changsha/exchange_six') == {'全幺九': 1}
    assert record == before


def test_draw_keeps_four_joy_money_without_counting_a_self_draw():
    record = grouped_record()
    rd = record['game_round']['round_index_1']
    rd['action_ticks'] = rd['action_ticks'][:1] + [['changsha_exchange_six_draw_scores', [-2, 6, -2, -2]], ['liuju'], ['end']]
    for original, expected in enumerate([6, -2, -2, -2]):
        row = analyze_shared_record_for_player(record, original)
        assert row['win_count'] == row['self_draw_count'] == 0
        assert row['total_round_score'] == expected
        assert extract_player_round_deltas(record, original) == [expected]


@pytest.mark.parametrize('seconds', [10, 30, 60])
def test_admin_room_keeps_exchange_time_and_normalizes_fixed_settings(seconds):
    async def run():
        manager = make_server([]).room_manager
        response = await manager.create_empty_event_room('', 'changsha', dict(
            room_name='exchange', sub_rule='changsha/exchange_six', game_round=1,
            exchange_selection_seconds=seconds, open_kong_replacement_count=2,
            dealer_bird=True, score_cap_type='score', score_cap_value=64), broadcast=False)
        assert response.success, response.message
        room = next(iter(manager.rooms.values()))
        assert room['sub_rule'] == 'changsha/exchange_six'
        assert room['exchange_selection_seconds'] == seconds
        assert room['open_kong_replacement_count'] == 4
        assert room['dealer_bird'] is False and room['score_cap_type'] == 'none'
    asyncio.run(run())
