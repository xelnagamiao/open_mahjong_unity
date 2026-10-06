"""Real three-player rob-kong, wrong-win, ready/resume and replay branches."""
import asyncio
import importlib
from collections import Counter
from unittest.mock import AsyncMock
import pytest
from .test_sanma import sanma
from .test_qianggang_cuohe import LOW, HIGH
from .action_check import refresh_waiting_tiles
from ..verifier.record_sim import RecordSim


def configure(s):
    s.round_random_seed = 20261004
    tiles = [11, 19] + list(range(21, 30)) + list(range(31, 40)) + list(range(41, 48))
    pool = Counter({tile: 4 for tile in tiles})
    pool.subtract(LOW + HIGH + [31, 32, 33] + [23] * 4)
    assert min(pool.values()) >= 0
    available = list(pool.elements())
    for p in s.player_list:
        p.combination_tiles = []; p.combination_mask = []; p.huapai_list = []
        p.discard_tiles = []; p.waiting_tiles = set(); p.has_draw_slot = False
        p.last_drawn_tile = None
    s.player_list[0].hand_tiles = [available.pop() for _ in range(10)] + [23]
    s.player_list[0].combination_tiles = ['k23']
    s.player_list[0].combination_mask = [[0, 23, 0, 23, 1, 23]]
    s.player_list[1].hand_tiles = LOW.copy()
    s.player_list[1].combination_tiles = ['s32']
    s.player_list[1].combination_mask = [[0, 31, 0, 32, 1, 33]]
    s.player_list[2].hand_tiles = HIGH.copy()
    s.tiles_list = available
    for seat in [1, 2]: refresh_waiting_tiles(s, seat)


