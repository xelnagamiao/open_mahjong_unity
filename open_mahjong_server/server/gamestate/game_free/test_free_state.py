import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

from server.gamestate.game_free import boardcast
from server.gamestate.game_free.FreeGameState import FreeGameState
from server.gamestate.game_free.init_tiles import build_wall_tiles
from server.gamestate.game_free.player import VOTE_BLANK, VOTE_END_MATCH, VOTE_END_ROUND, VOTE_RESTART_ROUND


def test_build_wall_tiles_counts_and_empty() -> None:
    full = build_wall_tiles({})
    assert len(full) == 9 * 4 * 3 + 7 * 4 + 8
    empty = build_wall_tiles({
        "wall_wan": False, "wall_tong": False, "wall_suo": False,
        "wall_winds": False, "wall_dragons": False, "wall_flowers": False,
    })
    assert empty == []
    only_flowers = build_wall_tiles({
        "wall_wan": False, "wall_tong": False, "wall_suo": False,
        "wall_winds": False, "wall_dragons": False, "wall_flowers": True,
    })
    assert only_flowers == list(range(51, 59))


def test_hidden_transfer_masks_actions_snapshots_and_unrelated_broadcasts() -> None:
    async def scenario():
        state, sockets = _make_state()
        state.player_list[0].hand_tiles.extend([19, 22])
        await state.handle_command(101, {"action": "transfer_put", "tile": 19, "face_down": True})
        assert state.transfer_tile == 19 and state.transfer_face_down
        assert state.player_list[0].hand_tiles == [22]
        for index, socket in enumerate(sockets.values()):
            action = socket.messages[-1]["do_action_info"]
            assert action["cut_tile"] == (19 if index == 0 else 0)
            assert action["transfer_face_down"] is True
            info = boardcast.game_info_payload(state, index)
            assert info["detailed_config"]["transfer_tile"] == 0
            assert info["detailed_config"]["transfer_face_down"] is True
        await state.handle_command(102, {"action": "set_vote", "vote": VOTE_END_ROUND})
        await boardcast.broadcast_game_start(state)
        for socket in sockets.values():
            for message in socket.messages:
                assert message["free_table_info"]["transfer_tile"] == 0
                assert message["free_table_info"]["transfer_face_down"] is True

    asyncio.run(scenario())


def test_hidden_transfer_reveals_only_to_taker_and_stands_open_hand() -> None:
    async def scenario():
        state, sockets = _make_state()
        state.player_list[0].hand_tiles.append(19)
        taker = state.player_list[1]
        taker.hand_tiles.append(22)
        taker.revealed = True
        await state.handle_command(101, {"action": "transfer_put", "tile": 19, "face_down": True})
        await state.handle_command(102, {"action": "transfer_take"})
        assert taker.hand_tiles == [22, 19] and not taker.revealed
        assert state.transfer_tile is None and not state.transfer_face_down
        for index, socket in enumerate(sockets.values()):
            message = socket.messages[-1]
            assert message["do_action_info"]["deal_tile"] == (19 if index == 1 else 0)
            assert message["do_action_info"]["transfer_face_down"] is True
            assert message["free_table_info"]["transfer_tile"] is None
            assert message["free_table_info"]["transfer_face_down"] is False
            assert message["free_table_info"]["revealed"]["1"] is False
            restored = boardcast.game_info_payload(state, index)
            assert restored["players_info"][1]["hand_tiles"] == ([22, 19] if index == 1 else None)
        # 已竖起的玩家再按一次立牌，不能通过 revealed_hand 字段泄露暗牌。
        await state.handle_command(102, {"action": "stand"})
        for socket in sockets.values():
            assert socket.messages[-1]["free_table_info"]["revealed_hand"] is None
        await state.handle_command(102, {"action": "reveal"})
        for socket in sockets.values():
            assert socket.messages[-1]["free_table_info"]["revealed_hand"] == [22, 19]

    asyncio.run(scenario())


