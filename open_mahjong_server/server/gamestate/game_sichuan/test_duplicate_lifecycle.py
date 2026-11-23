"""Completed duplicate tables retain their tasks until paired finalization."""
import asyncio
from contextlib import asynccontextmanager

import pytest

from .DuplicateGameState import SbrDuplicateGameState
from .test_duplicate import run_async
from ..test_room_lifecycle import make_server, make_room, parked_loop
from ..public.lifecycle import should_close_when_offline
from ..public.sichuan_duplicate import PairedMatch, new_match
from ...game_calculation.sichuan.duplicate_rules import SBR_DUPLICATE, make_board, team_by_seat


@asynccontextmanager
async def paired_states(*, run_tasks=True):
    server = make_server(range(101, 109))
    gm = server.gamestate_manager
    data = new_match("offline-pair", SBR_DUPLICATE, {"open": "1", "closed": "2"},
                     [make_board("offline", n) for n in (1, 2)], "offline", sync_mode=False)
    data.update(status="running", completed_boards=1,
                table_completed_boards={"open": 1, "closed": 1})
    pair = PairedMatch(data)
    gm.sichuan_duplicate_matches[pair.data["id"]] = pair
    for table, first in (("open", 101), ("closed", 105)):
        room = make_room(range(first, first + 4), "sichuan", "events")
        room.update(room_id=pair.data["room_ids"][table], sub_rule=SBR_DUPLICATE,
                    team_by_seat=team_by_seat(table), duplicate_match_id=pair.data["id"],
                    duplicate_table=table)
        state = SbrDuplicateGameState(server, room, server.calculation_service, server.db_manager, table)
        for index, player in enumerate(state.player_list):
            player.player_index = player.original_player_index = index
        gm.register_game(state, room)
        state.game_task = asyncio.create_task(gm.run_game(state, lambda s=state: parked_loop(s)))
    if run_tasks:
        await asyncio.sleep(0)
    try:
        yield server, pair
    finally:
        for gid in list(gm.gamestate_id_to_game_state):
            await gm.cleanup_game_state_complete(gamestate_id=gid)


@pytest.mark.parametrize("entry", ["state", "manager", "abandon"])
@pytest.mark.parametrize("stage", ["unsubmitted", "waiting", "finalizing"])
@run_async
async def test_offline_cleanup_uses_submitted_progress(entry, stage):
    async with paired_states() as (server, pair):
        state = pair.states["closed"]
        # Merely entering the last round / displaying a result isn't enough.
        state.current_round = 2
        state.game_status = "duplicate_wait"
        if stage != "unsubmitted":
            pair.data["table_completed_boards"]["closed"] = 2
        if stage == "finalizing":
            pair.data.update(status="completed", completed_boards=2)
        gm = server.gamestate_manager
        for player in state.player_list:
            if entry == "state":
                await state.player_disconnect(player.user_id)
            elif entry == "manager":
                server.user_id_to_connection.pop(player.user_id)
                await gm.player_disconnect(player.user_id)
            else:
                await gm.abandon_player_game(str(player.user_id), player.user_id)
        if stage == "unsubmitted":
            assert pair.data["status"] == "interrupted"
            assert pair.data["interruption"] == "all_humans_offline"
            assert state.lifecycle_state == "closed"
        else:
            assert pair.data["status"] == ("completed" if stage == "finalizing" else "running")
            assert "interruption" not in pair.data
            assert all(not s.game_task.done() for s in pair.states.values())
            assert all(s.lifecycle_state == "running" for s in pair.states.values())
            assert all(gm.get_game_state_by_user_id(p.user_id) is state for p in state.player_list)
            if entry == "abandon":
                assert state.abandoned_users == {p.user_id for p in state.player_list}


@pytest.mark.parametrize("synchronous", [False, True])
@run_async
async def test_added_tiebreak_board_requires_play_even_after_planned_boards(synchronous):
    async with paired_states() as (_, pair):
        state = pair.states["closed"]
        pair.data["sync_mode"] = synchronous
        pair.data["table_completed_boards"]["closed"] = 2
        for player in state.player_list:
            player.tag_list.append("offline")
        assert not should_close_when_offline(state)
        pair.data["boards"].append(make_board("offline", 3))
        assert should_close_when_offline(state), "planned count must not hide an unplayed tiebreak"


@run_async
async def test_unfinished_sibling_offline_still_interrupts_waiting_table():
    async with paired_states() as (server, pair):
        pair.data["table_completed_boards"]["closed"] = 2
        for table in ("closed", "open"):
            for player in pair.states[table].player_list:
                await server.gamestate_manager.player_disconnect(player.user_id)
            assert pair.data["status"] == ("running" if table == "closed" else "interrupted")
        assert pair.data["interruption"] == "all_humans_offline"


@run_async
async def test_vote_closes_both_registered_tables_before_game_tasks_first_run():
    async with paired_states(run_tasks=False) as (server, pair):
        gm = server.gamestate_manager
        await gm.cleanup_game_state_complete(gamestate_id="open", reason="vote_end")
        assert not gm.gamestate_id_to_game_state and not gm.user_id_to_game_state
        assert not server.room_manager.active_game_room_ids
        assert all(state.lifecycle_state == "closed" for state in pair.states.values())
