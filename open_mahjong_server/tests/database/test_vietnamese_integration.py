"""Exercise authoritative rounds through the real unified record/statistics writer."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock
import pytest
from test_subrule_statistics import db
from server.gamestate.game_vietnamese.VietnameseGameState import VietnameseGameState
from server.gamestate.game_vietnamese.state_machine import Phase as P
from server.gamestate.game_vietnamese.bot import choose_action
from server.database.statistics_catalog import resolve_statistics_identity
from server.room.vietnamese_room import VietnameseRoomValidator,create_vietnamese_room


def finished_state():
    data=VietnameseGameState._default_room_data()
    data['player_list']=[10000101,10000102,10000103,10000104]
    data['player_settings']={i:{'username':f'Test {i}'} for i in data['player_list']}
    state=VietnameseGameState(room_data=data)
    state.initialize_round();state.start_game_recording();state.start_round_recording()
    state.open_action_window(state.opening_window())
    for step in range(1000):
        if state.machine.phase==P.END:break
        state.apply_action_results(state.live_pending_window,{i:choose_action(state,i) for i,a in state.action_dict.items() if a})
    assert state.machine.phase==P.END
    state.finalize_round_recording();state.finalize_game_recording()
    return state


def test_real_record_and_basic_statistics_are_persisted_without_fans(db):
    from server.database.player_recent_records import ensure_schema
    with db._get_connection().cursor() as cursor:ensure_schema(cursor)
    db._get_connection().commit()
    state=finished_state();state.db_manager=db
    game_id=state.persist_game_record()
    assert game_id
    rows=db.sql("SELECT rule,sub_rule,count(*) FROM game_player_records GROUP BY rule,sub_rule")
    assert rows==[('vietnamese','vietnamese/classic160',4)]
    metrics=db.sql("SELECT rule,sub_rule,count(*) FROM game_player_metrics GROUP BY rule,sub_rule")
    assert metrics==[('vietnamese','vietnamese/classic160',4)]
    assert db.sql('SELECT count(*) FROM scene_tier_fan_daily')==[(0,)]
    assert all(not row[0] for row in db.sql('SELECT fan_stats FROM game_player_metrics'))
    assert sum(p.score for p in state.player_list)==1200


def test_room_configuration_and_statistics_identity():
    config=VietnameseRoomValidator(room_name='test',game_round=1,round_timer=20,step_timer=5)
    assert config.sub_rule=='vietnamese/classic160'
    assert config.detailed_config['rule_version']
    assert resolve_statistics_identity('vietnamese',config.sub_rule)==('vietnamese',config.sub_rule)
    with pytest.raises(ValueError):VietnameseRoomValidator(room_name='test',detailed_config={'unknown':True})


def test_room_entry_requires_login_and_preserves_existing_room():
    connection=SimpleNamespace(user_id=10000101,current_room_id='existing',username='test')
    manager=SimpleNamespace(game_server=SimpleNamespace(players={'connection':connection}))
    result=asyncio.run(create_vietnamese_room(manager,'connection',room_name='test',gameround=1))
    assert not result.success and connection.current_room_id=='existing'
    result=asyncio.run(create_vietnamese_room(manager,'missing',room_name='test',gameround=1))
    assert not result.success


def test_successful_room_creation_preserves_versioned_rules_and_instantiates_domain():
    connection=SimpleNamespace(user_id=10000101,current_room_id=None,username='test')
    settings=SimpleNamespace(get_user_settings=lambda uid:{'username':'test'})
    manager=SimpleNamespace(game_server=SimpleNamespace(players={'connection':connection},db_manager=settings),
        rooms={},room_passwords={},_reject_room_entry_conflicts=lambda *a:None,
        _normalize_event_id=lambda x:x,_validate_event_for_room=lambda *a:None,
        _apply_event_fields=lambda *a:None,_generate_room_id=lambda:'isolated-rule-test',
        _broadcast_room_info=AsyncMock())
    result=asyncio.run(create_vietnamese_room(manager,'connection',room_name='test',gameround=1,password='fixture-only'))
    assert result.success and connection.current_room_id=='isolated-rule-test'
    room=manager.rooms[connection.current_room_id]
    assert room['room_rule']=='vietnamese' and room['detailed_config']['rule_version']
    assert manager.room_passwords[connection.current_room_id]=='fixture-only'
    assert manager._broadcast_room_info.await_count==1
    room['player_list']=[10000101,10000102,10000103,10000104]
    state=VietnameseGameState(room_data=room)
    assert state.room_rule=='vietnamese' and state.detailed_config==room['detailed_config']
