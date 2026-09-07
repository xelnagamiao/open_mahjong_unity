"""自由模式 GameState：无回合，命令到达即广播。"""
from __future__ import annotations

import asyncio
import logging
from typing import Any, Optional

from . import boardcast
from .init_tiles import WALL_FLAG_KEYS, build_wall_tiles, shuffle_wall
from .player import (
    VALID_VOTES,
    VOTE_BLANK,
    VOTE_END_MATCH,
    VOTE_END_ROUND,
    VOTE_RESTART_ROUND,
    FreePlayer,
)
from ..public.random_seed_manager import setup_random_seed_system

logger = logging.getLogger(__name__)

SHOUT_WORDS = {"chi_left", "peng", "gang", "buhua", "hu", "hu_self"}


class FreeGameState:
    def __init__(
        self,
        game_server: Any,
        room_data: dict,
        calculation_service: Any = None,
        db_manager: Any = None,
        gamestate_id: str = "free-test",
    ) -> None:
        self.game_server = game_server
        self.db_manager = db_manager
        self.room_id = room_data["room_id"]
        self.gamestate_id = gamestate_id
        self.room_rule = "free"
        self.sub_rule = room_data.get("sub_rule", "free/standard")
        self.room_type = room_data.get("room_type", "custom")
        self.allow_spectator_config = False
        self.spectator_enabled = False
        self.realtime_spectators: list = []
        self.spectator_manager = None
        self.game_record: dict = {}
        self.tips = False
        self.wall_flags = {key: bool(room_data.get(key, True)) for key in WALL_FLAG_KEYS}
        self.wall_template = build_wall_tiles({**room_data, **self.wall_flags})
        user_seed = room_data.get("random_seed")
        try:
            user_seed = int(user_seed) if user_seed not in (None, "") else None
        except (TypeError, ValueError):
            user_seed = None
        self.master_seed, self.salt, self.commitment, self.isPlayerSetRandomSeed = (
            setup_random_seed_system(user_seed)
        )
        self.current_round = 1
        self.round_random_seed = 0
        self.tiles_list: list[int] = []
        self.action_tick = 0
        self.transfer_tile: Optional[int] = None
        self.score_revision = 0
        self.discard_log: list[tuple[int, int]] = []
        self.ended = False
        self._ended = asyncio.Event()
        self._lock = asyncio.Lock()
        self.game_task: Optional[asyncio.Task] = None
        self.player_list: list[FreePlayer] = []
        settings = room_data.get("player_settings") or {}
        for index, user_id in enumerate(room_data.get("player_list", [])[:4]):
            profile = settings.get(user_id) or settings.get(str(user_id)) or {}
            self.player_list.append(FreePlayer(
                user_id=user_id,
                username=profile.get("username", f"玩家{index + 1}"),
                player_index=index,
                original_player_index=index,
                title_used=profile.get("title_id", 1),
                profile_used=profile.get("profile_image_id", 1),
                character_used=profile.get("character_id", 1),
                voice_used=profile.get("voice_id", 1),
            ))
        while len(self.player_list) < 4:
            index = len(self.player_list)
            self.player_list.append(FreePlayer(
                user_id=-(index + 1),
                username=f"空位{index + 1}",
                player_index=index,
                original_player_index=index,
            ))
        self._reset_table(reshuffle=True)

    def _player_by_user(self, user_id: int) -> Optional[FreePlayer]:
        for player in self.player_list:
            if player.user_id == user_id:
                return player
        return None

    def _bump_tick(self) -> None:
        self.action_tick += 1

    def _reset_table(self, *, reshuffle: bool) -> None:
        for player in self.player_list:
            player.clear_table()
        self.transfer_tile = None
        self.discard_log.clear()
        if reshuffle:
            shuffle_wall(self)

    def _remove_discard_log(self, player_index: int, tile_id: int) -> None:
        for i in range(len(self.discard_log) - 1, -1, -1):
            if self.discard_log[i] == (player_index, tile_id):
                del self.discard_log[i]
                return

    async def run_game_loop(self) -> None:
        try:
            await boardcast.broadcast_game_start(self)
            await self._ended.wait()
        except asyncio.CancelledError:
            raise
        except Exception:
            logger.exception("自由模式主循环异常 room=%s", self.room_id)

    async def handle_command(self, user_id: int, message: dict) -> None:
        if self.ended:
            return
        async with self._lock:
            player = self._player_by_user(user_id)
            if player is None:
                return
            action = (message.get("action") or "").strip()
            tile_id = message.get("TileId", message.get("tile"))
            if action == "cut" or (not action and tile_id is not None):
                await self._cut(player, int(tile_id or 0))
                return
            if action in SHOUT_WORDS:
                await self._shout(player, action)
                return
            handler = {
                "draw": self._draw,
                "to_flower": self._to_flower,
                "reveal": self._reveal,
                "stand": self._stand,
                "create_meld": self._create_meld,
                "recall_river": self._recall_river,
                "recall_flower": self._recall_flower,
                "recall_meld": self._recall_meld,
                "transfer_put": self._transfer_put,
                "transfer_take": self._transfer_take,
                "set_scores": self._set_scores,
                "set_vote": self._set_vote,
            }.get(action)
            if handler is None:
                return
            await handler(player, message)

    async def _draw(self, player: FreePlayer, _message: dict) -> None:
        if not self.tiles_list:
            return
        tile_id = self.tiles_list.pop(0)
        player.hand_tiles.append(tile_id)
        player.last_drawn_tile = tile_id
        player.has_draw_slot = True
        self._bump_tick()
        await boardcast.broadcast_do_action(self, boardcast.deal_payloads(self, player.player_index, tile_id))

    async def _cut(self, player: FreePlayer, tile_id: int) -> None:
        if tile_id <= 0:
            return
        cut_class = player.has_draw_slot and player.last_drawn_tile == tile_id
        if not player.take_hand_tile(tile_id):
            return
        player.discard_tiles.append(tile_id)
        self.discard_log.append((player.player_index, tile_id))
        self._bump_tick()
        await boardcast.broadcast_do_action(self, {
            viewer: boardcast.do_action_envelope(
                self,
                ["cut"],
                player.player_index,
                cut_tile=tile_id,
                cut_class=cut_class,
            )
            for viewer in range(4)
        })

    async def _to_flower(self, player: FreePlayer, message: dict) -> None:
        tile_id = int(message.get("tile") or message.get("TileId") or 0)
        if tile_id <= 0 or not player.take_hand_tile(tile_id):
            return
        player.huapai_list.append(tile_id)
        self._bump_tick()
        await boardcast.broadcast_do_action(self, {
            viewer: boardcast.do_action_envelope(
                self,
                ["free_to_flower"],
                player.player_index,
                buhua_tile=tile_id,
                is_mo_buhua=False,
                silent=True,
            )
            for viewer in range(4)
        })

    async def _shout(self, player: FreePlayer, word: str) -> None:
        self._bump_tick()
        await boardcast.broadcast_do_action(self, {
            viewer: boardcast.do_action_envelope(
                self,
                [word],
                player.player_index,
                is_claim=True,
            )
            for viewer in range(4)
        })

    async def _reveal(self, player: FreePlayer, _message: dict) -> None:
        if not player.hand_tiles:
            return
        player.revealed = True
        self._bump_tick()
        await boardcast.broadcast_free_table(self, "reveal", revealed_player_index=player.player_index)

    async def _stand(self, player: FreePlayer, _message: dict) -> None:
        if not player.revealed:
            return
        player.revealed = False
        self._bump_tick()
        await boardcast.broadcast_free_table(self, "stand", revealed_player_index=player.player_index)

    def _parse_mask(self, raw) -> Optional[list[int]]:
        if not isinstance(raw, list) or len(raw) not in (4, 6, 8) or len(raw) % 2:
            return None
        mask: list[int] = []
        for i in range(0, len(raw), 2):
            try:
                orient = int(raw[i])
                tile_id = int(raw[i + 1])
            except (TypeError, ValueError):
                return None
            if orient not in (0, 1, 2) or tile_id <= 0:
                return None
            mask.extend([orient, tile_id])
        return mask

    async def _create_meld(self, player: FreePlayer, message: dict) -> None:
        mask = self._parse_mask(message.get("combination_mask") or message.get("mask"))
        if mask is None:
            return
        tiles = [mask[i + 1] for i in range(0, len(mask), 2)]
        include_river = bool(message.get("include_river"))
        river = boardcast.last_alive_discard(self)
        river_player = None
        river_tile = None
        hand_needed = list(tiles)
        if include_river:
            if river is None:
                return
            river_player, river_tile = river
            if river_tile not in tiles:
                return
            hand_needed.remove(river_tile)
        remaining = list(player.hand_tiles)
        for tile_id in hand_needed:
            if tile_id not in remaining:
                return
            remaining.remove(tile_id)
        for tile_id in hand_needed:
            player.take_hand_tile(tile_id)
        cut_from = None
        if include_river and river_player is not None:
            owner = self.player_list[river_player]
            if river_tile not in owner.discard_tiles:
                return
            owner.discard_tiles.remove(river_tile)
            self._remove_discard_log(river_player, river_tile)
            cut_from = river_player
            for i in range(0, len(mask), 2):
                if mask[i + 1] == river_tile:
                    mask[i] = 1
                    break
        player.combination_mask.append(mask)
        player.combination_tiles.append(f"F{len(player.combination_tiles)}")
        self._bump_tick()
        await boardcast.broadcast_do_action(self, {
            viewer: boardcast.do_action_envelope(
                self,
                ["free_meld"],
                player.player_index,
                combination_mask=list(mask),
                combination_target=player.combination_tiles[-1],
                cut_tile=river_tile,
                cut_from_player=cut_from,
                silent=True,
            )
            for viewer in range(4)
        })

    async def _recall_river(self, player: FreePlayer, message: dict) -> None:
        index = int(message.get("index", -1))
        tile_id = int(message.get("tile") or 0)
        if index < 0 or index >= len(player.discard_tiles):
            return
        if player.discard_tiles[index] != tile_id:
            return
        player.discard_tiles.pop(index)
        self._remove_discard_log(player.player_index, tile_id)
        player.hand_tiles.append(tile_id)
        self._bump_tick()
        await boardcast.broadcast_do_action(self, {
            viewer: boardcast.do_action_envelope(
                self,
                ["free_recall_river"],
                player.player_index,
                cut_tile=tile_id,
                cut_tile_index=index,
                deal_tile=tile_id if viewer == player.player_index or player.revealed else None,
            )
            for viewer in range(4)
        })

    async def _recall_flower(self, player: FreePlayer, message: dict) -> None:
        index = int(message.get("index", -1))
        tile_id = int(message.get("tile") or 0)
        if index < 0 or index >= len(player.huapai_list):
            return
        if player.huapai_list[index] != tile_id:
            return
        player.huapai_list.pop(index)
        player.hand_tiles.append(tile_id)
        self._bump_tick()
        await boardcast.broadcast_do_action(self, {
            viewer: boardcast.do_action_envelope(
                self,
                ["free_recall_flower"],
                player.player_index,
                buhua_tile=tile_id,
                cut_tile_index=index,
                deal_tile=tile_id if viewer == player.player_index or player.revealed else None,
            )
            for viewer in range(4)
        })

    async def _recall_meld(self, player: FreePlayer, message: dict) -> None:
        index = int(message.get("meld_index", message.get("index", -1)))
        if index < 0 or index >= len(player.combination_mask):
            return
        mask = list(player.combination_mask.pop(index))
        if index < len(player.combination_tiles):
            player.combination_tiles.pop(index)
        tiles = [mask[i + 1] for i in range(0, len(mask), 2)]
        player.hand_tiles.extend(tiles)
        self._bump_tick()
        await boardcast.broadcast_do_action(self, {
            viewer: boardcast.do_action_envelope(
                self,
                ["free_recall_meld"],
                player.player_index,
                combination_mask=mask,
                cut_tile_index=index,
                deal_tiles=tiles if viewer == player.player_index or player.revealed else None,
            )
            for viewer in range(4)
        })

    async def _transfer_put(self, player: FreePlayer, message: dict) -> None:
        if self.transfer_tile is not None:
            return
        tile_id = int(message.get("tile") or 0)
        if tile_id <= 0 or not player.take_hand_tile(tile_id):
            return
        self.transfer_tile = tile_id
        self._bump_tick()
        await boardcast.broadcast_do_action(self, {
            viewer: boardcast.do_action_envelope(
                self,
                ["free_transfer_put"],
                player.player_index,
                cut_tile=tile_id,
            )
            for viewer in range(4)
        })

    async def _transfer_take(self, player: FreePlayer, _message: dict) -> None:
        if self.transfer_tile is None:
            return
        tile_id = self.transfer_tile
        self.transfer_tile = None
        player.hand_tiles.append(tile_id)
        self._bump_tick()
        await boardcast.broadcast_do_action(self, {
            viewer: boardcast.do_action_envelope(
                self,
                ["free_transfer_take"],
                player.player_index,
                deal_tile=tile_id,
            )
            for viewer in range(4)
        })

    async def _set_scores(self, player: FreePlayer, message: dict) -> None:
        revision = int(message.get("score_revision", -1))
        if revision != self.score_revision:
            await boardcast.broadcast_free_table(self, "scores")
            return
        scores = message.get("scores") or message.get("player_to_score") or {}
        parsed: dict[int, int] = {}
        for key, value in scores.items():
            parsed[int(key)] = int(value)
        if len(parsed) != 4:
            return
        for index, score in parsed.items():
            if 0 <= index < 4:
                self.player_list[index].score = score
        self.score_revision += 1
        await boardcast.broadcast_free_table(self, "scores")

    async def _set_vote(self, player: FreePlayer, message: dict) -> None:
        vote = str(message.get("vote") or VOTE_BLANK)
        if vote not in VALID_VOTES:
            return
        player.vote = vote
        await boardcast.broadcast_free_table(self, "votes")
        if vote == VOTE_BLANK:
            return
        if all(item.vote == vote for item in self.player_list):
            for item in self.player_list:
                item.vote = VOTE_BLANK
            if vote == VOTE_END_ROUND:
                self.current_round += 1
                self._reset_table(reshuffle=True)
                await boardcast.broadcast_game_start(self)
            elif vote == VOTE_RESTART_ROUND:
                self._reset_table(reshuffle=True)
                await boardcast.broadcast_game_start(self)
            elif vote == VOTE_END_MATCH:
                await self._finish_match()

    async def _finish_match(self) -> None:
        if self.ended:
            return
        self.ended = True
        await boardcast.broadcast_game_end(self)
        self._ended.set()
        if self.game_server is not None:
            await self.game_server.gamestate_manager.cleanup_game_state_complete(
                gamestate_id=self.gamestate_id
            )
            room_manager = getattr(self.game_server, "room_manager", None)
            if room_manager is not None and hasattr(room_manager, "finish_custom_game_room"):
                await room_manager.finish_custom_game_room(self.room_id)

    async def player_disconnect(self, user_id: int) -> None:
        player = self._player_by_user(user_id)
        if player is None:
            return
        if "offline" not in player.tag_list:
            player.tag_list.append("offline")

    async def player_reconnect(self, user_id: int) -> None:
        player = self._player_by_user(user_id)
        if player is None:
            return
        player.tag_list = [t for t in player.tag_list if t != "offline"]
        await boardcast.send_json(self, user_id, {
            "type": "gamestate/free/game_start",
            "success": True,
            "message": "reconnect",
            "gamestate_id": self.gamestate_id,
            "player_index": player.player_index,
            "game_info": boardcast.game_info_payload(self, player.player_index),
            "free_table_info": boardcast.free_table_payload(self),
        })

    async def cleanup_game_state(self) -> None:
        self.ended = True
        self._ended.set()
        current = asyncio.current_task()
        if self.game_task and self.game_task is not current and not self.game_task.done():
            self.game_task.cancel()
            await asyncio.gather(self.game_task, return_exceptions=True)

    async def add_spectator(self, user_id: int, connection: Any) -> None:
        raise ValueError("自由模式不支持观战")

    async def remove_spectator(self, user_id: int) -> None:
        return None
