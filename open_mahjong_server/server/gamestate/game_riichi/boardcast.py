"""
立直麻将广播：沿用 classical 的消息布局，新增 declare_riichi / riichi_accepted / update_dora；
GameInfo 额外携带 honba / riichi_sticks / dora_indicators / kan_dora_indicators / hepai_way / red_dora。
"""
from typing import List, Dict, Optional
import asyncio
from ..public.lifecycle import start_owned_task
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
from ..public.hand_draw_source import ensure_hand_draw_source_round, get_hand_draw_source, update_hand_draw_source
from ..public.game_record_manager import local_record_detail_for_end
from ..public.ask_timing import reconnect_clock
from ..public.ai.auto_cut_ai import auto_cut_action
from ..public.offline import offline_auto_action
from ..public.ai.riichi_smart_bot_ai import riichi_smart_bot_action as smart_bot_action
from ..public.deal_tile_view import sanitize_deal_tile_for_viewer
from ..public.hand_slot_utils import bot_ask_hand_game_status

logger = logging.getLogger(__name__)


from ..public.claim_protection import (
    claim_protection_enabled, is_protected_viewer, stash_protected_cut_payload,
    arm_claim_protection_timer, prepare_protected_meld_for_viewers,
    end_claim_protection_interval, mark_post_meld_gap, take_post_meld_gap_delay,
    REAL_MELD_ACTIONS,
)
from ..public.claim_protection import (begin_discard, send_cut,
    stage_protected_cut, schedule_meld)
from ..public.outbound_pipe import send_to_viewer, schedule_viewer_send
from ..public.ask_timing import begin_ask_round, note_ask_delivered


async def _deliver(self, viewer, payload):
    conn = self.game_server.user_id_to_connection.get(self.player_list[viewer].user_id)
    if conn is not None:
        data = payload if isinstance(payload, dict) else payload.dict(exclude_none=True)
        await conn.websocket.send_json(data)
        await self.send_to_realtime_spectators(viewer, payload)


async def _send_response(self, viewer, payload, *, block=True):
    async def deliver():
        await _deliver(self, viewer, payload)
    if block:
        await send_to_viewer(self, viewer, deliver, delay_before=take_post_meld_gap_delay(self, viewer))
    else:
        schedule_viewer_send(self, viewer, deliver, delay_before=take_post_meld_gap_delay(self, viewer))


async def _send_do_action_payload_to_viewer(self, viewer, payload):
    async def deliver():
        await _deliver(self, viewer, Response(type="gamestate/riichi/do_action", success=True,
            message="返回操作内容", do_action_info=Do_action_info(**payload)))
    if payload.get("action_list") == ["cut"] and await send_cut(self, viewer, payload, deliver):
        return
    await send_to_viewer(self, viewer, deliver, delay_before=take_post_meld_gap_delay(self, viewer))


async def _send_ask_response_to_viewer(
    self, viewer_index: int, response, *, block: bool = True
) -> None:
    """经 outbound_pipe 发送 ask，保证排在延迟鸣牌/第二追赶之后；送达时起算计时。

    block=True：await 本条（用于当前行动者，立刻可操作）。
    block=False：仅 schedule 入队（旁观者可带 post_gap，不拖住主循环/行动者）。
    """
    from ..public.outbound_pipe import send_to_viewer, schedule_viewer_send

    current_player = self.player_list[viewer_index]
    if current_player.user_id not in self.game_server.user_id_to_connection:
        logger.warning(
            f"玩家 {current_player.username} (user_id={current_player.user_id}) 未连接，跳过 ask 广播"
        )
        return
    player_conn = self.game_server.user_id_to_connection[current_player.user_id]
    delay_before = take_post_meld_gap_delay(self, viewer_index)
    ask_tick = self.server_action_tick

    async def _do():
        await player_conn.websocket.send_json(response.dict(exclude_none=True))
        await self.send_to_realtime_spectators(viewer_index, response)
        # A delayed observer ask may belong to a decision window already closed.
        # It must not start the clock for a newer ask waiting behind it.
        if self.server_action_tick == ask_tick:
            note_ask_delivered(self, viewer_index)

    if block:
        await send_to_viewer(self, viewer_index, _do, delay_before=delay_before)
    else:
        schedule_viewer_send(self, viewer_index, _do, delay_before=delay_before)


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
        "count_tips": getattr(self, "count_tips", False),
        "pointer_tips": getattr(self, "pointer_tips", True),
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
        "claim_protection": getattr(self, "claim_protection", False),
        "sub_rule": getattr(self, "sub_rule", "riichi/standard"),
        "hepai_limit": self.hepai_limit,
        "open_cuohe": self.open_cuohe,
        "show_moqie_hint": getattr(self, "show_moqie_hint", False),
        "isPlayerSetRandomSeed": self.isPlayerSetRandomSeed,
        "honba": self.honba,
        "riichi_sticks": self.riichi_sticks,
        "dora_indicators": list(self.dora_indicators),
        "kan_dora_indicators": list(self.kan_dora_indicators),
        "hepai_way": self.hepai_way,
        "red_dora": self.red_dora,
        "detailed_config": dict(self.detailed_config),
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
        "avatar_frame_used": getattr(player, "avatar_frame_used", 0),
        "character_used": player.character_used,
        "voice_used": player.voice_used,
        "score_history": player.score_history,
        "round_number_history": player.round_number_history,
        "tag_list": _tag_list_for_viewer(player.tag_list, player.player_index, viewer_player_index),
        "discard_riichi_flags": list(getattr(player, "discard_riichi_flags", []) or []),
        "riichi_accepted": bool(getattr(player, "riichi_paid_this_round", False)),
    }


