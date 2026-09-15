import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

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
        sockets[101].messages.clear()
        await state.handle_command(102, {
            "action": "set_scores",
            "score_revision": 0,
            "scores": {"0": 99, "1": 99, "2": 99, "3": 99},
        })
        assert state.player_list[0].score == 10
        assert state.score_revision == 1
        assert any(m.get("type") == "gamestate/free/scores" for m in sockets[102].messages)

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
        state.game_server.room_manager.finish_custom_game_room.assert_awaited_with("free-1")

    asyncio.run(scenario())
