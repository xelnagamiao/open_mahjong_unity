"""国标抢杠错和：可配置起和番、罚分与同一次抢杠的续判。"""
import asyncio
import importlib
from collections import Counter
from unittest.mock import AsyncMock

import pytest

from server.gamestate.game_guobiao.test_opening_wins import make_state
from server.gamestate.game_guobiao.action_check import check_action_jiagang, refresh_waiting_tiles
from server.gamestate.public.ask_timing import begin_ask_round
from server.gamestate.verifier.record_sim import RecordSim

LOW = [21, 22, 24, 25, 26, 36, 37, 38, 45, 45]
HIGH = [21, 22, 24, 25, 26, 27, 28, 29, 29, 29, 29, 24, 24]


def configure(s):
    s.round_random_seed = 20261004
    pool = Counter({tile: 4 for tile in list(range(11, 20)) + list(range(21, 30)) + list(range(31, 40)) + list(range(41, 48))})
    pool.subtract(LOW + HIGH + [11, 12, 13] + [23] * 4)
    assert min(pool.values()) >= 0
    available = list(pool.elements())
    for p in s.player_list:
        p.combination_tiles = []
        p.combination_mask = []
        p.huapai_list = []
        p.discard_tiles = []
        p.waiting_tiles = set()
        p.has_draw_slot = False
        p.last_drawn_tile = None
    s.player_list[0].hand_tiles = [available.pop() for _ in range(10)] + [23]
    s.player_list[0].combination_tiles = ['k23']
    s.player_list[0].combination_mask = [[0, 23, 0, 23, 1, 23]]
    s.player_list[1].hand_tiles = LOW.copy()
    s.player_list[1].combination_tiles = ['s12']
    s.player_list[1].combination_mask = [[0, 11, 0, 12, 1, 13]]
    s.player_list[2].hand_tiles = HIGH.copy()
    s.player_list[3].hand_tiles = [available.pop() for _ in range(13)]
    s.tiles_list = available
    for seat in [1, 2, 3]:
        refresh_waiting_tiles(s, seat)


@pytest.mark.parametrize('limit', [8, 11, 12, 16, 54, 55, 64])
@pytest.mark.parametrize('enabled', [False, True])
def test_configurable_threshold_reaches_wrong_win_action(limit, enabled):
    s = make_state(enabled=False)
    configure(s)
    s.hepai_limit, s.open_cuohe = limit, enabled
    actions = check_action_jiagang(s, 23)
    assert ('hu_first' in actions[1]) is (enabled or limit <= 11)
    assert ('hu_second' in actions[2]) is (enabled or limit <= 54)


@pytest.mark.parametrize('both_claim', [False, True])
@pytest.mark.parametrize('penalty', [0, 1])
@pytest.mark.parametrize('tactical', [False, True])
@pytest.mark.parametrize('limit', [16, 64])
@pytest.mark.parametrize('flowers', [0, 8])
def test_real_loop_wrong_rob_settlement_and_resume(monkeypatch, both_claim, penalty, tactical, limit, flowers):
    state_module = importlib.import_module('server.gamestate.game_guobiao.GuobiaoGameState')
    action_module = importlib.import_module('server.gamestate.game_guobiao.wait_action')
    s = make_state(enabled=False)
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
    assert payload['hu_score'] == 11 + flowers
    assert '错和' in payload['hu_fan'] and '抢杠和' in payload['hu_fan']
    expected_scores = [10, -30, 10, 10] if penalty == 0 else [0, -40, 0, 0]
    if both_claim:
        second_changes = [-62, -8, 78, -8] if limit == 16 else ([10, 10, -30, 10] if penalty == 0 else [0, 0, -40, 0])
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
        assert legal['score_changes'] == dict(enumerate([-62, -8, 78, -8]))
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
    assert [replay.players[i].score for i in range(4)] == expected_scores
    assert replay.players[1].tile_list == LOW
    assert sorted(replay.players[2].tile_list) == sorted(s.player_list[2].hand_tiles)
    assert RecordSim(s.game_record).goto_action('round_index_1', len(ticks)) == frames[-1]


def pending_rob_state(monkeypatch, tactical=False):
    module = importlib.import_module('server.gamestate.game_guobiao.wait_action')
    s = make_state(enabled=False)
    configure(s)
    s.open_cuohe = True
    s.hepai_limit = 16
    s.game_status = 'waiting_action_qianggang'
    s.jiagang_tile = 23
    s.action_dict = check_action_jiagang(s, 23)
    s.tactical_call = tactical
    s.tactical_pre_grace_delay = 0
    s.tactical_grace_seconds = 0.01
    s.broadcast_refresh_player_tag_list = AsyncMock()
    monkeypatch.setattr(module, 'broadcast_do_action', AsyncMock())
    monkeypatch.setattr(module, 'broadcast_ask_other_action', AsyncMock())
    return s, module


def test_unanswered_robber_continues_after_ready_and_old_tick_is_ignored(monkeypatch):
    s, _ = pending_rob_state(monkeypatch)

    async def run():
        s.action_queues[1].put_nowait({'action_type': 'hu_first', '_action_tick': 0})
        await s.wait_action()
        assert s.hu_class == 'hu_first'
        await s.run_hu_result_ready_phase(1)
        await s.apply_cuohe_resume_after_ready(1, 'hu_first', is_qianggang=True)
        assert s.game_status == 'waiting_action_qianggang'
        assert s.action_dict == {0: [], 1: [], 2: ['hu_second', 'pass'], 3: []}
        s.server_action_tick = 2
        begin_ask_round(s)
        s.action_queues[2].put_nowait({'action_type': 'pass', '_action_tick': 0})
        s.action_queues[2].put_nowait({'action_type': 'hu_second', '_action_tick': 2})
        await s.wait_action()
        assert s.game_status == 'check_hepai' and s.hu_class == 'hu_second'
        assert s.result_dict['hu_second'][0] == 54 and s.jiagang_tile == 23

    asyncio.run(asyncio.wait_for(run(), 2))


