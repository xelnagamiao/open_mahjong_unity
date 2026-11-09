"""Exercise dealer authority, result identity, restoration and both branches."""
import asyncio
from types import SimpleNamespace
import pytest
from unittest.mock import AsyncMock, patch

from . import dealer_choice
from .test_washizu_doubles import captain_game
from .boardcast import broadcast_result
from ..public.settlement_sync import snapshot, restore_result


def eligible():
    game, sockets = captain_game()
    game.current_round = 8
    game.max_round = 2
    game.honba = 3
    game.next_status = 'round_end_by_ready'
    game.hu_class = 'hu_self'
    game._dealer_choice_clock = lambda: 100.0
    for player, score in zip(game.player_list, (43200, 11700, 32700, 12400)):
        player.score = score
    game.game_record['game_round'] = {f'round_index_{game.round_index}': {'action_ticks': []}}
    return game, sockets


@pytest.mark.parametrize('change', ['not_last', 'not_dealer', 'tied', 'behind', 'bust', 'other_rule', 'disabled'])
def test_only_a_unique_leading_last_dealer_has_a_choice(change):
    game, _ = eligible()
    winner = 0
    if change == 'not_last': game.current_round = 7
    if change == 'not_dealer': winner = 1
    if change == 'tied': game.player_list[2].score = game.player_list[0].score
    if change == 'behind': game.player_list[1].score = 50000
    if change == 'bust': game.player_list[2].score = -1
    if change == 'other_rule': game.sub_rule = 'riichi/washizu'
    if change == 'disabled': game.detailed_config['agari_yame'] = False
    assert not dealer_choice.prepare(game, winner, 2)
    assert not getattr(game, '_dealer_choice', None)


def test_assistant_dealer_has_the_same_right_without_changing_team_scoring():
    game, _ = eligible()
    game.player_list[0].team_role = 'assistant'
    assert dealer_choice.prepare(game, 0, 2)
    assert game.next_status == dealer_choice.NEXT_STATUS
    assert game.current_round == 8 and game.honba == 3


async def open_choice():
    game, sockets = eligible()
    assert dealer_choice.prepare(game, 0, 2)
    await broadcast_result(game, hepai_player_index=0, hu_class='hu_self',
        hu_fan=['断幺九', '宝牌1'], next_status=game.next_status,
        player_to_score={p.player_index: p.score for p in game.player_list})
    game.game_status = 'waiting_dealer_choice'
    return game, sockets


def test_all_viewers_and_reconnect_receive_the_same_decision_and_deadline():
    async def check():
        game, sockets = await open_choice()
        records = [connection.websocket.send_json.call_args.args[0] for connection in sockets.values()]
        assert all(p['settlement_sync']['phase'] == 'waiting_dealer_choice' for p in records)
        assert len({p['settlement_sync']['result_id'] for p in records}) == 1
        first = snapshot(game)
        assert first['player_to_ready'] == {}
        assert first['dealer_choice']['available_in_ms'] > 0
        game._dealer_choice['clock'] = lambda: 110.0
        restored = restore_result(game, 1)
        assert restored['settlement_sync']['restore']
        assert restored['settlement_sync']['dealer_choice']['remaining_ms'] < first['dealer_choice']['remaining_ms']
        assert restored['settlement_sync']['result_id'] == first['result_id']
    asyncio.run(check())


@pytest.mark.parametrize('selection', ['continue', 'end'])
def test_authenticated_current_dealer_can_decide_once(selection):
    async def check():
        game, _ = await open_choice()
        pending = game._dealer_choice
        pending['clock'] = lambda: pending['visible_at']
        result_id = snapshot(game)['result_id']
        dealer = str(game.player_list[0].user_id)
        other = str(game.player_list[1].user_id)
        message = {'action': 'dealer_' + selection, 'settlement_id': result_id}
        assert not await dealer_choice.submit(game, other, message)
        assert not await dealer_choice.submit(game, dealer, {**message, 'settlement_id': 'old-result'})
        assert not await dealer_choice.submit(game, dealer, {**message, 'action': 'ready'})
        assert await dealer_choice.submit(game, dealer, message)
        assert not await dealer_choice.submit(game, dealer, message)
        before = ([p.score for p in game.player_list], game.current_round, game.honba, game.round_index)
        assert await dealer_choice.run(game)
        assert game.next_status == ('round_end_by_ready' if selection == 'continue' else 'match_end')
        assert ([p.score for p in game.player_list], game.current_round, game.honba, game.round_index) == before
        assert game.game_record['game_round'][f'round_index_{game.round_index}']['dealer_choice']['choice'] == selection
        dealer_choice.clear(game)
        assert snapshot(game).get('dealer_choice') is None
    asyncio.run(check())


