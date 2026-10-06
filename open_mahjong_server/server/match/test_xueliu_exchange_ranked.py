"""换三张排位的配置、真实建桌与评分归属。"""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock, patch

import pytest

from .rating_rules import (QUEUES, XUELIU_EXCHANGE_QUEUE as QUEUE,
    XUELIU_EXCHANGE_RATING_RULE as POOL, XUELIU_EXCHANGE_SUB_RULE as SUB_RULE, default_rating)
from .rank_calculator import queue_type_to_room_config
from .settlement import settle_ranked_game
from .test_lifecycle import server_fixture, USERS
from ..gamestate.game_sichuan.XueliuGameState import XueliuGameState


def test_exchange_queue_constructs_real_flow_state_with_sixteen_hands():
    async def run():
        server = server_fixture()
        manager = server.match_manager
        manager.committed_users = set(USERS)
        manager.winning_queues = dict.fromkeys(USERS, QUEUE)
        async def hold(*args):
            await asyncio.Event().wait()
        server.gamestate_manager.run_game = hold
        with patch('server.match.match_manager.asyncio.sleep', new=AsyncMock()):
            await manager._start_game(QUEUE, USERS)
        game = next(iter(server.gamestate_manager.gamestate_id_to_game_state.values()))
        assert isinstance(game, XueliuGameState)
        assert game.xueliu_exchange and game.xueliu_meld_count == 4
        assert game.sub_rule == SUB_RULE and game.max_round * 4 == 16
        assert not game.blood_battle and game.xueliu_rule_profile['gang_score_immediate']
        assert manager.playing_counts[QUEUE] == 4
        manager.release_match(game.gamestate_id)
        assert manager.playing_counts[QUEUE] == 0 and not manager.committed_users
    asyncio.run(run())


@pytest.mark.parametrize('sub_rule', ['sichuan/standard', 'sichuan/xueliu', 'unknown'])
def test_exchange_settlement_rejects_other_subrules_before_writing(sub_rule):
    state = SimpleNamespace(room_type='match', room_rule='sichuan', sub_rule=sub_rule,
        match_queue_type=QUEUE, player_list=[None] * 4, db_manager=Mock(), max_round=4)
    with pytest.raises(ValueError):
        settle_ranked_game(state)
    state.db_manager.settle_rated_game.assert_not_called()


def test_elo_config_and_default_pool_do_not_reuse_blood_battle_rating():
    spec = QUEUES[QUEUE]
    config = queue_type_to_room_config(QUEUE)
    assert spec.rule == 'sichuan' and spec.rating_rule == POOL and not spec.graded
    assert config['sub_rule'] == SUB_RULE and config['game_round'] == 4
    assert config['tips'] and config['hepai_limit'] == 0 and config['claim_protection']
    assert default_rating(POOL)['elo'] == 1500
    assert queue_type_to_room_config('sichuan_elo_xuezhan')['sub_rule'] == 'sichuan/standard'
