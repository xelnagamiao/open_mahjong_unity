"""Four independent walls per round, bound to the original Guobiao seats."""
from .hand_slot_utils import clear_draw_slot


class DuplicateFinished(Exception):
    """Normal duplicate lifecycle completion, never a failed game."""


def duplicate_fields(state):
    if not getattr(state, "duplicate_key", None):
        return {}
    return {"is_duplicate": True, "duplicate_wall_type": state.duplicate_wall_type,
            "duplicate_round_count": getattr(state, "duplicate_round_count", 1)}


def duplicate_match_complete(state):
    total = getattr(state, "duplicate_round_count", 1) if getattr(state, "duplicate_key", None) else state.max_round * 4
    return state.current_round >= total


def duplicate_remaining_tile_counts(state):
    """Public count snapshot in original-seat order, without exposing wall tiles."""
    wall = getattr(state, "tiles_list", None)
    if not getattr(state, "duplicate_key", None) or not isinstance(wall, DuplicateWall):
        return None
    return [len(part) for part in wall.parts]


def configure_duplicate_state(state, room):
    if not room.get("duplicate_key"):
        return
    if room.get("room_rule") != "guobiao":
        raise ValueError("复式牌墙仅支持国标麻将（含蓝十改）")
    if room.get("sub_rule") == "guobiao/blood_battle":
        raise ValueError("国标血战暂不支持复式牌墙")
    from ...database.duplicate_walls import load_duplicate_wall, validate_duplicate_wall, reserve_duplicate_game
    wall = load_duplicate_wall(state.db_manager, room["duplicate_key"])
    validate_duplicate_wall(wall, room)
    state.duplicate_key = wall["key"]
    state.duplicate_wall_type = wall["wall_type"]
    state._duplicate_tiles = tuple(wall["tiles"])
    state._duplicate_round_tiles = tuple(tuple(tiles) for tiles in wall["round_tiles"])
    state.duplicate_round_count = wall["round_count"]
    state.max_round = max(1, wall["round_count"] // 4)
    state._duplicate_seed = wall.get("seed") if wall["wall_type"] != "manual" else None
    state.use_flowers = wall["use_flowers"]
    state._duplicate_game_id = reserve_duplicate_game(state.db_manager, wall["id"])
    state.allow_spectator_config = False
    state.spectator_enabled = False
    if getattr(state, "spectator_manager", None):
        state.spectator_manager.enabled = False
    state.Debug = False


class DuplicateWall(list):
    """The flat list remains a record/count view; only the actor's partition pops."""
    def __init__(self, state, tiles):
        super().__init__(tiles)
        self.state = state
        size = len(tiles) // 4
        self.parts = [list(tiles[i * size:(i + 1) * size]) for i in range(4)]
        self.supplement_positions = [2, 2, 2, 2]

    def pop_for_player(self, player, index=0):
        seat = player.original_player_index
        if not self.parts[seat]:
            self.state.duplicate_exhausted_seat = seat
            raise DuplicateFinished("duplicate_wall_exhausted")
        # Each original seat has its own tail cursor: second-last, last, repeat.
        # Taking the last available tile is legal; only a subsequent empty draw ends.
        local_index = 0
        if index < 0:
            local_index = -min(self.supplement_positions[seat], len(self.parts[seat]))
            self.supplement_positions[seat] = 3 - self.supplement_positions[seat]
            local_index += len(self.parts[seat])
        flat_offset = sum(len(part) for part in self.parts[:seat])
        tile = self.parts[seat].pop(local_index)
        super().pop(flat_offset + local_index)
        return tile

    def pop(self, index=-1):
        player = self.state.player_list[self.state.current_player_index]
        return self.pop_for_player(player, index)


def draw_tile_for_player(player, wall, index=0):
    return wall.pop_for_player(player, index) if isinstance(wall, DuplicateWall) else wall.pop(index)


def init_duplicate_tiles(state):
    if not getattr(state, "duplicate_key", None):
        return False
    if state.room_rule != "guobiao":
        raise ValueError("复式牌墙仅支持国标麻将（含蓝十改）")
    rounds = getattr(state, "_duplicate_round_tiles", (state._duplicate_tiles,))
    index = getattr(state, "current_round", 1) - 1
    if index < 0 or index >= len(rounds):
        raise ValueError("复式局数超出已保存的牌山范围")
    if getattr(state, "_duplicate_dealt_round", None) == index:
        raise ValueError("本局复式牌山已发牌")
    state._duplicate_dealt_round = index
    state.duplicate_exhausted_seat = None
    state._duplicate_dealing = True
    state.tiles_list = DuplicateWall(state, rounds[index])
    state.round_random_seed = 0
    try:
        for player in state.player_list:
            clear_draw_slot(player)
            player.hand_tiles.clear()
            for _ in range(13):
                player.get_tile(state.tiles_list, mark_draw_slot=False)
        state.player_list[0].get_tile(state.tiles_list, mark_draw_slot=False)
    finally:
        state._duplicate_dealing = False
    return True


async def finish_duplicate_game(state, reason):
    """Finish once, save a locked server record, send only scores, and release room."""
    from .game_record_manager import end_game_record, player_action_record_liuju, player_action_record_round_end
    from .logic_common import assign_competition_final_ranks
    if getattr(state, "_duplicate_finished", False):
        return
    state._duplicate_finished = True
    title = state.game_record.setdefault("game_title", {})
    title.update(duplicate_fields(state), duplicate_end_reason=str(reason))
    rounds = state.game_record.get("game_round", {})
    current = rounds.get(f"round_index_{state.round_index}")
    if current is not None and (not current.get("action_ticks") or current["action_ticks"][-1] != ["end"]):
        player_action_record_liuju(state)
        player_action_record_round_end(state)
    end_game_record(state)
    assign_competition_final_ranks(state.player_list)
    state.player_list.sort(key=lambda player: player.player_index)
    save = state.db_manager.store_guobiao_game_record
    game_id = save(state.game_record, state.player_list, state.room_type, "duplicate")
    state._local_record_detail = None
    state.game_status = "END"
    await state.broadcast_game_end()
    await state.game_server.gamestate_manager.cleanup_game_state_complete(gamestate_id=state.gamestate_id)
    return game_id
