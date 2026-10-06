"""暗杠保密规则：检查实际广播、重连与实时观战的可见字段。"""
import asyncio
import copy
from types import SimpleNamespace
import unittest
from unittest.mock import AsyncMock

from server.gamestate.game_classical import boardcast as classical
from server.gamestate.game_classical.ClassicalGameState import ClassicalGameState
from server.gamestate.game_mmcr import boardcast as qingque
from server.gamestate.game_mmcr.QingqueGameState import QingqueGameState
from server.gamestate.game_taiwan.boardcast import _build_do_action_payload
from server.gamestate.public.test_realtime_spectator_snapshot import DummyPlayer, _attach_angang
from server.response import GameInfo


def make_state(rule):
    players = [DummyPlayer(i, 100 + i, [11 + i]) for i in range(4)]
    for player in players:
        _attach_angang(player, 31 + player.player_index)
        player.combination_tiles.extend(["k22", "g23"])
        player.combination_mask.extend([[0, 22, 1, 22, 0, 22], [0, 23, 1, 23, 0, 23, 0, 23]])
    sockets = {p.user_id: SimpleNamespace(websocket=SimpleNamespace(send_json=AsyncMock())) for p in players}
    sockets[999] = SimpleNamespace(websocket=SimpleNamespace(send_json=AsyncMock()))
    return SimpleNamespace(
        room_id=1, gamestate_id="concealed-kong-test", tips=True, current_player_index=0,
        count_tips=False, pointer_tips=True, show_moqie_hint=False,
        server_action_tick=7, max_round=8, tiles_list=[11, 12, 13, 14], dead_wall_count=0,
        commitment=123, salt="salt", current_round=1, step_time=5, round_time=20,
        room_type="custom", room_rule=rule, sub_rule=f"{rule}/standard", hepai_limit=1,
        open_cuohe=False, isPlayerSetRandomSeed=False, player_entry_order=[100, 101, 102, 103],
        game_status="waiting", action_dict={i: [] for i in range(4)}, player_list=players,
        game_server=SimpleNamespace(user_id_to_connection=sockets), send_to_realtime_spectators=AsyncMock(),
    )