async def broadcast_game_start(self):
    ensure_hand_draw_source_round(self)
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
            await _send_response(self, cp.player_index, response)
        except Exception as e:
            logger.error(f"riichi broadcast_game_start 失败: {e}", exc_info=True)
    if hasattr(self, "spectator_manager"):
        self.spectator_manager.record_game_title()
        self.spectator_manager.record_round_start()


async def broadcast_ask_hand_action(self):
    self.server_action_tick += 1
    begin_ask_round(self)
    for i in [self.current_player_index] + [j for j in range(len(self.player_list)) if j != self.current_player_index]:
        cp = self.player_list[i]
        try:
            if "offline" in cp.tag_list:
                if self.action_dict.get(i, []):
                    start_owned_task(self, offline_auto_action(self, i, self.action_dict[i], bot_ask_hand_game_status(self, i)))
                continue
            if cp.user_id == 0:
                if self.action_dict.get(i, []):
                    start_owned_task(self, auto_cut_action(self, i, self.action_dict[i], bot_ask_hand_game_status(self, i)))
                continue
            if cp.user_id == 2:
                if self.action_dict.get(i, []):
                    start_owned_task(self, smart_bot_action(self, i, self.action_dict[i], bot_ask_hand_game_status(self, i)))
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
                    deal_tile_type=get_hand_draw_source(self, self.current_player_index),
                    riichi_candidate_cuts=riichi_cuts,
                    forbidden_cut_tiles=forbidden,
                ),
            )
            await _send_ask_response_to_viewer(self, i, response, block=(i == self.current_player_index))
        except Exception as e:
            logger.error(f"riichi broadcast_ask_hand_action 失败: {e}")
    if hasattr(self, "spectator_manager"):
        self.spectator_manager.record_ask_hand(self.current_player_index, self.action_dict.get(self.current_player_index, []))


