"""Rule edge cases and multi-hand lifecycle driven through the real executor."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock, patch

import pytest

from .test_state import make_state, hand, draw, HAND
from .test_adapters import room_manager, Socket
from . import bot
from .result import broadcast_result
from ...game_calculation.tuidao import rules as book
from ...room.tuidao_room import create_tuidao_room, normalize_tuidao_config, CONFIG
from ..game_taiwan.TaiwanGameState import TaiwanGameState

MODULE = "server.gamestate.game_tuidao.TuidaoGameState"


@pytest.mark.parametrize("tiles,melds", [(None, []), (HAND, None), (set(HAND), [])])
def test_invalid_container_is_rejected_without_type_coercion(tiles, melds):
    assert not book.valid_tiles(tiles, melds)
    assert not book.waits(tiles, melds)


@pytest.mark.parametrize("player,kind,payer", [(True,"added",None),(4,"concealed",None),
    (0,"invalid",None),(0,"direct",0),(0,"direct",True),(0,"direct",4)])
def test_invalid_kong_settlement_has_no_payment(player,kind,payer):
    with pytest.raises(ValueError):
        book.kong_payments(player,kind,payer=payer)


def test_valid_explicit_profile_roundtrips_and_returns_independent_copy():
    result=normalize_tuidao_config(CONFIG)
    assert result==CONFIG and result is not CONFIG
    assert normalize_tuidao_config({})==CONFIG


def test_invalid_subrule_cannot_instantiate_state():
    with pytest.raises(ValueError):
        make_state(sub_rule="guangdong/other")


def test_score_input_guards_do_not_treat_missing_discard_as_draw():
    state=make_state()
    player=hand(state,1,HAND)
    assert state.score_candidate(1,"discard") is None
    player.tag_list.append("peida")
    assert state.score_candidate(1,"discard",28) is None
    assert not any(a in ("angang","jiagang") for a in state.check_hand_actions(1)[1])
    player.tag_list.clear()
    assert state.score_candidate(1,"self_draw") is None
    player.hand_tiles.clear()
    assert state.score_candidate(1,"self_draw") is None


def test_reconnect_without_connection_or_terminal_snapshot_never_rebroadcasts():
    state=make_state()
    asyncio.run(state.player_reconnect(101))
    socket=Socket()
    state.game_server.user_id_to_connection[101]=SimpleNamespace(websocket=socket)
    asyncio.run(state.player_reconnect(101))
    assert socket.messages and not any(m["type"].endswith("show_result") for m in socket.messages)


def test_claim_clock_without_delivery_timestamp_keeps_full_window():
    state=make_state()
    assert state.claim_clock(state.player_list[1],reconnecting=True)==(0,3)
    assert state.enter_water(1) is False
    assert state.player_list[1].passed_fan==-1


def test_explicit_self_draw_tile_agrees_with_draw_slot():
    state=make_state();player=hand(state,0,HAND);draw(player,28)
    assert state.score_candidate(0,"self_draw",28)==state.score_candidate(0,"self_draw")


def test_ready_added_kong_preserves_waits():
    state=make_state()
    player=hand(state,0,[22,23,24,31,32,33,41,41,41,47],["k11"],ready=True)
    draw(player,11)
    assert state.kong_allowed(0,11,"added")
    asyncio.run(state.execute_jiagang(0,11))
    assert player.ready_locked and book.waits(player.hand_tiles,player.combination_tiles)=={47}
    assert [p.score for p in state.player_list]==[3,-1,-1,-1]


def test_pung_with_complete_remaining_hand_does_not_clear_pass_water():
    state=make_state()
    state.player_list[0].discard_tiles=[11]
    player=hand(state,1,[11,11,22,23,24,31,32,33,41,41,41,47,47])
    player.passed_fan=10
    asyncio.run(state.execute_claim(1,"peng"))
    assert player.combination_tiles==["k11"] and player.passed_fan==10


def test_opening_sets_draw_slot_and_does_not_auto_declare():
    state=make_state()
    player=hand(state,0,HAND+[19])
    asyncio.run(state._opening_flower_replacement())
    state._register_initial_heavenly_ready()
    state._mark_eight_flowers_if_ready(0)
    state._remember_claim_liability(1,"peng",0,11)
    assert player.has_draw_slot and player.last_drawn_tile==19
    assert not player.declared_ready
    assert state._liability_payer_for_win(0,"self_draw",None,19,{}) is None


@pytest.mark.parametrize("tile,kind", [(True,"concealed"),(51,"concealed"),(11,"added")])
def test_invalid_or_absent_kong_target_is_noop(tile,kind):
    state=make_state()
    player=hand(state,0,HAND);draw(player,19)
    before=list(player.hand_tiles)
    if kind=="added": asyncio.run(state.execute_jiagang(0,tile))
    else: asyncio.run(state.execute_angang(0,tile))
    assert player.hand_tiles==before and not state.kong_ledger


def test_ready_kong_rejects_old_concealed_triplet_and_changed_waits():
    state=make_state()
    player=hand(state,0,[11]*3+[22,23,24,31,32,33,41,41,41,47],ready=True)
    draw(player,11)
    player.last_drawn_tile=47
    assert not state.kong_allowed(0,11,"concealed")
    player.last_drawn_tile=11
    player.locked_waits=set()
    assert not state.kong_allowed(0,11,"concealed")
    player.locked_waits={46}
    assert not state.kong_allowed(0,11,"concealed")


def test_ready_direct_kong_preserves_waits_and_charges_discarder():
    state=make_state()
    source=hand(state,0,[]);source.discard_tiles=[11]
    player=hand(state,1,[11]*3+[22,23,24,31,32,33,41,41,41,47],ready=True)
    assert state.kong_allowed(1,11,"direct")
    assert state.check_discard_actions(11)[1]==["gang","pass"]
    asyncio.run(state.execute_claim(1,"gang"))
    assert player.ready_locked and player.combination_tiles==["g11"]
    assert [p.score for p in state.player_list]==[-2,2,0,0]


def test_illegal_claims_do_not_change_hand_or_turn():
    state=make_state()
    player=hand(state,1,HAND)
    asyncio.run(state.execute_claim(1,"peng"))
    state.player_list[0].discard_tiles=[47]
    asyncio.run(state.execute_claim(1,"peng"))
    assert state.current_player_index==0 and player.hand_tiles==HAND


@pytest.mark.parametrize("reason", ["missing", "logged_out", "already_seated", "conflict", "event", "settings"])
def test_room_failure_paths_leave_no_partially_created_room(reason):
    manager=room_manager()
    if reason=="missing": manager.game_server.players.clear()
    elif reason=="logged_out": manager.game_server.players["unit"].user_id=None
    elif reason=="already_seated": manager.game_server.players["unit"].current_room_id="existing"
    elif reason=="conflict": manager._reject_room_entry_conflicts=lambda *_: SimpleNamespace(success=False)
    elif reason=="event": manager._validate_event_for_room=lambda *_: SimpleNamespace(success=False)
    elif reason=="settings": manager.game_server.db_manager.get_user_settings=lambda _: None
    result=asyncio.run(create_tuidao_room(manager,"unit",room_name="推倒和",gameround=1))
    assert not result.success and not manager.rooms and not manager.room_passwords


def run_bot(state,available,status="waiting_hand_action",*,actionable=True,current=True,cpu=(19,13)):
    with patch.object(bot,"_wait_until_actionable",new=AsyncMock(return_value=actionable)), \
         patch.object(bot,"bot_action_is_current",side_effect=current if isinstance(current,list) else None,return_value=current), \
         patch.object(bot,"run_room_bot_cpu",new=AsyncMock(return_value=cpu)), \
         patch.object(bot,"submit_bot_action",new=AsyncMock()) as send:
        asyncio.run(bot.tuidao_bot_action.__wrapped__(state,0,available,status))
        return send


@pytest.mark.parametrize("kind,action", [("concealed","angang"),("added","jiagang")])
def test_bot_selects_authoritative_kong_target(kind,action):
    state=make_state()
    if kind=="concealed":
        player=hand(state,0,[11]*3+[22,23,24,31,32,33,41,41,41,47]);draw(player,11)
    else:
        player=hand(state,0,[22,23,24,31,32,33,41,41,47,47],["k11"]);draw(player,11)
    send=run_bot(state,[action,"cut"])
    assert send.await_args.args[3]==action and send.await_args.args[7]==11


def test_bot_declares_ready_using_live_waits_and_not_arbitrary_cut():
    state=make_state();player=hand(state,0,HAND);draw(player,19)
    player.riichi_candidate_cuts={19:[28],11:[47]}
    state.player_list[2].discard_tiles=[47]*4
    send=run_bot(state,["riichi_cut","cut"])
    assert send.await_args.args[3]=="riichi_cut"
    assert send.await_args.args[5]==19


def test_bot_falls_back_to_discard_and_drops_stale_cpu_result():
    state=make_state();player=hand(state,0,HAND);draw(player,19)
    send=run_bot(state,["angang","jiagang","riichi_cut","cut"])
    assert send.await_args.args[3:7]==("cut",True,19,13)
    send=run_bot(state,["cut"],current=[True,False])
    send.assert_not_awaited()
    run_bot(state,["hu_self"],actionable=False).assert_not_awaited()
    run_bot(state,[],status="waiting_action_after_cut").assert_not_awaited()


def test_terminal_broadcast_isolates_failed_connections(caplog):
    state=make_state()
    state.player_list[0].user_id=1
    state.player_list[1].tag_list.append("offline")
    failed=SimpleNamespace(send_json=AsyncMock(side_effect=ConnectionError("closed test socket")))
    state.game_server.user_id_to_connection[103]=SimpleNamespace(websocket=failed)
    good=Socket();state.game_server.user_id_to_connection[104]=SimpleNamespace(websocket=good)
    asyncio.run(broadcast_result(state,hu_class="liuju",next_status="round_end_by_ready"))
    assert len(good.messages)==1 and "推倒和终局广播失败" in caplog.text


def test_terminal_spectators_receive_their_seat_even_if_player_is_offline():
    state=make_state()
    state.player_list[0].tag_list.append("offline")
    state.player_list[1].user_id=1
    state.send_to_realtime_spectators=AsyncMock()
    asyncio.run(broadcast_result(state,hu_class="liuju",next_status="round_end_by_ready"))
    assert [call.args[0] for call in state.send_to_realtime_spectators.await_args_list]==[0,1,2,3]


def test_terminal_spectator_failure_does_not_block_player_result(caplog):
    state=make_state();socket=Socket()
    state.game_server.user_id_to_connection[101]=SimpleNamespace(websocket=socket)
    state.send_to_realtime_spectators=AsyncMock(side_effect=ConnectionError("closed spectator"))
    asyncio.run(broadcast_result(state,hu_class="liuju",next_status="round_end_by_ready"))
    assert len(socket.messages)==1 and "推倒和终局观战广播失败" in caplog.text


@pytest.mark.parametrize("rounds,has_writer", [(1,True),(2,False),(3,True),(4,True)])
def test_full_match_fixed_hand_count_dealer_rotation_and_record_cleanup(rounds,has_writer):
    state=make_state(game_round=rounds)
    rounds_seen=[]
    clean=AsyncMock()
    state.game_server.gamestate_manager.cleanup_game_state_complete=clean
    writer=Mock(return_value="test-record-id")
    state.db_manager=SimpleNamespace(store_tuidao_game_record=writer) if has_writer else None

    def deal():
        # Valid ordinary four-meld + pair, 4 fan -> base 6, no first-draw bonus after a call.
        for p in state.player_list:
            p.hand_tiles[:]=HAND
        state.player_list[0].hand_tiles.append(28)
        state.tiles_list=[19]*14

    async def wait_action():
        rounds_seen.append((state.current_round,[p.user_id for p in state.player_list]))
        if state.current_round%3==1:
            state.table_claim_or_kong=True
            state.result_dict.clear()
            state.accept_self_draw(0)
        elif state.current_round%3==2:
            state.table_claim_or_kong=True
            state.current_player_index=0
            state.player_list[0].hand_tiles.remove(28)
            state.player_list[0].discard_tiles=[28]
            state.player_list[0].discard_origin_tiles=[False]
            allowed=state.check_discard_actions(28)
            await state.resolve_discard_responses({1:{"action_type":"hu_first"}},allowed)
        else:
            state.draw_reason="wall_exhausted"
            state.game_status="END"

    with patch.object(state,"init_tiles",side_effect=deal), \
         patch.object(state,"wait_action",side_effect=wait_action), \
         patch.object(state,"run_hu_result_ready_phase",new=AsyncMock()), \
         patch(MODULE+".asyncio.sleep",new=AsyncMock()):
        asyncio.run(state.game_loop_chinese())
    assert len(rounds_seen)==rounds*4
    assert rounds_seen[0][1]==rounds_seen[1][1]
    assert rounds_seen[2][1]==rounds_seen[1][1][1:]+rounds_seen[1][1][:1]
    assert all(len(p.score_history)==rounds*4 for p in state.player_list)
    assert sum(p.score for p in state.player_list)==0
    assert len(state.game_record["game_round"])==rounds*4
    assert state.game_record["game_title"]["sub_rule"]==book.SUB_RULE
    assert state.game_record["game_title"]["detailed_config"]==CONFIG
    assert state._local_record_detail is not None
    clean.assert_awaited_once_with(gamestate_id=state.gamestate_id)
    if has_writer:
        writer.assert_called_once()
        assert writer.call_args.args[3]==f"{rounds}/4"
