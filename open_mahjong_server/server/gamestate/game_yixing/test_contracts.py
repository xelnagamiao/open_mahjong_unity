"""Offline protocol/privacy/room boundaries; these are not live socket tests."""

import asyncio
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock

import pytest

from .get_action import handle_action
from .test_flow import PLAIN, OTHER, act, turn, state, start, river, set_melds
from .state_machine import Phase as P
from ...room.yixing_room import YixingRoomValidator, create_yixing_room, handle_create_yixing_room
from ...game_calculation.yixing.rules import score_hand, waiting_tiles, shapes


@pytest.mark.parametrize("bad", [None,"tiles",[True],[11,"12"],[11,[]]])
def test_bad_shape_data_never_raises_or_qualifies(bad):
    assert score_hand(bad) is None
    assert not waiting_tiles(bad)
    assert not shapes(bad)
    assert score_hand(PLAIN,flowers=bad) is None


@pytest.mark.parametrize("actor", range(4))
def test_concealed_kong_reveals_one_middle_tile_but_keeps_hand_and_draw_private(actor):
    s=turn([11]*4+[21,22,23,34,35,36,28,28,41,41],index=actor)
    s.player_list[actor].huapai_list=[51]
    act(s,actor,"angang",target_tile=11)
    act(s)
    mask=[2,11,0,11,2,11,2,11]
    assert s.player_list[actor].combination_tiles==["G11"]
    assert s.player_list[actor].combination_mask==[mask]
    for viewer in range(4):
        snapshot=s.build_game_start_payload(viewer)["game_info"]
        assert snapshot["players_info"][actor]["huapai_list"]==[51]
        assert all((p["hand_tiles"] is not None)==(i==viewer) for i,p in enumerate(snapshot["players_info"]))
        assert snapshot["players_info"][actor]["combination_tiles"]==["G11"]
        assert snapshot["players_info"][actor]["combination_mask"]==[mask]
        kong=next(p for p in s.outbound_payloads if p.get("player_index")==viewer and p.get("action")=="angang")
        assert kong["tile"]==11
        assert kong["meld_code"]=="G11"
        assert kong["do_action_info"]["combination_target"]=="G11"
        assert kong["do_action_info"]["combination_mask"]==mask
        assert kong["game_info"]["players_info"][actor]["combination_mask"]==[mask]
        restored=s.restore_payloads(viewer)[0]["game_info"]
        assert restored["players_info"][actor]["combination_tiles"]==["G11"]
        assert restored["players_info"][actor]["combination_mask"]==[mask]
        drawn=next(p for p in s.outbound_payloads if p.get("player_index")==viewer and p.get("action")=="deal_gang_tile")
        assert drawn["tile"]==(19 if viewer==actor else None)


def test_added_kong_targets_existing_pung_in_every_client_view():
    s=turn([11,21,22,23,34,35,36,28,28,41,41])
    set_melds(s,0,["k11"])
    s.open_action_window(s.begin_turn(0))
    act(s,0,"jiagang",target_tile=11)
    act(s)
    for viewer in range(4):
        payload=next(p for p in s.outbound_payloads if p.get("player_index")==viewer and p.get("action")=="jiagang")
        assert payload["do_action_info"]["combination_target"]=="k11"
        assert len(payload["do_action_info"]["combination_mask"])==8
        snapshot=s.build_game_start_payload(viewer)["game_info"]["players_info"][0]
        assert snapshot["combination_tiles"]==["g11"]
        assert len(snapshot["combination_mask"])==1


def test_flowers_from_held_hand_and_draw_slot_and_no_opponent_auto_replacement():
    s=turn()
    s.player_list[0].hand_tiles[0]=51
    s.player_list[1].hand_tiles[0]=52
    s.open_action_window(s.begin_turn(0))
    act(s,0,"buhua")
    payload=next(p for p in s.outbound_payloads if p.get("action")=="buhua")
    assert payload["do_action_info"]["buhua_tile"]==51
    assert not payload["do_action_info"]["is_mo_buhua"]
    assert 52 in s.player_list[1].hand_tiles
    s.player_list[0].hand_tiles[-1]=53
    s.open_action_window(s.begin_turn(0))
    act(s,0,"buhua")
    payload=[p for p in s.outbound_payloads if p.get("action")=="buhua"][-1]
    assert payload["do_action_info"]["is_mo_buhua"]


