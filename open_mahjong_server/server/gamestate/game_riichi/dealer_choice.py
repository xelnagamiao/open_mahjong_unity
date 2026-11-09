"""Optional Washizu all-last dealer stop, after scoring and before advancement."""
import asyncio
import math
import time

from ...game_calculation.riichi.washizu_doubles import enabled, captain_scores
from ..public.round_end_timing import hu_result_ready_wait_seconds, HU_CONFIRM_COUNTDOWN_SEC

CHOICE_SECONDS = 20
ACTIONS = ('dealer_continue', 'dealer_end')
NEXT_STATUS = 'round_end_dealer_choice'


def prepare(game, winner_index, fan_count):
    if (not enabled(game.sub_rule) or not game.detailed_config.get('agari_yame')
            or game.current_round != game.max_round * 4 or winner_index != 0
            or game.next_status == 'match_end' or getattr(game, '_cuohe_triggered', False)
            or any(score < 0 for score in captain_scores(game.player_list).values())
            or any(p.score >= game.player_list[0].score for p in game.player_list[1:])):
        return False
    clock = getattr(game, '_dealer_choice_clock', time.monotonic)
    visible_at = clock() + hu_result_ready_wait_seconds(fan_count) - HU_CONFIRM_COUNTDOWN_SEC
    game._dealer_choice = dict(dealer_index=0, visible_at=visible_at,
                               deadline=visible_at + CHOICE_SECONDS, selected=None,
                               clock=clock, queue=asyncio.Queue())
    game.next_status = NEXT_STATUS
    return True


def snapshot(game):
    choice = getattr(game, '_dealer_choice', None)
    if not choice:
        return None
    now = choice['clock']()
    return dict(dealer_index=choice['dealer_index'], selected=choice['selected'],
                available_in_ms=max(0, math.ceil((choice['visible_at'] - now) * 1000)),
                remaining_ms=max(0, math.ceil((choice['deadline'] - now) * 1000)))


async def submit(game, connection_id, message):
    """The authenticated dealer and current result identity are both required."""
    choice = getattr(game, '_dealer_choice', None)
    if not choice or game.game_status != 'waiting_dealer_choice' or choice['selected']:
        return False
    now = choice['clock']()
    if now < choice['visible_at'] or now >= choice['deadline']:
        return False
    connection = game.game_server.players.get(connection_id)
    dealer = game.player_list[choice['dealer_index']]
    if connection is None or connection.user_id != dealer.user_id:
        return False
    from ..public.settlement_sync import snapshot as settlement_snapshot
    result_id = settlement_snapshot(game)['result_id']
    if not result_id or message.get('settlement_id') != result_id:
        return False
    action = message.get('action')
    if action not in ACTIONS:
        return False
    choice['selected'] = 'continue' if action == 'dealer_continue' else 'end'
    choice['queue'].put_nowait(choice['selected'])
    return True


async def run(game):
    """Only the dealer replies; other seats never enter a ready barrier."""
    choice = getattr(game, '_dealer_choice', None)
    if not choice or game.next_status != NEXT_STATUS:
        return False
    game.game_status = 'waiting_dealer_choice'
    game.action_dict = {i: [] for i in range(4)}
    game.waiting_players_list = []
    await asyncio.sleep(max(0, choice['visible_at'] - choice['clock']()))
    dealer = game.player_list[choice['dealer_index']]
    reason = 'player'
    if dealer.user_id <= 10:
        selection, reason = 'end', 'bot'
    else:
        try:
            selection = await asyncio.wait_for(choice['queue'].get(),
                timeout=max(0, choice['deadline'] - choice['clock']()))
        except asyncio.TimeoutError:
            selection, reason = 'end', 'timeout'
    choice['selected'] = selection
    game.next_status = 'round_end_by_ready' if selection == 'continue' else 'match_end'
    # Round metadata avoids introducing an unknown action tick into old replayers.
    round_data = game.game_record['game_round'][f'round_index_{game.round_index}']
    round_data['dealer_choice'] = dict(dealer_index=choice['dealer_index'],
        original_player_index=dealer.original_player_index, choice=selection, reason=reason)
    return True


def clear(game):
    game._dealer_choice = None
