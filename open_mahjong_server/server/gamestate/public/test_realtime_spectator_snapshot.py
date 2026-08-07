"""实时观战初始快照保真回归测试。

核心约定：观战者中途加入时收到的 game_start，必须与「发给被观战座位」的
game_start 完全一致（字段、按视角脱敏结果一致），不得额外携带宿主看不到的数据。
"""

import asyncio
import unittest
from types import SimpleNamespace
from unittest.mock import AsyncMock

from server.response import GameInfo
from server.gamestate.game_changsha.ChangshaGameState import ChangshaGameState
from server.gamestate.game_changsha.boardcast import (
    _build_game_start_payload_for_viewer as _changsha_payload,
)
from server.gamestate.game_classical.ClassicalGameState import ClassicalGameState
from server.gamestate.game_guobiao.GuobiaoGameState import GuobiaoGameState
from server.gamestate.game_guobiao.boardcast import (
    _build_game_start_payload_for_viewer as _guobiao_payload,
)
from server.gamestate.game_jiandan.JiandanGameState import JiandanGameState
from server.gamestate.game_mmcr.QingqueGameState import QingqueGameState
from server.gamestate.game_sichuan.SichuanGameState import SichuanGameState


class DummyPlayer:
    def __init__(self, player_index, user_id, hand_tiles=None):
        self.player_index = player_index
        self.user_id = user_id
        self.username = f"player-{player_index}"
        self.hand_tiles = list(hand_tiles or [])
        self.combination_tiles = []
        self.combination_mask = []
        self.discard_tiles = []
        self.discard_origin_tiles = []
        self.huapai_list = []
        self.remaining_time = 5
        self.original_player_index = player_index
        self.score = 0
        self.title_used = None
        self.profile_used = None
        self.character_used = None
        self.voice_used = None
        self.score_history = []
        self.round_number_history = []
        self.tag_list = []


def _fake_game_server(spectator_user_id):
    websocket = SimpleNamespace(send_json=AsyncMock())
    return SimpleNamespace(
        user_id_to_connection={
            spectator_user_id: SimpleNamespace(websocket=websocket),
        }
    ), websocket


def _attach_angang(player, tile_id=35, mask_flags=(2, 2, 2, 2)):
    player.combination_tiles.append(f"G{tile_id}")
    mask = []
    for flag in mask_flags:
        mask.extend([flag, tile_id])
    player.combination_mask.append(mask)


