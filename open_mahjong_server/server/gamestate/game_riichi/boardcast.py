"""
立直麻将广播：沿用 classical 的消息布局，新增 declare_riichi / update_dora；
GameInfo 额外携带 honba / riichi_sticks / dora_indicators / kan_dora_indicators / hepai_way / red_dora。
"""
from typing import List, Dict, Optional
import asyncio
import logging
import time

from ...response import (
    Response,
    GameInfo,
    Ask_hand_action_info,
    Ask_other_action_info,
    Do_action_info,
    Show_result_info,
    Game_end_info,
    Player_final_data,
    Switch_seat_info,
    Refresh_player_tag_list_info,
    Ready_status_info,
)
from ..public.ai.auto_cut_ai import auto_cut_action
from ..public.ai.riichi_smart_bot_ai import riichi_smart_bot_action as smart_bot_action

logger = logging.getLogger(__name__)


def _tag_list_for_viewer(tags, subject_player_index: int, viewer_player_index: int) -> list:
    """振听 furiten 仅同步给本人视角：非本人座位的 tag 副本中移除 furiten。"""
    out = list(tags) if tags is not None else []
    if subject_player_index != viewer_player_index:
        return [t for t in out if t != "furiten"]
    return out


def _player_to_tag_list_for_viewer(player_list, viewer_player_index: int) -> dict:
    return {p.player_index: _tag_list_for_viewer(p.tag_list, p.player_index, viewer_player_index) for p in player_list}


def _build_base_game_info(self) -> dict:
    info = {
        "room_id": self.room_id,
        "gamestate_id": self.gamestate_id,
        "tips": self.tips,
        "current_player_index": self.current_player_index,
        "action_tick": self.server_action_tick,
        "max_round": self.max_round,
        "tile_count": max(0, len(self.tiles_list) - self.dead_wall_count),
        "commitment": self.commitment,
        "salt": self.salt,
        "current_round": self.current_round,
        "step_time": self.step_time,
        "round_time": self.round_time,
        "room_type": self.room_type,
        "room_rule": self.room_rule,
        "sub_rule": getattr(self, "sub_rule", "riichi/standard"),
        "hepai_limit": 1,
        "open_cuohe": self.open_cuohe,
        "show_moqie_hint": getattr(self, "show_moqie_hint", False),
        "isPlayerSetRandomSeed": self.isPlayerSetRandomSeed,
        "honba": self.honba,
        "riichi_sticks": self.riichi_sticks,
        "dora_indicators": list(self.dora_indicators),
        "kan_dora_indicators": list(self.kan_dora_indicators),
        "hepai_way": self.hepai_way,
        "red_dora": self.red_dora,
    }
    from ..public.game_record_manager import build_player_entry_order_fields
    info.update(build_player_entry_order_fields(self))
    return info


def _build_player_info(player, viewer_uid: int, viewer_player_index: int) -> dict:
    return {
        "user_id": player.user_id,
        "username": player.username,
        "hand_tiles_count": len(player.hand_tiles),
        "hand_tiles": player.hand_tiles if player.user_id == viewer_uid else None,
        "discard_tiles": player.discard_tiles,
        "discard_origin_tiles": player.discard_origin_tiles,
        "combination_tiles": player.combination_tiles,
        "combination_mask": player.combination_mask,
        "huapai_list": player.huapai_list,
        "remaining_time": player.remaining_time,
        "player_index": player.player_index,
        "original_player_index": player.original_player_index,
        "score": player.score,
        "title_used": player.title_used,
        "profile_used": player.profile_used,
        "character_used": player.character_used,
        "voice_used": player.voice_used,
        "score_history": player.score_history,
        "round_number_history": player.round_number_history,
        "tag_list": _tag_list_for_viewer(player.tag_list, player.player_index, viewer_player_index),
        "discard_riichi_flags": list(getattr(player, "discard_riichi_flags", []) or []),
    }


async def broadcast_game_start(self):
    base = _build_base_game_info(self)
    for cp in self.player_list:
        try:
            if "offline" in cp.tag_list or cp.user_id == 0:
                continue
            if cp.user_id not in self.game_server.user_id_to_connection:
                continue
            player_conn = self.game_server.user_id_to_connection[cp.user_id]
            infos = [_build_player_info(p, cp.user_id, cp.player_index) for p in self.player_list]
            game_info = GameInfo(**{**base, "players_info": infos, "self_hand_tiles": None})
            response = Response(type="gamestate/riichi/game_start", success=True, message="游戏开始", game_info=game_info)
            await player_conn.websocket.send_json(response.dict(exclude_none=True))
            await self.send_to_realtime_spectators(cp.player_index, response)
        except Exception as e:
            logger.error(f"riichi broadcast_game_start 失败: {e}", exc_info=True)
    if hasattr(self, "spectator_manager"):
        self.spectator_manager.record_game_title()
        self.spectator_manager.record_round_start()


