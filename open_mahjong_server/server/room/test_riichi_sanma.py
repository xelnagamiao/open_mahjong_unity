"""Sanma is a riichi subrule across custom/event creation and lobby seats."""
import asyncio
from unittest.mock import AsyncMock, patch

import pytest

from . import test_riichi_starting_score as helpers
from .room_validators import RiichiRoomValidator
from .room_seats import get_seats, seat_player, unseat_player
from ..game_calculation.riichi.rule_config import preset_room_config, normalize_riichi_config
from ..gamestate.game_riichi.RiichiGameState import RiichiGameState
from ..gamestate.gamestate_manager import GameStateManager


@pytest.mark.parametrize('preset',['sanma_majsoul','sanma_tenhou'])
@pytest.mark.parametrize('event',[False,True])
def test_three_seat_custom_and_event_room_capacity_defaults_and_explicit_score(preset,event):
    async def run():
        manager,server=helpers.RiichiStartingScoreTests().manager();config=preset_room_config(preset)
        if event:
            response=await manager.create_empty_event_room('sanma-event','riichi',config,broadcast=False)
        else:
            response=await manager.create_Riichi_room('connection','三人立直',2,'',20,5,False,
                sub_rule=config['sub_rule'],detailed_config=config['detailed_config'])
        assert response.success,response.message
        room=manager.rooms[response.room_info['room_id']]
        assert room['max_player']==3 and len(get_seats(room))==3 and room['starting_score']==35000
        assert room['detailed_config']==config['detailed_config']
        while len(room['player_list'])<3:seat_player(room,20000+len(room['player_list']))
        with pytest.raises(ValueError,match='房间已满'):seat_player(room,30000)
        with pytest.raises(ValueError,match='座位编号无效'):seat_player(room,30000,3)
        uid=get_seats(room)[1];unseat_player(room,uid,1);seat_player(room,30000,1)
        state=RiichiGameState(server,room,None,None,'test')
        assert len(state.player_list)==3 and [state._player_starting_score(i) for i in range(3)]==[35000]*3
    asyncio.run(run())


def test_validator_uses_three_player_defaults_and_keeps_four_player_defaults():
    kwargs=dict(room_name='rules',game_round=2,round_timer=20,step_timer=5)
    assert RiichiRoomValidator(**kwargs,sub_rule='riichi/sanma').starting_score==35000
    assert RiichiRoomValidator(**kwargs).starting_score==25000
    assert RiichiRoomValidator(**kwargs,sub_rule='riichi/langyong').starting_score==50000
    assert RiichiRoomValidator(**kwargs,sub_rule='riichi/sanma',starting_score=40000).starting_score==40000
    assert normalize_riichi_config(None,'riichi/sanma')['target_score']==40000


@pytest.mark.parametrize('sub',['riichi/standard','riichi/langyong'])
@pytest.mark.parametrize('rank',['sanma_15','sanma_20'])
def test_four_player_room_rejects_three_player_rank_table(sub,rank):
    with pytest.raises(ValueError,match='三人顺位点'):
        normalize_riichi_config({'rank_points':rank},sub)


@pytest.mark.parametrize('sub,count,can_start',[
    ('riichi/sanma',2,False),('riichi/sanma',3,True),
    ('riichi/standard',3,False),('riichi/standard',4,True),
])
def test_public_game_factory_requires_the_subrule_player_count(sub,count,can_start):
    async def run():
        rooms,server=helpers.RiichiStartingScoreTests().manager()
        response=await rooms.create_Riichi_room('connection','factory',1,'',20,5,False,sub_rule=sub)
        assert response.success
        room=rooms.rooms[response.room_info['room_id']]
        while len(room['player_list'])<count:
            uid=10001+len(room['player_list'])
            seat_player(room,uid)
            room.setdefault('ready_list',[]).append(uid)
        server.room_manager=rooms
        server.calculation_service=None
        games=GameStateManager(server)
        server.gamestate_manager=games
        games.run_game=AsyncMock()
        with patch('server.gamestate.gamestate_manager.attach_game_frames'):
            result=await games.start_game('connection',room['room_id'])
        if can_start:
            assert result is None,getattr(result,'message','')
            state=games.get_game_state_by_room_id(room['room_id'])
            assert isinstance(state,RiichiGameState) and len(state.player_list)==count
            assert state.room_rule=='riichi' and state.sub_rule==sub
            state.game_task.cancel()
            await asyncio.gather(state.game_task,return_exceptions=True)
        else:
            assert not result.success and result.message=='人数不足'
            assert games.gamestate_id_to_game_state=={}
    asyncio.run(run())