def test_early_late_and_unauthenticated_decisions_cannot_end_the_match():
    async def check():
        game, _ = await open_choice()
        dealer = str(game.player_list[0].user_id)
        message = {'action': 'dealer_end', 'settlement_id': snapshot(game)['result_id']}
        assert not await dealer_choice.submit(game, dealer, message)
        pending = game._dealer_choice
        pending['clock'] = lambda: pending['visible_at']
        assert not await dealer_choice.submit(game, 'spectator', message)
        pending['clock'] = lambda: pending['deadline']
        assert not await dealer_choice.submit(game, dealer, message)
        assert pending['selected'] is None
    asyncio.run(check())


@pytest.mark.parametrize('bot', [False, True])
def test_timeout_or_bot_ends_without_waiting_for_the_other_three(bot):
    async def check():
        game, _ = await open_choice()
        pending = game._dealer_choice
        pending['clock'] = lambda: pending['deadline']
        if bot: game.player_list[0].user_id = 1
        assert await dealer_choice.run(game)
        assert game.next_status == 'match_end'
        assert game.action_dict == {i: [] for i in range(4)}
        assert game.waiting_players_list == []
        entry = game.game_record['game_round'][f'round_index_{game.round_index}']['dealer_choice']
        assert entry['reason'] == ('bot' if bot else 'timeout')
    asyncio.run(check())


@pytest.mark.parametrize('selection', ['continue', 'end'])
def test_live_game_loop_waits_before_advancing_and_skips_other_seat_ready(selection):
    async def check():
        game, sockets = captain_game()
        game.current_round = 8
        game.max_round = 2
        starting = [40300, 11700, 35600, 12400]
        game._player_starting_score = lambda seat: starting[seat]
        game.game_server.gamestate_manager = SimpleNamespace(
            get_game_state_by_gamestate_id=lambda _: game, cleanup_game_state_complete=AsyncMock())
        rounds = []
        async def finish_hand():
            rounds.append((game.current_round, game.honba))
            winner = 0 if len(rounds) == 1 else 1
            game.current_player_index = winner
            game.hu_class = 'hu_self'
            game.result_dict['hu_self'] = dict(han=2, fu=30, yaku=['断幺九', '宝牌1'],
                score=3000, cost=dict(main=1000, additional=1000))
            game.game_status = 'END'
        game.wait_action = finish_hand
        original_run = dealer_choice.run
        async def decide(state):
            if not getattr(state, '_dealer_choice', None):
                return await original_run(state)
            assert state.current_round == 8 and state.honba == 0
            state.game_status = 'waiting_dealer_choice'
            pending = state._dealer_choice
            pending['clock'] = lambda: pending['visible_at']
            from ..gamestate_router import handle_send_action
            message = dict(gamestate_id=state.gamestate_id, action='dealer_' + selection,
                settlement_id=snapshot(state)['result_id'])
            await handle_send_action(state.game_server, str(state.player_list[0].user_id), message, None)
            return await original_run(state)
        async def immediate_sleep(_): pass
        ready = AsyncMock()
        with patch('server.gamestate.game_riichi.doubles.shuffle_players', lambda *_: None), \
             patch('server.gamestate.game_riichi.dealer_choice.run', decide), \
             patch('server.gamestate.game_riichi.RiichiGameState.asyncio.sleep', immediate_sleep), \
             patch('server.gamestate.game_riichi.RiichiGameState.run_synced_hu_ready_phase', ready), \
             patch('server.gamestate.game_riichi.RiichiGameState.vote_checkpoint', new=AsyncMock()):
            await game.game_loop_riichi()
        assert rounds == ([(8, 0), (8, 1)] if selection == 'continue' else [(8, 0)])
        ready.assert_not_awaited()
        assert game.game_record['game_round']['round_index_1']['dealer_choice']['choice'] == selection
        assert game.game_record['game_round']['round_index_1']['action_ticks'][-1] == ['end']
        assert sum(p.score for p in game.player_list) == sum(starting)
        assert game.team_result is not None
    asyncio.run(check())
