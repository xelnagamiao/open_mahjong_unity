"""The real opening loop must expose one unambiguous boundary to every viewer."""
import asyncio
import importlib
from unittest.mock import patch

import pytest

from .test_sanma import sanma
from .boardcast import _build_game_start_payload_for_viewer, reconnected_send_pending_ask_for_viewer
from ..public.outbound_pipe import drain_viewer
from ..public.hand_slot_utils import resolve_timeout_cut

SUB_RULES = ['standard', 'sixteen', 'sanma', 'four_jokers', 'huahun',
             'blood_battle', 'transparent', 'transparent_realtime', 'xiaolin', 'kshen']


class OpeningReached(Exception):
    pass


@pytest.mark.parametrize('sub_rule', SUB_RULES)
@pytest.mark.parametrize('mode', ['none', 'dealer', 'last', 'all', 'chain', 'pass'])
def test_opening_boundary_delivery_record_and_reconnect(sub_rule, mode):
    async def run():
        game = sanma(sub_rule='guobiao/' + sub_rule)
        game.room_random_seed = 42
        count = len(game.player_list)
        size = 16 if sub_rule == 'sixteen' else 13
        flower = 58  # Not a flower-joker in the huahun branch.
        opening_requests = []

        def initialize(state):
            for seat, player in enumerate(state.player_list):
                player.hand_tiles = ([11, 11, 19, 21, 22, 23, 31, 32, 33, 41, 42, 43, 44, 45, 46, 47, 29])[:size + (seat == 0)]
                if mode in ('all', 'pass') or mode in ('dealer', 'chain') and seat == 0 or mode == 'last' and seat == count - 1:
                    player.hand_tiles[-1] = flower - seat
            state.tiles_list = [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 42, 43, 44, 45, 46, 47] * 3
            if mode == 'chain': state.tiles_list[-2] = 57
            state.backward_tiles_list_type = 'double'

        async def respond():
            if game.game_status == 'waiting_buhua_round':
                opening_requests.append(game.current_player_index)
                return not (mode == 'pass' and game.current_player_index == count - 1)
            assert game.game_status == 'waiting_hand_action'
            assert game.current_player_index == 0
            assert game._opening_buhua_complete_pending
            for seat in range(count): await drain_viewer(game, seat)
            for seat, player in enumerate(game.player_list):
                messages = [call.args[0] for call in game.game_server.user_id_to_connection[player.user_id].websocket.send_json.await_args_list]
                endings = [m['ask_hand_action_info'] for m in messages if m.get('ask_hand_action_info', {}).get('opening_buhua_complete')]
                assert len(endings) == 1
                assert endings[0]['player_index'] == 0
                assert bool(endings[0]['action_list']) == (seat == 0)
                assert len(player.hand_tiles) == size + (seat == 0)
                snapshot = _build_game_start_payload_for_viewer(game, seat)
                private = [p for p in snapshot['players_info'] if p['hand_tiles'] is not None]
                assert len(private) == 1 and private[0]['player_index'] == seat
                assert private[0]['has_draw_slot'] == (seat == 0 and bool(player.huapai_list))
                if sub_rule == 'transparent_realtime':
                    layouts = snapshot['washizu_layouts']
                    assert all(not layout['draw_pinned'] for layout in layouts.values())
                    assert bool(layouts[0]['drawn_key']) == bool(game.player_list[0].huapai_list)
            ticks = game.game_record['game_round']['round_index_1']['action_ticks']
            assert [tick for tick in ticks if tick[0] == 'reset'] == [['reset', 0]]
            assert ticks[-1] == ['reset', 0]
            first = game.player_list[0]
            tile, moqie = resolve_timeout_cut(first, opening_first_discard=True)
            assert tile in first.hand_tiles and moqie == bool(first.huapai_list)
            await reconnected_send_pending_ask_for_viewer(game, first.user_id, 0)
            pending = game.game_server.user_id_to_connection[first.user_id].websocket.send_json.await_args.args[0]
            assert pending['ask_hand_action_info']['opening_buhua_complete']
            raise OpeningReached

        game.wait_action = respond
        module = importlib.import_module('.GuobiaoGameState', __package__)
        with patch.object(module, 'init_guobiao_tiles', initialize):
            with pytest.raises(OpeningReached):
                await game.game_loop_chinese()
        if mode == 'chain': assert opening_requests.count(0) >= 2
        if mode == 'none': assert not opening_requests
    asyncio.run(run())
