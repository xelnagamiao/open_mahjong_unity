"""2026-10-12 标注图：赔未听牌输家自己的明花，发财基础分只计一次。"""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .test_state import drawn, make_state, ticks
from ..game_taiwan.TaiwanGameState import TaiwanGameState
from ...game_calculation.jiujiang.patterns import structural_waits


UNREADY = [11, 13, 15, 17, 19, 21, 23, 25, 27, 29, 31, 34, 37]


def set_tiles(player, hand, melds=(), flowers=()):
    player.hand_tiles = list(hand)
    player.combination_tiles = list(melds)
    player.huapai_list = list(flowers)
    player.combination_mask = []
    for code in melds:
        kind, tile = code[0], int(code[1:])
        if kind == 'G':
            mask = [2, tile] * 4
        elif kind == 's':
            mask = [1, tile - 1, 0, tile, 0, tile + 1]
        else:
            mask = [1, tile] + [0, tile] * (3 if kind == 'g' else 2)
        player.combination_mask.append(mask)


async def settle_and_check(state, winner, payer, tile, expected, expected_flowers, rotation=0):
    """同时验证终局分变、四端广播、牌谱、历史和重连不重复扣分。"""
    state.current_round = 4
    before = {}
    for player in state.player_list:
        player.original_player_index = (player.player_index + rotation) % 4
        player.score = 10 * player.original_player_index
        before[player.original_player_index] = player.score
        state.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(websocket=AsyncMock())
    source = 'self_draw' if payer is None else 'discard'
    detail = state.score_candidate(winner, source, tile)
    assert detail
    hu_class = 'hu_self' if payer is None else {1: 'hu_first', 2: 'hu_second', 3: 'hu_third'}[(winner - payer) % 4]
    state.pending_winners = [dict(index=winner, payer=payer, tile=tile, source=source, detail=detail, hu_class=hu_class)]
    assert await state._settle_hand(before)
    result = state._terminal_result['show_result_info']
    assert sum(expected.values()) == 0
    assert {row['payer']: row['unready_flowers'] for row in result['jiujiang_payments']} == expected_flowers
    assert result['hu_score'] == detail['base_score']
    assert result['score_changes'] == {(i + rotation) % 4: value for i, value in expected.items()}
    for player in state.player_list:
        delta = expected[player.player_index]
        assert player.score == before[player.original_player_index] + delta
        assert player.score_history == [f'{delta:+d}' if delta else '0']
        assert player.round_number_history == [4]
        socket = state.game_server.user_id_to_connection[player.user_id].websocket
        socket.send_json.assert_any_await(state._terminal_result)
        socket.reset_mock()
        with patch.object(TaiwanGameState, 'player_reconnect', AsyncMock()):
            await state.player_reconnect(player.user_id)
        socket.send_json.assert_awaited_once_with(state._terminal_result)
        assert player.score == before[player.original_player_index] + delta
        assert len(player.score_history) == 1
    payment_tick = next(t for t in ticks(state) if t[:2] == ['jiujiang', 'payments'])
    assert payment_tick[2] == result['jiujiang_payments']
    win_tick = next(t for t in ticks(state) if t[0] == hu_class)
    assert win_tick[4] == [expected[i] for i in range(4)]
    return result


@pytest.mark.parametrize('rotation', range(4))
def test_annotated_round_7_three_replaced_fa_pay_four_not_six(rotation):
    async def run():
        state = make_state()
        for player in state.player_list:
            set_tiles(player, UNREADY)
        # 庄家在下方（0）；右侧胡牌者（1）明白杠、补发财，胡牌分为3。
        set_tiles(state.player_list[1], [13, 14, 15, 21, 21, 31, 32, 33, 35, 36], ['g46'], [47])
        # 上方放炮者（2）补了3张发财，明花基础分仍只有1。
        state.player_list[2].huapai_list = [47] * 3
        state.player_list[2].discard_tiles = [34]
        result = await settle_and_check(state, 1, 2, 34, {0: 0, 1: 4, 2: -4, 3: 0}, {2: 1}, rotation)
        assert result['hu_score'] == 3
        assert result['hu_fan'] == ['明箭刻（1分）', '明杠（1分）', '发财（1分）']
    asyncio.run(run())


