"""Sichuan walls. Empty seats wait; SBR may inherit a retired partner's wall."""
from ...game_calculation.sichuan.duplicate_rules import validate_board


class SichuanDuplicateWall(list):
    def __init__(self, state, board):
        validate_board(board)
        self.state = state
        self.parts = [list(part) for part in board["walls"]]
        self.consumed = [0] * 4
        super().__init__(tile for part in self.parts for tile in part)

    def source(self, player):
        if player.is_hu:
            return None
        seat = player.original_player_index
        if self.parts[seat]:
            return seat
        if not self.state.is_xueliu:
            partner = next(p for p in self.state.player_list if p.original_player_index == (seat + 2) % 4)
            if partner.is_hu and self.parts[partner.original_player_index]:
                return partner.original_player_index
        return None

    def can_draw(self, player):
        return self.source(player) is not None

    def pop_for_player(self, player, index=0):
        if index != 0:
            raise ValueError("川麻复式杠补与普通摸牌均从墙头取牌")
        seat = self.source(player)
        if seat is None:
            raise ValueError("待机玩家没有可摸牌墙")
        offset = sum(len(p) for p in self.parts[:seat])
        tile = self.parts[seat].pop(0)
        super().pop(offset)
        self.state.duplicate_last_draw = {"draw_from_seat": seat, "wall_index": self.consumed[seat]}
        self.consumed[seat] += 1
        return tile

    def pop(self, index=0):
        return self.pop_for_player(self.state.player_list[self.state.current_player_index], index)

    def remaining_counts(self):
        return [len(part) for part in self.parts]


class XueliuDuplicateWall(SichuanDuplicateWall):
    """Eight fixed draws per seat, then one shared queue (JJ bloodflow).

    The dealer's fourteenth tile is the first fixed draw. On the first empty
    fixed wall, append all fixed remainders in E/S/W/N order ahead of the
    original common wall. Queries never advance this transition.
    """
    def __init__(self, state, board):
        if board.get("wall_layout") != "xueliu":
            raise ValueError("血流复式需要固定 8 张和公共牌墙布局")
        super().__init__(state, board)
        self.common = list(board["common_wall"])
        self.common_sources = [(-1, i) for i in range(len(self.common))]
        self.common_started = False
        self.extend(self.common)

    def source(self, player):
        return (-1 if self.common_started else player.original_player_index) if self else None

    def pop_for_player(self, player, index=0):
        if index != 0:
            raise ValueError("川麻复式杠补与普通摸牌均从墙头取牌")
        if not self:
            raise ValueError("血流复式牌墙已摸完")
        if self.common_started:
            tile = self.common.pop(0)
            seat, position = self.common_sources.pop(0)
            list.pop(self, 0)
            self.state.duplicate_last_draw = {"draw_from_seat": seat, "wall_index": position}
            return tile
        tile = super().pop_for_player(player, index)
        if not self.parts[player.original_player_index]:
            fixed = [t for part in self.parts for t in part]
            sources = [(s, self.consumed[s] + i) for s, part in enumerate(self.parts) for i in range(len(part))]
            self.common = fixed + self.common
            self.common_sources = sources + self.common_sources
            self.parts = [[], [], [], []]
            self.common_started = True
        return tile


def can_draw(state, player_index):
    wall = state.tiles_list
    return wall.can_draw(state.player_list[player_index]) if isinstance(wall, SichuanDuplicateWall) else len(wall) > state.dead_wall_count


def wall_exhausted(state):
    if isinstance(state.tiles_list, SichuanDuplicateWall):
        return not any(state.tiles_list.can_draw(p) for p in state.player_list)
    return len(state.tiles_list) <= state.dead_wall_count


def init_duplicate_board(state, board):
    wall_type = XueliuDuplicateWall if board.get("wall_layout") == "xueliu" else SichuanDuplicateWall
    state.tiles_list = wall_type(state, board)
    state.dealer_index = board["dealer"]
    for player in state.player_list:
        player.hand_tiles = list(board["hands"][player.original_player_index])
        player.has_draw_slot = False
    state.player_list[state.dealer_index].get_tile(state.tiles_list, mark_draw_slot=False)
    state.duplicate_last_draw = None