def test_flower_after_chow_does_not_remove_food_swap_restriction():
    s=turn()
    s.player_list[1].hand_tiles=[14,15,51,13,16,21,22,23,34,35,36,28,28]
    river(s,0,13)
    act(s,1,"chi_right")
    assert s.machine.phase==P.REPLACEMENT
    act(s,1,"buhua")
    info=s.build_pending_action_payload(1)["ask_hand_action_info"]
    assert set(info["forbidden_cut_tiles"])=={13,16}
    with pytest.raises(ValueError):
        act(s,1,"cut",TileId=16)


def test_reconnect_and_spectator_view_match_pending_and_settled_snapshots():
    async def run():
        s=turn()
        sent=[]
        async def capture(index,payload):
            sent.append((index,deepcopy(payload)))
        s.send_payload_to_player=capture
        s.player_list[1].tag_list.append("offline")
        await s.player_reconnect(s.player_list[1].user_id)
        assert "offline" not in s.player_list[1].tag_list
        assert sent==[(1,p) for p in s.restore_payloads(1)]
        count=len(sent)
        await s.player_reconnect(999)
        assert len(sent)==count
        await s.player_reconnect(s.player_list[1].user_id)
        ws=SimpleNamespace(send_json=AsyncMock())
        s.game_server=SimpleNamespace(user_id_to_connection={999:SimpleNamespace(websocket=ws)})
        await s.send_realtime_spectator_snapshot(999,1)
        assert [c.args[0] for c in ws.send_json.call_args_list]==s.restore_payloads(1)
        count=ws.send_json.call_count
        await s.send_realtime_spectator_snapshot(999,-1)
        await s.send_realtime_spectator_snapshot(1000,1)
        assert ws.send_json.call_count==count
        s.player_list[0].huapai_list=[51]
        act(s,0,"hu_self")
        restored=s.restore_payloads(1)
        assert len(restored)==2
        info=restored[-1]["show_result_info"]
        assert info["hu_score"]==5 and info["hepai_player_huapai"]==[51]
        assert info["yixing_fan_details"]["base_flowers"]==5
        assert sum(info["score_changes"].values())==0
        assert restored[0]["game_info"]["players_info"][0]["score"]==15
        assert s.build_pending_action_payload(0) is None
        s.machine.transition(P.READY)
        assert s.restore_payloads(0)[-1]["type"]=="gamestate/yixing/ready_status"
        assert s.build_pending_action_payload(0) is None
    asyncio.run(run())


def test_network_action_authentication_tick_recovery_and_duplicate_rejection():
    async def run():
        s=turn()
        ws=SimpleNamespace(send_json=AsyncMock())
        server=SimpleNamespace(gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda _:s),
            players={"own":SimpleNamespace(user_id=s.player_list[0].user_id),"outside":SimpleNamespace(user_id=999)})
        message=dict(type="gamestate/yixing/cut_tile",gamestate_id=s.gamestate_id,
                     action_tick=s.server_action_tick,TileId=28,cutClass=True,cutIndex=13)
        for key in ("outside","missing"):
            await handle_action(server,key,message,ws)
        assert all(q.empty() for q in s.action_queues.values())
        await handle_action(server,"own",dict(message,action_tick=True),ws)
        assert ws.send_json.call_args.args[0]["type"].endswith("broadcast_hand_action")
        await handle_action(server,"own",dict(message,TileId=99),ws)
        assert ws.send_json.call_args.args[0]["type"]=="tips"
        await handle_action(server,"own",message,ws)
        await handle_action(server,"own",message,ws)
        assert s.action_queues[0].qsize()==1
        s.open_action_window(s.begin_turn(0))
        assert s.action_queues[0].empty()
        act(s,0,"hu_self")
        count=ws.send_json.call_count
        await handle_action(server,"own",dict(message,action_tick=-1),ws)
        assert ws.send_json.call_count==count
        s.machine.transition(P.READY)
        await handle_action(server,"own",dict(message,action_tick=-1),ws)
        assert ws.send_json.call_args.args[0]["type"].endswith("ready_status")
        s.action_dict={0:["ready"],1:[],2:[],3:[]}
        s.waiting_players_list=[0]
        await handle_action(server,"own",dict(type="gamestate/yixing/send_action",action="ready",action_tick=s.server_action_tick),ws)
        assert s.action_queues[0].get_nowait()["action_type"]=="ready"
        s.room_rule="guobiao"
        await handle_action(server,"own",message,ws)
        server.gamestate_manager.get_game_state_by_gamestate_id=lambda _:None
        await handle_action(server,"own",message,ws)
    asyncio.run(run())


