"""The selected three-gold action must survive transport, scoring and replay."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .get_action import handle_action
from .state_machine import Phase as P
from .test_state import WIN, act, state_with


@pytest.mark.parametrize('base', [10, 15, 20])
@pytest.mark.parametrize('streak', [0, 2])
@pytest.mark.parametrize('case', [
    'normal', 'also_standard_win', 'flower_replacement', 'kong_replacement',
    'last_four', 'mandatory_flower_choice', 'last_four_flower_choice',
])
def test_sanjin_request_keeps_forty_points_in_all_settlement_channels(base, streak, case):
    async def run():
        # Two golds before the draw; the incomplete hand cannot win normally.
        hand = [11, 12, 14, 16, 18, 21, 23, 25, 27, 29, 31, 33, 35, 37, 39, 39]
        if case == 'also_standard_win':
            hand = WIN[:12] + [29, 29, 39, 39]
        elif case == 'kong_replacement':
            hand = hand[:11] + [39, 39]
        elif case == 'last_four_flower_choice':
            hand = hand[:-3] + [39, 39, 39]
        state = state_with(hand)
        player = state.player_list[0]
        state.detailed_config['base_score'] = base
        state.dealer_streak = streak
        state.step_time = 5
        player.remaining_time = 20
        player.huapai_list = [41, 42, 43]
        if case in ('mandatory_flower_choice', 'last_four_flower_choice'):
            player.huapai_list = [tile for tile in range(41, 46) for _ in range(4)]
        if case == 'kong_replacement':
            player.combination_tiles = ['G19']
            player.combination_mask = [[2, 19, 0, 19, 0, 19, 2, 19]]
            player.after_ready_kong = True
        state.update_reserve()
        state.start_game_recording()
        state.start_round_recording()
        state.machine.phase = P.RESPONSE
        if case in ('last_four', 'last_four_flower_choice'):
            tile = 47 if case == 'last_four_flower_choice' else 39
            state.tiles_list = [tile, 21, 22, 23] + [11] * state.dead_wall_count
        elif case == 'flower_replacement':
            state.tiles_list = [44] + [11] * 50 + [39]
        else:
            state.tiles_list = [39] + [11] * 50
            if case == 'kong_replacement':
                state.tiles_list.reverse()
        state.open_action_window(state.draw_for(0, 'kong' if case == 'kong_replacement' else 'normal'))
        if case == 'flower_replacement':
            assert state.machine.phase == P.REPLACEMENT
            act(state, {0: dict(action_type='buhua')})
        assert 'sanjin' in state.action_dict[0]
        if case == 'also_standard_win':
            assert 'hu_self' in state.action_dict[0]
        elif case == 'normal':
            assert 'hu_self' not in state.action_dict[0]

        socket = SimpleNamespace(send_json=AsyncMock())
        server = SimpleNamespace(
            players={'self': SimpleNamespace(user_id=player.user_id)},
            gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda _: state),
        )
        await handle_action(server, 'self', dict(
            type='gamestate/fuzhou/send_action', gamestate_id=state.gamestate_id,
            action_tick=state.server_action_tick, action='sanjin', targetTile=0,
        ), socket)
        socket.send_json.assert_not_awaited()
        await state.resolve_action_window()

        # 三金倒 suppresses other special awards, but keeps every base item.
        flower_points = (30 if case in ('mandatory_flower_choice', 'last_four_flower_choice')
                         else 4 if case == 'flower_replacement' else 3)
        expected = dict(base=base, flowers=flower_points,
                        gold=3, kongs=2 if case == 'kong_replacement' else 0,
                        dealer=streak * 3, three_gold=40)
        points = sum(expected.values())
        quote = state.round_settlement['score']
        assert quote['special'] == 'three_gold'
        assert quote['pattern_points'] == 40
        assert {row['id']: row['points'] for row in quote['items']} == expected
        assert quote['points'] == points
        assert state.round_changes == [points * 3, -points, -points, -points]

        for viewer in range(4):
            result = state.build_final_settlement_payload(viewer)['show_result_info']
            assert result['fuzhou_special'] == 'three_gold'
            assert result['hu_score'] == points
            assert result['hu_fan'].count('FZ|three_gold|40|三金倒') == 1
            assert {row['id']: row['points'] for row in result['fuzhou_fan_details']} == expected
            assert state.restore_payloads(viewer)[1]['show_result_info']['hu_fan'] == result['hu_fan']
        assert [p.score for p in state.player_list] == state.round_changes
        assert player.record_counter.recorded_fans == [list(expected)]
        entry = state.deferred_hu_settlements[0]
        assert entry['recorded'] and entry['points'] == points
        assert entry['fan_names'].count('FZ|three_gold|40|三金倒') == 1

    asyncio.run(run())
