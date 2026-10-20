"""Winning snapshots preserve variable-length closed hands and the actual tile."""
import asyncio
import copy

import pytest

from .boardcast import state_packet
from .test_hongque_reconnect import stage_fixture, pass_pending
from .record import code_to_id


async def variable_hand_fixture(*, winner_seat=1, self_draw=False):
    dealer = winner_seat if self_draw else (winner_seat + 3) % 4
    game, server = await stage_fixture('turn', dealer=dealer)
    winner = game.players[winner_seat]
    winner.hand = 'DY2 DY6 DY4'.split()
    winner.melds = [dict(kind='sequence', tiles=tiles.split(), from_player=dealer, claimed_tile=tiles.split()[0])
                    for tiles in ('BY7 BY8 BY9', 'EY2 EY4 EY6', 'AY8 BX6 BY4 CX2')]
    reserved = set(winner.hand + ['DY8'] + [tile for meld in winner.melds for tile in meld['tiles']])
    available = [colour + str(number) for colour in
                 ('AX', 'AY', 'BX', 'BY', 'CX', 'CY', 'DX', 'DY', 'EX', 'EY', 'FX', 'FY', 'GX', 'GY')
                 for number in range(1, 10) if colour + str(number) not in reserved]
    for player in game.players:
        player.drawn_tile = None
        if player is not winner:
            player.hand = [available.pop() for _ in range(11)]
        server.user_id_to_connection[player.user_id].websocket.packets.clear()
    drawing_player = winner if self_draw else game.players[dealer]
    drawing_player.hand.append('DY8')
    drawing_player.drawn_tile = 'DY8'
    game.wall = available
    game.events = []
    initial = [copy.deepcopy(state_packet(game, seat, sync_mode='reconnect')) for seat in range(4)]
    if self_draw:
        await game.submit_action(winner.user_id, 'win')
    else:
        await game.submit_action(drawing_player.user_id, 'discard', tile='DY8')
        candidate = next(c for c in game.claim_options[winner_seat] if c['kind'] == 'win')
        await game.submit_action(winner.user_id, 'claim', candidate_id=candidate['id'])
        await pass_pending(game)
    if game._round_task:
        game._round_task.cancel()
        await asyncio.gather(game._round_task, return_exceptions=True)
    return game, server, initial


@pytest.mark.parametrize('winner_seat', range(4))
@pytest.mark.parametrize('self_draw', (False, True))
def test_variable_hand_win_is_complete_for_live_reconnect_spectator_and_record(winner_seat, self_draw):
    async def run():
        game, server, initial = await variable_hand_fixture(winner_seat=winner_seat, self_draw=self_draw)
        try:
            result = game.round_result['winners'][0]
            assert result['hand'] == 'DY2 DY6 DY4 DY8'.split()
            assert (result['base'], result['fan_total'], result['points']) == (5, 6, 30)
            assert sum(len(meld['tiles']) for meld in result['melds']) + len(result['hand']) == 14
            assert game.players[winner_seat].hand == initial[winner_seat]['hongque_state']['hand']
            for seat in range(4):
                restored = state_packet(game, seat, sync_mode='reconnect')['hongque_state']
                assert restored['round_result']['winners'][0] == result
                packets = server.user_id_to_connection[game.players[seat].user_id].websocket.packets
                assert any(p.get('hongque_state', {}).get('round_result') == game.round_result for p in packets)
            wins = [tick for tick in game.game_record['game_round']['round_index_1']['action_ticks']
                    if tick[0].startswith('hu_')]
            assert len(wins) == 1 and wins[0][5] == code_to_id('DY8')
            assert wins[0][2] == 30
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())
