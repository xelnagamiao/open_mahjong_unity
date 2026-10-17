"""Replay fan values must match settlement, without losing achievement counts."""
import asyncio
from types import SimpleNamespace as NS

import pytest

from server.database.subrule_fans import collect_subrule_fans
from .record import fan_labels
from .test_hongque_reconnect import PacketSocket, pass_pending, stage_fixture


def test_fan_labels_preserve_both_count_and_total():
    assert fan_labels([
        {"name": "双同顺", "value": 2, "count": 2, "total": 4},
        {"name": "清顺", "value": 1, "count": 4, "total": 4},
        {"name": "门清", "value": 1, "count": 1, "total": 1},
    ]) == ["双同顺*2|4", "清顺*4|4", "门清|1"]
    assert fan_labels([]) == []


@pytest.mark.parametrize("stage", ["self_draw", "ron", "multi_ron"])
@pytest.mark.parametrize("dealer", range(4))
def test_recorded_fans_match_authoritative_winners(stage, dealer):
    async def run():
        game, _ = await stage_fixture(stage, dealer=dealer)
        try:
            result = game.build_state(0)["round_result"]
            ticks = game.game_record["game_round"]["round_index_1"]["action_ticks"]
            wins = [tick for tick in ticks if tick[0].startswith("hu_")]
            assert len(wins) == len(result["winners"])
            for tick, winner in zip(wins, result["winners"]):
                assert tick[1] == winner["player"]
                assert tick[2] == winner["points"]
                labels = tick[3]
                assert len(labels) == len(winner["fans"])
                for label, fan in zip(labels, winner["fans"]):
                    assert "|" in label, f"replay loses fan value: {label!r}"
                    assert int(label.rsplit("|", 1)[1]) == fan["total"]
                assert sum(int(label.rsplit("|", 1)[1]) for label in labels) == winner["fan_total"]
                original = game.players[winner["player"]].original_player_index
                assert collect_subrule_fans(game.game_record, original, "hongque/v1.6") == {
                    fan["name"]: fan["count"] for fan in winner["fans"]
                }
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


@pytest.mark.parametrize('self_draw', [True, False])
@pytest.mark.parametrize('seat', range(4))
def test_cross_boundary_same_flower_reaches_settlement_spectator_reconnect_and_record(self_draw, seat):
    async def run():
        dealer = seat if self_draw else (seat + 3) % 4
        game, server = await stage_fixture('turn', dealer=dealer)
        try:
            winner = game.players[seat]
            winner.hand = 'AY1 AY9 GY2 GY9 FY3 FY9'.split()
            winner.melds = [
                {'kind': 'triplet', 'tiles': 'CY1 DX1 DY1'.split(), 'from_player': (seat + 1) % 4, 'claimed_tile': 'CY1'},
                {'kind': 'triplet', 'tiles': 'DY8 EY8 FY8'.split(), 'from_player': (seat + 2) % 4, 'claimed_tile': 'DY8'},
            ]
            # Controlled state: only the reported hand may claim the winning tile.
            for player in game.players:
                if player is not winner:
                    player.hand = []
            watcher = NS(user_id=12000001, websocket=PacketSocket(), event_disconnecting=False)
            server.user_id_to_connection[watcher.user_id] = watcher
            game.realtime_spectators = [NS(user_id=watcher.user_id, host_user_id=winner.user_id)]
            if self_draw:
                winner.drawn_tile = 'AY1'
                await game.submit_action(winner.user_id, 'win')
            else:
                winner.hand.remove('AY1')
                discarder = game.players[dealer]
                discarder.hand = ['AY1']
                discarder.drawn_tile = 'AY1'
                await game.submit_action(discarder.user_id, 'discard', tile='AY1')
                candidate = next(c for c in game.claim_options[seat] if c['kind'] == 'win')
                await game.submit_action(winner.user_id, 'claim', candidate_id=candidate['id'])
                await pass_pending(game)
            result = game.round_result['winners'][0]
            assert (result['base'], result['fan_total'], result['points']) == (3, 2, 6)
            assert result['fans'] == [{'name': '双同花', 'value': 2, 'count': 1, 'total': 2}]
            assert winner.score == 6
            assert any(packet.get('hongque_state', {}).get('round_result') == game.round_result
                       for packet in watcher.websocket.packets)
            await game.player_disconnect(winner.user_id)
            await game.player_reconnect(winner.user_id)
            packets = server.user_id_to_connection[winner.user_id].websocket.packets
            reconnect = next(p for p in reversed(packets) if p['type'] == 'gamestate/hongque/reconnect')
            assert reconnect['hongque_state']['round_result'] == game.round_result
            ticks = game.game_record['game_round']['round_index_1']['action_ticks']
            win = next(tick for tick in ticks if tick[0].startswith('hu_'))
            assert win[2:4] == [6, ['双同花|2']]
            assert collect_subrule_fans(game.game_record, winner.original_player_index, 'hongque/v1.6') == {'双同花': 1}
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())
