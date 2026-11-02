import asyncio
from collections import Counter
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock

import pytest

from .HongqueGameState import HongqueGameState
from . import get_action, w_rules
from .record import snapshot_round_header
from .state_machine import HongqueStatus
from .test_hongque_reconnect import PacketSocket, pass_pending
from .wait_action import ClaimWindow, validate_submitted_action
from ...database.statistics_catalog import resolve_record_statistics_identity
from ...database.subrule_fans import collect_subrule_fans


async def opened(**settings):
    connections = {uid: NS(user_id=uid, websocket=PacketSocket(), event_disconnecting=False)
                   for uid in range(11000001, 11000005)}
    server = NS(user_id_to_connection=connections, match_manager=None,
                gamestate_manager=NS(cleanup_game_state_complete=AsyncMock()))
    game = HongqueGameState(server, dict(room_id=12345, player_list=list(connections),
        game_round=4, sub_rule=w_rules.SUB_RULE, round_timer=300, step_timer=90, **settings))
    game.tactical_grace_seconds = .01
    game._schedule_bot_if_needed = lambda: None
    await game._start_round()
    return game, server


def stage_hands(game, hands, melds=None):
    for seat, player in enumerate(game.players):
        player.hand = list(hands.get(seat, []))
        player.melds = [{"kind": w_rules.classify_meld(group).kind, "tiles": group,
                         "from_player": (seat + 3) % 4, "claimed_tile": group[0]}
                        for group in (melds or {}).get(seat, [])]
        player.discards = []
        player.drawn_tile = player.hand[-1] if seat == game.current_player_index and player.hand else None
    game.wall = ["HX9", "HX8", "HX7"]
    snapshot_round_header(game)


@pytest.mark.parametrize("colourless,size", [(True, 144), (False, 126)])
def test_opening_inventory_configuration_and_reconnect(colourless, size):
    async def run():
        game, server = await opened(hongque_colourless=colourless, hongque_min_fan=3, hongque_min_points=12)
        try:
            physical = Counter(game.wall + [t for p in game.players for t in p.hand])
            assert sum(physical.values()) == size and set(physical.values()) == {2}
            assert sorted(len(p.hand) for p in game.players) == [11, 11, 11, 12]
            for seat in range(4):
                expected = game.build_state(seat, sync_mode="reconnect")
                await game.player_disconnect(game.players[seat].user_id)
                await game.player_reconnect(game.players[seat].user_id)
                packet = next(p for p in reversed(server.user_id_to_connection[game.players[seat].user_id].websocket.packets)
                              if p['type'] == 'gamestate/hongque/reconnect')['hongque_state']
                assert packet['sub_rule'] == w_rules.SUB_RULE
                assert packet['hongque_colourless'] == colourless
                assert (packet['hongque_min_fan'], packet['hongque_min_points']) == (3, 12)
                assert packet['hand'] == expected['hand']
                assert packet['legal_actions'] == expected['legal_actions']
                assert all('hand' not in p for p in packet['players'])
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


def test_ordinary_triplet_cannot_be_submitted_but_rainbow_triplet_can():
    async def run():
        game, _ = await opened()
        try:
            stage_hands(game, {0: ['GX3'], 1: ['AX3', 'CX3', 'DX8'],
                               2: [f'{c}X3' for c in 'ABCDEF'] + ['EX8']})
            await game.submit_action(game.players[0].user_id, 'discard', tile='GX3')
            assert game.phase == 'claim' and 1 not in game.claim_options
            with pytest.raises(ValueError):
                await game.submit_action(game.players[1].user_id, 'claim', candidate_id='peng_first')
            peng = next(c for c in game.claim_options[2] if c['kind'] == 'triplet')
            await game.submit_action(game.players[2].user_id, 'claim', candidate_id=peng['id'])
            await pass_pending(game)
            assert game.current_player_index == 2
            assert game.game_status == 'onlycut_after_action'
            assert game.players[2].melds[0]['tiles'] == [f'{c}X3' for c in 'ABCDEFG']
            with pytest.raises(ValueError, match='补牌'):
                await game.submit_action(game.players[2].user_id, 'win')
            await game.submit_action(game.players[2].user_id, 'supplement')
            assert game.game_status == 'waiting_hand_action'
            assert game.players[2].supplements == 1 and game.players[1].supplements == 0
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


