"""Exercise factory-created rules at the real session/message boundaries."""
import asyncio
from contextlib import asynccontextmanager
from unittest.mock import patch

import pytest

from . import gamestate_manager as factory
from .test_room_lifecycle import USERS, make_room, make_server, parked_loop


CASES = {
    "guobiao": factory.GuobiaoGameState, "qingque": factory.QingqueGameState,
    "changsha": factory.ChangshaGameState, "classical": factory.ClassicalGameState,
    "riichi": factory.RiichiGameState, "sichuan": factory.SichuanGameState,
    "zhongyong": factory.ZhongyongGameState,
    "taiwan": factory.TaiwanGameState, "shanghai": factory.ShanghaiGameState,
    "hongque": factory.HongqueGameState, "hongkong": factory.HongKongGameState,
    "filipino": factory.FilipinoGameState, "vietnamese": factory.VietnameseGameState,
    "guangdong": factory.TuidaoGameState, "shanxi": factory.ShanxiGameState,
    "wuhan": factory.WuhanGameState, "shenyang": factory.ShenyangGameState,
    "changchun": factory.ChangchunGameState, "free": factory.FreeGameState,
    "guizhou": factory.GuizhouGameState, "yixing": factory.YixingGameState,
    "nanjing": factory.NanjingGameState, "fuzhou": factory.FuzhouGameState,
    "wenzhou": factory.WenzhouGameState, "nanchang": factory.NanchangGameState,
    "shangrao": factory.ShangraoGameState, "beijing": factory.BeijingGameState,
    "hangzhou": factory.HangzhouGameState, "hongzhong": factory.HongzhongGameState,
    "jiujiang": factory.JiujiangGameState, "singapore": factory.SingaporeGameState,
    "malaysia": factory.MalaysiaGameState, "duocai": factory.DuocaiGameState,
    "guangdong/mil2023": factory.GuangdongGameState,
    "sichuan/xueliu": factory.XueliuGameState,
    "sichuan/xueliu_exchange": factory.XueliuGameState,
    "sichuan/xueliu_doubles": factory.XueliuDoublesGameState,
    "shanghai/qinghunpeng": factory.QinghunpengGameState,
    "shanghai/lajihe": factory.LajiheGameState,
    "zhongyong/nanque": factory.NanqueGameState,
    "guobiao/sanma": factory.GuobiaoGameState,
    "riichi/sanma": factory.RiichiGameState,
}


@asynccontextmanager
async def running_game(case):
    rule = case.split("/")[0]
    users = USERS[:3] if case.endswith("/sanma") or rule == "malaysia" else USERS
    server = make_server(users)
    room = make_room(users, rule, "events")
    if "/" in case:
        room["sub_rule"] = case
    if rule == "malaysia":
        room["sub_rule"] = "malaysia/traditional84"
    if case.endswith("_doubles"):
        room["team_by_seat"] = [0, 1, 0, 1]
    from server.room.room_seats import get_seats
    get_seats(room)
    room["max_player"] = len(users)
    server.room_manager.rooms["1"] = room
    with patch.object(CASES[case], "run_game_loop", parked_loop):
        result = await server.gamestate_manager.start_game(
            str(users[0]), "1", roster_revision=room.get("roster_revision"))
        assert result is None, result.message if result else None
        state = server.gamestate_manager.get_game_state_by_room_id("1")
        assert type(state) is CASES[case]
        for seat, player in enumerate(state.player_list):
            if rule != "hongque":
                player.player_index = player.original_player_index = seat
        await asyncio.sleep(0)
        try:
            yield server, room, state
        finally:
            await server.gamestate_manager.cleanup_game_state_complete(gamestate_id=state.gamestate_id)


async def broadcast_end(state):
    if (getattr(state, "sub_rule", "") or "").endswith("_doubles"):
        from server.game_calculation.sichuan.doubles_rules import match_result, team_scores
        state.team_result = match_result(team_scores(state.player_list), [])
    if hasattr(state, "broadcast_game_end"):
        await state.broadcast_game_end()
    elif hasattr(state, "emit_game_end_payloads"):
        state.emit_game_end_payloads()
        await state.flush_outbound_payloads()
    elif state.room_rule == "hongque":
        from .game_hongque.boardcast import broadcast_game_end
        await broadcast_game_end(state)
    elif state.room_rule == "free":
        from .game_free.boardcast import broadcast_game_end
        await broadcast_game_end(state)
    else:
        from .game_taiwan.boardcast import broadcast_game_end
        await broadcast_game_end(state)


@pytest.mark.parametrize("case", CASES)
@pytest.mark.parametrize("stalled_seat", [False, True])
def test_real_terminal_packets_identify_their_game(case, stalled_seat, monkeypatch):
    async def run():
        async with running_game(case) as (server, _, state):
            if stalled_seat:
                monkeypatch.setattr("server.gamestate.public.connection_delivery.SEND_TIMEOUT_SECONDS", .02)
                async def stall(_):
                    await asyncio.Event().wait()
                next(iter(server.user_id_to_connection.values())).websocket.send_json.side_effect = stall
            await broadcast_end(state)
            for conn in server.user_id_to_connection.values():
                packets = [call.args[0] for call in conn.websocket.send_json.call_args_list
                           if call.args[0]["type"].endswith("/game_end")]
                assert len(packets) == 1, (case, packets)
                assert packets[0].get("gamestate_id") == state.gamestate_id
    asyncio.run(run())


@pytest.mark.parametrize("case", CASES)
def test_factory_rules_release_every_index_after_last_disconnect(case):
    async def run():
        async with running_game(case) as (server, room, state):
            for player in state.player_list:
                await server.gamestate_manager.player_disconnect(player.user_id)
            assert state.lifecycle_state == "closed"
            assert state.close_reason == "all_humans_offline"
            assert state.game_task.done()
            assert not server.gamestate_manager.gamestate_id_to_game_state
            assert not server.gamestate_manager.user_id_to_game_state
            assert not server.room_manager.active_game_room_ids
            assert room["is_game_running"] is False
    asyncio.run(run())