async def broadcast_ask_hand_action(self):
    self.server_action_tick += 1
    self._ask_broadcast_time = time.time()
    for i, cp in enumerate(self.player_list):
        try:
            if "offline" in cp.tag_list:
                if self.action_dict.get(i, []):
                    asyncio.create_task(auto_cut_action(self, i, self.action_dict[i], self.game_status))
                continue
            if cp.user_id == 0:
                if self.action_dict.get(i, []):
                    asyncio.create_task(auto_cut_action(self, i, self.action_dict[i], self.game_status))
                continue
            if cp.user_id == 2:
                if self.action_dict.get(i, []):
                    asyncio.create_task(smart_bot_action(self, i, self.action_dict[i], self.game_status))
                continue
            if cp.user_id not in self.game_server.user_id_to_connection:
                continue
            player_conn = self.game_server.user_id_to_connection[cp.user_id]
            riichi_cuts = cp.riichi_candidate_cuts if "riichi_cut" in self.action_dict[i] else None
            forbidden = list(cp.kuikae_forbidden_tiles) if i == self.current_player_index and cp.kuikae_forbidden_tiles else None
            response = Response(
                type="gamestate/riichi/broadcast_hand_action",
                success=True,
                message="发牌，并询问手牌操作",
                ask_hand_action_info=Ask_hand_action_info(
                    remaining_time=cp.remaining_time,
                    player_index=self.current_player_index,
                    remain_tiles=max(0, len(self.tiles_list) - self.dead_wall_count),
                    action_list=self.action_dict[i],
                    action_tick=self.server_action_tick,
                    riichi_candidate_cuts=riichi_cuts,
                    forbidden_cut_tiles=forbidden,
                ),
            )
            await player_conn.websocket.send_json(response.dict(exclude_none=True))
            await self.send_to_realtime_spectators(cp.player_index, response)
        except Exception as e:
            logger.error(f"riichi broadcast_ask_hand_action 失败: {e}")
    if hasattr(self, "spectator_manager"):
        self.spectator_manager.record_ask_hand(self.current_player_index, self.action_dict.get(self.current_player_index, []))


async def broadcast_ask_other_action(self):
    cut_tile = self.player_list[self.current_player_index].discard_tiles[-1]
    self.server_action_tick += 1
    self._ask_broadcast_time = time.time()
    for i, cp in enumerate(self.player_list):
        try:
            if "offline" in cp.tag_list:
                if self.action_dict.get(i, []):
                    asyncio.create_task(auto_cut_action(self, i, self.action_dict[i], self.game_status))
                continue
            if cp.user_id == 0:
                if self.action_dict.get(i, []):
                    asyncio.create_task(auto_cut_action(self, i, self.action_dict[i], self.game_status))
                continue
            if cp.user_id == 2:
                if self.action_dict.get(i, []):
                    asyncio.create_task(smart_bot_action(self, i, self.action_dict[i], self.game_status))
                continue
            if cp.user_id not in self.game_server.user_id_to_connection:
                continue
            player_conn = self.game_server.user_id_to_connection[cp.user_id]
            response = Response(
                type="gamestate/riichi/ask_other_action",
                success=True,
                message="询问操作",
                ask_other_action_info=Ask_other_action_info(
                    remaining_time=cp.remaining_time,
                    action_list=self.action_dict[i],
                    cut_tile=cut_tile,
                    action_tick=self.server_action_tick,
                    chi_candidates=cp.chi_candidates if cp.chi_candidates else None,
                ),
            )
            await player_conn.websocket.send_json(response.dict(exclude_none=True))
            await self.send_to_realtime_spectators(cp.player_index, response)
        except Exception as e:
            logger.error(f"riichi broadcast_ask_other_action 失败: {e}")
    if hasattr(self, "spectator_manager"):
        player_action_map = {idx: actions for idx, actions in self.action_dict.items() if actions}
        if player_action_map:
            self.spectator_manager.record_ask_other(player_action_map, cut_tile)