def test_face_up_transfer_remains_compatible_and_updates_revealed_hand() -> None:
    async def scenario():
        state, sockets = _make_state()
        state.player_list[0].hand_tiles.extend([19, 22])
        state.player_list[0].revealed = True
        state.player_list[1].revealed = True
        await state.handle_command(101, {"action": "transfer_put", "tile": 19})
        for socket in sockets.values():
            message = socket.messages[-1]
            assert message["do_action_info"]["cut_tile"] == 19
            assert message["free_table_info"]["transfer_tile"] == 19
            assert message["free_table_info"]["revealed_hand"] == [22]
            assert message["free_table_info"]["revealed_player_index"] == 0
        await state.handle_command(102, {"action": "transfer_take"})
        for socket in sockets.values():
            message = socket.messages[-1]
            assert message["do_action_info"]["deal_tile"] == 19
            assert message["do_action_info"]["transfer_face_down"] is False
            assert message["free_table_info"]["revealed_hand"] == [19]
            assert message["free_table_info"]["revealed_player_index"] == 1
        assert state.player_list[1].revealed

    asyncio.run(scenario())


def test_hidden_transfer_can_only_be_taken_once_when_players_race() -> None:
    async def scenario():
        state, sockets = _make_state()
        state.player_list[0].hand_tiles.append(19)
        await state.handle_command(101, {"action": "transfer_put", "tile": 19, "face_down": True})
        await asyncio.gather(*(state.handle_command(uid, {"action": "transfer_take"}) for uid in (102, 103, 104)))
        assert sum(p.hand_tiles.count(19) for p in state.player_list) == 1
        for socket in sockets.values():
            takes = [m for m in socket.messages if m.get("do_action_info", {}).get("action_list") == ["free_transfer_take"]]
            assert len(takes) == 1

    asyncio.run(scenario())


def test_invalid_or_occupied_transfer_keeps_tile_and_visibility() -> None:
    async def scenario():
        state, sockets = _make_state()
        state.player_list[0].hand_tiles.extend([19, 22])
        await state.handle_command(101, {"action": "transfer_put", "tile": 18, "face_down": True})
        assert state.transfer_tile is None and not state.transfer_face_down
        await state.handle_command(101, {"action": "transfer_put", "tile": 19, "face_down": True})
        tick = state.action_tick
        await state.handle_command(101, {"action": "transfer_put", "tile": 22, "face_down": False})
        assert state.transfer_tile == 19 and state.transfer_face_down
        assert state.player_list[0].hand_tiles == [22] and state.action_tick == tick
        for uid in sockets:
            await state.handle_command(uid, {"action": "set_vote", "vote": VOTE_RESTART_ROUND})
        assert state.transfer_tile is None and not state.transfer_face_down

    asyncio.run(scenario())


def test_consecutive_draw_commands_preserve_wall_order_and_latest_draw() -> None:
    async def scenario():
        state, sockets = _make_state()
        expected = state.tiles_list[:10]
        await asyncio.gather(*(state.handle_command(101, {"action": "draw"}) for _ in range(10)))
        assert state.player_list[0].hand_tiles == expected
        assert state.player_list[0].last_drawn_tile == expected[-1]
        assert state.player_list[0].has_draw_slot
        assert [m["do_action_info"]["deal_tile"] for m in sockets[101].messages] == expected
        assert len({m["do_action_info"]["action_tick"] for m in sockets[101].messages}) == 10

    asyncio.run(scenario())


class _FakeWs:
    def __init__(self):
        self.messages = []

    async def send_json(self, payload):
        self.messages.append(payload)


def _make_state(player_ids=(101, 102, 103, 104), **flags) -> tuple[FreeGameState, dict[int, _FakeWs]]:
    sockets = {uid: _FakeWs() for uid in player_ids}
    connections = {
        uid: SimpleNamespace(websocket=sockets[uid]) for uid in sockets
    }
    room = {
        "room_id": "free-1",
        "player_list": list(player_ids),
        "player_settings": {
            uid: {"username": chr(ord("a") + i)} for i, uid in enumerate(player_ids)
        },
        "random_seed": 12345,
        "sub_rule": "free/standard",
        **flags,
    }
    server = SimpleNamespace(
        user_id_to_connection=connections,
        gamestate_manager=SimpleNamespace(cleanup_game_state_complete=AsyncMock()),
        room_manager=SimpleNamespace(finish_custom_game_room=AsyncMock()),
    )
    state = FreeGameState(server, room, gamestate_id="free-test")
    return state, sockets


