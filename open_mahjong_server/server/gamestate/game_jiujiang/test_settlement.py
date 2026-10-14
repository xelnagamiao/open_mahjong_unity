"""从真实计分进入结算，交叉验证各座位支付、广播与牌谱字段。"""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock, patch

import pytest

from .test_state import make_state, drawn, ticks
from ...game_calculation.jiujiang.test_scoring import PLAIN


CASES = [(source, winner, payer)
         for source in ('self_draw', 'discard', 'robbing_kong')
         for winner in range(4)
         for payer in ([None] if source == 'self_draw' else [i for i in range(4) if i != winner])]


@pytest.mark.parametrize('source,winner,payer', CASES)
def test_settlement_all_win_sources_and_seats(source, winner, payer):
    async def run():
        state = make_state()
        state.current_round = 4
        before = {}
        for i, player in enumerate(state.player_list):
            player.original_player_index = (i + 2) % 4
            player.score = 10 * player.original_player_index
            before[player.original_player_index] = player.score
            # 一半输家听牌、一半未听；报听赔付单独叠加，不代替结构判定。
            player.hand_tiles = PLAIN[:-1] if i % 2 else [11] * 13
            player.declared_ready = i == (winner + 1) % 4
            state.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(websocket=AsyncMock())
        player = state.player_list[winner]
        player.combination_tiles = ['g45', 'G46']
        player.combination_mask = [[1,45,0,45,0,45,0,45], [2,46]*4]
        player.huapai_list = [47]*4
        hand = [11,12,13,21,22,23,25,25]
        if source == 'self_draw':
            drawn(player, hand)
        else:
            player.hand_tiles = hand[:-1]
            if source == 'discard': state.player_list[payer].discard_tiles = [25]
        detail = state.score_candidate(winner, source, 25)
        assert detail and detail['exposed_flower_points'] == 6
        hu_class = 'hu_self' if payer is None else {1:'hu_first',2:'hu_second',3:'hu_third'}[(winner-payer)%4]
        state.pending_winners = [{'index':winner,'payer':payer,'source':source,'tile':25,'detail':detail,'hu_class':hu_class}]
        expected = {i:0 for i in range(4)}
        for loser in ([i for i in range(4) if i != winner] if payer is None else [payer]):
            # 输家没有明花，即便胡牌者明花为6分也不能向输家额外收取。
            amount = detail['base_score'] + (10 if loser == (winner+1)%4 else 0)
            amount *= 2 if 0 in (loser,winner) else 1
            expected[loser] -= amount
            expected[winner] += amount
        assert await state._settle_hand(before)
        assert sum(expected.values()) == 0 and state.next_dealer() == winner
        for i, other in enumerate(state.player_list):
            assert other.score == before[other.original_player_index] + expected[i]
            assert other.score_history == [f'{expected[i]:+d}' if expected[i] else '0']
            assert other.round_number_history == [4]
        recorded = next(t for t in ticks(state) if t[0] == hu_class)
        assert recorded[1:3] == [winner,detail['base_score']]
        assert recorded[4:6] == [[expected[i] for i in range(4)],25]
        assert recorded[6:] == ([] if payer is None else [0,payer,int(source == 'discard')])
        assert sum(row['points'] for t in ticks(state) if t[:2] == ['jiujiang','payments'] for row in t[2]) == expected[winner]
        assert any(t[:2] == ['jiujiang','reveal_kongs'] for t in ticks(state))
        result = state._terminal_result['show_result_info']
        assert result['score_changes'] == {p.original_player_index:expected[p.player_index] for p in state.player_list}
        assert result['revealed_angang_masks'][winner] == [[2,46]*4]
        assert result.get('is_qianggang') == (True if source == 'robbing_kong' else None)
    asyncio.run(run())


def test_passing_a_win_does_not_lock_later_wins():
    async def run():
        state = make_state()
        state.player_list[0].discard_tiles = [25]
        state.player_list[1].hand_tiles = PLAIN[:-1]
        allowed = state.check_discard_actions(25)
        assert 'hu_first' in allowed[1]
        await state.resolve_discard_responses({1:{'action_type':'pass'}}, allowed)
        assert not state.pending_winners
        assert state.score_candidate(1, 'discard', 25) is not None
        assert state.game_status == 'deal_card'
    asyncio.run(run())


@pytest.mark.parametrize('room_type',['custom','events'])
def test_persistence_keeps_independent_rule_identity_and_all_player_indexes(room_type):
    import json
    from ...database.db_manager import DatabaseManager
    state=make_state()
    state.game_record['game_title']['room_type']=room_type
    if room_type=='events': state.game_record['game_title']['event_id']='jiujiang-storage-test'
    cursor=Mock();connection=Mock();connection.cursor.return_value=cursor
    database=SimpleNamespace(_get_connection=Mock(return_value=connection),_put_connection=Mock())
    with patch('server.database.player_recent_records.update_player_recent_records') as recent, \
         patch('server.database.scene_stats.record_game_metrics') as metrics:
        game_id=DatabaseManager.store_jiujiang_game_record(database,state.game_record,state.player_list,room_type,'1/4')
    assert game_id and len(cursor.execute.call_args_list)==5
    saved=json.loads(cursor.execute.call_args_list[0].args[1][1])
    assert saved['game_title']['rule']=='jiujiang' and saved['game_title']['sub_rule']=='jiujiang/standard'
    for call in cursor.execute.call_args_list[1:]:
        assert call.args[1][6:10]==('jiujiang','jiujiang/standard','1/4',room_type)
    recent.assert_called_once_with(cursor,game_id,state.game_record)
    assert metrics.call_args.args[4]['rule']=='jiujiang'
    connection.commit.assert_called_once();database._put_connection.assert_called_once_with(connection)
