"""Captain roles and match accounting layered over the ordinary riichi engine."""
from copy import deepcopy

from ...game_calculation.riichi.washizu_doubles import (
    PROFILE, FIXED_OPTIONS, enabled, valid_roles, captain_scores, result,
)


def configure(game, room):
    game.is_team_game = enabled(game.sub_rule)
    if not game.is_team_game:
        return
    from ...room.room_seats import get_seats
    seats = get_seats(room)
    roles = room.get('team_role_by_seat')
    if not valid_roles(roles) or len(seats) != 4 or any(uid < 0 for uid in seats):
        raise ValueError('鹫巢双打需要A队主将、A队助手、B队主将、B队助手各一人')
    # Consume seat instances so duplicate bot IDs cannot collapse different roles.
    remaining = list(enumerate(seats))
    for player in game.player_list:
        entry = next(((i, uid) for i, uid in remaining if uid == player.user_id), None)
        if entry is None:
            raise ValueError('玩家与房间座位不一致')
        remaining.remove(entry)
        role = roles[entry[0]]
        player.team_id, player.team_role = role // 2, 'captain' if role % 2 == 0 else 'assistant'
        player.team_role_id = role
    if remaining:
        raise ValueError('鹫巢双打需要四名玩家')
    game.detailed_config.update(FIXED_OPTIONS)
    game.open_tobi = True
    game.open_xiru = False
    game.starting_scores = None
    game.team_result = None
    game.team_round_history = []
    game.doubles_busted_scores = None
    game.riichi_contributors = []


def shuffle_players(game, rng):
    if not getattr(game, 'is_team_game', False):
        rng.shuffle(game.player_list)
        return
    ordered = sorted(game.player_list, key=lambda p: p.team_role_id)
    offset = rng.randrange(4)
    game.player_list[:] = ordered[offset:] + ordered[:offset]


def record_profile(game):
    if not getattr(game, 'is_team_game', False):
        return
    title = game.game_record['game_title']
    players = sorted(game.player_list, key=lambda p: p.original_player_index)
    title.update(washizu_doubles_profile=deepcopy(PROFILE),
                 team_by_player_index=[p.team_id for p in players],
                 team_role_by_player_index=[p.team_role for p in players])


def public_state(game):
    if not getattr(game, 'is_team_game', False):
        return None
    players = sorted(game.player_list, key=lambda p: p.player_index)
    return {'version': PROFILE['version'], 'team_by_player_index': [p.team_id for p in players],
            'team_role_by_player_index': [p.team_role for p in players],
            'captain_scores': captain_scores(players), 'result': game.team_result}


def record_round(game):
    if not getattr(game, 'is_team_game', False):
        return
    summary = {'round_index': game.round_index, 'round': game.current_round,
               'captain_scores': captain_scores(game.player_list), 'end_reason': game.hu_class}
    game.team_round_history.append(summary)
    game.game_record['doubles_rounds'] = list(game.team_round_history)


def should_end(game, round_after):
    scores = captain_scores(game.player_list)
    if any(score < 0 for score in scores.values()):
        if game.doubles_busted_scores is None:
            game.doubles_busted_scores = dict(scores)
        return True
    if game.hu_class in ('jiuzhongjiupai', 'four_wind_abort', 'four_kan_abort', 'four_riichi_abort', 'three_ron_abort'):
        return False
    return round_after > game.max_round * 4


def accepted_deposit(game, player):
    if getattr(game, 'is_team_game', False):
        game.riichi_contributors.append(player.original_player_index)


def collected_deposits(game):
    if getattr(game, 'is_team_game', False):
        game.riichi_contributors.clear()


def refund_current_deposit(game, player):
    if getattr(game, 'is_team_game', False):
        game.riichi_contributors.remove(player.original_player_index)


def finalize(game):
    if getattr(game, '_riichi_finalized', False):
        return
    if len(game.riichi_contributors) != game.riichi_sticks:
        raise ValueError('鹫巢双打供托账目不一致')
    by_original = {p.original_player_index: p for p in game.player_list}
    refunds = {index: 0 for index in by_original}
    for contributor in game.riichi_contributors:
        refunds[contributor] += 1000
    for index, amount in refunds.items():
        by_original[index].score += amount
    game.riichi_contributors.clear()
    game.riichi_sticks = 0
    game._riichi_finalized = True
    game.team_result = result(captain_scores(game.player_list), game.doubles_busted_scores)
    game.team_result['deposit_refunds'] = refunds
    for player in game.player_list:
        player.riichi_points = game.team_result['match_points'][player.team_id]
    title = game.game_record.setdefault('game_title', {})
    title.update(riichi_final_scores=[by_original[i].score for i in sorted(by_original)],
                 riichi_final_sticks=0, doubles_result=deepcopy(game.team_result))
    game.game_record['doubles_result'] = deepcopy(game.team_result)


def assign_final_ranks(game):
    winner = game.team_result['winner_team_id']
    for player in game.player_list:
        player.record_counter.rank_result = 1 if winner is None or player.team_id == winner else 2
    game.player_list.sort(key=lambda p: (p.record_counter.rank_result, p.team_id, p.team_role_id))
