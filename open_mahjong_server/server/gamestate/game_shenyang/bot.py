import asyncio


async def shenyang_bot_action(state, player, *args, **kwargs):
    index = player.player_index if hasattr(player, "player_index") else int(player)
    actions = state.action_dict.get(index, ())
    if not actions:
        return
    choice = next((a for a in actions if a.startswith("hu_")), None)
    if choice is None:
        choice = next((a for a in ("sy_reveal", "sy_pass", "sy_xi", "sy_wind", "riichi_cut", "pass", "cut") if a in actions), actions[0])
    data = {"action_type": choice, "_action_tick": state.server_action_tick}
    p = state.player_list[index]
    if choice == "riichi_cut":
        data["TileId"] = max(p.riichi_candidate_cuts, key=lambda t: len(p.riichi_candidate_cuts[t]))
    elif choice == "cut":
        data["TileId"] = p.last_drawn_tile if p.ready_locked else p.hand_tiles[-1]
    if "TileId" in data:
        data.update(cutClass=data["TileId"] == p.last_drawn_tile, cutIndex=len(p.hand_tiles)-1 if data["TileId"] == p.hand_tiles[-1] else p.hand_tiles.index(data["TileId"]))
    await asyncio.sleep(0)
    state.action_queues[index].put_nowait(data)
    state.action_events[index].set()
