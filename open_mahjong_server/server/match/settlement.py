"""Shared end-of-game bridge. Custom rooms never change ratings."""
from .rating_rules import QUEUES


def settle_ranked_game(state):
    queue = getattr(state, 'match_queue_type', None)
    if state.room_type != 'match':
        return f'{state.max_round}/4'
    spec = QUEUES.get(queue)
    if not spec or spec.rule != state.room_rule:
        raise ValueError('排位对局的规则与队列不一致')
    state.game_record['game_title']['match_queue_type'] = queue
    state.game_record['game_title']['match_tier'] = spec.tier
    result = state.db_manager.settle_rated_game(state.gamestate_id, queue,
        [dict(user_id=p.user_id,place=p.record_counter.rank_result) for p in state.player_list])
    for player in state.player_list:
        for key,value in result[str(player.user_id)].items():
            setattr(player,key,value)
        if spec.rule == 'guobiao':
            player.guobiao_rank,player.guobiao_score = player.rank_after,player.score_after
    state.game_record['game_title']['rating_result'] = result
    return f'{spec.rounds}/4_rank'


def rating_result_fields(player):
    keys = ('rating_rule','rating_system','rank_before','rank_after','score_before','score_after','rating_pt',
            'elo_before','elo_after','elo_delta','rating_games')
    return dict(user_id=player.user_id, **{key:getattr(player,key,None) for key in keys})
