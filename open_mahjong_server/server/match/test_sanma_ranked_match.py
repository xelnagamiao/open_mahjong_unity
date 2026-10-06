"""Three-player ranked routing, pool isolation and lifecycle contracts."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock, patch
import pytest
from .rating_rules import QUEUES, RULES, default_rating
from .test_lifecycle import server_fixture, USERS
from .settlement import settle_ranked_game

SANMA_QUEUES = [q for q,s in QUEUES.items() if s.rating_rule == 'riichi_sanma']


def fixture():
    server = server_fixture()
    server.match_manager.committed_users.clear()
    server.match_manager.winning_queues.clear()
    ratings = {r: default_rating(r) for r in RULES}
    ratings['riichi_sanma']['rank_name'] = '四段'
    server.db_manager.get_rank_data.return_value = {'guobiao_rank':'四段','ratings':ratings}
    server.db_manager.get_user_sponsor_mcrpl.return_value = {}
    return server


@pytest.mark.parametrize('queue', SANMA_QUEUES)
def test_two_wait_three_commit_and_fourth_stays_waiting(queue):
    async def run():
        server=fixture();m=server.match_manager;m._start_game=AsyncMock()
        for uid in USERS[:2]:
            assert (await m.join_queue(str(uid),queue)).success
            assert not m.committed_users
        assert m.get_rule_player_counts()['riichi_sanma']==2
        assert m.get_rule_player_counts()['riichi']==0
        assert (await m.join_queue(str(USERS[2]),queue)).success
        await asyncio.sleep(0)
        m._start_game.assert_awaited_once_with(queue,USERS[:3])
        assert m.committed_users==set(USERS[:3])
        assert not m.queues[queue]
        assert (await m.join_queue(str(USERS[3]),queue)).success
        assert m.queues[queue]==USERS[3:]
        assert not (await m.join_queue(str(USERS[0]),queue)).success
        assert not (await m.leave_queue(str(USERS[0]))).success
    asyncio.run(run())


def test_three_player_commit_removes_every_four_player_queue_atomically():
    async def run():
        server=fixture();m=server.match_manager;m._start_game=AsyncMock()
        four='riichi_beginner_dongfeng';three=SANMA_QUEUES[0]
        for uid in USERS[:3]:
            assert (await m.join_queue(str(uid),four)).success
            assert (await m.join_queue(str(uid),three)).success
        await asyncio.sleep(0)
        assert not m.queues[four] and not m.queues[three]
        assert m.committed_users==set(USERS[:3])
        assert not m.user_to_queues
        m._start_game.assert_awaited_once_with(three,USERS[:3])
        assert (await m.join_queue(str(USERS[3]),four)).success
        assert m.queues[four]==USERS[3:]
    asyncio.run(run())


def test_four_player_commit_clears_three_player_waiters():
    async def run():
        server=fixture();m=server.match_manager;m._start_game=AsyncMock()
        three=SANMA_QUEUES[0];four='riichi_beginner_dongfeng'
        for uid in USERS[:2]:assert (await m.join_queue(str(uid),three)).success
        for uid in USERS:assert (await m.join_queue(str(uid),four)).success
        await asyncio.sleep(0)
        m._start_game.assert_awaited_once_with(four,USERS)
        assert not m.queues[three] and not m.user_to_queues
    asyncio.run(run())


def test_admission_reads_three_player_rank_instead_of_four_player_rank():
    async def run():
        server=fixture();m=server.match_manager
        ratings=server.db_manager.get_rank_data.return_value['ratings']
        ratings['riichi']['rank_name']='七段';ratings['riichi_sanma']['rank_name']='10级'
        assert not (await m.join_queue(str(USERS[0]),'riichi_sanma_advanced_dongfeng')).success
        ratings['riichi_sanma']['rank_name']='四段'
        assert (await m.join_queue(str(USERS[0]),'riichi_sanma_advanced_dongfeng')).success
        ratings['riichi_sanma']['rank_name']='七段'
        server.db_manager.get_user_sponsor_mcrpl.return_value={'is_intermediate_qualified':True}
        assert not (await m.join_queue(str(USERS[0]),'riichi_sanma_intermediate_dongfeng')).success
    asyncio.run(run())


@pytest.mark.parametrize('all_offline', [False,True])
def test_real_three_player_start_and_cleanup_releases_exactly_three(all_offline):
    async def run():
        server=fixture();m=server.match_manager;queue=SANMA_QUEUES[0]
        m.committed_users.update(USERS[:3]);m.winning_queues.update({u:queue for u in USERS[:3]})
        async def hold(*args):await asyncio.Event().wait()
        server.gamestate_manager.run_game=hold
        offline=USERS[:3] if all_offline else USERS[:1]
        for uid in offline:server.user_id_to_connection.pop(uid);server.players.pop(str(uid))
        with patch('server.match.match_manager.asyncio.sleep',new=AsyncMock()):await m._start_game(queue,USERS[:3])
        if all_offline:
            assert not server.gamestate_manager.gamestate_id_to_game_state
        else:
            game=next(iter(server.gamestate_manager.gamestate_id_to_game_state.values()))
            assert len(game.player_list)==3 and game.sub_rule=='riichi/sanma'
            assert 'offline' in next(p for p in game.player_list if p.user_id==USERS[0]).tag_list
            assert m.playing_counts[queue]==3
            assert m.get_rule_player_counts()['riichi_sanma']==3
            await server.gamestate_manager.cleanup_game_state_complete(game.gamestate_id)
        assert m.playing_counts[queue]==0
        assert not m.committed_users and not m.winning_queues and not m.gamestate_to_match
        assert not server.room_manager.match_room_ids
    asyncio.run(run())


@pytest.mark.parametrize('count,sub_rule',[(4,'riichi/sanma'),(3,'riichi/standard'),(2,'riichi/sanma')])
def test_settlement_rejects_table_contract_before_database_write(count,sub_rule):
    db=SimpleNamespace(settle_rated_game=Mock())
    state=SimpleNamespace(room_type='match',room_rule='riichi',sub_rule=sub_rule,
        match_queue_type=SANMA_QUEUES[0],max_round=1,player_list=[None]*count,db_manager=db)
    with pytest.raises(ValueError):settle_ranked_game(state)
    db.settle_rated_game.assert_not_called()