async def broadcast_do_action(
    self,
    action_list: List[str],
    action_player: int,
    cut_tile: int = None,
    cut_class: bool = None,
    cut_tile_index: int = None,
    deal_tile: int = None,
    buhua_tile: int = None,
    combination_target: str = None,
    combination_mask: List[int] = None,
    is_riichi_horizontal: bool = None,
    is_mo_gang: bool = None,
    is_claim: bool = False,
    silent: bool = False,
):
    if not is_claim:
        self.server_action_tick += 1
        if hasattr(self, "_ask_broadcast_time"):
            delattr(self, "_ask_broadcast_time")
    for cp in self.player_list:
        try:
            if "offline" in cp.tag_list or cp.user_id == 0:
                continue
            if cp.user_id not in self.game_server.user_id_to_connection:
                continue
            conn = self.game_server.user_id_to_connection[cp.user_id]
            response = Response(
                type="gamestate/riichi/do_action",
                success=True,
                message="返回操作内容",
                do_action_info=Do_action_info(
                    action_list=action_list,
                    action_player=action_player,
                    action_tick=self.server_action_tick,
                    cut_tile=cut_tile,
                    cut_class=cut_class,
                    cut_tile_index=cut_tile_index,
                    deal_tile=deal_tile,
                    buhua_tile=buhua_tile,
                    combination_mask=combination_mask,
                    combination_target=combination_target,
                    is_riichi_horizontal=is_riichi_horizontal,
                    is_mo_gang=is_mo_gang,
                    is_claim=True if is_claim else None,
                    silent=True if silent else None,
                ),
            )
            await conn.websocket.send_json(response.dict(exclude_none=True))
            await self.send_to_realtime_spectators(cp.player_index, response)
        except Exception as e:
            logger.error(f"riichi broadcast_do_action 失败: {e}")
    if hasattr(self, "spectator_manager") and not is_claim:
        self.spectator_manager.record_do_action_ticks(
            action_list, action_player,
            cut_tile=cut_tile, cut_class=cut_class,
            deal_tile=deal_tile, buhua_tile=buhua_tile,
            combination_mask=combination_mask,
            is_riichi_horizontal=is_riichi_horizontal,
            is_mo_gang=is_mo_gang,
        )


async def broadcast_result(
    self,
    hepai_player_index: Optional[int] = None,
    player_to_score: Optional[Dict[int, int]] = None,
    hu_score: Optional[int] = None,
    hu_fan: Optional[List[str]] = None,
    hu_class: str = None,
    hepai_player_hand: Optional[List[int]] = None,
    hepai_player_combination_mask: Optional[List[List[int]]] = None,
    han: Optional[int] = None,
    fu: Optional[int] = None,
    aka_count: Optional[int] = None,
    dora_count: Optional[int] = None,
    ura_dora_count: Optional[int] = None,
    dora_indicators: Optional[List[int]] = None,
    ura_dora_indicators: Optional[List[int]] = None,
    honba: Optional[int] = None,
    riichi_sticks_collected: Optional[int] = None,
    score_changes: Optional[Dict[int, int]] = None,
    tenpai_tiles: Optional[Dict[int, List[int]]] = None,
    tenpai_hands: Optional[Dict[int, List[int]]] = None,
    exhaustive_penalty: Optional[bool] = None,
    langyong_multiplier: Optional[int] = None,
    langyong_scored_points: Optional[int] = None,
    silent: bool = False,
):
    self.server_action_tick += 1
    for cp in self.player_list:
        try:
            if "offline" in cp.tag_list or cp.user_id == 0:
                continue
            if cp.user_id not in self.game_server.user_id_to_connection:
                continue
            conn = self.game_server.user_id_to_connection[cp.user_id]
            response = Response(
                type="gamestate/riichi/show_result",
                success=True,
                message="显示结算结果",
                show_result_info=Show_result_info(
                    hepai_player_index=hepai_player_index,
                    player_to_score=player_to_score,
                    hu_score=hu_score,
                    hu_fan=hu_fan,
                    hu_class=hu_class,
                    hepai_player_hand=hepai_player_hand,
                    hepai_player_combination_mask=hepai_player_combination_mask,
                    action_tick=self.server_action_tick,
                    han=han,
                    fu=fu,
                    aka_count=aka_count,
                    dora_count=dora_count,
                    ura_dora_count=ura_dora_count,
                    dora_indicators=dora_indicators,
                    ura_dora_indicators=ura_dora_indicators,
                    honba=honba,
                    riichi_sticks_collected=riichi_sticks_collected,
                    score_changes=score_changes,
                    tenpai_tiles=tenpai_tiles,
                    tenpai_hands=tenpai_hands,
                    exhaustive_penalty=exhaustive_penalty,
                    langyong_multiplier=langyong_multiplier,
                    langyong_scored_points=langyong_scored_points,
                    silent=True if silent else None,
                ),
            )
            await conn.websocket.send_json(response.dict(exclude_none=True))
            await self.send_to_realtime_spectators(cp.player_index, response)
        except Exception as e:
            logger.error(f"riichi broadcast_result 失败: {e}")