@pytest.mark.parametrize('winner,first', [('double', 'triplet'), ('triplet', 'rainbow'), ('rainbow', 'sequence')])
def test_claim_upgrade_priority_is_authoritative(winner, first):
    async def run():
        game, _ = await opened(hongque_min_fan=100)
        hands = {
            'double': 'AX3 AX3 CX3 CX3 GX3 EX9'.split(),
            'triplet': 'AX3 BX3 CX3 DX3 EX3 FX3 EX9'.split(),
            'rainbow': 'AX1 BX2 CX4 DX5 EX6 FX7 EX9'.split(),
            'sequence': 'GX1 GX2 EX9'.split(),
        }
        try:
            stage_hands(game, {0: ['GX3'], 1: hands[first], 2: hands[winner]})
            await game.submit_action(game.players[0].user_id, 'discard', tile='GX3')
            a = next(c for c in game.claim_options[1] if c['kind'] == first)
            b = next(c for c in game.claim_options[2] if c['kind'] == winner)
            await game.submit_action(game.players[1].user_id, 'claim', candidate_id=a['id'])
            assert game.claim_window.active.player_index == 1
            await game.submit_action(game.players[2].user_id, 'claim', candidate_id=b['id'])
            await pass_pending(game)
            assert game.current_player_index == 2
            assert game.players[2].melds[0]['kind'] == winner
            assert not game.players[1].melds and len(game.players[1].hand) == len(hands[first])
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


def test_multitile_kong_records_drawn_copy_and_target_before_mutating():
    async def run():
        game, _ = await opened()
        try:
            stage_hands(game, {0: 'HX1 HX5 AX9 HX3'.split()}, {0: ['HX1 HX3 HX5'.split()]})
            candidate = next(c for c in game._legal_turn_actions(game.players[0])[1] if len(c['hand_tiles']) == 3)
            await game.submit_action(game.players[0].user_id, 'kong', candidate_id=candidate['id'])
            assert game.players[0].hand == ['AX9']
            assert Counter(game.players[0].melds[0]['tiles']) == Counter('HX1 HX1 HX3 HX3 HX5 HX5'.split())
            tick = game.game_record['game_round']['round_index_1']['action_ticks'][-1]
            assert tick == ['jg', 1143, 'T', 0, [1143, 1141, 1145]]
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


def test_multi_ron_statistics_and_dealer_are_w_specific():
    async def run():
        game, _ = await opened()
        try:
            stage_hands(game, {0: ['AX3'], 1: ['AX1', 'AX2'], 2: ['AX1', 'AX2']})
            await game.submit_action(game.players[0].user_id, 'discard', tile='AX3')
            for seat in (1, 2):
                await game.submit_action(game.players[seat].user_id, 'claim', candidate_id='ron')
            await pass_pending(game)
            result = game.round_result
            assert [w['player'] for w in result['winners']] == [1, 2]
            assert game.players[0].score == 0 and game.players[3].score == 0
            assert game.players[1].score == game.players[2].score > 0
            assert resolve_record_statistics_identity(game.game_record) == ('hongque', w_rules.SUB_RULE)
            for w in result['winners']:
                assert collect_subrule_fans(game.game_record, game.players[w['player']].original_player_index, w_rules.SUB_RULE) == {
                    f['name']: f['count'] for f in w['fans']}
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


@pytest.mark.parametrize('bot', [0, 2, 3])
def test_bot_queue_preserves_tsumogiri_and_simple_efficiency(monkeypatch, bot):
    async def cpu(state, function, *args, **kwargs):
        return function(*args, **kwargs)
    monkeypatch.setattr(get_action, 'run_room_bot_cpu', cpu)
    monkeypatch.setattr(get_action, 'BOT_ACTION_DELAY', 0)
    async def run():
        game = HongqueGameState(None, dict(room_id=987, player_list=[101, bot, bot, bot],
                                          game_round=4, sub_rule=w_rules.SUB_RULE))
        game._in_wait_action = True
        game.phase = 'turn'
        game.current_player_index = 1
        game.players[1].hand = ['AX1', 'AX2', 'AX3']
        game.players[1].drawn_tile = 'AX3'
        await get_action.bot_turn(game, game.action_tick)
        queued = game.action_queues[1].get_nowait()
        assert queued['action_type'] == ('discard' if bot == 0 else 'win')
        if bot == 0:
            assert queued['tile'] == 'AX3'
        game.phase = 'claim'
        game._transition(HongqueStatus.WAITING_ACTION_AFTER_CUT)
        game.claim_options = {1: [{'id': 'ron', 'kind': 'win', 'priority': 100}]}
        game.claim_window = ClaimWindow(game.claim_options, {1})
        await get_action.bot_claim(game, 1, game.action_tick)
        queued = game.action_queues[1].get_nowait()
        assert queued['action_type'] == ('pass' if bot == 0 else 'claim')
        await game.cleanup_game_state()
    asyncio.run(run())
