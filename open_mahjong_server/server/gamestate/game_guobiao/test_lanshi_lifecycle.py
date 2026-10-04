"""蓝十完整四局状态循环：真实判和/结算/换位/记录，仅替换输入与传输。"""
import asyncio
import importlib
from collections import Counter
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .test_lanshi_v4_integration import state
from ...game_calculation.test_lanshi_v4 import tiles
from ...game_calculation.lanshi_v4 import TILES
from ..verifier.record_sim import RecordSim


def test_four_dealer_wins_rotate_seats_and_preserve_settlements_in_replay(monkeypatch):
    module=importlib.import_module('server.gamestate.game_guobiao.GuobiaoGameState')
    gs=state(); gs.current_round=gs.round_index=1;gs.max_round=1
    gs.room_random_seed=20261003;gs.claim_protection=False;gs.tactical_call=False
    gs.game_server.gamestate_manager=SimpleNamespace(cleanup_game_state_complete=AsyncMock())
    gs.db_manager.store_guobiao_game_record.return_value='lanshi-unit-record'
    winners=[];results=[]
    def deal(s):
        pool=Counter({t:4 for t in TILES})
        winning=tiles('11345m334455p234s');pool.subtract(winning)
        s.round_random_seed=20261003+s.current_round
        available=list(pool.elements())
        for i,p in enumerate(s.player_list):
            p.hand_tiles=winning.copy() if i==0 else [available.pop() for _ in range(13)]
            p.combination_tiles=[];p.combination_mask=[];p.discard_tiles=[];p.huapai_list=[]
            p.waiting_tiles=set();p.has_draw_slot=False;p.last_drawn_tile=None
        s.tiles_list=available
        physical=Counter(available)
        for p in s.player_list:physical.update(p.hand_tiles)
        assert physical==Counter({t:4 for t in TILES})
    async def ask():
        assert 'hu_self' in gs.action_dict[0]
        gs.action_queues[0].put_nowait({'action_type':'hu_self','action_tick':gs.server_action_tick})
    async def result(s,**payload):
        results.append(payload)
        winners.append(s.player_list[payload['hepai_player_index']].original_player_index)
    monkeypatch.setattr(module,'init_guobiao_tiles',deal)
    monkeypatch.setattr(module,'broadcast_result',result)
    gs.broadcast_game_start=AsyncMock();gs.broadcast_ask_hand_action=ask
    gs.broadcast_game_end=AsyncMock();gs.run_hu_result_ready_phase=AsyncMock()
    asyncio.run(asyncio.wait_for(gs.game_loop_chinese(),5))
    assert len(results)==4 and len(set(winners))==4
    for result in results:
        assert result['hu_score']==5
        assert result['hu_fan']==['一般高*1','门前清','喜相逢*1','自摸']
        assert sorted(result['score_changes'].values())==[-10,-10,-10,30]
        assert result['is_qianggang'] is False
        assert result['ron_discarder_index'] is None
        assert result['hepai_tile'] == 34
    assert [p.score for p in gs.player_list]==[0,0,0,0]
    assert gs.current_round==4 and gs.round_index==4
    rounds=gs.game_record['game_round']
    assert len(rounds)==4
    for data in rounds.values():
        ticks=data['action_ticks']
        assert ticks[-1]==['end']
        wins=[tick for tick in ticks if tick[0]=='hu_self']
        assert len(wins)==1 and wins[0][2]==5
        assert wins[0][3]==['一般高*1','门前清','喜相逢*1','自摸']
    gs.broadcast_game_end.assert_awaited_once()
    gs.db_manager.store_guobiao_game_record.assert_called_once()
    gs.game_server.gamestate_manager.cleanup_game_state_complete.assert_awaited_once()
    # 按生产动作生成的四局牌谱，逐帧与任意节点重建必须一致。
    for key, data in rounds.items():
        frames = RecordSim(gs.game_record).apply_all(key)
        for node in range(len(data['action_ticks']) + 1):
            actual = RecordSim(gs.game_record).goto_action(key, node)
            assert actual == frames[node]
    legacy = {**gs.game_record, 'game_title': dict(gs.game_record['game_title'])}
    legacy['game_title'].pop('rule_version', None)
    assert RecordSim(legacy).apply_all('round_index_4') == RecordSim(gs.game_record).apply_all('round_index_4')


