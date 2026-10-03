"""Shared game lifetime rules; lobby membership never decides game presence."""
import asyncio


def human_players(state):
    return [player for player in state.player_list if player.user_id > 10]


def is_offline(player):
    return "offline" in getattr(player, "tag_list", ()) or getattr(player, "online", True) is False


def all_humans_offline(state):
    humans = human_players(state)
    return bool(humans) and all(is_offline(player) for player in humans)


async def close_if_all_humans_offline(state):
    if all_humans_offline(state):
        state.close_reason = "all_humans_offline"
        await state.game_server.gamestate_manager.cleanup_game_state_complete(
            gamestate_id=state.gamestate_id
        )


def start_owned_task(state, coroutine):
    """Track auxiliary work so game termination can cancel it as well."""
    tasks = getattr(state, "_lifecycle_tasks", None)
    if tasks is None:
        tasks = state._lifecycle_tasks = set()
    task = asyncio.create_task(coroutine)
    if getattr(state, "lifecycle_state", "running") != "running":
        task.cancel()
    tasks.add(task)
    task.add_done_callback(tasks.discard)
    return task


async def cancel_auxiliary_tasks(state):
    current = asyncio.current_task()
    state._outbound_closed = True
    tasks = set(getattr(state, "_lifecycle_tasks", ()))
    tasks.update(getattr(state, "bot_tasks", ()))
    tasks.update((getattr(state, "_bot_claim_tasks", None) or {}).values())
    tasks.update((getattr(state, "_bot_task", None), getattr(state, "_round_task", None)))
    tasks.update((getattr(state, "_outbound_tails", None) or {}).values())
    tasks.add(getattr(state, "_cp_timer_task", None))
    vote = getattr(state, "vote_manager", None)
    if vote is not None:
        tasks.update((getattr(vote, "_timer_task", None), getattr(vote, "_pause_deadline_task", None)))
    tasks = {task for task in tasks if task is not None and task is not current and not task.done()}
    for task in tasks:
        task.cancel()
    if tasks:
        await asyncio.gather(*tasks, return_exceptions=True)


async def cancel_game_task(state):
    task = getattr(state, "game_task", None)
    if task is not None and task is not asyncio.current_task() and not task.done():
        task.cancel()
        await asyncio.gather(task, return_exceptions=True)