@pytest.mark.parametrize('both_claim', [False, True])
@pytest.mark.parametrize('penalty', [0, 1])
@pytest.mark.parametrize('tactical', [False, True])
@pytest.mark.parametrize('limit', [16, 64])
@pytest.mark.parametrize('flowers', [0, 8])
def test_real_loop_wrong_rob_settlement_and_resume(monkeypatch, both_claim, penalty, tactical, limit, flowers):
    state_module = importlib.import_module('server.gamestate.game_guobiao.GuobiaoGameState')
    action_module = importlib.import_module('server.gamestate.game_guobiao.wait_action')
    s = sanma(tian_di_ren_he=False)
    for p, uid in zip(s.player_list, [0, 2, 3]): p.user_id = uid
    s.current_round = s.round_index = s.max_round = 1
    s.room_random_seed = 20261004
    s.open_cuohe = True
    s.hepai_limit = limit
    s.cuohe_type = penalty
    s.claim_protection = False
    s.tactical_call = tactical
    s.tactical_pre_grace_delay = 0
    s.tactical_grace_seconds = 0.01
    s.broadcast_game_start = AsyncMock()
    s.broadcast_result = AsyncMock()
    s.broadcast_refresh_player_tag_list = AsyncMock()
    final_result = AsyncMock()
    captured = {}

    def deal(gs):
        configure(gs)
        gs.player_list[1].huapai_list = list(range(51, 51 + flowers))

    async def ask_hand():
        assert 'jiagang' in s.action_dict[0]
        s.action_queues[0].put_nowait({'action_type': 'jiagang', 'target_tile': 23})

    async def ask_other():
        assert s.game_status == 'waiting_action_qianggang'
        assert 'offered' not in captured, '已提交和牌或 pass 的玩家不应被重新询问'
        captured['offered'] = {seat: list(actions) for seat, actions in s.action_dict.items()}
        captured['results'] = dict(s.result_dict)
        for seat, actions in s.action_dict.items():
            if actions:
                claim = 'hu_first' if seat == 1 else ('hu_second' if seat == 2 and both_claim else 'pass')
                s.action_queues[seat].put_nowait({'action_type': claim})

    class Finished(Exception):
        pass

    original_resume = s.apply_cuohe_resume_after_ready
    original_ready = s.run_hu_result_ready_phase

    async def ready(fan_count):
        # 使用真正的 ready 阶段：它会覆盖 action_dict、game_status 和询问时钟。
        s.server_action_tick += 1
        await original_ready(fan_count)
        assert not any(s.action_dict.values())
        if final_result.await_count:
            raise Finished

    async def resume(*args, **kwargs):
        await original_resume(*args, **kwargs)
        if both_claim and args[0] == 1:
            assert s.game_status == 'check_hepai' and s.hu_class == 'hu_second'
            assert s.jiagang_tile == 23 and s.result_dict['hu_second'][0] == 54
        else:
            assert s.game_status == 'deal_card_after_gang'
            raise Finished

    monkeypatch.setattr(state_module, 'init_guobiao_tiles', deal)
    monkeypatch.setattr(state_module, 'broadcast_result', final_result)
    monkeypatch.setattr(action_module, 'broadcast_do_action', AsyncMock())
    monkeypatch.setattr(action_module, 'finalize_claim_protection', AsyncMock())
    s.broadcast_ask_hand_action = ask_hand
    s.broadcast_ask_other_action = ask_other
    s.run_hu_result_ready_phase = ready
    s.apply_cuohe_resume_after_ready = resume
    with pytest.raises(Finished):
        asyncio.run(asyncio.wait_for(s.game_loop_chinese(), 3))

    payload = s.broadcast_result.await_args_list[0].kwargs
    assert payload['hu_score'] == captured['results']['hu_first'][0]
    assert '错和' in payload['hu_fan'] and '抢杠和' in payload['hu_fan']
    expected_scores = [10, -20, 10] if penalty == 0 else [0, -40, 0]
    if both_claim:
        second_changes = [-62, -8, 70] if limit == 16 else ([10, 10, -20] if penalty == 0 else [0, 0, -40])
        expected_scores = [score + change for score, change in zip(expected_scores, second_changes)]
    assert [p.score for p in s.player_list] == expected_scores
    assert s.player_list[1].hand_tiles == LOW
    assert s.player_list[0].combination_tiles == ['g23']
    assert s.player_list[1].record_counter.cuohe_times == 1
    assert 'peida' in s.player_list[1].tag_list
    ticks = s.game_record['game_round']['round_index_1']['action_ticks']
    assert any(tick[0] == 'jg' for tick in ticks)
    hu_ticks = [tick for tick in ticks if tick[0].startswith('hu_')]
    assert [tick[0] for tick in hu_ticks] == (['hu_first', 'hu_second'] if both_claim else ['hu_first'])
    assert '错和' in hu_ticks[0][3]
    assert s.current_round == 1
    assert all(p.round_number_history == ([1, 1] if both_claim else [1]) for p in s.player_list)
    assert not s.qianggang_action_dict and not s.qianggang_responses
    if both_claim and limit == 16:
        assert captured['results']['hu_second'][0] == 54
        assert 'hu_second' in captured['offered'][2]
        assert 'peida' not in s.player_list[2].tag_list
        assert s.player_list[2].hand_tiles == HIGH + [23]
        assert ticks[-1] == ['end'] and ticks.count(['end']) == 1
        legal = final_result.await_args.kwargs
        assert legal['is_qianggang'] is True and legal['hepai_tile'] == 23
        assert legal['ron_discarder_index'] == 0 and legal['hepai_player_index'] == 2
        assert legal['score_changes'] == dict(enumerate([-62, -8, 70]))
    else:
        assert ['end'] not in ticks and final_result.await_count == 0
        assert s.game_status == 'deal_card_after_gang'
        assert s.jiagang_tile is None and s.result_dict == {}
        assert s.player_list[2].hand_tiles == HIGH
        if both_claim:
            assert s.player_list[2].record_counter.cuohe_times == 1
            assert 'peida' in s.player_list[2].tag_list
            assert '错和' in hu_ticks[1][3]

    replay = RecordSim(s.game_record)
    frames = replay.apply_all('round_index_1')
    assert [replay.players[i].score for i in range(3)] == expected_scores
    if both_claim and limit == 16:
        assert replay.players[0].combination_tiles == ['k23']
        assert len(replay.players[0].combination_masks[0]) == 6
    else:
        assert replay.players[0].combination_tiles == ['g23']
        assert len(replay.players[0].combination_masks[0]) == 8
    assert replay.players[1].tile_list == LOW
    assert sorted(replay.players[2].tile_list) == sorted(s.player_list[2].hand_tiles)
    assert RecordSim(s.game_record).goto_action('round_index_1', len(ticks)) == frames[-1]
