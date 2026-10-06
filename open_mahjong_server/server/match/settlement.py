"""Shared end-of-game bridge. Custom rooms never change ratings."""
from .rating_rules import QUEUES
from .riichi_rank_calculator import LEGACY_PT_ALGORITHM


def settle_ranked_game(state):
    queue = getattr(state, 'match_queue_type', None)
    if state.room_type != 'match':
        return f'{state.max_round}/4'
    spec = QUEUES.get(queue)
    if not spec or spec.rule != state.room_rule:
        raise ValueError('排位对局的规则与队列不一致')
    if (len(state.player_list) != spec.player_count
            or (spec.rule == 'riichi' and (getattr(state, 'sub_rule', '') == 'riichi/sanma') != (spec.player_count == 3))
            or (spec.rule == 'sichuan' and getattr(state, 'sub_rule', 'sichuan/standard') != (spec.sub_rule or 'sichuan/standard'))):
        raise ValueError('排位对局的人数或子规则与队列不一致')
    state.game_record['game_title']['match_queue_type'] = queue
    state.game_record['game_title']['match_tier'] = spec.tier
    state.game_record['game_title']['rating_rule'] = spec.rating_rule
    state.game_record['game_title']['rating_game_id'] = str(state.gamestate_id)
    players = [dict(user_id=p.user_id,place=p.record_counter.rank_result) for p in state.player_list]
    options = dict(ended_at=state.game_record['game_title'].get('end_time'))
    if spec.rule == 'riichi':
        from .sanma_rank_calculator import SANMA_PT_ALGORITHM
        options['rating_algorithm'] = getattr(state, 'match_rating_algorithm',
            SANMA_PT_ALGORITHM if spec.player_count == 3 else LEGACY_PT_ALGORITHM)
        for player, payload in zip(state.player_list, players):
            payload['match_points'] = player.riichi_points
    result = state.db_manager.settle_rated_game(state.gamestate_id, queue, players, **options)
    for player in state.player_list:
        for key,value in result[str(player.user_id)].items():
            setattr(player,key,value)
        if spec.rule == 'guobiao':
            player.guobiao_rank,player.guobiao_score = player.rank_after,player.score_after
    state.game_record['game_title']['rating_result'] = result
    if spec.rule == 'riichi':
        state.game_record['game_title']['rating_algorithm'] = next(iter(result.values())).get(
            'rating_algorithm', LEGACY_PT_ALGORITHM)
    return f'{spec.rounds}/4' + ('_sanma' if spec.player_count == 3 else '') + '_rank'


def rating_result_fields(player):
    keys = ('rating_rule','rating_system','rank_before','rank_after','score_before','score_after','rating_pt',
            'elo_before','elo_after','elo_delta','rating_games','rating_algorithm','rating_match_points',
            'rating_game_multiplier','rating_tier_multiplier','rating_rank_cost')
    if getattr(player, 'rating_system', None) == 'grade':
        keys = tuple(key for key in keys if not key.startswith('elo_'))
    return dict(user_id=player.user_id, **{key:getattr(player,key,None) for key in keys})
