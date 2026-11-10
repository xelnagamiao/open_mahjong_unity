"""Keyless training retains paired play, reproducible walls and spectator policy."""
from copy import deepcopy
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock

import pytest

from .test_sichuan_duplicate import rooms
from .sichuan_duplicate import create_pair, restore_pairs
from ..gamestate.game_sichuan.test_duplicate import run_async
from ..gamestate.game_sichuan.DuplicateGameState import SbrDuplicateGameState, XueliuDuplicateGameState
from ..gamestate.game_sichuan.init_tiles import init_sichuan_tiles
from ..gamestate.public.game_record_manager import build_round_header_data, init_game_record, apply_game_title_end_fields
from ..gamestate.public.spectator_rules import deliver_realtime_spectator_message, duplicate_spectator_blocked
from ..game_calculation.sichuan.duplicate_rules import SBR_DUPLICATE, XUELIU_DUPLICATE, make_board

SEED = '0abcdef123456789' * 4


@pytest.mark.parametrize('sub,cls,layout', [(SBR_DUPLICATE,SbrDuplicateGameState,'standard'),
                                         (XUELIU_DUPLICATE,XueliuDuplicateGameState,'xueliu')])
@pytest.mark.parametrize('sync', [False,True])
@pytest.mark.parametrize('allowed', [None,False,True])
@run_async
async def test_keyless_training_keeps_paired_walls_and_spectator_metadata(rooms,sub,cls,layout,sync,allowed):
    config=dict(sub_rule=sub,wall_source='training',random_seed=SEED.upper(),duplicate_round_count=4,duplicate_sync_mode=sync)
    if allowed is not None: config['allow_spectator']=allowed
    result=await rooms.create_empty_event_room('evt_pair','sichuan',config,created_by=101)
    assert result.success,result.message
    assert result.room_info['wall_source']=='training' and result.room_info['is_duplicate']
    pair=next(iter(rooms.game_server.gamestate_manager.sichuan_duplicate_matches.values()))
    assert pair.data['wall_id'] is None and 'key' not in pair.data
    assert pair.data['seed']==SEED and pair.synchronous is sync
    assert pair.data['boards']==[make_board(SEED,n,layout=layout) for n in range(1,5)]
    pair.data['status']='running'
    expected=allowed is not False
    for room in rooms.rooms.values():
        assert 'duplicate_key' not in room and 'duplicate_game_id' not in room
        assert room['allow_spectator'] is expected
        state=cls(rooms.game_server,dict(room,player_list=[101,102,103,104]),None,rooms.game_server.db_manager,'training')
        assert state.spectator_enabled is expected and state.spectator_manager.enabled is expected
        assert duplicate_spectator_blocked(state) is not expected
        assert state.room_random_seed==int(SEED,16)
        init_sichuan_tiles(state)
        header=build_round_header_data(state)
        assert 'duplicate_walls' in header
        if layout=='xueliu': assert len(header['duplicate_common_wall'])==24
        state.master_seed=int(SEED,16)
        init_game_record(state)
        state._record_rule_profile()
        title=state.game_record['game_title']
        assert title['wall_source']==title['duplicate_wall_type']=='training'
        assert title['is_duplicate'] and title['duplicate_match_id']==pair.data['id']
        apply_game_title_end_fields(state,title)
        assert title['duplicate_seed']==SEED and 'duplicate_key' not in title
        state.spectator_manager.record_game_title()
        state.spectator_manager.record_round_start()
        spectator=state.spectator_manager._build_spectator_record(float('inf'))
        assert bool(spectator) is expected
        if expected:
            assert spectator['game_title']['is_duplicate']
            assert spectator['game_round']['round_index_1']['duplicate_walls']==header['duplicate_walls']
        socket=NS(send_json=AsyncMock())
        rooms.game_server.user_id_to_connection[201]=NS(websocket=socket)
        state.realtime_spectators=[NS(user_id=201,host_user_id=101)]
        await deliver_realtime_spectator_message(state,0,{'type':'training','success':True})
        assert socket.send_json.await_count==int(expected)