@pytest.mark.parametrize("key,value", [("use_flowers",False),("open_cuohe",True),("tactical_call",True),
    ("claim_protection",True),("tian_di_ren_he",True),("tips","false"),("allow_spectator",1),
    ("game_round",True),("game_round",5),("round_timer",-1),("step_timer",101),
    ("sub_rule","yixing/joker"),("detailed_config",{"seven_pairs":1}),("detailed_config",{"jokers":True})])
def test_room_rejects_invalid_options(key,value):
    config=dict(room_name="test",game_round=4,round_timer=20,step_timer=5)
    config[key]=value
    with pytest.raises(ValueError):
        YixingRoomValidator(**config)


@pytest.mark.parametrize("rounds",[1,2,3,4])
@pytest.mark.parametrize("switch",[False,True])
def test_every_room_option_value_roundtrips(rounds,switch):
    config=YixingRoomValidator(room_name=" 宜兴 ",game_round=rounds,round_timer=0,step_timer=0,
        tips=switch,tourist_limit=switch,allow_spectator=switch,count_tips=switch,pointer_tips=switch,
        detailed_config={"seven_pairs":switch},use_flowers=True).model_dump()
    assert config["room_name"]=="宜兴" and config["game_round"]==rounds
    assert config["detailed_config"]=={"seven_pairs":switch} and config["use_flowers"]
    assert all(config[k]==switch for k in ("tips","tourist_limit","allow_spectator","count_tips","pointer_tips"))


def fake_room_manager():
    connection=SimpleNamespace(user_id=101,username="tester",current_room_id=None)
    manager=SimpleNamespace(game_server=SimpleNamespace(players={"c":connection},db_manager=SimpleNamespace(
        get_user_settings=Mock(return_value={"username":"tester","voice_id":2}))),
        _reject_room_entry_conflicts=Mock(return_value=None),_normalize_event_id=lambda x:x,
        _validate_event_for_room=Mock(return_value=None),_generate_room_id=lambda:123456,
        _apply_event_fields=Mock(),_broadcast_room_info=AsyncMock(),rooms={},room_passwords={})
    return manager,connection


def test_room_creation_defaults_password_event_and_handler():
    async def run():
        manager,connection=fake_room_manager()
        result=await create_yixing_room(manager,"c",room_name="test",use_flowers=False)
        assert not result.success and not manager.rooms and connection.current_room_id is None
        result=await create_yixing_room(manager,"c",room_name="test",password="pw",event_id="e",count_tips=True)
        assert result.success and connection.current_room_id==123456
        room=manager.rooms[123456]
        assert room["room_rule"]=="yixing" and room["sub_rule"]=="yixing/standard" and room["game_round"]==4
        assert room["detailed_config"]=={"seven_pairs":False}
        assert manager.room_passwords[123456]=="pw" and room["player_settings"][101]["voice_id"]==2
        manager,connection=fake_room_manager()
        ws=SimpleNamespace(send_json=AsyncMock())
        server=SimpleNamespace(room_manager=manager)
        await handle_create_yixing_room(server,"c",dict(roomname="test",use_flowers=True,detailed_config={"seven_pairs":True}),ws)
        assert ws.send_json.call_args.args[0]["room_info"]["detailed_config"]["seven_pairs"]
        assert not manager.room_passwords
    asyncio.run(run())


@pytest.mark.parametrize("failure",["missing","logged_out","already_in_room","conflict","event","settings"])
def test_room_creation_failures_do_not_mutate(failure):
    async def run():
        manager,connection=fake_room_manager()
        if failure=="missing": manager.game_server.players={}
        if failure=="logged_out": connection.user_id=None
        if failure=="already_in_room": connection.current_room_id=999
        if failure=="conflict": manager._reject_room_entry_conflicts.return_value="blocked"
        if failure=="event": manager._validate_event_for_room.return_value="blocked"
        if failure=="settings": manager.game_server.db_manager.get_user_settings.return_value=None
        response=await create_yixing_room(manager,"c",room_name="test")
        assert response=="blocked" or not response.success
        assert not manager.rooms
    asyncio.run(run())