async def broadcast_declare_riichi(self, player_index: int, is_daburu: bool = False):
    """广播立直宣告"""
    self.server_action_tick += 1
    for cp in self.player_list:
        try:
            if "offline" in cp.tag_list or cp.user_id == 0:
                continue
            if cp.user_id not in self.game_server.user_id_to_connection:
                continue
            conn = self.game_server.user_id_to_connection[cp.user_id]
            filtered_tags = _player_to_tag_list_for_viewer(self.player_list, cp.player_index)
            response = Response(
                type="gamestate/riichi/declare_riichi",
                success=True,
                message="立直宣告",
                refresh_player_tag_list_info=Refresh_player_tag_list_info(
                    player_to_tag_list=filtered_tags,
                    riichi_declared_player_index=player_index,
                ),
            )
            await conn.websocket.send_json(response.dict(exclude_none=True))
            await self.send_to_realtime_spectators(cp.player_index, response)
        except Exception as e:
            logger.error(f"riichi broadcast_declare_riichi 失败: {e}")
    if hasattr(self, "spectator_manager"):
        self.spectator_manager.record_tick(["riichi", player_index, 1 if is_daburu else 0])


async def broadcast_update_dora(self, new_indicator: int, is_kan_dora: bool = False):
    """广播新翻宝牌 / 杠宝牌指示牌"""
    self.server_action_tick += 1
    payload = {"new_indicator": new_indicator, "is_kan_dora": is_kan_dora}
    for cp in self.player_list:
        try:
            if "offline" in cp.tag_list or cp.user_id == 0:
                continue
            if cp.user_id not in self.game_server.user_id_to_connection:
                continue
            conn = self.game_server.user_id_to_connection[cp.user_id]
            response = Response(
                type="gamestate/riichi/update_dora",
                success=True,
                message="翻新宝牌",
                message_info=None,
            )
            dict_data = response.dict(exclude_none=True)
            dict_data["dora_info"] = payload
            dict_data["dora_indicators"] = list(self.dora_indicators)
            dict_data["kan_dora_indicators"] = list(self.kan_dora_indicators)
            await conn.websocket.send_json(dict_data)
            await self.send_to_realtime_spectators(cp.player_index, dict_data)
        except Exception as e:
            logger.error(f"riichi broadcast_update_dora 失败: {e}")
    if hasattr(self, "spectator_manager"):
        self.spectator_manager.record_tick(["dora", new_indicator])


async def broadcast_game_end(self):
    self.server_action_tick += 1
    player_final_data = {}
    for player in self.player_list:
        player_final_data[str(player.player_index)] = Player_final_data(
            rank=player.record_counter.rank_result,
            score=player.score,
            pt=0,
            username=player.username,
            original_player_index=player.original_player_index,
        )
    for cp in self.player_list:
        try:
            if "offline" in cp.tag_list or cp.user_id == 0:
                continue
            if cp.user_id not in self.game_server.user_id_to_connection:
                continue
            conn = self.game_server.user_id_to_connection[cp.user_id]
            response = Response(
                type="gamestate/riichi/game_end",
                success=True,
                message="游戏结束",
                game_end_info=Game_end_info(
                    master_seed=self.master_seed,
                    commitment=self.commitment,
                    salt=self.salt,
                    player_final_data=player_final_data,
                ),
            )
            await conn.websocket.send_json(response.dict(exclude_none=True))
            await self.send_to_realtime_spectators(cp.player_index, response)
        except Exception as e:
            logger.error(f"riichi broadcast_game_end 失败: {e}")


async def broadcast_switch_seat(self):
    info = Switch_seat_info(current_round=self.current_round)
    for cp in self.player_list:
        try:
            if "offline" in cp.tag_list or cp.user_id == 0:
                continue
            if cp.user_id not in self.game_server.user_id_to_connection:
                continue
            conn = self.game_server.user_id_to_connection[cp.user_id]
            response = Response(type="switch_seat", success=True, message="换位信息", switch_seat_info=info)
            await conn.websocket.send_json(response.dict(exclude_none=True))
            await self.send_to_realtime_spectators(cp.player_index, response)
        except Exception as e:
            logger.error(f"riichi broadcast_switch_seat 失败: {e}")


