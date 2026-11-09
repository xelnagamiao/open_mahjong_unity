"""The offered kong targets must obey the same rules as their execution."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .action_check import check_action_hand_action, hand_kong_candidates, refresh_waiting_tiles
from .boardcast import broadcast_ask_hand_action, reconnected_send_pending_ask_for_viewer
from .test_rule_branches import game
from .test_sanma import sanma
from .wait_action import _is_valid_cut_action, wait_action
from ..public.ai.get_action import get_action
from ..public.outbound_pipe import drain_viewer


# Screenshot: 333455556p 2345s, drawing 3p. The fourth 5p is red.
SCREENSHOT_HAND = [23, 23, 23, 24, 25, 25, 25, 205, 26, 32, 33, 34, 305, 23]


def kan_game(three_player=True, restriction='shape'):
    g = sanma() if three_player else game()
    g.current_player_index = 0
    g.game_status = 'waiting_hand_action'
    p = g.player_list[0]
    p.hand_tiles = list(SCREENSHOT_HAND)
    p.tag_list = ['riichi']
    p.has_draw_slot = True
    p.combination_tiles = []
    p.combination_mask = []
    g.detailed_config['riichi_kan_rule'] = restriction
    refresh_waiting_tiles(g, 0)
    g.action_dict = check_action_hand_action(g, 0)
    sockets = {p.user_id: SimpleNamespace(websocket=AsyncMock(), user_id=p.user_id) for p in g.player_list}
    g.game_server.user_id_to_connection = sockets
    g.game_server.players = {str(uid): conn for uid, conn in sockets.items()}
    g.send_to_realtime_spectators = AsyncMock()
    return g, sockets


@pytest.mark.parametrize('three_player', [False, True])
@pytest.mark.parametrize('restriction', ['wait', 'shape', 'shape_yaku'])
def test_screenshot_only_offers_3p_for_live_reconnect_and_spectator(three_player, restriction):
    async def run():
        g, sockets = kan_game(three_player, restriction)
        assert _is_valid_cut_action(g, 0, {'action_type': 'angang', 'target_tile': 23})
        assert not _is_valid_cut_action(g, 0, {'action_type': 'angang', 'target_tile': 25})
        await broadcast_ask_hand_action(g)
        for seat in range(len(g.player_list)):
            await drain_viewer(g, seat)
        live = sockets[100].websocket.send_json.call_args_list[-1].args[0]['ask_hand_action_info']
        assert live['kong_candidates'] == {'angang': [23], 'jiagang': []}
        for uid in sockets.keys() - {100}:
            other = sockets[uid].websocket.send_json.call_args_list[-1].args[0]['ask_hand_action_info']
            assert other['kong_candidates'] == {'angang': [], 'jiagang': []}
        tick = g.server_action_tick
        clock = g._ask_delivered_at[0]
        for viewer in (100, 101):
            await reconnected_send_pending_ask_for_viewer(g, viewer, 0)
            restored = sockets[viewer].websocket.send_json.call_args_list[-1].args[0]['ask_hand_action_info']
            assert restored['kong_candidates'] == live['kong_candidates']
            assert restored['action_tick'] == tick
        assert g._ask_delivered_at[0] == clock
    asyncio.run(run())


@pytest.mark.parametrize('constraint', ['none', 'last_tile', 'four_kans', 'rinshan_limit'])
def test_unlocked_hand_offers_both_quads_unless_wall_or_kan_limit(constraint):
    async def run():
        g, sockets = kan_game()
        g.player_list[0].tag_list = []
        if constraint == 'last_tile':
            g.tiles_list = g.tiles_list[:g.dead_wall_count]
        elif constraint == 'four_kans':
            g.total_kans = 4
        elif constraint == 'rinshan_limit':
            g.rinshan_count = 8
        g.action_dict = check_action_hand_action(g, 0)
        await broadcast_ask_hand_action(g)
        info = sockets[100].websocket.send_json.call_args_list[-1].args[0]['ask_hand_action_info']
        expected = [23, 25] if constraint == 'none' else []
        assert info['kong_candidates']['angang'] == expected
        assert g.action_dict[0].count('angang') == len(expected)
        for tile in expected:
            assert _is_valid_cut_action(g, 0, {'action_type': 'angang', 'target_tile': tile})
    asyncio.run(run())


def test_rejected_5p_resends_same_window_then_3p_completes_without_timeout():
    async def run():
        g, sockets = kan_game()
        await broadcast_ask_hand_action(g)
        tick = g.server_action_tick
        clock = g._ask_delivered_at[0]
        hand = list(g.player_list[0].hand_tiles)
        recovered = []

        async def on_packet(packet):
            if packet['type'] != 'gamestate/riichi/broadcast_hand_action':
                return
            info = packet['ask_hand_action_info']
            assert info['action_tick'] == tick
            assert info['kong_candidates']['angang'] == [23]
            assert g._ask_delivered_at[0] == clock
            assert g.player_list[0].hand_tiles == hand
            assert g.waiting_players_list == [0]
            recovered.append(info)
            await get_action(g, '100', 'angang', None, None, None, 23, action_tick=tick)

        sockets[100].websocket.send_json.side_effect = on_packet
        await get_action(g, '100', 'angang', None, None, None, 205, action_tick=tick)
        await asyncio.wait_for(wait_action(g), timeout=3)
        assert len(recovered) == 1
        assert g.player_list[0].combination_tiles == ['G23']
        assert g.game_status == 'deal_card_after_gang'
        assert g._pending_kan is None
        assert 205 in g.player_list[0].hand_tiles
    asyncio.run(run())


@pytest.mark.parametrize('drawn', [25, 205])
def test_red_five_candidate_uses_normalized_face_but_keeps_physical_tiles(drawn):
    g, _ = kan_game()
    p = g.player_list[0]
    p.hand_tiles = [25, 25, 205 if drawn == 25 else 25, 11, 12, 13,
                    31, 32, 33, 36, 37, 29, 29, drawn]
    g.sub_rule = 'riichi/standard'
    before = list(p.hand_tiles)
    assert hand_kong_candidates(g, 0)['angang'] == [25]
    assert _is_valid_cut_action(g, 0, {'action_type': 'angang', 'target_tile': drawn})
    assert p.hand_tiles == before


def test_smart_bot_filters_illegal_quad_even_when_it_appears_first():
    from ..public.ai import riichi_smart_bot_ai as bot

    async def run():
        g, _ = kan_game()
        p = g.player_list[0]
        p.hand_tiles = [25, 25, 25, 205, 23, 23, 23, 24, 26, 32, 33, 34, 305, 23]
        g.waiting_players_list = [0]
        submitted = AsyncMock()

        async def run_cpu(state, compute, *args):
            return compute(*args)

        with patch.object(bot, 'evaluate_hand', return_value=(0, 0)), \
                patch.object(bot, 'run_room_bot_cpu', side_effect=run_cpu), \
                patch.object(bot, 'submit_bot_action', submitted):
            await bot._handle_hand_action(g, 0, g.action_dict[0], p, set())
        args = submitted.call_args.args
        assert args[3] == 'angang' and args[-1] == 23
    asyncio.run(run())