class ConcealedKongVisibilityTest(unittest.TestCase):
    def test_live_and_reconnect_snapshots_hide_other_players_kongs(self):
        for rule, wire in (("classical", classical), ("qingque", qingque)):
            with self.subTest(rule=rule):
                state = make_state(rule)
                before = copy.deepcopy([(p.combination_tiles, p.combination_mask) for p in state.player_list])
                for viewer in range(4):
                    payload = wire._build_game_start_payload_for_viewer(state, viewer)
                    # Check the serialized packet as well as the builder.
                    players = GameInfo(**payload).model_dump(exclude_none=True)["players_info"]
                    for owner, info in enumerate(players):
                        expected_tile = f"G{31 + owner}" if owner == viewer else "G0"
                        expected_mask = [2, 31 + owner] * 4 if owner == viewer else [2, 0] * 4
                        self.assertEqual(info["combination_tiles"], [expected_tile, "k22", "g23"])
                        self.assertEqual(info["combination_mask"][0], expected_mask)
                        self.assertEqual(info["combination_mask"][1:], state.player_list[owner].combination_mask[1:])
                self.assertEqual([(p.combination_tiles, p.combination_mask) for p in state.player_list], before)

    def test_actual_reconnect_handler_hides_other_players_kongs(self):
        for rule, state_type in (("classical", ClassicalGameState), ("qingque", QingqueGameState)):
            with self.subTest(rule=rule):
                state = make_state(rule)
                for viewer in range(4):
                    asyncio.run(state_type.player_reconnect(state, 100 + viewer))
                    packet = state.game_server.user_id_to_connection[100 + viewer].websocket.send_json.await_args.args[0]
                    for owner, info in enumerate(packet["game_info"]["players_info"]):
                        self.assertEqual(info["combination_tiles"][0], f"G{31 + owner}" if owner == viewer else "G0")
                        self.assertEqual(info["combination_mask"][0], [2, 31 + owner] * 4 if owner == viewer else [2, 0] * 4)

    def test_realtime_spectator_receives_only_the_watched_seats_view(self):
        for rule, wire in (("classical", classical), ("qingque", qingque)):
            with self.subTest(rule=rule):
                state = make_state(rule)
                asyncio.run(wire.send_realtime_spectator_snapshot(state, 999, 1))
                socket = state.game_server.user_id_to_connection[999].websocket
                packet = socket.send_json.await_args.args[0]
                players = packet["game_info"]["players_info"]
                self.assertEqual(players[0]["combination_tiles"][0], "G0")
                self.assertEqual(players[0]["combination_mask"][0], [2, 0] * 4)
                self.assertEqual(players[1]["combination_tiles"][0], "G32")
                self.assertEqual(players[1]["combination_mask"][0], [2, 32] * 4)
                expected = wire._build_game_start_payload_for_viewer(state, 1)
                expected["view_player_index"] = 1
                self.assertEqual(packet["game_info"], GameInfo(**expected).model_dump(exclude_none=True))

    def test_classical_angang_broadcast_is_private_for_all_seats_and_observers(self):
        state = make_state("classical")
        mask = [2, 35] * 4
        asyncio.run(classical.broadcast_do_action(
            state, ["angang"], 0, combination_mask=mask, combination_target="G35", is_mo_gang=True,
        ))
        for viewer in range(4):
            packet = state.game_server.user_id_to_connection[100 + viewer].websocket.send_json.await_args.args[0]
            info = packet["do_action_info"]
            self.assertEqual(info["combination_target"], "G35" if viewer == 0 else "G0")
            self.assertEqual(info["combination_mask"], mask if viewer == 0 else [2, 0] * 4)
            self.assertEqual(info["action_list"], ["angang"])
            self.assertTrue(info["is_mo_gang"])
            spectator_view, response = state.send_to_realtime_spectators.await_args_list[viewer].args
            self.assertEqual(spectator_view, viewer)
            self.assertEqual(response.model_dump(exclude_none=True), packet)
        self.assertEqual(mask, [2, 35] * 4)

    def test_classical_public_melds_are_unchanged(self):
        state = make_state("classical")
        mask = [0, 22, 1, 22, 0, 22, 0, 22]
        asyncio.run(classical.broadcast_do_action(state, ["gang"], 0, combination_mask=mask, combination_target="g22"))
        for viewer in range(4):
            packet = state.game_server.user_id_to_connection[100 + viewer].websocket.send_json.await_args.args[0]
            self.assertEqual(packet["do_action_info"]["combination_target"], "g22")
            self.assertEqual(packet["do_action_info"]["combination_mask"], mask)

    def test_rules_with_public_kongs_keep_visible_tiles(self):
        for mask, public in (([2, 35, 0, 35, 0, 35, 2, 35], False), ([0, 35] * 4, True)):
            with self.subTest(mask=mask):
                state = SimpleNamespace(server_action_tick=1, concealed_kongs_public=public,
                                        build_private_do_action_info=lambda *_: {})
                info = _build_do_action_payload(state, ["angang"], 0, 1, combination_mask=mask, combination_target="G35")
                self.assertEqual(info["combination_mask"], mask)

    def test_classical_settlement_still_reveals_the_winners_real_kong(self):
        state = make_state("classical")
        mask = [2, 35] * 4
        scores = {i: 0 for i in range(4)}
        names = {i: [] for i in range(4)}
        asyncio.run(classical.broadcast_shuhewei(
            state, scores, scores, scores, names, names, "hu_self", 0,
            hepai_player_hand=[11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41],
            hepai_player_combination_mask=[mask], next_status="round_end_by_ready",
        ))
        for viewer in range(4):
            packet = state.game_server.user_id_to_connection[100 + viewer].websocket.send_json.await_args.args[0]
            self.assertEqual(packet["show_shuhewei_info"]["hepai_player_combination_mask"], [mask])


if __name__ == "__main__":
    unittest.main()
