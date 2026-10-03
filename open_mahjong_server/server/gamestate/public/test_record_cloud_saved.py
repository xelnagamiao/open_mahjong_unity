"""牌谱云端标记：终局本地副本与云端查询使用同一分享依据。"""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock

import pytest

from server.database.data_router import handle_get_record_by_id
from server.gamestate.public.game_record_manager import (
    local_record_detail_for_end,
    remember_local_record_detail,
)
from server.response import Game_end_info, Response


def _state():
    return SimpleNamespace(
        game_record={
            "game_title": {"rule": "guobiao", "end_time": "2026-09-24 12:00:00"},
            "game_round": {},
        },
        player_list=[],
        max_round=1,
    )


@pytest.mark.parametrize("saved_game_id", [None, "", "Abc123def4", "Lbc123def4"])
def test_game_end_preserves_cloud_result_before_generating_local_id(saved_game_id):
    state = _state()
    remember_local_record_detail(state, saved_game_id)
    response = Response(
        type="game_end",
        success=True,
        message="对局结束",
        game_end_info=Game_end_info(
            commitment=0,
            salt="",
            player_final_data={},
            record_detail=local_record_detail_for_end(state),
        ),
    )
    detail = response.model_dump(exclude_none=True)["game_end_info"]["record_detail"]
    assert detail["cloud_saved"] is bool(saved_game_id)
    if saved_game_id:
        assert detail["game_id"] == saved_game_id
    else:
        # 有自动生成的本地 ID 仍不能被当作云端牌谱。
        assert detail["game_id"].startswith("L")
        assert len(detail["game_id"]) == 10


@pytest.mark.parametrize("found", [True, False])
def test_cloud_lookup_returns_flag_without_an_extra_query(found):
    result = {
        "game_id": "Lbc123def4",
        "rule": "guobiao",
        "record": _state().game_record,
        "created_at": "2026-09-24 12:00:00",
        "players": [],
    } if found else None
    database = SimpleNamespace(get_record_by_id=Mock(return_value=result))
    server = SimpleNamespace(
        players={"connection": SimpleNamespace(user_id=10001)},
        db_manager=database,
    )
    socket = SimpleNamespace(send_json=AsyncMock())

    asyncio.run(handle_get_record_by_id(
        server, "connection", {"game_id": "Lbc123def4"}, socket,
    ))

    database.get_record_by_id.assert_called_once_with("Lbc123def4")
    socket.send_json.assert_awaited_once()
    response = socket.send_json.call_args.args[0]
    assert response["success"] is found
    if found:
        assert response["record_detail"]["cloud_saved"] is True
    else:
        assert "record_detail" not in response
