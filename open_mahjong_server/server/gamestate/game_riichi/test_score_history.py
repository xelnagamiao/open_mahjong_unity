"""Riichi deposits must appear once in live, reconnected and recorded score histories."""
import asyncio
import json
from unittest.mock import AsyncMock, patch

import pytest

from .boardcast import broadcast_game_start
from .test_double_riichi import declare, WAITING_HAND
from .test_execution_paths import result
from .test_protocol import connected
from .RiichiGameState import RiichiGameState
from .test_match_simulation import test_complete_matches_keep_physical_tiles_scores_and_record_config as _run_match
from ..verifier.record_sim.decoder import accumulate_score_changes_from_tick


def pay_deposit(game, seat):
    game.current_player_index = seat
    game.player_list[seat].hand_tiles = WAITING_HAND + [47]
    game.player_list[seat].has_draw_slot = True
    declare(game)


def result_messages(sockets, player):
    return [json.loads(json.dumps(call.args[0]['show_result_info']))
            for call in sockets[player.user_id].websocket.send_json.call_args_list
            if call.args[0]['type'] == 'gamestate/riichi/show_result']


@pytest.mark.parametrize('payer', range(4))
@pytest.mark.parametrize('rotated', [False, True])
def test_paid_riichi_is_in_round_history_and_live_result_without_changing_hu_payments(payer, rotated):
    game, sockets = connected()
    if rotated:
        for p in game.player_list:
            p.original_player_index = (p.player_index + 1) % 4
    game._begin_round_score_history()
    pay_deposit(game, payer)
    game.current_player_index = 0
    game.ron_player_index = 1
    game.hu_class = 'hu_first'
    game.result_dict = {'hu_first': result(8000)}
    asyncio.run(game._settle_hu(is_last_settle=True))
    game._append_round_score_history()
    expected = [-8000, 9000, 0, 0]
    expected[payer] -= 1000
    payload = result_messages(sockets, game.player_list[0])[-1]
    original = {str(p.original_player_index): expected[p.player_index] for p in game.player_list}
    assert payload['score_history_changes'] == original
    assert payload['score_changes'] == {
        str(p.original_player_index): [-8000, 9000, 0, 0][p.player_index] for p in game.player_list}
    assert [int(p.score_history[-1]) for p in game.player_list] == expected
    assert sum(p.score for p in game.player_list) == 100000
    asyncio.run(broadcast_game_start(game))
    info = sockets[game.player_list[0].user_id].websocket.send_json.call_args.args[0]['game_info']
    assert {p['original_player_index']: int(p['score_history'][-1]) for p in info['players_info']} == {
        int(key): value for key, value in original.items()}


def test_draw_keeps_paid_deposit_outside_player_total_and_in_history():
    game, sockets = connected()
    game._begin_round_score_history()
    pay_deposit(game, 2)
    game.player_list[2].discard_tiles = [12]  # 普通荒牌流局，不触发流局满贯。
    game.player_list[2].discard_origin_tiles = [12]
    for p in game.player_list:
        p.waiting_tiles = set()
        if p.player_index != 2:
            p.hand_tiles = []
    game.hu_class = 'ryuukyoku'
    asyncio.run(game._settle_round())
    game._append_round_score_history()
    payload = result_messages(sockets, game.player_list[0])[-1]
    assert [int(p.score_history[-1]) for p in game.player_list] == [-1000, -1000, 2000, -1000]
    assert sum(payload['score_history_changes'].values()) == -1000
    assert sum(payload['score_changes'].values()) == 0
    assert sum(p.score for p in game.player_list) + game.riichi_sticks * 1000 == 100000


def test_abort_records_four_paid_riichi_deposits_even_without_hu_score_changes():
    game, sockets = connected()
    game._begin_round_score_history()
    for seat in range(4):
        pay_deposit(game, seat)
    game.hu_class = 'four_riichi_abort'
    asyncio.run(game._settle_round())
    game._append_round_score_history()
    payload = result_messages(sockets, game.player_list[0])[-1]
    assert payload['score_history_changes'] == {str(i): -1000 for i in range(4)}
    assert payload['player_to_score'] == {str(i): 24000 for i in range(4)}
    assert all(p.score_history == ['-1000'] for p in game.player_list)
    assert sum(p.score for p in game.player_list) + game.riichi_sticks * 1000 == 100000