class RealtimeSpectatorSnapshotTest(unittest.TestCase):
    """国标：观战快照与被观战玩家 game_start 完全一致，暗杠字段按视角脱敏。"""

    def test_guobiao_snapshot_equals_host_game_start_and_masks_angang(self):
        state = object.__new__(GuobiaoGameState)
        state.room_id = 1
        state.gamestate_id = "guobiao-realtime-test"
        state.tips = True
        state.current_player_index = 0
        state.dealer_index = 0
        state.server_action_tick = 7
        state.max_round = 8
        state.tiles_list = [11, 12, 13, 14]
        state.commitment = 123
        state.salt = "salt"
        state.current_round = 1
        state.step_time = 5
        state.round_time = 20
        state.room_type = "custom"
        state.room_rule = "guobiao"
        state.sub_rule = "guobiao/standard"
        state.hepai_limit = 8
        state.open_cuohe = False
        state.show_moqie_hint = False
        state.tactical_call = False
        state.claim_protection = False
        state.isPlayerSetRandomSeed = False
        state.player_entry_order = [100, 101, 102, 103]
        state.game_status = "waiting"
        state.action_dict = {i: [] for i in range(4)}

        players = []
        for index in range(4):
            player = DummyPlayer(index, 100 + index, [11 + index])
            player.guobiao_rank = ""
            player.guobiao_score = 0
            player.has_draw_slot = False
            players.append(player)
        # 玩家 0 有一副暗杠，视角玩家是 1
        _attach_angang(players[0])
        state.player_list = players

        spectator_user_id = 999
        state.game_server, websocket = _fake_game_server(spectator_user_id)

        asyncio.run(state.send_realtime_spectator_snapshot(spectator_user_id, 1))

        self.assertEqual(websocket.send_json.await_count, 1)
        snapshot = websocket.send_json.await_args.args[0]
        self.assertEqual(snapshot["type"], "gamestate/guobiao/game_start")
        self.assertEqual(snapshot["game_info"]["view_player_index"], 1)

        # 非视角玩家的暗杠字段与宿主视角一致：G 串与掩码牌 id 全部脱敏
        p0 = snapshot["game_info"]["players_info"][0]
        self.assertEqual(p0["combination_tiles"], ["G0"])
        self.assertEqual(p0["combination_mask"], [[2, 0, 2, 0, 2, 0, 2, 0]])
        # 视角玩家 1 能看到自己的手牌，其余玩家不能
        for index, info in enumerate(snapshot["game_info"]["players_info"]):
            if index == 1:
                self.assertEqual(info["hand_tiles"], [12])
            else:
                self.assertIsNone(info.get("hand_tiles"))

        # 保真：快照 == 发给被观战座位的 game_start（外加 view_player_index）
        expected_payload = _guobiao_payload(state, 1)
        expected_payload["view_player_index"] = 1
        self.assertEqual(
            snapshot["game_info"],
            GameInfo(**expected_payload).dict(exclude_none=True),
        )

    def test_changsha_snapshot_equals_host_game_start_and_preserves_rule_data(self):
        """长沙规则本身明示暗杠：快照保持与被观战玩家一致即可，不做额外隐藏。"""
        state = object.__new__(ChangshaGameState)
        state.room_id = 1
        state.gamestate_id = "changsha-realtime-test"
        state.tips = True
        state.current_player_index = 0
        state.server_action_tick = 7
        state.max_round = 8
        state.tiles_list = [11, 12, 13, 14]
        state.commitment = 123
        state.salt = "salt"
        state.current_round = 1
        state.step_time = 5
        state.round_time = 20
        state.room_type = "custom"
        state.room_rule = "changsha"
        state.sub_rule = "changsha/classic_double_bird"
        state.hepai_limit = 1
        state.open_cuohe = False
        state.show_moqie_hint = False
        state.tactical_call = False
        state.claim_protection = False
        state.open_kong_replacement_count = 2
        state.initial_hu_enabled = {}
        state.initial_hu_types = {}
        state.bird_count = 2
        state.dealer_bird = True
        state.base_score_no_dealer = False
        state.small_hu_score = 2
        state.big_hu_score = 8
        state.isPlayerSetRandomSeed = False
        state.player_entry_order = [100, 101, 102, 103]
        state.game_status = "waiting"
        state.action_dict = {i: [] for i in range(4)}

        players = [DummyPlayer(i, 100 + i, [11 + i]) for i in range(4)]
        # 长沙暗杠掩码标志位为 0（规则明示），快照必须原样保留
        _attach_angang(players[0], mask_flags=(0, 0, 0, 0))
        state.player_list = players

        spectator_user_id = 999
        state.game_server, websocket = _fake_game_server(spectator_user_id)

        asyncio.run(state.send_realtime_spectator_snapshot(spectator_user_id, 1))

        self.assertEqual(websocket.send_json.await_count, 1)
        snapshot = websocket.send_json.await_args.args[0]
        self.assertEqual(snapshot["type"], "gamestate/changsha/game_start")
        self.assertEqual(snapshot["game_info"]["view_player_index"], 1)

        p0 = snapshot["game_info"]["players_info"][0]
        self.assertEqual(p0["combination_tiles"], ["G35"])
        self.assertEqual(p0["combination_mask"], [[0, 35, 0, 35, 0, 35, 0, 35]])

        expected_payload = _changsha_payload(state, 1)
        expected_payload["view_player_index"] = 1
        self.assertEqual(
            snapshot["game_info"],
            GameInfo(**expected_payload).dict(exclude_none=True),
        )

    def test_all_realtime_rules_mount_rule_local_snapshot(self):
        expected = {
            GuobiaoGameState: "server.gamestate.game_guobiao.boardcast",
            ChangshaGameState: "server.gamestate.game_changsha.boardcast",
            ClassicalGameState: "server.gamestate.game_classical.boardcast",
            QingqueGameState: "server.gamestate.game_mmcr.boardcast",
            SichuanGameState: "server.gamestate.game_sichuan.boardcast",
            JiandanGameState: "server.gamestate.game_jiandan.JiandanGameState",
        }
        for cls, module in expected.items():
            self.assertEqual(
                cls.send_realtime_spectator_snapshot.__module__,
                module,
                f"{cls.__name__} 未挂载规则本地 send_realtime_spectator_snapshot",
            )


if __name__ == "__main__":
    unittest.main()
