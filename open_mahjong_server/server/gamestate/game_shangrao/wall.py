class Wall:
    def _take_wall_position(self, position):
        index = self.wall_positions.index(position)
        tile = self.tiles_list.pop(index)
        self.wall_positions.pop(index)
        self.indicator_index = self.wall_positions.index(self.indicator_position)
        return tile

    def _kong_positions(self):
        available = [position for position in self.wall_positions if position != self.indicator_position]
        if not available:
            return None, None
        bottom = max(available)
        if bottom % 2 == 0:
            raise RuntimeError("牌尾下张缺失")
        upper = bottom - 1
        return bottom, upper if upper in available else None

    def can_draw_kong(self):
        bottom, upper = self._kong_positions()
        if self.rule_version == "shangrao-v1":
            return bottom is not None
        return bottom is not None and len(self.tiles_list) > 2 and (upper is not None or bottom - 1 == self.indicator_position)

    def take_normal_tile(self):
        position = next((p for p in self.wall_positions if p != self.indicator_position), None)
        return None if position is None else self._take_wall_position(position)

    def take_kong_tile(self):
        if not self.can_draw_kong():
            raise ValueError("牌尾没有合法补杠牌")
        bottom, upper = self._kong_positions()
        tile = self._take_wall_position(bottom)
        burnt = self._take_wall_position(upper) if upper is not None else None
        if burnt is not None:
            self.burnt_tiles.append(burnt)
        return tile, burnt
