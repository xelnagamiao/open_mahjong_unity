import asyncio
import time
from typing import List, Optional


class ThreeTileOpening:
    async def _collect_xueliu_selection(self, seats):
        self.game_status = 'waiting_xueliu_throw'
        self.action_dict = {i: [self.xueliu_opening_action] if i in seats else [] for i in range(4)}
        self.waiting_players_list = list(seats)
        self.server_action_tick += 1
        for p in self.player_list:
            p.remaining_time = self.round_time
        for i in range(4):
            while not self.action_queues[i].empty():
                try:
                    self.action_queues[i].get_nowait()
                except Exception:
                    break
            self.action_events[i].clear()
        from .broadcast import broadcast_xueliu_throw_three_ask
        self.xueliu_throw_deadline = time.time() + max(self.step_time, 10)
        await broadcast_xueliu_throw_three_ask(self)
        pending = set(seats)
        deadline = self.xueliu_throw_deadline
        while pending and time.time() < deadline:
            tasks = [asyncio.create_task(self.action_events[i].wait()) for i in pending]
            try:
                await asyncio.wait(tasks, timeout=1, return_when=asyncio.FIRST_COMPLETED)
            finally:
                for task in tasks:
                    if not task.done():
                        task.cancel()
                await asyncio.gather(*tasks, return_exceptions=True)
            for i in list(pending):
                if not self.action_events[i].is_set():
                    continue
                chosen = None
                while not self.action_queues[i].empty():
                    data = self.action_queues[i].get_nowait()
                    if data.get('_action_tick', self.server_action_tick) != self.server_action_tick or data.get('action_type') != self.xueliu_opening_action:
                        continue
                    chosen = self._validate_xueliu_throw_tiles(i, data.get('selected_tiles'))
                    if chosen is not None:
                        break
                if chosen is None:
                    self.action_events[i].clear()
                    continue
                self._consume_xueliu_throw_tiles(i, chosen)
                self.action_dict[i] = []
                self.action_events[i].clear()
                pending.discard(i)
        for i in pending:
            chosen = self._default_xueliu_throw_tiles(i)
            self._consume_xueliu_throw_tiles(i, chosen)
            self.action_dict[i] = []
            self.action_events[i].clear()
        self.waiting_players_list = []

    def _validate_xueliu_throw_tiles(self, player_index: int, selected_tiles) -> Optional[List[int]]:
        if not isinstance(selected_tiles, (list, tuple)) or len(selected_tiles) != 3:
            return None
        if any(type(tile) is not int or tile // 10 not in (1, 2, 3) or not 1 <= tile % 10 <= 9 for tile in selected_tiles):
            return None
        chosen = list(selected_tiles)
        player = self.player_list[player_index]
        if any((player.hand_tiles.count(tile) < chosen.count(tile) for tile in set(chosen))):
            return None
        suits = {tile // 10 for tile in chosen}
        if len(suits) != 1 or next(iter(suits), 0) not in (1, 2, 3):
            return None
        return chosen

    def _default_xueliu_throw_tiles(self, player_index: int) -> List[int]:
        from .xueliu_bot import choose_opening
        return choose_opening(self.player_list[player_index].hand_tiles, self.xueliu_exchange)

    def _consume_xueliu_throw_tiles(self, player_index: int, chosen: List[int]) -> None:
        player = self.player_list[player_index]
        for tile in chosen:
            player.hand_tiles.remove(tile)
        player.xueliu_throw_tiles = list(chosen)
