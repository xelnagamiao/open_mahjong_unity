"""Final M points and shared ranks must reach ranked settlement unchanged."""
import asyncio
import pytest

from .test_protocol import connected
from .boardcast import broadcast_game_end
from ...match.riichi_rank_calculator import riichi_pt_details


@pytest.mark.parametrize('scores,points,ranks', [
    ((40000,30000,20000,10000), (60,10,-20,-50), (1,2,3,4)),
    ((35000,35000,20000,10000), (35,35,-20,-50), (1,1,3,4)),
    ((40000,25000,25000,10000), (60,-5,-5,-50), (1,2,2,4)),
    ((40000,30000,15000,15000), (60,10,-35,-35), (1,2,3,3)),
    ((25000,25000,25000,25000), (0,0,0,0), (1,1,1,1)),
])
def test_mleague_awards_include_oka_once_and_share_ties(scores, points, ranks):
    game, _ = connected('mleague')
    for p, score in zip(game.player_list, scores): p.score = score
    game._finalize_scores_and_history()
    game._assign_final_ranks()
    ordered = sorted(game.player_list, key=lambda p: p.original_player_index)
    assert [p.riichi_points for p in ordered] == list(points)
    assert [p.record_counter.rank_result for p in ordered] == list(ranks)
    assert sum(p.riichi_points for p in ordered) == pytest.approx(0)
    expected_pt = [riichi_pt_details('advanced','banzhuang','七段',m)['rating_pt'] for m in points]
    assert sum(expected_pt) == pytest.approx(-21)


def test_triple_top_deposit_remainder_does_not_create_different_places_or_repeat():
    game, _ = connected('mleague')
    for p, score in zip(game.player_list, (30000,30000,30000,9000)):
        p.score = score
        p.score_history = [str(score - 25000)]
    game.riichi_sticks = 1
    game._finalize_scores_and_history()
    game._assign_final_ranks()
    assert [p.score for p in game.player_list] == [30400,30300,30300,9000]
    assert [p.riichi_points for p in game.player_list] == [17.1,17,16.9,-51]
    assert [p.record_counter.rank_result for p in game.player_list] == [1,1,1,4]
    assert [int(p.score_history[-1]) for p in game.player_list] == [5400,5300,5300,-16000]
    game._finalize_scores_and_history()
    assert [p.riichi_points for p in game.player_list] == [17.1,17,16.9,-51]
    assert sum(p.score for p in game.player_list) == 100000


def test_tenhou_keeps_original_seat_tiebreak():
    game, _ = connected('tenhou')
    game._finalize_scores_and_history()
    game._assign_final_ranks()
    assert [p.record_counter.rank_result for p in game.player_list] == [1,2,3,4]


def test_game_end_protocol_preserves_game_points_and_distinct_rank_pt_for_all_four():
    game, sockets = connected('mleague')
    for player, score in zip(game.player_list, (40000,30000,20000,10000)): player.score = score
    game._finalize_scores_and_history()
    game._assign_final_ranks()
    for player in game.player_list:
        details = riichi_pt_details('advanced','banzhuang','七段',player.riichi_points)
        details.update(rating_rule='riichi',rating_system='grade',rank_before='七段',rank_after='七段',
                       score_before=1500,score_after=1500+details['rating_pt'],rating_games=1)
        for key,value in details.items(): setattr(player,key,value)
    asyncio.run(broadcast_game_end(game))
    for socket in sockets.values():
        data = socket.websocket.send_json.call_args.args[0]['game_end_info']['player_final_data']
        ordered = sorted(data.values(), key=lambda p:p['original_player_index'])
        assert [p['pt'] for p in ordered] == [60,10,-20,-50]
        assert [p['rating_pt'] for p in ordered] == [54.75,4.75,-25.25,-55.25]
        assert all(p['rating_rank_cost'] == 5.25 and p['rating_game_multiplier'] == 1
                   and p['rating_tier_multiplier'] == 1 and p['rating_algorithm'] == 'riichi_mleague_pt_v1' for p in ordered)
        assert [p['rating_match_points'] for p in ordered] == [60,10,-20,-50]