def test_multiple_ron_records_deposit_only_in_first_result():
    game, sockets = connected('majsoul')
    game._begin_round_score_history()
    pay_deposit(game, 2)
    game.current_player_index = 0
    queue = [(1, 'hu_first'), (2, 'hu_second')]
    game.result_dict = {action: result(8000) for _, action in queue}
    with patch('server.gamestate.game_riichi.RiichiGameState.run_synced_hu_ready_phase', new=AsyncMock()), \
         patch('server.gamestate.game_riichi.RiichiGameState.asyncio.sleep', new=AsyncMock()):
        asyncio.run(game._settle_multi_ron_sequence(queue))
    game._append_round_score_history()
    payloads = result_messages(sockets, game.player_list[0])
    assert len(payloads) == 2
    assert payloads[0]['score_history_changes'] == {'0': -8000, '1': 9000, '2': -1000, '3': 0}
    assert payloads[1]['score_history_changes'] == {'0': -8000, '1': 0, '2': 8000, '3': 0}
    assert [int(p.score_history[-1]) for p in game.player_list] == [-16000, 9000, 7000, 0]
    assert sum(p.score for p in game.player_list) == 100000


def test_chombo_refund_cancels_deposit_in_history():
    game, sockets = connected()
    game._begin_round_score_history()
    pay_deposit(game, 1)
    game.current_player_index = 0
    game.ron_player_index = 1
    game.hu_class = 'hu_first'
    game.result_dict = {'hu_first': dict(han=0, fu=30, yaku=[], no_yaku=True)}
    asyncio.run(game._settle_cuohe())
    game._append_round_score_history()
    payload = result_messages(sockets, game.player_list[0])[-1]
    expected = {str(p.original_player_index): p.score - 25000 for p in game.player_list}
    assert payload['score_history_changes'] == expected
    assert sum(expected.values()) == 0
    assert payload['score_changes']['1'] == expected['1'] + 1000
    assert game.riichi_sticks == 0


def test_new_hand_resets_history_baseline_and_does_not_repeat_previous_deposit():
    game, sockets = connected()
    game._begin_round_score_history()
    pay_deposit(game, 2)
    game.hu_class = 'four_riichi_abort'
    asyncio.run(game._settle_round())
    game._append_round_score_history()
    game.current_round = 2
    game._begin_round_score_history()
    game.hu_class = 'four_wind_abort'
    asyncio.run(game._settle_round())
    game._append_round_score_history()
    assert all(int(p.score_history[-1]) == 0 for p in game.player_list)
    assert result_messages(sockets, game.player_list[0])[-1]['score_history_changes'] == {str(i): 0 for i in range(4)}


@pytest.mark.parametrize('preset', ['tenhou', 'majsoul', 'jpml_a', 'mleague'])
def test_final_deposit_distribution_is_in_last_history_row_and_is_idempotent(preset):
    game, _ = connected(preset)
    game._begin_round_score_history()
    pay_deposit(game, 2)
    game._append_round_score_history()
    game._finalize_scores_and_history()
    assert all(25000 + sum(map(int, p.score_history)) == p.score for p in game.player_list)
    assert sum(p.score for p in game.player_list) + game.riichi_sticks * 1000 == 100000
    history = [list(p.score_history) for p in game.player_list]
    game._finalize_scores_and_history()
    assert [p.score_history for p in game.player_list] == history


def test_complete_match_history_matches_each_recorded_hand_and_final_scores(caplog):
    append = RiichiGameState._append_round_score_history
    finalize = RiichiGameState._finalize_scores_and_history

    def checked_append(game):
        append(game)
        record = game.game_record['game_round'][f'round_index_{game.round_index}']
        expected = None
        for tick in record['action_ticks']:
            expected = accumulate_score_changes_from_tick(expected, tick, record['seats'])
        expected = expected or [0] * 4
        for p in game.player_list:
            assert int(p.score_history[-1]) == expected[p.original_player_index]

    def checked_finalize(game):
        finalize(game)
        for p in game.player_list:
            assert game._player_starting_score(p.original_player_index) + sum(map(int, p.score_history)) == p.score

    with patch.object(RiichiGameState, '_append_round_score_history', checked_append), \
         patch.object(RiichiGameState, '_finalize_scores_and_history', checked_finalize):
        _run_match('majsoul', False, caplog)
