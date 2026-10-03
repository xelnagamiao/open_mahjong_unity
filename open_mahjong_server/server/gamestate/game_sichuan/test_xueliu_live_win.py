"""局中和牌的公开消息、花区持久状态与续打时序。"""
import asyncio
from unittest.mock import AsyncMock, patch

import pytest

from .test_xueliu_integration import full_state, WIN_HAND, WAIT_HAND


@pytest.mark.parametrize("kind,winners", [("zimo", [0]), ("ron", [1]), ("ron", [1, 2]), ("qianggang", [1])])
def test_win_is_private_immediate_and_persists_all_win_tiles(kind, winners):
    state, messages = full_state()
    state.current_player_index = 0
    state.pending_win = {"type": kind, "discarder": 0, "hepai_tile": 25}
    state.sichuan_hu_results = {
        winner: {"fan": 2, "fan_list": ["基本胡", "门清自摸"], "hepai_tile": 25}
        for winner in winners
    }
    if kind == "zimo":
        state.player_list[0].hand_tiles = list(WIN_HAND)
        state.player_list[0].has_draw_slot = True
    for winner in winners:
        state.player_list[winner].waiting_tiles = {25}
        state.player_list[winner].huapai_list = [26]

    with patch(__package__ + ".XueliuGameState.asyncio.sleep", new_callable=AsyncMock) as sleep:
        asyncio.run(state._settle_win())
    for call in sleep.await_args_list:
        assert call.args[0] <= 0.5, "局中和牌不能等待完整结算面板"
    for message in messages:
        payload = message["message"]
        if message["type"].endswith("/show_result"):
            result = payload["show_result_info"]
            assert result["suppress_hand_reveal"] is True
            assert not result.get("hepai_player_hand"), "局中消息不能泄露任何和牌者暗手"
            assert result["hepai_tile"] == 25, "和牌张公开进入花区"
            assert sum(result["score_changes"].values()) == 0
        elif message["type"].endswith("/xueliu_continue"):
            for player in payload["game_info"]["players_info"]:
                if player["player_index"] in winners:
                    assert player["huapai_list"] == [26, 25]
                    assert player["hand_tiles_count"] == 10
                if player["user_id"] != message["user_id"]:
                    assert not player.get("hand_tiles")
    for winner in winners:
        player = state.player_list[winner]
        assert player.hand_tiles == WAIT_HAND
        assert player.huapai_list == [26, 25]
        assert player.post_hu_lock and not player.is_hu
    assert state.game_status == "deal_card"
    assert sum(player.score for player in state.player_list) == 0
