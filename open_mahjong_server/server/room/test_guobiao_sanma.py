"""Three-player Guobiao reaches custom/event rooms and the public game factory."""
import asyncio
from unittest.mock import AsyncMock, patch

import pytest

from .test_guobiao_flowers import room_manager
from .room_seats import get_seats, seat_player, unseat_player
from ..gamestate.game_guobiao.GuobiaoGameState import GuobiaoGameState
from ..gamestate.gamestate_manager import GameStateManager
from ..gamestate.public.player_count import player_count_for_sub_rule, game_player_count
from ..game_calculation.game_calculation_service import GameCalculationService


@pytest.mark.parametrize("event", [False, True])
@pytest.mark.parametrize("flowers", [False, True])
@pytest.mark.parametrize("opening", [False, True])
def test_custom_event_capacity_and_inherited_settings(event, flowers, opening):
    async def run():
        manager = room_manager()
        settings = dict(sub_rule="guobiao/sanma", use_flowers=flowers, tian_di_ren_he=opening,
                        hepai_limit=8, open_cuohe=True, cuohe_type=0, tactical_call=True, claim_protection=True)
        if event:
            response = await manager.create_empty_event_room("gb3-event", "guobiao", settings, broadcast=False)
        else:
            response = await manager.create_GB_room("connection", "三人国标", 4, "", 20, 5, True, **settings)
        assert response.success, response.message
        room = manager.rooms[response.room_info["room_id"]]
        assert room["max_player"] == 3 and len(get_seats(room)) == 3
        for key in ("use_flowers", "tian_di_ren_he", "hepai_limit", "open_cuohe", "cuohe_type", "tactical_call", "claim_protection"):
            assert room[key] == settings[key]
        while len(room["player_list"]) < 3: seat_player(room, 20000 + len(room["player_list"]))
        with pytest.raises(ValueError, match="房间已满"): seat_player(room, 30000)
        with pytest.raises(ValueError, match="座位编号无效"): seat_player(room, 30000, 3)
        uid = get_seats(room)[1]; unseat_player(room, uid, 1); seat_player(room, 30000, 1)
        assert len(get_seats(room)) == 3
    asyncio.run(run())


@pytest.mark.parametrize("sub,count,allowed", [("guobiao/sanma", 2, False), ("guobiao/sanma", 3, True),
    ("guobiao/standard", 3, False), ("guobiao/standard", 4, True)])
def test_factory_requires_rule_player_count(sub, count, allowed):
    async def run():
        rooms = room_manager(); server = rooms.game_server
        server.db_manager.get_rank_data = lambda _: None
        response = await rooms.create_GB_room("connection", "factory", 1, "", 20, 5, False, sub_rule=sub)
        assert response.success
        room = rooms.rooms[response.room_info["room_id"]]
        while len(room["player_list"]) < count:
            uid = 20000 + len(room["player_list"]); seat_player(room, uid)
            room.setdefault("ready_list", []).append(uid)
        server.room_manager = rooms; server.calculation_service = GameCalculationService()
        games = GameStateManager(server); server.gamestate_manager = games; games.run_game = AsyncMock()
        with patch("server.gamestate.gamestate_manager.attach_game_frames"):
            result = await games.start_game("connection", room["room_id"])
        if allowed:
            assert result is None, getattr(result, "message", "")
            state = games.get_game_state_by_room_id(room["room_id"])
            assert isinstance(state, GuobiaoGameState) and len(state.player_list) == count
            assert state.sub_rule == sub and state.room_rule == "guobiao"
            state.game_task.cancel(); await asyncio.gather(state.game_task, return_exceptions=True)
        else:
            assert not result.success and result.message == "人数不足"
    asyncio.run(run())


@pytest.mark.parametrize("sub,count", [("guobiao/sanma", 3), ("riichi/sanma", 3),
    ("guobiao/standard", 4), ("guobiao/blood_battle", 4), (None, 4)])
def test_player_count_is_isolated_to_three_player_subrules(sub, count):
    assert player_count_for_sub_rule(sub) == count


def test_legacy_protocol_callers_keep_four_player_count():
    from types import SimpleNamespace
    assert game_player_count(SimpleNamespace()) == 4
    assert game_player_count(SimpleNamespace(player_list=[0, 1, 2])) == 3
    assert game_player_count(SimpleNamespace(player_list=[0, 1, 2, 3])) == 4