async def broadcast_ask_other_action(self):
    cut_tile = self.jiagang_tile if self.game_status == 'waiting_action_qianggang' else self.player_list[self.current_player_index].discard_tiles[-1]
    self.server_action_tick += 1
    begin_ask_round(self)
    for i, cp in enumerate(self.player_list):
        try:
            if not self.action_dict.get(i):
                continue
            if "offline" in cp.tag_list:
                if self.action_dict.get(i, []):
                    start_owned_task(self, offline_auto_action(self, i, self.action_dict[i], self.game_status))
                continue
            if cp.user_id == 0:
                if self.action_dict.get(i, []):
                    start_owned_task(self, auto_cut_action(self, i, self.action_dict[i], self.game_status))
                continue
            if cp.user_id == 2:
                if self.action_dict.get(i, []):
                    start_owned_task(self, smart_bot_action(self, i, self.action_dict[i], self.game_status))
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
            await _send_ask_response_to_viewer(self, i, response)
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
    is_mo_buhua: bool = None,
    is_claim: bool = False,
    silent: bool = False,
    cut_from_player: int = None,
    is_timeout_action: bool = False,
):
    if not is_claim:
        update_hand_draw_source(self, action_list, action_player)
        self.server_action_tick += 1
        if hasattr(self, "_ask_broadcast_time"):
            delattr(self, "_ask_broadcast_time")
    interval = claim_protection_enabled(self) and getattr(self, "_cp_active", False)
    is_cut = action_list == ["cut"]
    real_meld = not is_claim and bool(action_list) and action_list[0] in REAL_MELD_ACTIONS
    if is_cut:
        begin_discard(self, action_player)
    if interval and real_meld:
        await prepare_protected_meld_for_viewers(self, _send_do_action_payload_to_viewer)
    for cp in self.player_list:
        try:
            if "offline" in cp.tag_list or cp.user_id == 0:
                continue
            if cp.user_id not in self.game_server.user_id_to_connection:
                continue
            conn = self.game_server.user_id_to_connection[cp.user_id]
            viewer_deal_tile = sanitize_deal_tile_for_viewer(deal_tile, action_player, cp.player_index)
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
                    is_timeout_action=True if is_timeout_action else None,
                    deal_tile=viewer_deal_tile,
                    buhua_tile=buhua_tile,
                    combination_mask=combination_mask,
                    combination_target=combination_target,
                    is_riichi_horizontal=is_riichi_horizontal,
                    is_mo_gang=is_mo_gang,
                    is_mo_buhua=is_mo_buhua,
                    is_claim=True if is_claim else None,
                    silent=True if silent else None,
                    cut_from_player=cut_from_player,
                ),
            )
            viewer = cp.player_index
            protected = interval and is_protected_viewer(self, viewer)
            payload = response.do_action_info.dict(exclude_none=True)
            if is_cut and protected:
                async def deliver_cut(vi=viewer, resp=response):
                    await _deliver(self, vi, resp)
                stage_protected_cut(self, viewer, payload, deliver_cut)
                stash_protected_cut_payload(self, viewer, payload)
                continue
            if real_meld:
                async def deliver_meld(vi=viewer, resp=response):
                    await _deliver(self, vi, resp)
                if schedule_meld(self, viewer, deliver_meld, protected=protected):
                    if protected:
                        mark_post_meld_gap(self, viewer)
                    continue
            await _send_do_action_payload_to_viewer(self, viewer, payload)
        except Exception as e:
            logger.error(f"riichi broadcast_do_action 失败: {e}")

    if interval and is_cut:
        arm_claim_protection_timer(self, _send_do_action_payload_to_viewer)
    if interval and real_meld:
        end_claim_protection_interval(self)


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
    nagashi_mangan_winners: Optional[List[int]] = None,
    langyong_multiplier: Optional[int] = None,
    langyong_scored_points: Optional[int] = None,
    simultaneous_hu_hands: Optional[Dict[int, List[int]]] = None,
    skip_hand_reveal: Optional[bool] = None,
    silent: bool = False,
    next_status: Optional[str] = None,
):
    # 和牌分变只描述本次结算；计分板还须计入此前支付的立直棒。
    history_before = getattr(self, "_score_history_scores_before", None)
    score_history_changes = None
    if history_before is not None:
        score_history_changes = {
            p.original_player_index: p.score - history_before[p.original_player_index]
            for p in self.player_list
        }
        self._score_history_scores_before = {p.original_player_index: p.score for p in self.player_list}
        if player_to_score is None:
            player_to_score = {p.player_index: p.score for p in self.player_list}
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
                    score_history_changes=score_history_changes,
                    tenpai_tiles=tenpai_tiles,
                    tenpai_hands=tenpai_hands,
                    exhaustive_penalty=exhaustive_penalty,
                    nagashi_mangan_winners=nagashi_mangan_winners,
                    langyong_multiplier=langyong_multiplier,
                    langyong_scored_points=langyong_scored_points,
                    simultaneous_hu_hands=simultaneous_hu_hands,
                    skip_hand_reveal=skip_hand_reveal,
                    silent=True if silent else None,
                    next_status=next_status,
                ),
            )
            await _send_response(self, cp.player_index, response)
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
            await _send_response(self, cp.player_index, response, block=not claim_protection_enabled(self))
        except Exception as e:
            logger.error(f"riichi broadcast_declare_riichi 失败: {e}")


