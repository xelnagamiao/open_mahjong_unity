"""四川麻将（血战到底）广播。消息前缀 gamestate/sichuan/*。"""
from ...response import (
    Response, GameInfo, Ask_hand_action_info, Ask_other_action_info, Do_action_info,
    Show_result_info, Game_end_info, Player_final_data, Refresh_player_tag_list_info,
    Ready_status_info,
)
from typing import List, Dict, Optional
import logging
import asyncio
import time
from ..public.ai.auto_cut_ai import auto_cut_action
from ..public.ai.smart_bot_ai import smart_bot_action
from ..public.deal_tile_view import sanitize_deal_tile_for_viewer
from ..public.hand_slot_utils import bot_ask_hand_game_status
from ..public.claim_protection import (
    claim_protection_enabled,
    is_protected_viewer,
    stash_protected_cut_payload,
    arm_claim_protection_timer,
    prepare_protected_meld_for_viewers,
    end_claim_protection_interval,
    schedule_protected_meld_send,
    REAL_MELD_ACTIONS,
)
from .shunhe import tag_list_for_viewer

logger = logging.getLogger(__name__)


def _build_players_info(self, viewer_user_id: Optional[int]):
    viewer_index = None
    if viewer_user_id is not None:
        for p in self.player_list:
            if p.user_id == viewer_user_id:
                viewer_index = p.player_index
                break
    players_info = []
    for player in self.player_list:
        tags = tag_list_for_viewer(player.tag_list, player.player_index, viewer_index)
        players_info.append({
            'user_id': player.user_id,
            'username': player.username,
            'hand_tiles_count': len(player.hand_tiles),
            'hand_tiles': player.hand_tiles if (viewer_user_id is not None and player.user_id == viewer_user_id) else None,
            'discard_tiles': player.discard_tiles,
            'discard_origin_tiles': player.discard_origin_tiles,
            'combination_tiles': player.combination_tiles,
            "combination_mask": player.combination_mask,
            "huapai_list": player.huapai_list,
            'remaining_time': player.remaining_time,
            'player_index': player.player_index,
            'original_player_index': player.original_player_index,
            'score': player.score,
            "title_used": player.title_used,
            'profile_used': player.profile_used,
            'character_used': player.character_used,
            'voice_used': player.voice_used,
            'score_history': player.score_history,
            'round_number_history': player.round_number_history,
            'tag_list': tags,
            'dingque_suit': getattr(player, 'dingque_suit', 0),
            'is_hu': getattr(player, 'is_hu', False),
        })
    return players_info


def _base_game_info(self):
    base = {
        'room_id': self.room_id,
        'gamestate_id': self.gamestate_id,
        'tips': self.tips,
        'current_player_index': self.current_player_index,
        "action_tick": self.server_action_tick,
        'max_round': self.max_round,
        'tile_count': max(0, len(self.tiles_list) - self.dead_wall_count),
        'commitment': self.commitment,
        'salt': self.salt,
        'current_round': self.current_round,
        'step_time': self.step_time,
        'round_time': self.round_time,
        'room_type': self.room_type,
        'room_rule': self.room_rule,
        'sub_rule': getattr(self, 'sub_rule', 'sichuan/standard'),
        'hepai_limit': getattr(self, 'hepai_limit', 1),
        'open_cuohe': getattr(self, 'open_cuohe', False),
        'show_moqie_hint': getattr(self, 'show_moqie_hint', False),
        'tactical_call': getattr(self, 'tactical_call', False),
        'claim_protection': getattr(self, 'claim_protection', False),
        'isPlayerSetRandomSeed': self.isPlayerSetRandomSeed,
        'blood_battle': getattr(self, 'blood_battle', True),
        'dealer_index': getattr(self, 'dealer_index', 0),
        'players_info': [],
    }
    from ..public.game_record_manager import build_player_entry_order_fields
    base.update(build_player_entry_order_fields(self))
    return base