@pytest.mark.parametrize('bad', [None,0,1,False,1.5,'','0'*64,'f'*63,'f'*65,'z'*64,'0x'+'a'*64])
@run_async
async def test_invalid_training_seed_never_creates_half_rooms(rooms,bad):
    result=await create_pair(rooms,'evt_pair',SBR_DUPLICATE,dict(wall_source='training',random_seed=bad),'',101)
    assert not result.success and not rooms.rooms and not rooms.saved


@pytest.mark.parametrize('patch', [dict(duplicate_key='dup_test_key'),dict(allow_spectator='false'),
                                  dict(allow_spectator=1),dict(wall_source='unknown'),dict(wall_source='seed',random_seed=0),
                                  dict(wall_source='manual',random_seed=0)])
@run_async
async def test_invalid_source_combinations_are_atomic(rooms,patch):
    config=dict(wall_source='training',random_seed=SEED,**{})
    config.update(patch)
    result=await create_pair(rooms,'evt_pair',SBR_DUPLICATE,config,'',101)
    assert not result.success and not rooms.rooms and not rooms.saved


@pytest.mark.parametrize('requested', ['key','seed','manual'])
@pytest.mark.parametrize('actual', ['key','seed','manual'])
@run_async
async def test_explicit_key_source_must_match_database(rooms,monkeypatch,requested,actual):
    from ..game_calculation.sichuan.duplicate_rules import flatten_board
    tiles=flatten_board(make_board('test',1))
    wall=dict(id=1,key='dup_'+'b'*32,rule='sichuan/sbr',wall_type=actual,event_id='evt_pair',
              use_flowers=False,round_count=1,round_tiles=[tiles],tiles=tiles,seed='test')
    monkeypatch.setattr('server.database.duplicate_walls.load_duplicate_wall',lambda *_:deepcopy(wall))
    result=await create_pair(rooms,'evt_pair',SBR_DUPLICATE,dict(wall_source=requested,duplicate_key=wall['key'],allow_spectator=True),'',101)
    assert result.success is (requested==actual)
    if result.success:
        assert all(not room['allow_spectator'] for room in rooms.rooms.values())
    else: assert not rooms.rooms and not rooms.saved


@pytest.mark.parametrize('sub', [SBR_DUPLICATE,XUELIU_DUPLICATE])
@run_async
async def test_training_restart_and_restore_preserve_seed_boards_and_no_key(rooms,monkeypatch,sub):
    result=await create_pair(rooms,'evt_pair',sub,dict(sub_rule=sub,wall_source='training',random_seed=SEED),'',101)
    assert result.success
    pair=next(iter(rooms.game_server.gamestate_manager.sichuan_duplicate_matches.values()))
    original=deepcopy(pair.data['boards'])
    pair.data['status']='running'
    pair.interrupt('test')
    pair.restart_unfinished_board()
    assert pair.data['boards']==original
    saved=deepcopy(pair.data)
    monkeypatch.setattr('server.room.sichuan_duplicate.PairedMatchStore.unfinished',lambda _: [saved])
    restore_pairs(rooms.game_server)
    restored=rooms.game_server.gamestate_manager.sichuan_duplicate_matches[pair.data['id']]
    assert restored.data['seed']==SEED and restored.data['boards']==original
    assert all(room['allow_spectator'] and 'duplicate_key' not in room for room in rooms.rooms.values())


@pytest.mark.parametrize('sub,cls', [(SBR_DUPLICATE,SbrDuplicateGameState),(XUELIU_DUPLICATE,XueliuDuplicateGameState)])
@run_async
async def test_training_preserves_three_bot_spectator_limit(rooms,sub,cls):
    result=await create_pair(rooms,'evt_pair',sub,dict(sub_rule=sub,wall_source='training',random_seed=SEED),'',101)
    assert result.success
    room=next(iter(rooms.rooms.values()))
    state=cls(rooms.game_server,dict(room,player_list=[101,1,2,3]),None,rooms.game_server.db_manager,'training')
    assert not state.spectator_enabled and duplicate_spectator_blocked(state)