@pytest.mark.parametrize('tactical', [False, True])
@pytest.mark.parametrize('reply', ['pass', 'force_pass', 'timeout'])
def test_declined_or_timed_out_robber_is_not_reasked_after_wrong_win(monkeypatch, tactical, reply):
    s, module = pending_rob_state(monkeypatch, tactical)
    s.hepai_limit = 64
    s.action_dict[1].append('force_pass')

    async def recheck(gs, **kwargs):
        if reply != 'timeout':
            gs.action_queues[1].put_nowait({'action_type': reply})
            gs.action_events[1].set()

    monkeypatch.setattr(module, 'broadcast_ask_other_action', recheck)
    if not tactical and reply == 'timeout':
        # 第二家回复后第一家仍未回复，直接推进到超时边界，避免真实等待局时。
        monkeypatch.setattr(module, 'get_ask_elapsed', lambda gs, i: 100 if not gs.action_dict[2] else 0)

    async def run():
        s.action_queues[2].put_nowait({'action_type': 'hu_second'})
        if reply != 'timeout':
            s.action_queues[1].put_nowait({'action_type': reply})
        await s.wait_action()
        assert s.hu_class == 'hu_second'
        await s.run_hu_result_ready_phase(1)
        await s.apply_cuohe_resume_after_ready(2, 'hu_second', is_qianggang=True)
        assert s.game_status == 'deal_card_after_gang' and s.jiagang_tile is None
        assert not any(s.action_dict.values())

    asyncio.run(asyncio.wait_for(run(), 2))


@pytest.mark.parametrize('tactical', [False, True])
def test_three_claims_keep_seat_priority_across_consecutive_wrong_wins(monkeypatch, tactical):
    s, _ = pending_rob_state(monkeypatch, tactical)
    s.hepai_limit = 96
    s.action_dict[3] = ['hu_third', 'pass']
    s.result_dict['hu_third'] = (88, ['大三元', '抢杠和'])

    async def run():
        for seat, action in enumerate(('hu_first', 'hu_second', 'hu_third'), 1):
            s.action_queues[seat].put_nowait({'action_type': action})
        await s.wait_action()
        for seat, action in enumerate(('hu_first', 'hu_second', 'hu_third'), 1):
            assert s.game_status == 'check_hepai' and s.hu_class == action
            assert s.jiagang_tile == 23
            await s.run_hu_result_ready_phase(1)
            await s.apply_cuohe_resume_after_ready(seat, action, is_qianggang=True)
        assert s.game_status == 'deal_card_after_gang'
        assert not s.result_dict and s.jiagang_tile is None

    asyncio.run(asyncio.wait_for(run(), 2))


@pytest.mark.parametrize('tactical', [False, True])
@pytest.mark.parametrize('reply', ['pass', 'hu_second'])
def test_unanswered_higher_claim_preserves_already_submitted_lower_claim(monkeypatch, tactical, reply):
    s, _ = pending_rob_state(monkeypatch, tactical)
    s.hepai_limit = 64
    s.action_dict[3] = ['hu_third', 'pass']
    s.result_dict['hu_third'] = (88, ['大三元', '抢杠和'])

    async def run():
        s.action_queues[1].put_nowait({'action_type': 'hu_first'})
        s.action_queues[3].put_nowait({'action_type': 'hu_third'})
        await s.wait_action()
        assert s.hu_class == 'hu_first'
        await s.run_hu_result_ready_phase(1)
        await s.apply_cuohe_resume_after_ready(1, 'hu_first', is_qianggang=True)
        assert s.game_status == 'waiting_action_qianggang'
        assert s.action_dict == {0: [], 1: [], 2: ['hu_second', 'pass'], 3: []}
        s.server_action_tick += 1
        begin_ask_round(s)
        s.action_queues[2].put_nowait({'action_type': reply, '_action_tick': s.server_action_tick})
        await s.wait_action()
        assert s.game_status == 'check_hepai'
        if reply == 'hu_second':
            assert s.hu_class == 'hu_second'
            await s.run_hu_result_ready_phase(1)
            await s.apply_cuohe_resume_after_ready(2, 'hu_second', is_qianggang=True)
        assert s.game_status == 'check_hepai' and s.hu_class == 'hu_third'
        assert s.result_dict['hu_third'][0] == 88 and s.jiagang_tile == 23

    asyncio.run(asyncio.wait_for(run(), 2))


def test_tactical_grace_keeps_all_late_claims(monkeypatch):
    s, module = pending_rob_state(monkeypatch, tactical=True)
    s.action_dict[3] = ['hu_third', 'pass']
    s.result_dict['hu_third'] = (88, ['大三元', '抢杠和'])

    async def recheck(gs, **kwargs):
        assert kwargs['is_tactical_recheck'] is True
        for seat, action in ((1, 'hu_first'), (2, 'hu_second')):
            gs.action_queues[seat].put_nowait({'action_type': action})
            gs.action_events[seat].set()

    monkeypatch.setattr(module, 'broadcast_ask_other_action', recheck)

    async def run():
        s.action_queues[3].put_nowait({'action_type': 'hu_third'})
        await s.wait_action()
        assert s.hu_class == 'hu_first'
        await s.run_hu_result_ready_phase(1)
        await s.apply_cuohe_resume_after_ready(1, 'hu_first', is_qianggang=True)
        assert s.game_status == 'check_hepai' and s.hu_class == 'hu_second'
        assert s.result_dict['hu_second'][0] == 54

    asyncio.run(asyncio.wait_for(run(), 2))