async def broadcast_refresh_player_tag_list(self):
    for cp in self.player_list:
        try:
            if "offline" in cp.tag_list or cp.user_id == 0:
                continue
            if cp.user_id not in self.game_server.user_id_to_connection:
                continue
            conn = self.game_server.user_id_to_connection[cp.user_id]
            info = Refresh_player_tag_list_info(
                player_to_tag_list=_player_to_tag_list_for_viewer(self.player_list, cp.player_index)
            )
            response = Response(type="refresh_player_tag_list", success=True, message="刷新玩家标签列表",
                                refresh_player_tag_list_info=info)
            await conn.websocket.send_json(response.dict(exclude_none=True))
            await self.send_to_realtime_spectators(cp.player_index, response)
        except Exception as e:
            logger.error(f"riichi broadcast_refresh_player_tag_list 失败: {e}")


async def broadcast_ready_status(self):
    player_to_ready = {}
    for player in self.player_list:
        pending = self.action_dict.get(player.player_index, [])
        player_to_ready[player.player_index] = "ready" not in pending
    info = Ready_status_info(player_to_ready=player_to_ready)
    for cp in self.player_list:
        try:
            if "offline" in cp.tag_list or cp.user_id == 0:
                continue
            if cp.user_id not in self.game_server.user_id_to_connection:
                continue
            conn = self.game_server.user_id_to_connection[cp.user_id]
            response = Response(type="gamestate/riichi/ready_status", success=True, message="准备状态更新",
                                ready_status_info=info)
            await conn.websocket.send_json(response.dict(exclude_none=True))
            await self.send_to_realtime_spectators(cp.player_index, response)
        except Exception as e:
            logger.error(f"riichi broadcast_ready_status 失败: {e}")


async def reconnected_send_pending_ask_for_viewer(self, spectator_user_id: int, view_player_index: int):
    """按指定座位视角向实时观战者补发当前等待中的操作询问。"""
    if spectator_user_id not in self.game_server.user_id_to_connection:
        return
    if view_player_index < 0 or view_player_index >= len(self.player_list):
        return
    conn = self.game_server.user_id_to_connection[spectator_user_id]
    idx = view_player_index
    player = self.player_list[idx]
    t0 = getattr(self, "_ask_broadcast_time", None)
    remaining = player.remaining_time if t0 is None else max(0, player.remaining_time - int(max(0, time.time() - t0)))
    if self.game_status == "waiting_hand_action":
        if idx == self.current_player_index:
            riichi_cuts = player.riichi_candidate_cuts if "riichi_cut" in self.action_dict.get(idx, []) else None
            forbidden = list(player.kuikae_forbidden_tiles) if player.kuikae_forbidden_tiles else None
            response = Response(
                type="gamestate/riichi/broadcast_hand_action",
                success=True,
                message="发牌，并询问手牌操作",
                ask_hand_action_info=Ask_hand_action_info(
                    remaining_time=remaining,
                    player_index=self.current_player_index,
                    remain_tiles=max(0, len(self.tiles_list) - self.dead_wall_count),
                    action_list=self.action_dict.get(idx, []),
                    action_tick=self.server_action_tick,
                    riichi_candidate_cuts=riichi_cuts,
                    forbidden_cut_tiles=forbidden,
                ),
            )
            await conn.websocket.send_json(response.dict(exclude_none=True))
    elif self.game_status in ("waiting_action_after_cut", "waiting_action_qianggang"):
        if self.action_dict.get(idx):
            cut_tile = self.player_list[self.current_player_index].discard_tiles[-1]
            response = Response(
                type="gamestate/riichi/ask_other_action",
                success=True,
                message="询问操作",
                ask_other_action_info=Ask_other_action_info(
                    remaining_time=remaining,
                    action_list=self.action_dict[idx],
                    cut_tile=cut_tile,
                    action_tick=self.server_action_tick,
                    chi_candidates=player.chi_candidates if player.chi_candidates else None,
                ),
            )
            await conn.websocket.send_json(response.dict(exclude_none=True))


async def reconnected_send_pending_ask(self, user_id: int):
    idx = next((i for i, p in enumerate(self.player_list) if p.user_id == user_id), None)
    if idx is None or user_id not in self.game_server.user_id_to_connection:
        return
    await reconnected_send_pending_ask_for_viewer(self, user_id, idx)