async def broadcast_game_start(self):
    base_game_info = _base_game_info(self)
    for current_player in self.player_list:
        try:
            if "offline" in current_player.tag_list or current_player.user_id == 0:
                continue
            if current_player.user_id in self.game_server.user_id_to_connection:
                player_conn = self.game_server.user_id_to_connection[current_player.user_id]
                game_info = GameInfo(
                    **{**base_game_info, 'players_info': _build_players_info(self, current_player.user_id)},
                    self_hand_tiles=None,
                )
                response = Response(type="gamestate/sichuan/game_start", success=True,
                                    message="游戏开始", game_info=game_info)
                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                await self.send_to_realtime_spectators(current_player.player_index, response)
        except Exception as e:
            logger.error(f"四川 game_start 广播失败 {current_player.user_id}: {e}")
    if hasattr(self, 'spectator_manager'):
        self.spectator_manager.record_game_title()
        self.spectator_manager.record_round_start()


async def broadcast_dingque_ask(self):
    """定缺阶段：询问四家选择缺门花色（action_list=['dingque']）。"""
    self.server_action_tick += 1
    self._ask_broadcast_time = time.time()
    for i, current_player in enumerate(self.player_list):
        try:
            if "offline" in current_player.tag_list:
                if self.action_dict.get(i, []):
                    asyncio.create_task(_auto_dingque(self, i))
                continue
            if current_player.user_id in (0, 2):
                if self.action_dict.get(i, []):
                    asyncio.create_task(_auto_dingque(self, i))
                continue
            if current_player.user_id in self.game_server.user_id_to_connection:
                player_conn = self.game_server.user_id_to_connection[current_player.user_id]
                response = Response(
                    type="gamestate/sichuan/ask_dingque", success=True, message="请选择定缺花色",
                    ask_hand_action_info=Ask_hand_action_info(
                        remaining_time=current_player.remaining_time,
                        player_index=i,
                        remain_tiles=max(0, len(self.tiles_list) - self.dead_wall_count),
                        action_list=self.action_dict.get(i, []),
                        action_tick=self.server_action_tick,
                    ),
                )
                await player_conn.websocket.send_json(response.dict(exclude_none=True))
        except Exception as e:
            logger.error(f"四川 ask_dingque 广播失败 {current_player.user_id}: {e}")