def test_same_seed_restart_restores_wall() -> None:
    async def scenario():
        state, _ = _make_state()
        original = list(state.tiles_list)
        await state.handle_command(101, {"action": "draw"})
        await state.handle_command(101, {"action": "cut", "TileId": state.player_list[0].hand_tiles[0]})
        for uid in (101, 102, 103, 104):
            await state.handle_command(uid, {"action": "set_vote", "vote": VOTE_RESTART_ROUND})
        assert state.current_round == 1
        assert state.tiles_list == original
        assert state.player_list[0].hand_tiles == []
        assert state.player_list[0].discard_tiles == []
        assert state.transfer_tile is None

    asyncio.run(scenario())


def test_end_round_uses_new_seed_and_keeps_scores() -> None:
    async def scenario():
        state, _ = _make_state()
        original = list(state.tiles_list)
        state.player_list[0].score = 50
        for uid in (101, 102, 103, 104):
            await state.handle_command(uid, {"action": "set_vote", "vote": VOTE_END_ROUND})
        assert state.current_round == 2
        assert state.tiles_list != original
        assert state.player_list[0].score == 50
        assert all(p.vote == VOTE_BLANK for p in state.player_list)

    asyncio.run(scenario())


def test_river_taken_cannot_enter_another_meld() -> None:
    async def scenario():
        state, _ = _make_state()
        p0 = state.player_list[0]
        p1 = state.player_list[1]
        p0.hand_tiles.extend([11, 12])
        p1.hand_tiles.extend([11, 11])
        p0.discard_tiles.append(13)
        state.discard_log.append((0, 13))
        await state.handle_command(102, {
            "action": "create_meld",
            "include_river": True,
            "combination_mask": [0, 11, 0, 11, 1, 13],
        })
        assert len(p1.combination_mask) == 1
        assert 13 not in p0.discard_tiles
        before = len(p1.combination_mask)
        await state.handle_command(102, {
            "action": "create_meld",
            "include_river": True,
            "combination_mask": [0, 11, 1, 13],
        })
        assert len(p1.combination_mask) == before

    asyncio.run(scenario())


def test_score_revision_conflict_discards_draft() -> None:
    async def scenario():
        state, sockets = _make_state()
        await state.handle_command(101, {
            "action": "set_scores",
            "score_revision": 0,
            "scores": {"0": 10, "1": 20, "2": 30, "3": 40},
        })
        assert state.score_revision == 1
        assert state.player_list[0].score == 10
        for socket in sockets.values():
            socket.messages.clear()
        await state.handle_command(102, {
            "action": "set_scores",
            "score_revision": 0,
            "actor_player_index": 0,
            "scores": {"0": 99, "1": 99, "2": 99, "3": 99},
        })
        assert [player.score for player in state.player_list] == [10, 20, 30, 40]
        assert state.score_revision == 1
        for socket in sockets.values():
            assert len(socket.messages) == 1
            response = socket.messages[0]
            assert response["type"] == "gamestate/free/scores"
            assert response["free_table_info"]["actor_player_index"] is None
            assert response["free_table_info"]["scores"] == {"0": 10, "1": 20, "2": 30, "3": 40}
            assert response["free_table_info"]["score_revision"] == 1

    asyncio.run(scenario())


def test_dragged_meld_river_slot_preserves_identical_hand_tile_orientation() -> None:
    async def scenario():
        for river_index in range(4):
            state, sockets = _make_state()
            owner, player = state.player_list[:2]
            owner.discard_tiles.append(11)
            state.discard_log.append((0, 11))
            player.hand_tiles.extend([11, 11, 12])
            pairs = [[2, 11], [0, 11], [0, 12]]
            pairs.insert(river_index, [0, 11])
            mask = [value for pair in pairs for value in pair]
            await state.handle_command(102, {
                "action": "create_meld", "include_river": True,
                "river_tile_index": river_index, "combination_mask": mask,
            })
            expected = list(mask)
            expected[river_index * 2] = 1
            assert player.combination_mask == [expected]
            assert player.hand_tiles == []
            assert owner.discard_tiles == []
            for viewer, socket in enumerate(sockets.values()):
                visible = list(expected)
                if viewer != player.player_index:
                    for index in range(0, len(visible), 2):
                        if visible[index] == 2:
                            visible[index + 1] = 0
                assert socket.messages[-1]["do_action_info"]["combination_mask"] == visible

    asyncio.run(scenario())


