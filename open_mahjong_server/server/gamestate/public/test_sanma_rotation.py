"""Three fixed physical places, rotating winds, and three-player mode identifiers."""
import pytest
from .logic_common import get_index_relative_position
from .next_game_round import next_game_round_guobiao_switchseat
from ..game_guobiao.test_sanma import sanma as guobiao_sanma
from ..game_riichi.test_sanma import sanma as riichi_sanma
from ...match.settlement import settle_ranked_game
from ...match.rank_calculator import queue_type_to_match_type


@pytest.mark.parametrize('rule', ['guobiao', 'riichi'])
@pytest.mark.parametrize('viewer', range(3))
def test_three_players_stay_on_same_sides_through_every_wind_round(rule, viewer):
    game=guobiao_sanma() if rule=='guobiao' else riichi_sanma()
    game.max_round=4
    players={p.original_player_index:p for p in game.player_list}
    expected={viewer:'self',(viewer+1)%3:'right',(viewer+2)%3:'left'}
    for hand in range(1,13):
        for original,p in players.items():
            assert p.player_index==(original-hand+1)%3
            assert get_index_relative_position(players[viewer].player_index,p.player_index,3)==expected[original]
        assert len({p.player_index for p in players.values()})==3
        if rule=='guobiao':next_game_round_guobiao_switchseat(game)
        else:game._rotate_seats()


@pytest.mark.parametrize('rule', ['guobiao','riichi'])
@pytest.mark.parametrize('circles',range(1,5))
def test_custom_three_player_mode_uses_actual_count(rule,circles):
    game=guobiao_sanma() if rule=='guobiao' else riichi_sanma()
    game.max_round=circles
    assert settle_ranked_game(game)==f'{circles}/3'


@pytest.mark.parametrize('tier',['beginner','intermediate','advanced'])
@pytest.mark.parametrize('mode,circles',[('dongfeng',1),('banzhuang',2)])
def test_ranked_three_player_mode_uses_actual_count(tier,mode,circles):
    assert queue_type_to_match_type(f'riichi_sanma_{tier}_{mode}')==f'{circles}/3_rank'