async def _auto_dingque(self, player_index):
    """AI/掉线托管自动定缺：选数量最少的花色。"""
    player = self.player_list[player_index]
    counts = {1: 0, 2: 0, 3: 0}
    for t in player.hand_tiles:
        counts[t // 10] = counts.get(t // 10, 0) + 1
    suit = min(counts, key=lambda s: counts[s])
    await self.action_queues[player_index].put({"action_type": "dingque", "target_tile": suit})
    self.action_events[player_index].set()


async def broadcast_dingque_done(self):
    """定缺完成：广播各家定缺花色（自己可见全部，用于头像图标）。"""
    self.server_action_tick += 1
    player_to_dingque = {p.player_index: getattr(p, 'dingque_suit', 0) for p in self.player_list}
    for current_player in self.player_list:
        try:
            if "offline" in current_player.tag_list or current_player.user_id == 0:
                continue
            if current_player.user_id in self.game_server.user_id_to_connection:
                player_conn = self.game_server.user_id_to_connection[current_player.user_id]
                response = Response(
                    type="gamestate/sichuan/dingque_done", success=True, message="定缺完成",
                    show_result_info=Show_result_info(
                        hu_class="dingque_done",
                        action_tick=self.server_action_tick,
                        player_to_dingque=player_to_dingque,
                    ),
                )
                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                await self.send_to_realtime_spectators(current_player.player_index, response)
        except Exception as e:
            logger.error(f"四川 dingque_done 广播失败 {current_player.user_id}: {e}")


async def broadcast_ask_hand_action(self):
    self.server_action_tick += 1
    self._ask_broadcast_time = time.time()
    for i, current_player in enumerate(self.player_list):
        try:
            if "offline" in current_player.tag_list:
                if self.action_dict.get(i, []):
                    asyncio.create_task(auto_cut_action(self, i, self.action_dict[i], bot_ask_hand_game_status(self, i)))
                continue
            if current_player.user_id == 0:
                if self.action_dict.get(i, []):
                    asyncio.create_task(auto_cut_action(self, i, self.action_dict[i], bot_ask_hand_game_status(self, i)))
                continue
            elif current_player.user_id == 2:
                if self.action_dict.get(i, []):
                    asyncio.create_task(smart_bot_action(self, i, self.action_dict[i], bot_ask_hand_game_status(self, i)))
                continue
            if current_player.user_id in self.game_server.user_id_to_connection:
                player_conn = self.game_server.user_id_to_connection[current_player.user_id]
                response = Response(
                    type="gamestate/sichuan/broadcast_hand_action", success=True,
                    message="发牌并询问手牌操作",
                    ask_hand_action_info=Ask_hand_action_info(
                        remaining_time=current_player.remaining_time,
                        player_index=self.current_player_index,
                        remain_tiles=max(0, len(self.tiles_list) - self.dead_wall_count),
                        action_list=self.action_dict[i],
                        action_tick=self.server_action_tick,
                    ),
                )
                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                await self.send_to_realtime_spectators(current_player.player_index, response)
        except Exception as e:
            logger.error(f"四川 ask_hand 广播失败 {current_player.user_id}: {e}")
    if hasattr(self, 'spectator_manager'):
        self.spectator_manager.record_ask_hand(self.current_player_index, self.action_dict.get(self.current_player_index, []))


async def broadcast_ask_other_action(self, remaining_time_override: Optional[int] = None, is_tactical_recheck: bool = False):
    cut_tile = self.player_list[self.current_player_index].discard_tiles[-1]
    self.server_action_tick += 1
    self._ask_broadcast_time = time.time()
    for i, current_player in enumerate(self.player_list):
        try:
            if "offline" in current_player.tag_list:
                if self.action_dict.get(i, []):
                    asyncio.create_task(auto_cut_action(self, i, self.action_dict[i], self.game_status))
                continue
            if current_player.user_id == 0:
                if self.action_dict.get(i, []):
                    asyncio.create_task(auto_cut_action(self, i, self.action_dict[i], self.game_status))
                continue
            elif current_player.user_id == 2:
                if self.action_dict.get(i, []):
                    asyncio.create_task(smart_bot_action(self, i, self.action_dict[i], self.game_status))
                continue
            remaining_time_for_player = (
                remaining_time_override if remaining_time_override is not None else current_player.remaining_time
            )
            if self.action_dict.get(i):
                if current_player.user_id in self.game_server.user_id_to_connection:
                    player_conn = self.game_server.user_id_to_connection[current_player.user_id]
                    response = Response(
                        type="gamestate/sichuan/ask_other_action", success=True, message="询问操作",
                        ask_other_action_info=Ask_other_action_info(
                            remaining_time=remaining_time_for_player,
                            action_list=self.action_dict[i],
                            cut_tile=cut_tile,
                            action_tick=self.server_action_tick,
                            is_tactical_recheck=is_tactical_recheck if is_tactical_recheck else None,
                        ),
                    )
                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    await self.send_to_realtime_spectators(current_player.player_index, response)
            elif not is_tactical_recheck:
                if current_player.user_id in self.game_server.user_id_to_connection:
                    player_conn = self.game_server.user_id_to_connection[current_player.user_id]
                    response = Response(
                        type="gamestate/sichuan/ask_other_action", success=True, message="询问操作",
                        ask_other_action_info=Ask_other_action_info(
                            remaining_time=remaining_time_for_player,
                            action_list=[],
                            cut_tile=cut_tile,
                            action_tick=self.server_action_tick,
                        ),
                    )
                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    await self.send_to_realtime_spectators(current_player.player_index, response)
        except Exception as e:
            logger.error(f"四川 ask_other 广播失败 {current_player.user_id}: {e}")


def _reconnect_remaining_time(self, player) -> int:
    t0 = getattr(self, "_ask_broadcast_time", None)
    if t0 is None:
        return player.remaining_time
    elapsed = max(0, time.time() - t0)
    return max(0, player.remaining_time - int(elapsed))


async def reconnected_send_pending_ask(self, user_id: int):
    reconnect_idx = next((i for i, p in enumerate(self.player_list) if p.user_id == user_id), None)
    if reconnect_idx is None or user_id not in self.game_server.user_id_to_connection:
        return
    player_conn = self.game_server.user_id_to_connection[user_id]
    player = self.player_list[reconnect_idx]
    remaining_sent = _reconnect_remaining_time(self, player)
    if self.game_status == "waiting_dingque" and self.action_dict.get(reconnect_idx):
        response = Response(
            type="gamestate/sichuan/ask_dingque", success=True, message="请选择定缺花色",
            ask_hand_action_info=Ask_hand_action_info(
                remaining_time=remaining_sent, player_index=reconnect_idx,
                remain_tiles=max(0, len(self.tiles_list) - self.dead_wall_count),
                action_list=self.action_dict.get(reconnect_idx, []), action_tick=self.server_action_tick,
            ),
        )
        await player_conn.websocket.send_json(response.dict(exclude_none=True))
    elif self.game_status == "waiting_hand_action" and reconnect_idx == self.current_player_index:
        response = Response(
            type="gamestate/sichuan/broadcast_hand_action", success=True, message="发牌并询问手牌操作",
            ask_hand_action_info=Ask_hand_action_info(
                remaining_time=remaining_sent, player_index=self.current_player_index,
                remain_tiles=max(0, len(self.tiles_list) - self.dead_wall_count),
                action_list=self.action_dict.get(reconnect_idx, []), action_tick=self.server_action_tick,
            ),
        )
        await player_conn.websocket.send_json(response.dict(exclude_none=True))
    elif self.game_status in ("waiting_action_after_cut", "waiting_action_qianggang") and self.action_dict.get(reconnect_idx):
        cut_tile = self.player_list[self.current_player_index].discard_tiles[-1]
        response = Response(
            type="gamestate/sichuan/ask_other_action", success=True, message="询问操作",
            ask_other_action_info=Ask_other_action_info(
                remaining_time=remaining_sent, action_list=self.action_dict[reconnect_idx],
                cut_tile=cut_tile, action_tick=self.server_action_tick,
            ),
        )
        await player_conn.websocket.send_json(response.dict(exclude_none=True))


async def broadcast_do_action(self, action_list: List[str], action_player: int,
                              cut_tile: int = None, cut_class: bool = None, cut_tile_index: int = None,
                              deal_tile: int = None, combination_target: str = None,
                              combination_mask: List[int] = None, is_mo_gang: bool = None,
                              gang_score_changes: Dict[int, int] = None, gang_score_type: str = None,
                              is_claim: bool = False, silent: bool = False):
    if not is_claim and not silent and getattr(self, "_tactical_silent_action", False):
        silent = True
        self._tactical_silent_action = False
    if not is_claim:
        self.server_action_tick += 1
        if hasattr(self, "_ask_broadcast_time"):
            delattr(self, "_ask_broadcast_time")
    elif action_list and cut_tile is not None:
        from ..public.game_record_manager import track_claim_application
        track_claim_application(self, action_player, action_list[0], cut_tile)

    interval_active = claim_protection_enabled(self) and getattr(self, "_cp_active", False)
    is_cut = bool(action_list) and action_list[0] == "cut"
    is_real_meld = (not is_claim) and bool(action_list) and action_list[0] in REAL_MELD_ACTIONS
    cut_already_revealed = getattr(self, "_cp_cut_flushed", False)

    protected_meld_delay = 0.0
    if interval_active and is_real_meld:
        protected_meld_delay = await prepare_protected_meld_for_viewers(
            self,
            _send_do_action_payload_to_viewer,
        )

    for i, current_player in enumerate(self.player_list):
        try:
            if "offline" in current_player.tag_list or current_player.user_id == 0:
                continue
            if current_player.user_id < 10:
                continue

            protected = interval_active and is_protected_viewer(self, i)

            if is_claim and protected and not getattr(self, "_cp_cut_flushed", False):
                continue

            if protected and is_real_meld:
                viewer_silent = silent if cut_already_revealed else False
                viewer_reveal_delay = protected_meld_delay
            else:
                viewer_silent = silent
                viewer_reveal_delay = 0.0

            payload = _build_do_action_payload(
                self,
                action_list,
                action_player,
                i,
                cut_tile=cut_tile,
                cut_class=cut_class,
                cut_tile_index=cut_tile_index,
                deal_tile=deal_tile,
                combination_mask=combination_mask,
                combination_target=combination_target,
                is_claim=is_claim,
                silent=viewer_silent,
                is_mo_gang=is_mo_gang,
                gang_score_changes=gang_score_changes,
                gang_score_type=gang_score_type,
                meld_reveal_delay=viewer_reveal_delay,
            )

            if protected and is_cut:
                stash_protected_cut_payload(self, i, payload)
                continue

            # 实际鸣牌按序 await 发送（不再用追赶协程延迟，避免受保护观众收到 N+4 在 N+2 之前的乱序）；
            # cut 已在 prepare_protected_meld_for_viewers 里 await flush 先发。

            if current_player.user_id in self.game_server.user_id_to_connection:
                await _send_do_action_payload_to_viewer(self, i, payload)
        except Exception as e:
            logger.error(f"四川 do_action 广播失败 {current_player.user_id}: {e}")

    if interval_active and is_cut:
        arm_claim_protection_timer(self, _send_do_action_payload_to_viewer)

    if interval_active and is_real_meld:
        end_claim_protection_interval(self)


def _build_do_action_payload(
    self,
    action_list,
    action_player,
    viewer_index,
    *,
    cut_tile=None,
    cut_class=None,
    cut_tile_index=None,
    deal_tile=None,
    combination_mask=None,
    combination_target=None,
    is_claim=False,
    silent=False,
    is_mo_gang=None,
    gang_score_changes=None,
    gang_score_type=None,
    meld_reveal_delay=None,
):
    viewer_deal_tile = sanitize_deal_tile_for_viewer(deal_tile, action_player, viewer_index)
    return {
        "action_list": action_list,
        "action_player": action_player,
        "action_tick": self.server_action_tick,
        "cut_tile": cut_tile,
        "cut_class": cut_class,
        "cut_tile_index": cut_tile_index,
        "deal_tile": viewer_deal_tile,
        "combination_mask": combination_mask,
        "combination_target": combination_target,
        "is_claim": True if is_claim else None,
        "silent": True if silent else None,
        "is_mo_gang": is_mo_gang,
        "gang_score_changes": gang_score_changes,
        "gang_score_type": gang_score_type,
        # 受保护观众鸣牌显示层延迟（秒）：服务器按序发送、客户端仅延迟鸣牌 3D 动画，复现 0.5s 间隔。
        "meld_reveal_delay": meld_reveal_delay,
    }


async def _send_do_action_payload_to_viewer(
    self, viewer_index: int, payload: dict, msg_type: str = "gamestate/sichuan/do_action"
):
    current_player = self.player_list[viewer_index]
    if "offline" in current_player.tag_list or current_player.user_id == 0:
        return
    if current_player.user_id < 10:
        return
    if current_player.user_id not in self.game_server.user_id_to_connection:
        return
    player_conn = self.game_server.user_id_to_connection[current_player.user_id]
    response = Response(
        type=msg_type,
        success=True,
        message="返回操作内容",
        do_action_info=Do_action_info(**payload),
    )
    await player_conn.websocket.send_json(response.dict(exclude_none=True))
    await self.send_to_realtime_spectators(current_player.player_index, response)


async def broadcast_result(self, hu_class: str, **kwargs):
    """通用结算广播：透传 Show_result_info 字段（四川扩展见 kwargs）。"""
    silent = kwargs.pop("silent", False)
    if not silent and getattr(self, "_tactical_silent_action", False):
        silent = True
        self._tactical_silent_action = False
    self.server_action_tick += 1
    for current_player in self.player_list:
        try:
            if "offline" in current_player.tag_list or current_player.user_id == 0:
                continue
            if current_player.user_id in self.game_server.user_id_to_connection:
                player_conn = self.game_server.user_id_to_connection[current_player.user_id]
                payload = dict(kwargs)
                payload = _viewer_sichuan_hu_payload(payload, current_player.player_index)
                response = Response(
                    type="gamestate/sichuan/show_result", success=True, message="显示结算结果",
                    show_result_info=Show_result_info(
                        hu_class=hu_class,
                        action_tick=self.server_action_tick,
                        silent=True if silent else None,
                        **payload,
                    ),
                )
                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                await self.send_to_realtime_spectators(current_player.player_index, response)
        except Exception as e:
            logger.error(f"四川 show_result 广播失败 {current_player.user_id}: {e}")


def _viewer_sichuan_hu_payload(kwargs: dict, viewer_index: int) -> dict:
    """自摸和牌张：仅和牌者本人可见真实 tile id（0=牌背且不可 peek）。"""
    payload = dict(kwargs)
    hepai_tile = payload.get("hepai_tile")
    winner_index = payload.get("hepai_player_index")
    if payload.get("is_zimo") and hepai_tile is not None and winner_index is not None:
        if viewer_index != winner_index:
            payload["hepai_tile"] = 0
    return payload


async def broadcast_game_end(self):
    self.server_action_tick += 1
    player_final_data = {}
    for player in self.player_list:
        player_final_data[str(player.player_index)] = Player_final_data(
            rank=player.record_counter.rank_result, score=player.score, pt=0,
            username=player.username, original_player_index=player.original_player_index,
        )
    for current_player in self.player_list:
        try:
            if "offline" in current_player.tag_list or current_player.user_id == 0:
                continue
            if current_player.user_id in self.game_server.user_id_to_connection:
                player_conn = self.game_server.user_id_to_connection[current_player.user_id]
                response = Response(
                    type="gamestate/sichuan/game_end", success=True, message="游戏结束",
                    game_end_info=Game_end_info(
                        master_seed=self.master_seed, commitment=self.commitment, salt=self.salt,
                        player_final_data=player_final_data,
                    ),
                )
                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                await self.send_to_realtime_spectators(current_player.player_index, response)
        except Exception as e:
            logger.error(f"四川 game_end 广播失败 {current_player.user_id}: {e}")


async def broadcast_refresh_player_tag_list(self):
    for current_player in self.player_list:
        try:
            if "offline" in current_player.tag_list or current_player.user_id == 0:
                continue
            if current_player.user_id in self.game_server.user_id_to_connection:
                player_to_tag_list = {
                    p.player_index: tag_list_for_viewer(
                        p.tag_list, p.player_index, current_player.player_index,
                    )
                    for p in self.player_list
                }
                refresh_tag_info = Refresh_player_tag_list_info(player_to_tag_list=player_to_tag_list)
                player_conn = self.game_server.user_id_to_connection[current_player.user_id]
                response = Response(type="refresh_player_tag_list", success=True,
                                    message="刷新玩家标签列表", refresh_player_tag_list_info=refresh_tag_info)
                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                await self.send_to_realtime_spectators(current_player.player_index, response)
        except Exception as e:
            logger.error(f"四川 refresh_tag 广播失败 {current_player.user_id}: {e}")


async def broadcast_ready_status(self):
    player_to_ready = {}
    for player in self.player_list:
        pending = self.action_dict.get(player.player_index, [])
        player_to_ready[player.player_index] = "ready" not in pending
    ready_info = Ready_status_info(player_to_ready=player_to_ready)
    for current_player in self.player_list:
        try:
            if "offline" in current_player.tag_list or current_player.user_id == 0:
                continue
            if current_player.user_id in self.game_server.user_id_to_connection:
                player_conn = self.game_server.user_id_to_connection[current_player.user_id]
                response = Response(type="gamestate/sichuan/ready_status", success=True,
                                    message="准备状态更新", ready_status_info=ready_info)
                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                await self.send_to_realtime_spectators(current_player.player_index, response)
        except Exception as e:
            logger.error(f"四川 ready_status 广播失败 {current_player.user_id}: {e}")