def test_invalid_meld_river_slot_is_rejected_before_consuming_tiles() -> None:
    async def scenario():
        for river_index in (-1, 3, True, 1.5, "1", 2):
            state, sockets = _make_state()
            owner, player = state.player_list[:2]
            owner.discard_tiles.append(11)
            state.discard_log.append((0, 11))
            player.hand_tiles.extend([11, 12])
            await state.handle_command(102, {
                "action": "create_meld", "include_river": True,
                "river_tile_index": river_index, "combination_mask": [2, 11, 1, 11, 0, 12],
            })
            assert player.hand_tiles == [11, 12]
            assert owner.discard_tiles == [11]
            assert player.combination_mask == []
            assert all(not socket.messages for socket in sockets.values())

    asyncio.run(scenario())


def test_score_actor_comes_from_authenticated_player_for_every_viewer() -> None:
    async def scenario():
        state, sockets = _make_state()
        await state.handle_command(103, {
            "action": "set_scores",
            "score_revision": 0,
            "actor_player_index": 0,
            "scores": {"0": 10, "1": 20, "2": 30, "3": 40},
        })
        for socket in sockets.values():
            assert len(socket.messages) == 1
            response = socket.messages[0]
            assert response["type"] == "gamestate/free/scores"
            table = response["free_table_info"]
            assert table["actor_player_index"] == 2
            assert table["score_revision"] == 1
            assert table["scores"] == {"0": 10, "1": 20, "2": 30, "3": 40}

    asyncio.run(scenario())


def test_table_action_actor_is_event_scoped_and_preserves_existing_fields() -> None:
    async def scenario():
        state, sockets = _make_state()
        state.player_list[1].hand_tiles.append(11)
        for action, suffix in (("set_vote", "votes"), ("reveal", "reveal"), ("stand", "stand")):
            await state.handle_command(102, {
                "action": action,
                "vote": VOTE_END_ROUND,
                "actor_player_index": 3,
            })
            for socket in sockets.values():
                response = socket.messages[-1]
                assert response["type"] == f"gamestate/free/{suffix}"
                table = response["free_table_info"]
                assert table["actor_player_index"] == 1
                if action == "set_vote":
                    assert table["votes"]["1"] == VOTE_END_ROUND
                else:
                    assert table["revealed_player_index"] == 1
                    assert table["revealed_hand"] == ([11] if action == "reveal" else None)
                    assert table["revealed"]["1"] == (action == "reveal")

        # Plain snapshots retain the old fields without borrowing the last actor.
        table = boardcast.free_table_payload(state)
        assert table.pop("actor_player_index") is None
        assert set(table) == {
            "votes", "transfer_tile", "transfer_face_down", "score_revision", "scores", "revealed",
            "revealed_player_index", "revealed_hand", "last_river_player", "last_river_tile",
        }
        await state.player_reconnect(101)
        reconnect = sockets[101].messages[-1]
        assert reconnect["free_table_info"]["actor_player_index"] is None
        assert reconnect["game_info"]["detailed_config"]["actor_player_index"] is None

    asyncio.run(scenario())


def test_votes_require_all_four() -> None:
    async def scenario():
        state, _ = _make_state()
        original_round = state.current_round
        await state.handle_command(101, {"action": "set_vote", "vote": VOTE_END_ROUND})
        await state.handle_command(102, {"action": "set_vote", "vote": VOTE_END_ROUND})
        await state.handle_command(103, {"action": "set_vote", "vote": VOTE_END_ROUND})
        assert state.current_round == original_round
        await state.handle_command(104, {"action": "set_vote", "vote": VOTE_RESTART_ROUND})
        assert state.current_round == original_round
        for uid in (101, 102, 103, 104):
            await state.handle_command(uid, {"action": "set_vote", "vote": VOTE_END_ROUND})
        assert state.current_round == original_round + 1

    asyncio.run(scenario())