@pytest.mark.parametrize('kind', ['hu_self', 'hu_first'])
def test_wrong_win_fines_only_offender_and_records_without_ending_round(monkeypatch, kind):
    """Run the real loop through a below-minimum claim; stop at its ready screen."""
    module = importlib.import_module('server.gamestate.game_guobiao.GuobiaoGameState')
    actions = importlib.import_module('server.gamestate.game_guobiao.wait_action')
    gs = state()
    gs.current_round = gs.round_index = gs.max_round = 1
    gs.room_random_seed = 20261003
    gs.claim_protection = gs.tactical_call = False
    winner = 0 if kind == 'hu_self' else 1

    def deal(s):
        pool = Counter({t: 4 for t in TILES})
        winning = tiles('123456m789p111s55z' if winner == 0 else '11345m334455p23s')
        pool.subtract(winning)
        pool[18] -= 1  # 已有一张弃牌，排除首巡天和/地和。
        if winner != 0:
            pool[34] -= 1
        available = list(pool.elements())
        for i, p in enumerate(s.player_list):
            p.hand_tiles = winning.copy() if i == winner else [available.pop() for _ in range(13)]
            if i == 0 and winner != 0:
                p.hand_tiles.append(34)
            p.combination_tiles = []; p.combination_mask = []; p.huapai_list = []
            p.discard_tiles = [18] if i == 0 else []
            p.waiting_tiles = set(); p.has_draw_slot = False; p.last_drawn_tile = None
        s.tiles_list = available
        s.round_random_seed = 20261003

    async def ask_hand():
        p = gs.player_list[0]
        if kind == 'hu_self':
            assert gs.result_dict['hu_self'][0] == 4
            data = {'action_type': 'hu_self'}
        else:
            data = {'action_type': 'cut', 'TileId': 34, 'cutClass': False, 'cutIndex': p.hand_tiles.index(34)}
        gs.action_queues[0].put_nowait(data)

    async def ask_others():
        assert gs.result_dict['hu_first'][0] == 4
        for seat, allowed in gs.action_dict.items():
            if allowed:
                gs.action_queues[seat].put_nowait({'action_type': 'hu_first' if seat == 1 else 'pass'})

    class AtWrongWinReady(Exception):
        pass

    async def ready(_):
        raise AtWrongWinReady

    monkeypatch.setattr(module, 'init_guobiao_tiles', deal)
    monkeypatch.setattr(actions, 'broadcast_do_action', AsyncMock())
    monkeypatch.setattr(actions, 'finalize_claim_protection', AsyncMock())
    gs.broadcast_game_start = AsyncMock()
    gs.broadcast_ask_hand_action = ask_hand
    gs.broadcast_ask_other_action = ask_others
    gs.broadcast_result = AsyncMock()
    gs.run_hu_result_ready_phase = ready
    with pytest.raises(AtWrongWinReady):
        asyncio.run(asyncio.wait_for(gs.game_loop_chinese(), 3))
    assert [p.score for p in gs.player_list] == [-40 if i == winner else 0 for i in range(4)]
    payload = gs.broadcast_result.await_args.kwargs
    assert payload['hu_score'] == 4 and payload['hu_fan'][-1] == '错和'
    assert len(payload['hepai_player_hand']) == 14
    assert len(gs.player_list[winner].hand_tiles) == (14 if winner == 0 else 13)
    ticks = gs.game_record['game_round']['round_index_1']['action_ticks']
    assert ticks[-1][0] == kind and '错和' in ticks[-1][3]
    assert ['end'] not in ticks and gs.current_round == 1
    assert gs.player_list[winner].record_counter.cuohe_times == 1
    assert all(p.round_number_history == [1] for p in gs.player_list)
    replay = RecordSim(gs.game_record)
    frames = replay.apply_all('round_index_1')
    assert len(replay.players[winner].tile_list) == (14 if winner == 0 else 13)
    # 无头 RecordSim 的 is_hu 标记只用于血战轮转；本规则核对分数与实体手牌。
    assert replay.players[winner].score == -40
    assert RecordSim(gs.game_record).goto_action('round_index_1', len(ticks)) == frames[-1]