async def broadcast_riichi_accepted(self, player_index: int):
    """宣言牌无人荣和后才放棒；绝对分数避免客户端/重连重复扣供托。"""
    self.server_action_tick += 1
    scores = {p.player_index: p.score for p in self.player_list}
    for cp in self.player_list:
        if "offline" in cp.tag_list or cp.user_id == 0:
            continue
        if cp.user_id not in self.game_server.user_id_to_connection:
            continue
        try:
            response = Response(
                type="gamestate/riichi/riichi_accepted",
                success=True,
                message="立直成立",
                refresh_player_tag_list_info=Refresh_player_tag_list_info(
                    player_to_tag_list=_player_to_tag_list_for_viewer(self.player_list, cp.player_index),
                    riichi_accepted_player_index=player_index,
                    player_to_score=scores,
                    riichi_sticks=self.riichi_sticks,
                ),
            )
            await _send_response(self, cp.player_index, response, block=not claim_protection_enabled(self))
        except Exception as e:
            logger.error(f"riichi broadcast_riichi_accepted 失败: {e}")


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
            await _send_response(self, cp.player_index, dict_data, block=not claim_protection_enabled(self))
        except Exception as e:
            logger.error(f"riichi broadcast_update_dora 失败: {e}")


async def broadcast_game_end(self):
    from ...match.settlement import rating_result_fields
    self.server_action_tick += 1
    player_final_data = {}
    for player in self.player_list:
        player_final_data[str(player.player_index)] = Player_final_data(
            **rating_result_fields(player),
            rank=player.record_counter.rank_result,
            score=player.score,
            pt=getattr(player, 'riichi_points', 0),
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
                    record_detail=local_record_detail_for_end(self),
                ),
            )
            await _send_response(self, cp.player_index, response)
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
            await _send_response(self, cp.player_index, response)
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
            await _send_response(self, cp.player_index, response, block=not claim_protection_enabled(self))
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
            await _send_response(self, cp.player_index, response)
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
    remaining, step_sent = reconnect_clock(self, player)
    if self.game_status in ("waiting_hand_action", "onlycut_after_action"):
        if idx == self.current_player_index:
            riichi_cuts = player.riichi_candidate_cuts if "riichi_cut" in self.action_dict.get(idx, []) else None
            forbidden = list(player.kuikae_forbidden_tiles) if player.kuikae_forbidden_tiles else None
            response = Response(
                type="gamestate/riichi/broadcast_hand_action",
                success=True,
                message="发牌，并询问手牌操作",
                ask_hand_action_info=Ask_hand_action_info(
                    remaining_time=remaining,
                    step_remaining=step_sent,
                    player_index=self.current_player_index,
                    remain_tiles=max(0, len(self.tiles_list) - self.dead_wall_count),
                    action_list=self.action_dict.get(idx, []),
                    action_tick=self.server_action_tick,
                    deal_tile_type=get_hand_draw_source(self, self.current_player_index),
                    riichi_candidate_cuts=riichi_cuts,
                    forbidden_cut_tiles=forbidden,
                ),
            )
            await conn.websocket.send_json(response.dict(exclude_none=True))
    elif self.game_status in ("waiting_action_after_cut", "waiting_action_qianggang"):
        if self.action_dict.get(idx):
            robbing = self.game_status == "waiting_action_qianggang"
            cut_tile = self.jiagang_tile if robbing else self.player_list[self.current_player_index].discard_tiles[-1]
            if robbing:
                pending = getattr(self, '_pending_kan', None) or {}
                declaration = Response(type="gamestate/riichi/do_action", success=True, message="恢复抢杠询问",
                    do_action_info=Do_action_info(action_list=[pending.get('kind', 'jiagang')],
                        action_player=self.current_player_index, action_tick=self.server_action_tick,
                        cut_tile=cut_tile, is_claim=True, silent=True))
                await conn.websocket.send_json(declaration.dict(exclude_none=True))
            response = Response(
                type="gamestate/riichi/ask_other_action",
                success=True,
                message="询问操作",
                ask_other_action_info=Ask_other_action_info(
                    remaining_time=remaining,
                    step_remaining=step_sent,
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