def test_transfer_slot_is_single_and_serialized() -> None:
    async def scenario():
        state, _ = _make_state()
        state.player_list[0].hand_tiles.append(11)
        state.player_list[1].hand_tiles.append(12)
        await state.handle_command(101, {"action": "transfer_put", "tile": 11})
        assert state.transfer_tile == 11
        await state.handle_command(102, {"action": "transfer_put", "tile": 12})
        assert state.transfer_tile == 11
        assert 12 in state.player_list[1].hand_tiles
        await state.handle_command(103, {"action": "transfer_take"})
        assert state.transfer_tile is None
        assert 11 in state.player_list[2].hand_tiles
        await state.handle_command(104, {"action": "transfer_take"})
        assert 11 not in state.player_list[3].hand_tiles

    asyncio.run(scenario())


def test_recall_river_flower_and_whole_meld() -> None:
    async def scenario():
        state, _ = _make_state()
        p0 = state.player_list[0]
        p0.discard_tiles.extend([11, 12])
        state.discard_log.extend([(0, 11), (0, 12)])
        p0.huapai_list.extend([51, 52])
        p0.combination_mask.append([0, 21, 0, 21, 1, 21])
        p0.combination_tiles.append("F0")
        await state.handle_command(101, {"action": "recall_river", "index": 0, "tile": 11})
        assert 11 in p0.hand_tiles
        assert p0.discard_tiles == [12]
        await state.handle_command(101, {"action": "recall_flower", "index": 1, "tile": 52})
        assert 52 in p0.hand_tiles
        assert p0.huapai_list == [51]
        await state.handle_command(101, {"action": "recall_meld", "meld_index": 0})
        assert p0.combination_mask == []
        assert p0.hand_tiles.count(21) == 3

    asyncio.run(scenario())


def test_hu_and_tsumo_only_shout_reveal_is_push() -> None:
    async def scenario():
        state, sockets = _make_state()
        p0 = state.player_list[0]
        p0.hand_tiles.append(11)
        await state.handle_command(101, {"action": "hu"})
        assert p0.revealed is False
        assert any(
            m.get("type") == "gamestate/free/do_action"
            and (m.get("do_action_info") or {}).get("is_claim")
            and (m.get("do_action_info") or {}).get("action_list") == ["hu"]
            for m in sockets[101].messages
        )
        sockets[101].messages.clear()
        await state.handle_command(101, {"action": "hu_self"})
        assert p0.revealed is False
        assert any(
            m.get("type") == "gamestate/free/do_action"
            and (m.get("do_action_info") or {}).get("action_list") == ["hu_self"]
            for m in sockets[101].messages
        )
        await state.handle_command(101, {"action": "reveal"})
        assert p0.revealed is True
        await state.handle_command(101, {"action": "stand"})
        assert p0.revealed is False

    asyncio.run(scenario())


def test_variable_player_count_does_not_pad_and_votes_among_seated() -> None:
    async def scenario():
        state, _ = _make_state(player_ids=(101, 102))
        assert len(state.player_list) == 2
        assert all(p.user_id > 0 for p in state.player_list)
        await state.handle_command(101, {"action": "set_vote", "vote": VOTE_END_ROUND})
        assert state.current_round == 1
        await state.handle_command(102, {"action": "set_vote", "vote": VOTE_END_ROUND})
        assert state.current_round == 2
        await state.handle_command(101, {
            "action": "set_scores",
            "score_revision": state.score_revision,
            "scores": {"0": 7, "1": 8},
        })
        assert state.player_list[0].score == 7
        assert state.player_list[1].score == 8

    asyncio.run(scenario())


def test_end_match_cleans_up_and_keeps_room() -> None:
    async def scenario():
        state, sockets = _make_state()
        for uid in (101, 102, 103, 104):
            await state.handle_command(uid, {"action": "set_vote", "vote": VOTE_END_MATCH})
        assert state.ended is True
        assert any(m.get("type") == "gamestate/free/game_end" for m in sockets[101].messages)
        state.game_server.gamestate_manager.cleanup_game_state_complete.assert_awaited()
        # Room restoration belongs to GameStateManager, not individual rules.
        state.game_server.room_manager.finish_custom_game_room.assert_not_awaited()

    asyncio.run(scenario())