@pytest.mark.parametrize('rotation', range(4))
def test_annotated_round_14_dealer_selfdraw_collects_four_six_six(rotation):
    async def run():
        state = make_state()
        # 右侧庄家（0）自摸1分，自身没有任何明花。
        set_tiles(state.player_list[0], [], ['k31'])
        drawn(state.player_list[0], [12, 13, 16, 17, 18, 22, 22, 33, 34, 35, 14])
        # 上方（1）只赔一个明杠；暗杠在终局翻开也不进入赔分。
        set_tiles(state.player_list[1], [13, 13, 17, 19, 19, 36, 36], ['G37', 'g29'])
        # 左侧（2）和下方（3）各有一个明箭刻及补过的发财，各赔2分。
        set_tiles(state.player_list[2], [13, 14, 16, 17, 19, 23, 25, 32, 33, 34], ['k46'], [47])
        set_tiles(state.player_list[3], [15, 16, 16, 17, 18, 18, 19, 34, 35, 35], ['k45'], [47])
        assert all(not structural_waits(tuple(p.hand_tiles), tuple(p.combination_tiles)) for p in state.player_list[1:])
        result = await settle_and_check(state, 0, None, 14, {0: 16, 1: -4, 2: -6, 3: -6}, {1: 1, 2: 2, 3: 2}, rotation)
        assert result['hu_score'] == 1 and result['hu_fan'] == ['自摸（1分）']
        assert all(row['dealer_multiplier'] == 2 for row in result['jiujiang_payments'])
    asyncio.run(run())


@pytest.mark.parametrize('count,flowers', [(0, 0), (1, 1), (2, 1), (3, 1), (4, 4)])
@pytest.mark.parametrize('self_draw', [False, True])
def test_replaced_fa_compensation_in_real_settlement(count, flowers, self_draw):
    state = make_state()
    for player in state.player_list:
        set_tiles(player, UNREADY)
    state.player_list[2].huapai_list = [47] * count
    # 其余发财在弃牌里，只按已补的数量算，四张都补才有四方来财。
    state.player_list[2].discard_tiles = [47] * (4 - count)
    set_tiles(state.player_list[1], [12, 13, 16, 17, 18, 22, 22, 33, 34, 35], ['k45'])
    if self_draw:
        drawn(state.player_list[1], state.player_list[1].hand_tiles + [14])
    detail = state.score_candidate(1, 'self_draw' if self_draw else 'discard', 14)
    assert detail['base_score'] == 1 + int(self_draw)
    changes, rows = state._settlement_for_winner(dict(index=1, payer=None if self_draw else 2, detail=detail))
    assert next(row for row in rows if row['payer'] == 2)['unready_flowers'] == flowers
    assert changes[2] == -(detail['base_score'] + flowers)
    assert sum(changes.values()) == 0


@pytest.mark.parametrize('declared', [False, True])
def test_structurally_ready_payer_does_not_pay_flowers_even_without_ron_score(declared):
    state = make_state()
    # 开口的普通牌形可以零分听牌；听牌判断不能依赖胡牌分或报听标记。
    payer = state.player_list[2]
    set_tiles(payer, [21, 22, 23, 31, 32, 33, 17, 18, 25, 25], ['s12'])
    assert structural_waits(tuple(payer.hand_tiles), tuple(payer.combination_tiles)) == (16, 19)
    assert state.score_candidate(2, 'discard', 19) is None
    payer.huapai_list = [47] * 3
    payer.declared_ready = declared
    changes, rows = state._settlement_for_winner(dict(index=1, payer=2, detail={'base_score':3, 'exposed_flower_points':5}))
    assert rows[0]['unready_flowers'] == 0
    assert changes[2] == -(3 + (10 if declared else 0))


@pytest.mark.parametrize('melds,expected', [
    ([], 0), (['k45'], 1), (['k46'], 1), (['g45'], 2), (['g46'], 2),
    (['g11'], 1), (['G11'], 0), (['G45'], 0), (['k45', 'k46'], 4),
    (['g45', 'k46'], 5), (['G45', 'k46'], 1), (['k45', 'G46'], 1),
    (['G45', 'G46'], 0), (['g45', 'g46'], 6), (['s12'], 0),
])
def test_unready_payer_compensates_only_public_flower_categories(melds, expected):
    state = make_state()
    payer = state.player_list[2]
    set_tiles(payer, UNREADY[:13 - 3 * len(melds)], melds)
    assert not structural_waits(tuple(payer.hand_tiles), tuple(melds))
    changes, rows = state._settlement_for_winner(dict(index=1, payer=2, detail={'base_score':3}))
    assert rows[0]['unready_flowers'] == expected
    assert changes == {0:0, 1:3 + expected, 2:-3 - expected, 3:0}


def test_robbed_added_kong_compensation_uses_restored_pung():
    async def run():
        state = make_state()
        payer = state.player_list[0]
        set_tiles(payer, [], ['k45'])
        drawn(payer, UNREADY[:10] + [45])
        set_tiles(state.player_list[1], [11,12,13,21,22,23,31,32,33,17,18,19,45])
        await state.execute_jiagang(0, 45)
        assert 'hu_first' in state.action_dict[1]
        await state.resolve_rob_kong_responses({1:{'action_type':'hu_first'}}, state.action_dict)
        assert payer.combination_tiles == ['k45'] and 45 not in payer.hand_tiles
        item = state.pending_winners[0]
        assert item['source'] == 'robbing_kong' and item['detail']['base_score'] == 3
        changes, rows = state._settlement_for_winner(item)
        assert rows[0]['unready_flowers'] == 1
        assert changes == {0:-8, 1:8, 2:0, 3:0}
    asyncio.run(run())
