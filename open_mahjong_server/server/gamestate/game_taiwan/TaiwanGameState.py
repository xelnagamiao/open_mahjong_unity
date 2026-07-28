"""台湾麻将对局状态机。"""

import asyncio
import logging
import random
from dataclasses import asdict
from typing import Any, Dict, List, Optional, Set, Tuple

from ...game_calculation.hand_structure import SIXTEEN_TILE_MAHJONG
from .action_check import (
    HU_ACTIONS,
    check_action_after_cut,
    check_action_hand_action,
    check_action_jiagang,
    hu_action_for_player,
    is_forced_ready_win,
    is_kuikae_forbidden_cut,
    refresh_waiting_tiles,
    strict_kuikae_forbidden,
)
from .init_tiles import init_taiwan_tiles
from .wait_action import wait_action
from .boardcast import (
    broadcast_ask_hand_action,
    broadcast_ask_other_action,
    broadcast_do_action,
    broadcast_game_end,
    broadcast_game_start,
    broadcast_ready_status,
    broadcast_refresh_player_tag_list,
    broadcast_result,
    broadcast_switch_seat,
    reconnected_send_pending_ask,
    send_reconnect_game_state,
    send_realtime_spectator_snapshot,
)
from ..game_guobiao.combination_mask_view import (
    build_revealed_angang_masks,
)
from ..public.game_record_manager import (
    append_action_tick,
    build_score_changes_by_seat,
    build_score_changes_dict,
    capture_player_entry_order,
    end_game_record,
    init_game_record,
    init_game_round,
    player_action_record_angang,
    player_action_record_buhua,
    player_action_record_chipenggang,
    player_action_record_cut,
    player_action_record_deal,
    player_action_record_hu,
    player_action_record_jiagang,
    player_action_record_round_end,
)
from ..public.hand_action_notify import apply_player_cut
from ..public.hand_slot_utils import (
    clear_draw_slot,
    has_draw_slot,
    normalize_tile,
    pick_timeout_discard_tile,
    remove_angang_tiles,
    remove_cut_tile,
    resolve_is_mo_buhua,
    resolve_is_mo_gang,
)
from ..public.logic_common import (
    assign_competition_final_ranks,
    get_index_relative_position,
    next_current_index,
)
from ..public.next_game_round import next_game_round_classical_switchseat
from ..public.random_seed_manager import setup_random_seed_system
from ..public.round_end_timing import liuju_ready_wait_seconds
from ..public.ready_phase import run_hu_result_ready_phase as run_synced_hu_ready_phase
from ..public.vote_manager import vote_checkpoint
from ...database.fulu_utils import record_fulu_rounds_for_players
from ...game_calculation.taiwan.rules import FLOWER_TILES, STRUCTURE_TILES, TaiwanRules
from ...game_calculation.taiwan.scoring import (
    combine_settlements,
    settle_win,
    settlement_display_fan_names,
)


logger = logging.getLogger(__name__)


class RecordCounter:
    """台湾麻将牌谱与统计计数。"""

    def __init__(self) -> None:
        self.fulu_times = 0
        self.recorded_fans = []
        self.rank_result = 0
        self.zimo_times = 0
        self.dianhe_times = 0
        self.fangchong_times = 0
        self.fangchong_score = 0
        self.cuohe_times = 0
        self.win_turn = 0
        self.win_score = 0
        self.round_score_total = 0


class TaiwanPlayer:
    """台湾麻将对局玩家状态。"""

    def __init__(self, user_id: int, username: str, tiles: list, remaining_time: int):
        self.user_id = user_id
        self.username = username
        self.is_bot = user_id <= 10
        self.hand_tiles = tiles
        self.huapai_list = []
        self.discard_tiles = []
        self.discard_origin_tiles = []
        self.combination_tiles = []
        self.combination_mask = []
        self.score = 0
        self.remaining_time = remaining_time
        self.player_index = 0
        self.original_player_index = 0
        self.tag_list = []
        self.waiting_tiles = set()
        self.record_counter = RecordCounter()
        self.score_history = []
        self.round_number_history = []
        self.title_used = 0
        self.profile_used = 0
        self.character_used = 0
        self.voice_used = 0
        self.has_draw_slot = False

        self.water = False
        self.kuikae_forbidden_tiles = set()
        self.normal_draw_count = 0
        self.pre_first_draw_waiting = False
        self.heavenly_ready = False
        self.earthly_ready = False
        self.qualification_alive = False
        self.qualification_ever = False
        self.first_discard_done = False
        self.discard_count = 0
        self.last_drawn_tile = None
        self.pending_eight_flowers = False
        self.eight_flowers_declined = False
        self.declared_ready = False
        self.ready_locked = False
        self.riichi_candidate_cuts = {}
        self.last_discarded_tile = None
        self.liability_payers = {}

    def get_tile(self, tiles_list, *, mark_draw_slot: bool = True):
        tile = tiles_list.pop(0)
        self.hand_tiles.append(tile)
        if mark_draw_slot:
            self.has_draw_slot = True
        return tile


class TaiwanGameState:
    """台湾麻将标准规则状态机。"""

    def __init__(self, game_server, room_data, calculation_service, db_manager, gamestate_id):
        self.game_server = game_server
        self.calculation_service = calculation_service
        self.db_manager = db_manager
        self.gamestate_id = gamestate_id
        self.game_record = {}
        self.game_task: Optional[asyncio.Task] = None

        self.player_list: List[TaiwanPlayer] = []
        player_settings = room_data.get("player_settings", {})
        for user_id in room_data["player_list"]:
            player_setting = player_settings.get(user_id, {})
            if user_id == 0:
                username = "麻雀罗伯特"
            elif user_id == 2:
                username = "牌效罗伯特"
            else:
                username = player_setting.get("username", f"用户{user_id}")
            player = TaiwanPlayer(user_id, username, [], room_data["round_timer"])
            player.title_used = player_setting.get("title_id", 1)
            player.profile_used = player_setting.get("profile_image_id", 1)
            player.character_used = player_setting.get("character_id", 1)
            player.voice_used = player_setting.get("voice_id", 1)
            self.player_list.append(player)

        self.room_id = room_data["room_id"]
        self.tips = room_data["tips"]
        self.max_round = room_data["game_round"]
        self.step_time = room_data["step_timer"]
        self.round_time = room_data["round_timer"]
        self.room_rule = "taiwan"
        self.room_type = room_data["room_type"]
        self.sub_rule = room_data.get("sub_rule", "taiwan/standard")
        self.match_tier = room_data.get("match_tier")
        self.event_id = room_data.get("event_id")
        self.match_queue_type = room_data.get("match_queue_type")
        self.room_random_seed = room_data.get("random_seed", 0)
        self.open_cuohe = room_data.get("open_cuohe", False)
        self.cuohe_type = room_data.get("cuohe_type", 0)
        self.show_moqie_hint = room_data.get("show_moqie_hint", False)

        self.rules = TaiwanRules.from_dict(room_data.get("detailed_config"))
        self.rules_dict = asdict(self.rules)
        self.hepai_limit = self.rules.minimum_tai
        self.dead_wall_count = self.rules.dead_wall_count
        self.replacement_wall_remaining = self.rules.dead_wall_count

        self.tactical_call = False
        self.tactical_commit_lock = False
        self.claim_protection = False
        self.allow_spectator_config = room_data.get("allow_spectator", True)
        self.isPlayerSetRandomSeed = False

        self.tiles_list = []
        self.current_player_index = 0
        self.dealer_index = 0
        self.xunmu = 1
        self.master_seed = 0
        self.commitment = 0
        self.salt = ""
        self.round_random_seed = 0
        self.game_status = "waiting"
        self.server_action_tick = 0
        self.player_action_tick = 0
        self.current_round = 1
        self.round_index = 1
        self.result_dict = {}
        self.hu_class = None

        self.action_events: Dict[int, asyncio.Event] = {
            index: asyncio.Event() for index in range(4)
        }
        self.action_queues: Dict[int, asyncio.Queue] = {
            index: asyncio.Queue() for index in range(4)
        }
        self.waiting_players_list = []
        self.action_dict: Dict[int, list] = {
            index: [] for index in range(4)
        }

        self.dealer_streak = 0
        self.pending_winners: List[dict] = []
        self.pending_cuohe: Optional[dict] = None
        self._hand_scores_before: Dict[int, int] = {}
        self.jiagang_tile: Optional[int] = None
        self._pending_jiagang: Optional[dict] = None
        self.draw_reason = "exhaustive"
        self.table_claim_or_kong = False
        self.opening_dealer_action = False
        self.last_draw_was_last = False
        self.last_draw_after_kong = False
        self.supplement_win_allowed = True
        self.next_supplement_kind: Optional[str] = None
        self.pending_four_winds_abort = False
        self._announced_wind_rounds: Set[int] = set()

        self.action_priority = {
            "hu_self": 3,
            "hu_first": 3,
            "hu_second": 3,
            "hu_third": 3,
            "peng": 2,
            "gang": 2,
            "chi_left": 1,
            "chi_mid": 1,
            "chi_right": 1,
            "cut": 0,
            "angang": 0,
            "jiagang": 0,
            "buhua": 0,
            "pass": 0,
            "ready": 0,
        }

        from ..public.outbound_pipe import init_outbound_pipes
        from ..public.spectator_manager import SpectatorManager
        from ..public.spectator_rules import too_many_ai_for_spectator

        init_outbound_pipes(self)
        self.spectator_enabled = (
            self.allow_spectator_config
            and not too_many_ai_for_spectator(self.player_list)
        )
        self.spectator_manager = SpectatorManager(
            self,
            delay=180.0,
            enabled=self.spectator_enabled,
        )
        self.realtime_spectators = []
        self._reset_taiwan_players()

    async def send_to_realtime_spectators(self, player_index: int, response):
        from ..public.spectator_rules import deliver_realtime_spectator_message

        await deliver_realtime_spectator_message(self, player_index, response)

    async def player_disconnect(self, user_id: int):
        newly_offline = False
        for player in self.player_list:
            if player.user_id != user_id:
                continue
            if "offline" not in player.tag_list:
                player.tag_list.append("offline")
                newly_offline = True
                await broadcast_refresh_player_tag_list(self)
            break

        if newly_offline:
            from ..public.offline import schedule_offline_auto_on_disconnect

            schedule_offline_auto_on_disconnect(self, user_id)

        non_ai_players = [
            player for player in self.player_list
            if player.user_id >= 10
        ]
        if non_ai_players and all(
            "offline" in player.tag_list
            for player in non_ai_players
        ):
            await self.game_server.gamestate_manager.cleanup_game_state_complete(
                gamestate_id=self.gamestate_id
            )

    async def player_reconnect(self, user_id: int):
        """恢复台湾麻将玩家的完整局面与当前操作窗口。"""
        for player in self.player_list:
            if player.user_id != user_id:
                continue
            if "offline" in player.tag_list:
                player.tag_list.remove("offline")
                await broadcast_refresh_player_tag_list(self)
            if user_id in self.game_server.user_id_to_connection:
                await send_reconnect_game_state(self, player)
            break

    async def cleanup_game_state(self):
        from ..public.outbound_pipe import close_outbound_pipes

        close_outbound_pipes(self)
        await self.spectator_manager.cleanup()

        current_task = asyncio.current_task()
        if (
            self.game_task
            and not self.game_task.done()
            and self.game_task is not current_task
        ):
            self.game_task.cancel()
            try:
                await self.game_task
            except asyncio.CancelledError:
                logger.info("台湾麻将游戏循环已取消 room_id=%s", self.room_id)
            except Exception as error:
                logger.error(
                    "取消台湾麻将游戏循环出错 room_id=%s: %s",
                    self.room_id,
                    error,
                )

    async def add_spectator(self, user_id: int, connection: Any):
        await self.spectator_manager.add_spectator(user_id, connection)

    async def remove_spectator(self, user_id: int):
        await self.spectator_manager.remove_spectator(user_id)

    async def run_game_loop(self):
        try:
            await self.game_loop_chinese()
        except asyncio.CancelledError:
            logger.info(
                "台湾麻将游戏循环被取消 room_id=%s, gamestate_id=%s",
                self.room_id,
                self.gamestate_id,
            )
            raise
        except Exception as error:
            logger.error(
                "台湾麻将游戏循环发生未捕获异常 room_id=%s, gamestate_id=%s: %s",
                self.room_id,
                self.gamestate_id,
                error,
                exc_info=True,
            )
            try:
                await self.cleanup_game_state()
            except Exception as cleanup_error:
                logger.error(
                    "清理台湾麻将游戏状态出错 room_id=%s: %s",
                    self.room_id,
                    cleanup_error,
                    exc_info=True,
                )

    # ------------------------------------------------------------------
    # 每手状态、计算上下文与可复现日志
    # ------------------------------------------------------------------

    def _reset_taiwan_players(self) -> None:
        for player in self.player_list:
            player.water = False
            player.kuikae_forbidden_tiles = set()
            player.normal_draw_count = 0
            player.pre_first_draw_waiting = False
            player.heavenly_ready = False
            player.earthly_ready = False
            player.qualification_alive = False
            player.qualification_ever = False
            player.first_discard_done = False
            player.discard_count = 0
            player.last_drawn_tile = None
            player.pending_eight_flowers = False
            player.eight_flowers_declined = False
            player.has_draw_slot = False
            player.declared_ready = False
            player.ready_locked = False
            player.riichi_candidate_cuts = {}
            player.last_discarded_tile = None
            player.liability_payers = {}
            if "declared_ready" in player.tag_list:
                player.tag_list.remove("declared_ready")

    def _reset_hand_runtime(self) -> None:
        self.pending_winners = []
        self.pending_cuohe = None
        self.jiagang_tile = None
        self._pending_jiagang = None
        self.result_dict = {}
        self.hu_class = None
        self.draw_reason = "exhaustive"
        self.table_claim_or_kong = False
        self.opening_dealer_action = False
        self.last_draw_was_last = False
        self.last_draw_after_kong = False
        self.supplement_win_allowed = True
        self.next_supplement_kind = None
        self.dead_wall_count = self.rules.dead_wall_count
        self.replacement_wall_remaining = self.rules.dead_wall_count
        self.pending_four_winds_abort = False
        self._reset_taiwan_players()

    def can_take_normal_tile(self) -> bool:
        return len(self.tiles_list) > self.dead_wall_count

    def can_take_supplement_tile(self) -> bool:
        mode = getattr(getattr(self, "rules", None), "dead_wall_mode", "fixed_tail_16")
        if mode == "fixed_replacement_wall_16":
            remaining = getattr(self, "replacement_wall_remaining", self.dead_wall_count)
            return remaining > 0 and bool(self.tiles_list)
        return len(self.tiles_list) > self.dead_wall_count

    def can_establish_kong(self) -> bool:
        """判断现在成立一杠后是否仍有合法补牌。"""
        if self.rules.four_kongs_abort and self._kong_count() >= 3:
            return True
        if self.rules.dead_wall_mode == "kong_expands_tail":
            return len(self.tiles_list) > self.dead_wall_count + 1
        return self.can_take_supplement_tile()

    def _can_establish_kong_for_action(self) -> bool:
        """检查当前是否允许成立杠牌。"""

        if not hasattr(self, "tiles_list"):
            return True
        return self.can_establish_kong()

    def can_take_wall_tile(self) -> bool:
        return self.can_take_normal_tile()

    def playable_wall_count(self) -> int:
        return max(0, len(self.tiles_list) - self.dead_wall_count)

    def _take_supplement_tile(self) -> int:
        tile = self.tiles_list.pop(-1)
        mode = getattr(getattr(self, "rules", None), "dead_wall_mode", "fixed_tail_16")
        if mode == "fixed_replacement_wall_16":
            remaining = getattr(self, "replacement_wall_remaining", self.dead_wall_count)
            self.replacement_wall_remaining = max(0, remaining - 1)
            if len(self.tiles_list) > self.replacement_wall_remaining:
                self.replacement_wall_remaining += 1
            self.dead_wall_count = self.replacement_wall_remaining
        return tile

    def _kong_count(self) -> int:
        return sum(
            1
            for player in self.player_list
            for code in player.combination_tiles
            if code and code[0] in ("g", "G")
        )

    def expected_playable_wall_after_kong(self, additional_kongs: int = 1) -> int:
        """估算完成一次杠后剩余的可摸牌墙数量。"""

        if type(additional_kongs) is not int or additional_kongs < 1:
            raise ValueError("additional_kongs must be a positive integer")

        try:
            before = max(0, int(self.playable_wall_count()))
        except (AttributeError, TypeError, ValueError):
            before = 0
        try:
            wall_length = len(self.tiles_list)
            dead_wall = int(self.dead_wall_count)
        except (AttributeError, TypeError, ValueError):
            if self.rules.dead_wall_mode == "kong_expands_tail":
                remaining = before
                established_kongs = self._kong_count()
                for _ in range(additional_kongs):
                    if self.rules.four_kongs_abort and established_kongs >= 3:
                        break
                    cost = 2 if remaining > 1 else (1 if remaining else 0)
                    remaining = max(0, remaining - cost)
                    established_kongs += 1
                return remaining
            supplement_checker = getattr(self, "can_take_supplement_tile", None)
            if callable(supplement_checker):
                remaining = before
                established_kongs = self._kong_count()
                for _ in range(additional_kongs):
                    if self.rules.four_kongs_abort and established_kongs >= 3:
                        break
                    if not supplement_checker():
                        break
                    remaining = max(0, remaining - 1)
                    established_kongs += 1
                return remaining
            return max(0, before - min(additional_kongs, before))

        if self.rules.four_kongs_abort and self._kong_count() >= 3:
            return before

        mode = self.rules.dead_wall_mode
        replacement_remaining = int(
            getattr(self, "replacement_wall_remaining", dead_wall)
        )
        established_kongs = self._kong_count()
        for _ in range(additional_kongs):
            if self.rules.four_kongs_abort and established_kongs >= 3:
                break
            if mode == "kong_expands_tail":
                dead_wall += 1
                if wall_length > dead_wall:
                    wall_length -= 1
            elif mode == "fixed_replacement_wall_16":
                if replacement_remaining > 0 and wall_length:
                    wall_length -= 1
                    replacement_remaining = max(0, replacement_remaining - 1)
                    if wall_length > replacement_remaining:
                        replacement_remaining += 1
                    dead_wall = replacement_remaining
            else:
                if wall_length > dead_wall:
                    wall_length -= 1
            established_kongs += 1

        return max(0, wall_length - dead_wall)

    def can_keep_wall_after_kong(self) -> bool:
        """检查当前杠牌后是否有合法补牌。"""

        reserve = self.rules.required_claim_wall_reserve
        return (
            reserve <= 0
            or self.expected_playable_wall_after_kong() >= reserve
        )

    def _on_kong_established(self) -> bool:
        """检查是否已按四杠散了结束本手。"""
        if self.rules.four_kongs_abort and self._kong_count() >= 4:
            self.draw_reason = "four_kongs_abort"
            self.game_status = "END"
            return True
        if self.rules.dead_wall_mode == "kong_expands_tail":
            self.dead_wall_count += 1
        return False

    def _round_wind(self) -> int:
        return 41 + min(3, (self.current_round - 1) // 4)

    def _has_table_concealed_kong(self) -> bool:
        return any(
            any(
                combination.startswith("G")
                for combination in getattr(player, "combination_tiles", ())
            )
            for player in self.player_list
        )

    def _record_ready_state(self, player_index: int) -> None:
        round_data = (
            getattr(self, "game_record", {})
            .get("game_round", {})
            .get(f"round_index_{getattr(self, 'round_index', None)}")
        )
        if not isinstance(round_data, dict):
            return
        player = self.player_list[player_index]
        qualification = "none"
        if player.qualification_alive:
            if player.heavenly_ready:
                qualification = "heavenly"
            elif player.earthly_ready:
                qualification = "earthly"
        if qualification == "none" and player.declared_ready:
            qualification = "public"
        self._record_state(
            "ready",
            player_index,
            qualification,
            "T" if player.declared_ready else "F",
        )

    def _record_water_state(self, player_index: int) -> None:
        round_data = (
            getattr(self, "game_record", {})
            .get("game_round", {})
            .get(f"round_index_{getattr(self, 'round_index', None)}")
        )
        if not isinstance(round_data, dict):
            return
        self._record_state(
            "water",
            player_index,
            "T" if self.player_list[player_index].water else "F",
        )

    def _score_context(
        self,
        player_index: int,
        source: str,
        *,
        include_special: bool,
        flowers_override: Optional[List[int]] = None,
        opening_flower_win: bool = False,
    ) -> dict:
        player = self.player_list[player_index]
        is_self = source == "self_draw"
        table_concealed_kong = self._has_table_concealed_kong()
        opening_flower_timing = bool(
            opening_flower_win
            and not self.table_claim_or_kong
            and not any(item.discard_count for item in self.player_list)
        )
        current_player_index = getattr(self, "current_player_index", -1)
        discarder_first_cut = (
            0 <= current_player_index < len(self.player_list)
            and self.player_list[current_player_index].discard_count == 1
        )
        context = {
            "rules": self.rules_dict,
            "win_source": source,
            "flowers": list(
                player.huapai_list
                if flowers_override is None
                else flowers_override
            ),
            "seat_wind": 41 + player_index,
            "round_wind": self._round_wind(),
            "out_with_replacement_tile": bool(self.last_draw_after_kong),
            "last_tile": bool(is_self and self.last_draw_was_last),
            "last_tile_claim": bool(source == "discard" and not self.can_take_wall_tile()),
            "heavenly_ready": bool(player.qualification_alive and player.heavenly_ready),
            "earthly_ready": bool(player.qualification_alive and player.earthly_ready),
            "declared_ready": bool(getattr(player, "declared_ready", False)),
            "heavenly_win": bool(
                (
                    is_self
                    and player_index == 0
                    and self.opening_dealer_action
                    and not self.table_claim_or_kong
                )
                or (opening_flower_timing and player_index == 0)
            ),
            "earthly_win": bool(
                (
                    is_self
                    and player_index != 0
                    and player.normal_draw_count == 1
                    and player.discard_count == 0
                    and (
                        not self.table_claim_or_kong
                        or (
                            self.rules.earthly_win_allows_open_calls
                            and not table_concealed_kong
                        )
                    )
                )
                or (opening_flower_timing and player_index != 0)
            ),
            "human_win": bool(
                source == "discard"
                and self.rules.human_win_definition != "disabled"
                and (
                    discarder_first_cut
                    if self.rules.human_win_definition == "discarder_first_discard"
                    else (
                        player_index != 0
                        and player.normal_draw_count == 0
                        and not self.table_claim_or_kong
                    )
                )
            ),
            "eight_flowers_declined": bool(player.eight_flowers_declined),
        }
        if include_special and player.pending_eight_flowers:
            context["eight_flowers_and_seasons"] = True
        return context

    def score_candidate(
        self,
        player_index: int,
        source: str,
        tile: Optional[int] = None,
        *,
        include_special: bool = True,
        allow_below_minimum: bool = False,
        flowers_override: Optional[List[int]] = None,
        opening_flower_win: bool = False,
    ) -> Optional[dict]:
        player = self.player_list[player_index]
        final_hand = list(player.hand_tiles)
        if source in ("discard", "robbing_kong"):
            if tile is None:
                return None
            final_hand.append(tile)
            winning_tile = tile
        else:
            winning_tile = tile or player.last_drawn_tile or (final_hand[-1] if final_hand else None)

        if winning_tile is None and not (include_special and player.pending_eight_flowers):
            return None
        contexts = []
        if include_special and player.pending_eight_flowers:
            contexts.append(
                self._score_context(
                    player_index,
                    source,
                    include_special=True,
                    flowers_override=flowers_override,
                    opening_flower_win=opening_flower_win,
                )
            )
        contexts.append(
            self._score_context(
                player_index,
                source,
                include_special=False,
                flowers_override=flowers_override,
                opening_flower_win=opening_flower_win,
            )
        )

        candidates = []
        below_minimum_candidates = []
        for context in contexts:
            detail = self.calculation_service.Taiwan_hepai_detail(
                final_hand,
                player.combination_tiles,
                [],
                winning_tile or 11,
                context,
            )
            if detail.get("is_win"):
                candidates.append(detail)
            elif (
                allow_below_minimum
                and self._is_cuohe_detail(detail)
            ):
                below_minimum_candidates.append(detail)
        if not candidates:
            candidates = below_minimum_candidates
        if not candidates:
            return None
        return max(
            candidates,
            key=lambda detail: (
                # 待处理的独立花胡必须优先于普通牌形；放弃后 pending 会清除，
                # 随后的正常动作窗口仍可单独选择普通胡牌。
                1 if detail.get("special") else 0,
                detail.get("capped_tai", 0),
                detail.get("tai", 0),
                len(detail.get("fan_ids", [])),
            ),
        )

    def _is_cuohe_detail(self, detail: Optional[dict]) -> bool:
        minimum_tai = int(getattr(self, "hepai_limit", 0) or 0)
        return bool(
            detail
            and getattr(self, "open_cuohe", False)
            and minimum_tai > 0
            and not detail.get("is_win", False)
            and int(detail.get("tai", 0)) < minimum_tai
            and detail.get("below_minimum", False)
        )

    def score_action_candidate(
        self,
        player_index: int,
        source: str,
        tile: Optional[int] = None,
    ) -> Optional[dict]:
        """胡牌按钮可额外包含已成牌但未达到最低台数的错和候选。"""
        return self.score_candidate(
            player_index,
            source,
            tile,
            allow_below_minimum=True,
        )

    def has_normal_self_draw(self, player_index: int) -> bool:
        if "peida" in self.player_list[player_index].tag_list:
            return False
        if self.player_list[player_index].water and self.rules.missed_win_blocks_self_draw:
            return False
        if not self.supplement_win_allowed:
            return False
        return self.score_candidate(player_index, "self_draw", include_special=False) is not None

    @staticmethod
    def _meld_structure_tiles(code: str) -> List[int]:
        if not code or len(code) < 2:
            return []
        try:
            tile = normalize_tile(int(code[1:]))
        except (TypeError, ValueError):
            return []
        if code[0] == "s":
            return [tile - 1, tile, tile + 1]
        return [tile, tile, tile]

    def _liability_fan_ids_for_codes(self, codes: List[str]) -> Set[str]:
        """返回给定公开组合已经达到责任门槛的候选台种。"""
        candidates: Set[str] = set()
        twelve_tile_meld_count = SIXTEEN_TILE_MAHJONG.meld_count - 1
        if len(codes) >= twelve_tile_meld_count:
            meld_tiles = [
                value
                for code in codes
                for value in self._meld_structure_tiles(code)
            ]
            number_suits = {value // 10 for value in meld_tiles if value < 40}
            has_honors = any(value >= 40 for value in meld_tiles)
            if len(number_suits) == 1 and not has_honors:
                candidates.add("full_flush")
            if meld_tiles and len(number_suits) <= 1:
                candidates.add("half_flush")
            if meld_tiles and all(value >= 40 for value in meld_tiles):
                candidates.add("all_honors")
            if all(code[0] in ("k", "g") for code in codes):
                candidates.add("all_pungs")
        triplets = {
            normalize_tile(int(code[1:]))
            for code in codes
            if code[0] in ("k", "g") and code[1:].isdigit()
        }
        wind_triplet_count = len(triplets.intersection((41, 42, 43, 44)))
        dragon_triplet_count = len(triplets.intersection((45, 46, 47)))
        if wind_triplet_count >= 3:
            candidates.add("little_four_winds")
        if wind_triplet_count >= 4:
            candidates.add("big_four_winds")
        if dragon_triplet_count >= 2:
            candidates.add("little_three_dragons")
        if dragon_triplet_count >= 3:
            candidates.add("big_three_dragons")
        return candidates

    def _liability_candidate_fan_ids(
        self,
        player_index: int,
    ) -> Set[str]:
        """只返回由刚落地的最后一副公开鸣牌新触发的候选台种。"""
        all_codes = [code for code in self.player_list[player_index].combination_tiles if code]
        external_codes = [code for code in all_codes if code[0] != "G"]
        if not external_codes:
            return set()
        candidates = (
            self._liability_fan_ids_for_codes(external_codes)
            - self._liability_fan_ids_for_codes(external_codes[:-1])
        )
        if all_codes and all_codes[-1][0] == "g":
            kong_count = sum(code[0] in ("g", "G") for code in all_codes)
            previous_kong_count = sum(
                code[0] in ("g", "G")
                for code in all_codes[:-1]
            )
            if kong_count >= 4 and previous_kong_count < 4:
                candidates.add("four_kongs")
            if kong_count >= 5 and previous_kong_count < 5:
                candidates.add("five_kongs")
        return candidates

    @staticmethod
    def _detail_fan_ids(detail: Optional[dict]) -> Set[str]:
        if not detail:
            return set()
        fan_ids = {
            fan_id
            for fan_id in (detail.get("fan_ids") or ())
            if isinstance(fan_id, str)
        }
        for fan in (detail.get("fan_detail") or ()):
            if isinstance(fan, dict) and isinstance(fan.get("id"), str):
                fan_ids.add(fan["id"])
        return fan_ids

    def _liability_payer_for_win(
        self,
        winner: int,
        source: str,
        payer: Optional[int],
        tile: Optional[int],
        detail: Optional[dict] = None,
    ) -> Optional[int]:
        if source not in ("self_draw", "discard", "robbing_kong"):
            return None
        player = self.player_list[winner]
        fan_ids = self._detail_fan_ids(detail)
        persisted = getattr(player, "liability_payers", {})
        if not isinstance(persisted, dict):
            return None
        for fan_id in self.rules.liability_fan_ids:
            if fan_id in fan_ids and fan_id in persisted:
                return persisted[fan_id]
        return None

    def _remember_claim_liability(self, player_index: int, payer: int, tile: int) -> None:
        player = self.player_list[player_index]
        persisted = getattr(player, "liability_payers", None)
        if not isinstance(persisted, dict):
            persisted = {}
            player.liability_payers = persisted
        for fan_id in self._liability_candidate_fan_ids(player_index):
            if self.rules.liability_enabled_for_fan(fan_id):
                persisted[fan_id] = payer

    def _special_flower_detail(
        self,
        player_index: int,
        seven_flowers_steal_eighth: bool = False,
        *,
        opening: bool = False,
    ) -> dict:
        player = self.player_list[player_index]
        context = self._score_context(
            player_index,
            "seven_flowers_steal_eighth" if seven_flowers_steal_eighth else "self_draw",
            include_special=False,
            opening_flower_win=opening,
        )
        context["seven_flowers_steal_eighth" if seven_flowers_steal_eighth else "eight_flowers_and_seasons"] = True
        return self.calculation_service.Taiwan_hepai_detail(
            list(player.hand_tiles),
            player.combination_tiles,
            [],
            player.last_drawn_tile or (player.hand_tiles[-1] if player.hand_tiles else 11),
            context,
        )

    def _combine_flower_details(self, normal: Optional[dict], flower: dict) -> dict:
        if not normal:
            result = dict(flower)
        else:
            result = dict(normal)
            result["tai"] = normal["tai"] + flower["tai"]
            for key in ("fan_ids", "fan_names", "fan_detail"):
                result[key] = list(normal.get(key, [])) + list(flower.get(key, []))
            result["decomposition"] = list(normal.get("decomposition", [])) + [
                "special:seven_flowers_steal_eighth"
            ]
            result["special"] = "seven_flowers_steal_eighth"

        minimum_tai = int(
            getattr(self, "hepai_limit", self.rules.minimum_tai) or 0
        )
        result["is_win"] = result["tai"] >= minimum_tai
        result["below_minimum"] = not result["is_win"]
        result["reason"] = (
            ""
            if result["is_win"]
            else f"未达到最低 {minimum_tai} 台"
        )
        result["capped_tai"] = (
            min(result["tai"], self.rules.tai_cap)
            if self.rules.tai_cap
            else result["tai"]
        )
        return result

    # ------------------------------------------------------------------
    # 过水、天地听与花胡
    # ------------------------------------------------------------------

    def _revoke_qualification(
        self,
        player_index: int,
        *,
        keep_declared: bool = False,
    ) -> bool:
        player = self.player_list[player_index]
        was_declared = bool(getattr(player, "declared_ready", False))
        if not player.qualification_alive and not was_declared:
            return False
        player.qualification_alive = False
        if was_declared and not keep_declared:
            player.declared_ready = False
            player.ready_locked = False
            if "declared_ready" in player.tag_list:
                player.tag_list.remove("declared_ready")
        self._record_ready_state(player_index)
        return was_declared and not keep_declared

    def _break_heavenly_earthly_ready_for_concealed_kong(self) -> None:
        for player_index in range(len(self.player_list)):
            if self.player_list[player_index].qualification_alive:
                self._revoke_qualification(player_index, keep_declared=True)

    def enter_water(self, player_index: int) -> bool:
        player = self.player_list[player_index]
        if not player.water:
            player.water = True
            self._record_water_state(player_index)
        if (
            self.rules.qualified_ready_win_policy == "lose_earthly_on_pass"
            and player.qualification_alive
            and player.earthly_ready
        ):
            player.qualification_alive = False
            player.earthly_ready = False
            self._record_ready_state(player_index)
        if (
            getattr(player, "declared_ready", False)
            and self.rules.declared_ready_win_policy == "allow_pass"
        ):
            # 允许拒胡仅产生正常过水；公开声明、资格与摸切锁定继续有效。
            return False
        return self._revoke_qualification(player_index)

    def _clear_water(self, player_index: int) -> None:
        player = self.player_list[player_index]
        if player.water:
            player.water = False
            self._record_water_state(player_index)

    def _discard_may_win(self, player_index: int, discarded_tile: int) -> bool:
        return self._tile_is_legal_win_without_water(
            player_index,
            "discard",
            discarded_tile,
        )

    def _kong_fourth_may_win(self, player_index: int, tile: int) -> bool:
        """判断杠所用的第四张牌本身是否原本就是合法胡牌张。"""

        player = self.player_list[player_index]
        normal = normalize_tile(tile)
        if not any(
            normalize_tile(hand_tile) == normal
            for hand_tile in player.hand_tiles
        ):
            return False
        return self._tile_is_legal_win_without_water(
            player_index,
            "self_draw",
            normal,
        )

    def _tile_is_legal_win_without_water(
        self,
        player_index: int,
        source: str,
        tile: int,
    ) -> bool:
        """判断移除过水限制后，该牌是否仍是完整合法和牌。"""

        player = self.player_list[player_index]
        was_water = bool(getattr(player, "water", False))
        player.water = False
        try:
            detail = self.score_candidate(
                player_index,
                source,
                tile,
                include_special=False,
            )
        finally:
            player.water = was_water
        return bool(detail and detail.get("is_win", False))

    def _is_four_winds_abort(self) -> bool:
        if not self.rules.four_winds_abort or self.table_claim_or_kong:
            return False
        if any(player.discard_count != 1 or not player.discard_tiles for player in self.player_list):
            return False
        first_tiles = [normalize_tile(player.discard_tiles[0]) for player in self.player_list]
        return first_tiles[0] in (41, 42, 43, 44) and len(set(first_tiles)) == 1

    def _finish_four_winds_abort(self) -> None:
        self.pending_four_winds_abort = False
        self.draw_reason = "four_winds_abort"
        self.game_status = "END"

    def _register_initial_heavenly_ready(self) -> None:
        if self.rules.ready_qualification_mode not in (
            "standard_with_dealer_heavenly_ready",
            "standard_without_dealer_heavenly_ready",
        ):
            return
        for index in (1, 2, 3):
            player = self.player_list[index]
            if refresh_waiting_tiles(self, index):
                player.heavenly_ready = True
                player.qualification_alive = True
                player.qualification_ever = True
                self._record_ready_state(index)

    def ready_candidate_cuts(self, player_index: int) -> Dict[int, List[int]]:
        """返回公开报听可选的弃牌及对应等待集合。"""
        player = self.player_list[player_index]
        table_claim_or_kong = bool(getattr(self, "table_claim_or_kong", False))
        dealer_opening_cut = (
            player_index == 0
            and player.discard_count == 0
            and player.normal_draw_count == 0
        )
        first_normal_draw_cut = (
            player.normal_draw_count == 1
            and player.discard_count == (1 if player_index == 0 else 0)
            and not player.pre_first_draw_waiting
            and not player.qualification_ever
        )
        total_discards = sum(item.discard_count for item in self.player_list)
        ready_mode = self.rules.ready_qualification_mode
        first_eight_discards_window = (
            ready_mode == "first_eight_table_discards"
            and not table_claim_or_kong
            and not player.qualification_ever
            and total_discards < 8
        )
        first_discard_window = (
            ready_mode == "each_player_first_discard"
            and not player.qualification_ever
            and player.discard_count == 0
            and not player.combination_tiles
            and not self._has_table_concealed_kong()
        )
        standard_window = (
            ready_mode in ("standard_with_dealer_heavenly_ready", "standard_without_dealer_heavenly_ready")
            and not table_claim_or_kong
            and (
                (dealer_opening_cut and ready_mode == "standard_with_dealer_heavenly_ready")
                or first_normal_draw_cut
            )
        )

        # 庄家的开局完整手牌没有普通摸牌槽，但第一打仍是合法的天听声明时点。
        if player.ready_locked or (not has_draw_slot(player) and not dealer_opening_cut):
            return {}

        qualification_window = (
            ready_mode != "disabled"
            and self.rules.public_ready_enabled
            and (
                player.qualification_alive
                or first_eight_discards_window
                or first_discard_window
                or standard_window
            )
        )
        if not qualification_window and not self.rules.public_ready_enabled:
            return {}

        candidates: Dict[int, List[int]] = {}
        for tile in dict.fromkeys(player.hand_tiles):
            if tile in FLOWER_TILES:
                continue
            hand_after_cut = list(player.hand_tiles)
            hand_after_cut.remove(tile)
            waits = self.calculation_service.Taiwan_tingpai_check(
                hand_after_cut,
                player.combination_tiles,
                self.rules_dict,
            )
            if waits:
                candidates[tile] = sorted(waits)
        return candidates

    def taiwan_hint_ready_qualification(self, player_index: int) -> str:
        """返回当前动作提示应采用的私有听牌资格。"""
        player = self.player_list[player_index]
        ready_mode = self.rules.ready_qualification_mode
        if ready_mode != "disabled":
            if player.qualification_alive:
                if player.heavenly_ready:
                    return "heavenly"
                if player.earthly_ready:
                    return "earthly"

            if not player.qualification_ever:
                dealer_opening_cut = (
                    player_index == 0
                    and player.discard_count == 0
                    and player.normal_draw_count == 0
                )
                first_normal_draw_cut = (
                    player.normal_draw_count == 1
                    and player.discard_count == (1 if player_index == 0 else 0)
                    and not player.pre_first_draw_waiting
                )
                if ready_mode == "first_eight_table_discards":
                    if not self.table_claim_or_kong:
                        if dealer_opening_cut:
                            return "heavenly"
                        if sum(item.discard_count for item in self.player_list) < 8:
                            return "earthly"
                    return (
                        "public"
                        if player.declared_ready and self.rules.public_ready_enabled
                        else "none"
                    )
                if ready_mode == "each_player_first_discard":
                    if (
                        player.discard_count == 0
                        and not player.combination_tiles
                        and not self._has_table_concealed_kong()
                    ):
                        return "earthly"
                    return (
                        "public"
                        if player.declared_ready and self.rules.public_ready_enabled
                        else "none"
                    )
                if ready_mode in ("standard_with_dealer_heavenly_ready", "standard_without_dealer_heavenly_ready"):
                    if self.table_claim_or_kong:
                        return (
                            "public"
                            if player.declared_ready and self.rules.public_ready_enabled
                            else "none"
                        )
                    if dealer_opening_cut and ready_mode == "standard_with_dealer_heavenly_ready":
                        return "heavenly"
                    if first_normal_draw_cut:
                        return "earthly"

        if player.declared_ready and self.rules.public_ready_enabled:
            return "public"
        return "none"

    def build_private_hand_action_info(self, player_index: int) -> dict:
        """返回当前行动视角所需的台湾提示元数据。"""
        info = {
            "ready_qualification": self.taiwan_hint_ready_qualification(player_index),
        }
        actions = getattr(self, "action_dict", {}).get(player_index, [])
        if "riichi_cut" in actions:
            info["riichi_candidate_cuts"] = getattr(
                self.player_list[player_index],
                "riichi_candidate_cuts",
                {},
            )
        return info

    def build_game_info_fields(self) -> dict:
        """返回台湾规则附加到开局与重连信息的公共字段。"""
        return {"detailed_config": self.rules_dict}

    def build_record_title_fields(self) -> dict:
        """返回台湾规则写入牌谱与延时观战表头的字段。"""
        return {"detailed_config": self.rules_dict}

    def _record_state(self, state_type: str, *payload) -> None:
        append_action_tick(self, ["state", state_type, *payload])

    def _record_buhua(
        self,
        tile: int,
        action_player: int,
        is_drawn: bool,
        recipient: Optional[int],
        transfer_from: Optional[int],
        transfer_tile: Optional[int],
    ) -> None:
        if recipient is None and transfer_from is None and transfer_tile is None:
            player_action_record_buhua(self, tile, action_player, is_drawn)
            return
        append_action_tick(self, [
            "bh",
            tile,
            action_player,
            "T" if is_drawn else "F",
            action_player if recipient is None else recipient,
            "" if transfer_from is None else transfer_from,
            "" if transfer_tile is None else transfer_tile,
        ])

    def _record_liuju(self, reason: Optional[str]) -> None:
        tick = ["liuju"]
        if reason:
            tick.append(reason)
        append_action_tick(self, tick)

    def build_private_do_action_info(self, action_player: int, viewer_index: int) -> dict:
        """返回动作完成后仅动作本人可见的台湾提示元数据。"""
        if viewer_index != action_player:
            return {}
        player = self.player_list[action_player]
        qualification = "none"
        if player.qualification_alive:
            if player.heavenly_ready:
                qualification = "heavenly"
            elif player.earthly_ready:
                qualification = "earthly"
        if qualification == "none" and player.declared_ready:
            qualification = "public"
        return {"ready_qualification": qualification}

    def _declare_ready(self, player_index: int) -> None:
        player = self.player_list[player_index]
        player.declared_ready = True
        player.ready_locked = True
        if "declared_ready" not in player.tag_list:
            player.tag_list.append("declared_ready")
        self._record_ready_state(player_index)

    def _register_ready_after_discard(self, player_index: int) -> None:
        ready_mode = self.rules.ready_qualification_mode
        if ready_mode == "disabled":
            return
        player = self.player_list[player_index]
        waits = refresh_waiting_tiles(self, player_index)
        if not waits or player.qualification_ever:
            return
        if ready_mode == "first_eight_table_discards":
            if self.table_claim_or_kong or sum(item.discard_count for item in self.player_list) > 8:
                return
            if player_index == 0 and player.discard_count == 1:
                player.heavenly_ready = True
            else:
                player.earthly_ready = True
            player.qualification_alive = True
            player.qualification_ever = True
            self._record_ready_state(player_index)
            return
        if ready_mode == "each_player_first_discard":
            if (
                player.discard_count != 1
                or player.combination_tiles
                or self._has_table_concealed_kong()
            ):
                return
            player.earthly_ready = True
            player.qualification_alive = True
            player.qualification_ever = True
            self._record_ready_state(player_index)
            return
        if ready_mode not in ("standard_with_dealer_heavenly_ready", "standard_without_dealer_heavenly_ready"):
            return
        if self.table_claim_or_kong:
            return
        if (
            player_index == 0
            and player.discard_count == 1
            and player.normal_draw_count == 0
            and ready_mode == "standard_with_dealer_heavenly_ready"
        ):
            player.heavenly_ready = True
            player.qualification_alive = True
            player.qualification_ever = True
            self._record_ready_state(player_index)
        elif (
            player.normal_draw_count == 1
            and not player.pre_first_draw_waiting
            and player.discard_count == (2 if player_index == 0 else 1)
        ):
            player.earthly_ready = True
            player.qualification_alive = True
            player.qualification_ever = True
            self._record_ready_state(player_index)

    def _finalize_ready_after_discard(self, player_index: int, declare_ready: bool) -> None:
        self._register_ready_after_discard(player_index)
        player = self.player_list[player_index]
        if declare_ready:
            self._declare_ready(player_index)
        elif (
            self.rules.public_ready_enabled
            and player.qualification_alive
            and not player.declared_ready
        ):
            # 公开模式必须在资格产生的当次动作确认；跳过后不能延后补报。
            self._revoke_qualification(player_index)

    def decline_eight_flowers(self, player_index: int) -> None:
        player = self.player_list[player_index]
        if player.pending_eight_flowers:
            player.pending_eight_flowers = False
            player.eight_flowers_declined = True

    async def _ask_eight_flowers(
        self,
        player_index: int,
        *,
        opening: bool = False,
    ) -> None:
        player = self.player_list[player_index]
        if not player.pending_eight_flowers or self.game_status == "END":
            return
        if getattr(player, "user_id", None) == 0:
            # 摸切机器人只负责推进牌局，所有和牌机会均放弃。
            self.decline_eight_flowers(player_index)
            return
        if self.rules.eight_flowers_mode == "forced_standalone":
            self.result_dict["hu_self"] = self._special_flower_detail(
                player_index,
                opening=opening,
            )
            self.accept_self_draw(player_index)
            return
        if self.rules.eight_flowers_mode == "compound":
            self.result_dict["hu_self"] = self.score_candidate(
                player_index,
                "self_draw",
                opening_flower_win=opening,
            )
            self.accept_self_draw(player_index)
            return
        if self.rules.eight_flowers_mode != "optional_standalone":
            player.pending_eight_flowers = False
            return
        self.current_player_index = player_index
        self.action_dict = {0: [], 1: [], 2: [], 3: []}
        self.action_dict[player_index] = ["hu_flower", "pass"]
        # 独立花胡选择必须固定结算八仙，不能与同时成立的普通牌形择高。
        self.result_dict["hu_self"] = self._special_flower_detail(
            player_index,
            opening=opening,
        )
        self.game_status = "waiting_flower_choice"
        self.prepare_action_window()
        await broadcast_ask_hand_action(self)
        if await self.wait_action():
            self.accept_self_draw(player_index)
        else:
            self.decline_eight_flowers(player_index)

    # ------------------------------------------------------------------
    # 花牌与统一牌墙
    # ------------------------------------------------------------------

    def _seven_flowers_steal_eighth_candidate(self, owner_index: int, tile: int) -> Optional[dict]:
        """检查本次补花是否产生七抢一机会，不提前改变花牌归属。"""

        if not self.rules.seven_flowers_steal_eighth_enabled:
            return None
        public_before = {
            flower
            for player in self.player_list
            for flower in player.huapai_list
        }

        # A 七花，B 补出唯一剩余花。
        for robber_index, robber in enumerate(self.player_list):
            if robber_index == owner_index:
                continue
            if len(set(robber.huapai_list)) == 7 and tile not in robber.huapai_list:
                return {
                    "winner": robber_index,
                    "payer": owner_index,
                    "tile": tile,
                    "mode": "seven_then_last",
                }

        # A 六花、B 一花；A 补出桌上唯一未出现花后，可抢走 B 的原花。
        owner = self.player_list[owner_index]
        if (
            len(set(owner.huapai_list)) == 6
            and tile not in public_before
            and len(public_before) == 7
        ):
            donors = [
                (idx, flower)
                for idx, player in enumerate(self.player_list)
                if idx != owner_index
                for flower in player.huapai_list
            ]
            if len(donors) == 1:
                donor_index, stolen = donors[0]
                return {
                    "winner": owner_index,
                    "payer": donor_index,
                    "tile": tile,
                    "stolen": stolen,
                    "mode": "six_plus_one",
                }
        return None

    def _publish_flower(self, owner_index: int, tile: int) -> None:
        """先按普通补花将花牌公开到行动者的花牌区。"""

        self.player_list[owner_index].huapai_list.append(tile)

    @staticmethod
    def _flower_win_transfer(special: dict) -> Tuple[int, int]:
        if special["mode"] == "seven_then_last":
            return special["payer"], special["tile"]
        return special["payer"], special["stolen"]

    def _transfer_flower_win(self, special: dict) -> int:
        """七抢一确认后，转移已经公开在桌面的花牌。"""

        winner = special["winner"]
        source, tile = self._flower_win_transfer(special)
        self.player_list[source].huapai_list.remove(tile)
        self.player_list[winner].huapai_list.append(tile)
        return winner

    def _supplement_draw_will_exhaust_normal_wall(self) -> bool:
        """预览取走当前牌尾补牌后，普通牌墙是否随即耗尽。"""

        wall_length = max(0, len(self.tiles_list) - 1)
        dead_wall_count = self.dead_wall_count
        if self.rules.dead_wall_mode == "fixed_replacement_wall_16":
            replacement_remaining = max(
                0,
                getattr(
                    self,
                    "replacement_wall_remaining",
                    dead_wall_count,
                ) - 1,
            )
            if wall_length > replacement_remaining:
                replacement_remaining += 1
            dead_wall_count = replacement_remaining
        return wall_length <= dead_wall_count

    def _seven_flowers_steal_eighth_details(
        self,
        info: dict,
        replacement_tile: int,
        *,
        opening: bool,
        transfer_completed: bool,
    ) -> Tuple[Optional[dict], dict, dict]:
        """按最低台数为零取得组成明细，再对花胡与普通牌形的合计台数统一验限。"""

        winner = info["winner"]
        player = self.player_list[winner]
        final_hand = list(player.hand_tiles)
        winner_flowers = list(player.huapai_list)
        if not transfer_completed:
            final_hand.append(replacement_tile)
            _, transfer_tile = self._flower_win_transfer(info)
            winner_flowers.append(transfer_tile)

        scoring_rules = dict(self.rules_dict)
        scoring_rules["minimum_tai"] = 0

        flower_context = self._score_context(
            winner,
            "seven_flowers_steal_eighth",
            include_special=False,
            flowers_override=winner_flowers,
            opening_flower_win=opening,
        )
        flower_context["rules"] = scoring_rules
        flower_context["out_with_replacement_tile"] = True
        flower_context["seven_flowers_steal_eighth"] = True
        flower_detail = self.calculation_service.Taiwan_hepai_detail(
            final_hand,
            player.combination_tiles,
            [],
            replacement_tile,
            flower_context,
        )
        if not flower_detail.get("is_win"):
            return None, flower_detail, flower_detail

        if info["mode"] == "seven_then_last":
            normal_source = "seven_flowers_steal_eighth"
            normal_flowers = []
        else:
            normal_source = "self_draw"
            normal_flowers = list(winner_flowers)
            normal_flowers.remove(info["stolen"])

        normal_context = self._score_context(
            winner,
            normal_source,
            include_special=False,
            flowers_override=normal_flowers,
        )
        normal_context["rules"] = scoring_rules
        normal_context["out_with_replacement_tile"] = True
        if normal_source == "self_draw":
            normal_context["last_tile"] = bool(
                self.last_draw_was_last
                if opening or transfer_completed
                else self._supplement_draw_will_exhaust_normal_wall()
            )
        normal_detail = self.calculation_service.Taiwan_hepai_detail(
            final_hand,
            player.combination_tiles,
            [],
            replacement_tile,
            normal_context,
        )
        if not normal_detail.get("is_win"):
            normal_detail = None

        return (
            normal_detail,
            flower_detail,
            self._combine_flower_details(normal_detail, flower_detail),
        )

    def _preview_seven_flowers_steal_eighth_detail(
        self,
        info: dict,
        *,
        opening: bool,
    ) -> Optional[dict]:
        if not self.can_take_supplement_tile() or not self.tiles_list:
            return None
        replacement_tile = self.tiles_list[-1]
        if replacement_tile in FLOWER_TILES:
            return None
        _, _, detail = self._seven_flowers_steal_eighth_details(
            info,
            replacement_tile,
            opening=opening,
            transfer_completed=False,
        )
        return detail

    async def _ask_seven_flowers_steal_eighth(
        self,
        special: Optional[dict],
        *,
        opening: bool = False,
    ) -> Optional[dict]:
        """提示七抢一花胡；取消或超时均按普通补花继续。"""

        if special is None:
            return None
        detail = self._preview_seven_flowers_steal_eighth_detail(
            special,
            opening=opening,
        )
        if not detail or (
            not detail.get("is_win", False)
            and not self._is_cuohe_detail(detail)
        ):
            return None

        previous_player_index = self.current_player_index
        winner = special["winner"]
        self.current_player_index = winner
        self.action_dict = {0: [], 1: [], 2: [], 3: []}
        self.action_dict[winner] = ["hu_flower", "pass"]
        self.game_status = "waiting_flower_choice"
        self.prepare_action_window()
        await broadcast_ask_hand_action(self)
        accepted = await self.wait_action()
        self.current_player_index = previous_player_index
        return special if accepted else None

    def _record_published_flower(
        self,
        owner_index: int,
        tile: int,
        *,
        is_drawn: bool,
        special: Optional[dict],
    ) -> None:
        recipient = None
        transfer_from = None
        transfer_tile = None
        if special:
            if special["winner"] != owner_index:
                recipient = special["winner"]
            transfer_from, transfer_tile = self._flower_win_transfer(special)
        self._record_buhua(
            tile,
            owner_index,
            is_drawn,
            recipient,
            transfer_from,
            transfer_tile,
        )

    async def _broadcast_flower(
        self,
        owner_index: int,
        tile: int,
        *,
        is_drawn: bool,
        record: bool = True,
    ) -> None:
        if record:
            self._record_published_flower(
                owner_index,
                tile,
                is_drawn=is_drawn,
                special=None,
            )
        await broadcast_do_action(
            self,
            action_list=["buhua"],
            action_player=owner_index,
            buhua_tile=tile,
            is_mo_buhua=is_drawn,
        )

    async def _broadcast_flower_win(self, special: dict) -> None:
        _, transfer_tile = self._flower_win_transfer(special)
        await broadcast_do_action(
            self,
            action_list=["hu_flower"],
            action_player=special["winner"],
            silent=True,
            buhua_tile=transfer_tile,
            buhua_recipient=special["winner"],
        )

    async def _replace_one_flower(
        self,
        player_index: int,
        flower_index: int,
        *,
        is_drawn: bool,
        opening: bool,
    ) -> bool:
        """公开一张花牌，七抢一窗口结束后才发出补牌。"""

        player = self.player_list[player_index]
        flower = player.hand_tiles[flower_index]
        candidate = self._seven_flowers_steal_eighth_candidate(player_index, flower)

        player.hand_tiles.pop(flower_index)
        self._publish_flower(player_index, flower)
        # 实时对局先看到普通补花；牌谱等选择结束后写入一次最终归属。
        await self._broadcast_flower(
            player_index,
            flower,
            is_drawn=is_drawn,
            record=False,
        )

        special = await self._ask_seven_flowers_steal_eighth(
            candidate,
            opening=opening,
        )
        recipient = player_index
        if special:
            recipient = self._transfer_flower_win(special)
        self._record_published_flower(
            player_index,
            flower,
            is_drawn=is_drawn,
            special=special,
        )

        if special:
            await self._broadcast_flower_win(special)
            await self._complete_seven_flowers_steal_eighth(special, opening=opening)
            return False
        return await self._draw_tail_for_player(recipient, opening=opening) is not None

    async def _draw_tail_for_player(self, player_index: int, *, opening: bool) -> Optional[int]:
        if not self.can_take_supplement_tile():
            self.draw_reason = "flower_or_kong_without_replacement"
            self.game_status = "END"
            return None
        tile = self._take_supplement_tile()
        known_flowers = sum(
            1
            for player in self.player_list
            for value in (
                list(getattr(player, "hand_tiles", ()))
                + list(getattr(player, "huapai_list", ()))
            )
            if value in FLOWER_TILES
        )
        if tile in FLOWER_TILES and known_flowers >= len(FLOWER_TILES):
            logger.error(
                "台湾麻将补牌违反八花牌墙不变量 player=%s tile=%s known_flowers=%s",
                player_index,
                tile,
                known_flowers,
            )
            self.draw_reason = "invalid_flower_wall"
            self.game_status = "END"
            return None
        player = self.player_list[player_index]
        player.hand_tiles.append(tile)
        if not opening:
            # 普通摸牌或任何补牌只要取走最后一张可摸牌，都属于海底。
            self.last_draw_was_last = not self.can_take_normal_tile()
        player.has_draw_slot = not opening
        player.last_drawn_tile = None if opening else tile
        player_action_record_deal(self, tile, "bd", player_index)
        await broadcast_do_action(
            self,
            action_list=["deal_buhua_tile"],
            action_player=player_index,
            deal_tile=tile,
        )
        return tile

    async def _complete_seven_flowers_steal_eighth(self, info: dict, *, opening: bool) -> None:
        winner = info["winner"]
        tile = await self._draw_tail_for_player(winner, opening=opening)
        if tile is None:
            return
        if tile in FLOWER_TILES:
            logger.error(
                "台湾麻将七抢一后补牌违反八花牌墙不变量 winner=%s tile=%s",
                winner,
                tile,
            )
            self.draw_reason = "invalid_flower_wall"
            self.game_status = "END"
            return
        self.player_list[winner].last_drawn_tile = tile
        self.last_draw_after_kong = True
        normal_detail, flower_detail, detail = (
            self._seven_flowers_steal_eighth_details(
                info,
                tile,
                opening=opening,
                transfer_completed=True,
            )
        )
        item = {
            "index": winner,
            "source": "seven_flowers_steal_eighth",
            "payer": info["payer"],
            "tile": info["tile"],
            "hu_class": hu_action_for_player(info["payer"], winner),
            "detail": detail,
            "seven_robs_mode": info["mode"],
            "normal_detail": normal_detail,
            "flower_detail": flower_detail,
        }
        if not detail.get("is_win", False) and not self._is_cuohe_detail(detail):
            logger.error(
                "台湾麻将七抢一确认后未通过最低台数检查 winner=%s detail=%s",
                winner,
                detail,
            )
            self.pending_winners = []
            self.draw_reason = "invalid_seven_flowers_steal_eighth"
            self.game_status = "END"
            return
        self._queue_winner_resolution([item])

    def _mark_eight_flowers_if_ready(
        self,
        player_index: int,
    ) -> None:
        player = self.player_list[player_index]
        if player.eight_flowers_declined or len(set(player.huapai_list)) != 8:
            return
        if self.rules.eight_flowers_mode in (
            "optional_standalone",
            "forced_standalone",
            "compound",
        ):
            player.pending_eight_flowers = True

    async def _request_opening_buhua(self, player_index: int) -> None:
        """开局补花仍按既定顺序执行，但先等待玩家的补花操作。"""

        self.current_player_index = player_index
        self.action_dict = {0: [], 1: [], 2: [], 3: []}
        self.action_dict[player_index] = ["buhua"]
        self.game_status = "waiting_buhua_round"
        self.prepare_action_window()
        await broadcast_ask_hand_action(self)
        # 补花是必选动作；超时只影响等待时长，不会把花牌留在手牌中。
        await self.wait_action()

    async def _replace_opening_flower(self, owner_index: int, flower: int) -> bool:
        """逐张确认并补掉一张指定的开局花牌。"""

        player = self.player_list[owner_index]
        await self._request_opening_buhua(owner_index)
        flower_index = player.hand_tiles.index(flower)
        if not await self._replace_one_flower(
            owner_index,
            flower_index,
            is_drawn=False,
            opening=True,
        ):
            return False

        self._mark_eight_flowers_if_ready(owner_index)
        await self._ask_eight_flowers(owner_index, opening=True)
        return self.game_status != "END"

    async def _opening_flower_replacement(self) -> None:
        """按馆规逐张确认、公开并补完开局花牌。"""

        if self.rules.opening_flower_replacement_order == "round_robin":
            # 分轮补花：每家本轮开始时已有的花全部补完；本轮补得的新花留到所有玩家完成后，再由庄家开始下一轮。
            while True:
                replaced_in_round = False
                for owner_index in range(4):
                    player = self.player_list[owner_index]
                    flowers_this_round = [
                        tile for tile in player.hand_tiles if tile in FLOWER_TILES
                    ]
                    if flowers_this_round:
                        replaced_in_round = True
                    for flower in flowers_this_round:
                        if not await self._replace_opening_flower(owner_index, flower):
                            return
                if not replaced_in_round:
                    break
        else:
            # 推荐标准：某家连同补得的新花全部补完后，再轮到下一家。
            for owner_index in range(4):
                player = self.player_list[owner_index]
                while True:
                    flower = next(
                        (tile for tile in player.hand_tiles if tile in FLOWER_TILES),
                        None,
                    )
                    if flower is None:
                        break
                    if not await self._replace_opening_flower(owner_index, flower):
                        return

        for player in self.player_list:
            player.has_draw_slot = False
            player.last_drawn_tile = None

    async def _process_drawn_flowers(self, player_index: int, origin: str) -> bool:
        player = self.player_list[player_index]
        if (
            origin == "normal"
            and getattr(self, "last_draw_was_last", False)
            and not self.can_take_supplement_tile()
            and player.hand_tiles
            and player.hand_tiles[-1] in FLOWER_TILES
        ):
            # 没有合法补牌时，仅公开末张花并结束本手。
            flower = player.hand_tiles.pop()
            self._publish_flower(player_index, flower)
            await self._broadcast_flower(
                player_index,
                flower,
                is_drawn=True,
            )
            player.has_draw_slot = False
            player.last_drawn_tile = None
            self.draw_reason = "terminal_flower"
            self.game_status = "END"
            return False
        player.last_drawn_tile = player.hand_tiles[-1] if player.hand_tiles else None
        self.last_draw_after_kong = origin in ("angang", "jiagang", "direct_kong")
        self.supplement_win_allowed = (
            origin != "direct_kong"
            or self.rules.direct_kong_replacement_win_allowed
        )
        if player.hand_tiles and player.hand_tiles[-1] in FLOWER_TILES:
            # 由标准手牌操作窗口提供 buhua；客户端的“自动补花”只决定是否自动提交。
            return True

        self._mark_eight_flowers_if_ready(player_index)
        if player.pending_eight_flowers:
            # 行牌中补齐八花也必须先处理独立花胡选择；放弃后才回到普通动作窗口。
            await self._ask_eight_flowers(player_index)
            if self.game_status == "END":
                return False
        return self.game_status != "END"

    async def execute_buhua(self, player_index: int) -> None:
        """执行一次行牌中补花，并把连续花牌交回下一次操作询问。"""

        player = self.player_list[player_index]
        flower_index = next(
            (
                index
                for index in range(len(player.hand_tiles) - 1, -1, -1)
                if player.hand_tiles[index] in FLOWER_TILES
            ),
            None,
        )
        if flower_index is None:
            logger.warning("台湾麻将拒绝非法补花 player=%s hand=%s", player_index, player.hand_tiles)
            await self._prepare_hand_action_after_draw()
            return

        flower = player.hand_tiles[flower_index]
        is_drawn = resolve_is_mo_buhua(
            player.hand_tiles,
            flower,
            draw_slot=has_draw_slot(player),
        )
        if not await self._replace_one_flower(
            player_index,
            flower_index,
            is_drawn=is_drawn,
            opening=False,
        ):
            return

        self.last_draw_after_kong = True
        if player.hand_tiles[-1] in FLOWER_TILES:
            await self._prepare_hand_action_after_draw()
            return

        self._mark_eight_flowers_if_ready(player_index)
        if player.pending_eight_flowers:
            await self._ask_eight_flowers(player_index)
            if self.game_status == "END":
                return
        await self._prepare_hand_action_after_draw()

    # ------------------------------------------------------------------
    # 玩家动作执行
    # ------------------------------------------------------------------

    def _build_pending_winner(
        self,
        player_index: int,
        source: str,
        hu_class: str,
        detail: dict,
        tile: Optional[int],
        payer: Optional[int] = None,
    ) -> dict:
        return {
            "index": player_index,
            "source": source,
            "payer": payer,
            "liable_payer": self._liability_payer_for_win(
                player_index,
                source,
                payer,
                tile,
                detail,
            ),
            "hu_class": hu_class,
            "detail": detail,
            "tile": tile,
        }

    def _queue_winner_resolution(self, winners: List[dict]) -> None:
        cuohe_players = []
        pending_winners = []
        for item in winners:
            target = (
                cuohe_players
                if self._is_cuohe_detail(item["detail"])
                else pending_winners
            )
            target.append(item)
        if cuohe_players:
            self.pending_winners = []
            self.pending_cuohe = {
                "players": cuohe_players,
                "winners": pending_winners,
            }
            self.hu_class = winners[0]["hu_class"]
            self.game_status = "check_cuohe"
            return

        self.pending_winners = winners
        if winners[0]["source"] == "robbing_kong":
            self.jiagang_tile = None
        self.hu_class = winners[0]["hu_class"]
        self.game_status = "END"

    def accept_self_draw(self, player_index: int) -> None:
        detail = self.result_dict.get("hu_self") or self.score_candidate(
            player_index,
            "self_draw",
        )
        if not detail:
            logger.error("台湾麻将自摸缺少有效计分结果 player=%s", player_index)
            return
        if not detail.get("is_win", False):
            if self._is_cuohe_detail(detail):
                self._queue_winner_resolution([
                    self._build_pending_winner(
                        player_index,
                        "self_draw",
                        "hu_self",
                        detail,
                        self.player_list[player_index].last_drawn_tile
                        or (
                            self.player_list[player_index].hand_tiles[-1]
                            if self.player_list[player_index].hand_tiles
                            else None
                        ),
                    )
                ])
                return
            if getattr(self.player_list[player_index], "pending_eight_flowers", False):
                self.decline_eight_flowers(player_index)
            logger.warning(
                "台湾麻将拒绝未通过合法性检查的自摸 player=%s detail=%s",
                player_index,
                detail,
            )
            return
        player = self.player_list[player_index]
        winning_tile = player.last_drawn_tile or (player.hand_tiles[-1] if player.hand_tiles else None)
        self._queue_winner_resolution([
            self._build_pending_winner(
                player_index,
                "self_draw",
                "hu_self",
                detail,
                winning_tile,
            )
        ])

    async def execute_cut(self, player_index: int, action_data: dict, *, declare_ready: bool = False) -> None:
        player = self.player_list[player_index]
        requested_tile = action_data.get("TileId")
        if requested_tile in FLOWER_TILES:
            # 花牌只能公开并补花。
            logger.warning(
                "台湾麻将拒绝弃出花牌 player=%s tile=%s",
                player_index,
                requested_tile,
            )
            return
        if getattr(player, "ready_locked", False) and player.last_drawn_tile is not None:
            if requested_tile is None or normalize_tile(requested_tile) != normalize_tile(player.last_drawn_tile):
                logger.warning(
                    "台湾麻将拒绝报听后换牌 player=%s requested=%s drawn=%s",
                    player_index,
                    requested_tile,
                    player.last_drawn_tile,
                )
                return
        if declare_ready and requested_tile not in self.ready_candidate_cuts(player_index):
            logger.warning(
                "台湾麻将拒绝非法报听 player=%s tile=%s candidates=%s",
                player_index,
                requested_tile,
                sorted(self.ready_candidate_cuts(player_index)),
            )
            return
        if requested_tile is not None and is_kuikae_forbidden_cut(
            player,
            normalize_tile(requested_tile),
        ):
            logger.warning(
                "台湾麻将拒绝食替禁切 player=%s tile=%s forbidden=%s",
                player_index,
                requested_tile,
                sorted(player.kuikae_forbidden_tiles),
            )
            return
        cut_result = await apply_player_cut(self, player_index, action_data)
        if cut_result is None:
            return
        tile, is_moqie, cut_index = cut_result

        if player.qualification_alive and player.last_drawn_tile is not None:
            if normalize_tile(tile) != normalize_tile(player.last_drawn_tile):
                self._revoke_qualification(player_index)

        if player.water and not self._discard_may_win(player_index, tile):
            self._clear_water(player_index)

        player.last_drawn_tile = None
        player.kuikae_forbidden_tiles = set()
        player.last_discarded_tile = tile
        player.discard_tiles.append(tile)
        player.discard_count += 1
        player.first_discard_done = True
        if player_index == 0:
            self.xunmu += 1

        refresh_waiting_tiles(self, player_index)
        player_action_record_cut(
            self,
            cut_tile=tile,
            is_moqie=is_moqie,
        )
        # 报听资格由这张弃牌产生；牌谱也应先落牌，再复现资格与公开标签。
        self._finalize_ready_after_discard(player_index, declare_ready)
        self.opening_dealer_action = False
        self.last_draw_after_kong = False
        self.last_draw_was_last = False
        self.supplement_win_allowed = True

        await broadcast_do_action(
            self,
            action_list=["cut"],
            action_player=player_index,
            cut_tile=tile,
            cut_class=is_moqie,
            cut_tile_index=cut_index,
        )
        if declare_ready:
            await broadcast_refresh_player_tag_list(self)
        self.action_dict = check_action_after_cut(self, tile)
        self.pending_four_winds_abort = self._is_four_winds_abort()
        if any(self.action_dict[index] for index in self.action_dict):
            self.game_status = "waiting_action_after_cut"
        elif self.pending_four_winds_abort:
            self._finish_four_winds_abort()
        else:
            self.game_status = "deal_card"

    async def execute_timeout_cut(self, player_index: int) -> None:
        player = self.player_list[player_index]
        forbidden = {normalize_tile(tile) for tile in player.kuikae_forbidden_tiles}
        draw_slot = has_draw_slot(player)
        if draw_slot and normalize_tile(player.hand_tiles[-1]) not in forbidden:
            tile = player.hand_tiles[-1]
            is_moqie = True
        else:
            tile = pick_timeout_discard_tile(player.hand_tiles, forbidden)
            is_moqie = False
        await self.execute_cut(
            player_index,
            {"TileId": tile, "cutClass": is_moqie, "cutIndex": None},
        )

    async def execute_angang(self, player_index: int, target_tile: int) -> None:
        player = self.player_list[player_index]
        normal = normalize_tile(target_tile)
        if normal not in STRUCTURE_TILES or sum(
            1 for tile in player.hand_tiles if normalize_tile(tile) == normal
        ) < 4:
            logger.warning(
                "台湾麻将拒绝非法暗杠 player=%s tile=%s hand=%s",
                player_index,
                target_tile,
                player.hand_tiles,
            )
            return
        can_supplement = self._can_establish_kong_for_action()
        terminal_kong = (
            getattr(self, "last_draw_was_last", False)
            and not can_supplement
        )
        if (
            not self.can_keep_wall_after_kong()
            or not (can_supplement or terminal_kong)
        ):
            try:
                playable = self.playable_wall_count()
            except (AttributeError, TypeError, ValueError):
                playable = "unknown"
            logger.warning(
                "台湾麻将拒绝牌墙不足的暗杠 player=%s playable=%s",
                player_index,
                playable,
            )
            return
        if (
            player.water
            and self.rules.missed_win_released_by_kong
            and not self._kong_fourth_may_win(player_index, normal)
        ):
            self._clear_water(player_index)
        draw_slot = has_draw_slot(player)
        is_mo = resolve_is_mo_gang(player.hand_tiles, normal, draw_slot=draw_slot)
        removed = remove_angang_tiles(player.hand_tiles, normal, draw_slot=draw_slot)
        clear_draw_slot(player)
        player.last_drawn_tile = None
        player.combination_tiles.append(f"G{normal}")
        mask = [value for tile in removed for value in (2, tile)]
        player.combination_mask.append(mask)
        self._break_heavenly_earthly_ready_for_concealed_kong()
        self.table_claim_or_kong = True
        player_action_record_angang(self, angang_tile=normal, is_mo_gang=is_mo, combination_mask=mask)
        await broadcast_do_action(
            self,
            action_list=["angang"],
            action_player=player_index,
            combination_mask=mask,
            combination_target=f"G{normal}",
            is_mo_gang=is_mo,
        )
        if self._on_kong_established():
            return
        if not self.can_take_supplement_tile():
            self.draw_reason = "terminal_concealed_kong"
            self.game_status = "END"
            return
        self.next_supplement_kind = "angang"
        self.game_status = "deal_card_after_gang"

    async def execute_jiagang(self, player_index: int, target_tile: int) -> None:
        player = self.player_list[player_index]
        normal = normalize_tile(target_tile)
        combination_index = next(
            (
                idx
                for idx, code in enumerate(player.combination_tiles)
                if code.startswith("k") and normalize_tile(int(code[1:])) == normal
            ),
            -1,
        )
        if (
            normal not in STRUCTURE_TILES
            or combination_index < 0
            or not any(normalize_tile(tile) == normal for tile in player.hand_tiles)
        ):
            logger.warning(
                "台湾麻将拒绝非法加杠 player=%s tile=%s combinations=%s",
                player_index,
                target_tile,
                player.combination_tiles,
            )
            return
        can_supplement = self._can_establish_kong_for_action()
        terminal_kong = (
            getattr(self, "last_draw_was_last", False)
            and not can_supplement
        )
        if (
            not self.can_keep_wall_after_kong()
            or not (can_supplement or terminal_kong)
        ):
            try:
                playable = self.playable_wall_count()
            except (AttributeError, TypeError, ValueError):
                playable = "unknown"
            logger.warning(
                "台湾麻将拒绝牌墙不足的加杠 player=%s playable=%s",
                player_index,
                playable,
            )
            return
        self._pending_jiagang = {
            "player_index": player_index,
            "combination_index": combination_index,
            "hand_tiles": list(player.hand_tiles),
            "combination_tiles": list(player.combination_tiles),
            "combination_mask": [
                list(mask) if isinstance(mask, (list, tuple)) else mask
                for mask in player.combination_mask
            ],
            "has_draw_slot": bool(getattr(player, "has_draw_slot", False)),
            "last_drawn_tile": getattr(player, "last_drawn_tile", None),
            "water": bool(getattr(player, "water", False)),
            "qualification_alive": bool(getattr(player, "qualification_alive", False)),
            "qualification_ever": bool(getattr(player, "qualification_ever", False)),
            "heavenly_ready": bool(getattr(player, "heavenly_ready", False)),
            "earthly_ready": bool(getattr(player, "earthly_ready", False)),
            "declared_ready": bool(getattr(player, "declared_ready", False)),
            "ready_locked": bool(getattr(player, "ready_locked", False)),
            "tag_list": list(getattr(player, "tag_list", [])),
            "table_claim_or_kong": bool(getattr(self, "table_claim_or_kong", False)),
            "jiagang_tile": getattr(self, "jiagang_tile", None),
            "is_mo_gang": False,
            "normal": normal,
            "actual_tile": None,
        }
        if (
            player.water
            and self.rules.missed_win_released_by_kong
            and not self._kong_fourth_may_win(player_index, normal)
        ):
            self._clear_water(player_index)
        draw_slot = has_draw_slot(player)
        is_mo = resolve_is_mo_gang(player.hand_tiles, normal, draw_slot=draw_slot)
        self._pending_jiagang["is_mo_gang"] = bool(is_mo)
        actual = remove_cut_tile(player.hand_tiles, target_tile, is_mo, draw_slot=draw_slot)
        if actual is None:
            self._rollback_pending_jiagang()
            return
        self._pending_jiagang["actual_tile"] = actual
        clear_draw_slot(player)
        player.last_drawn_tile = None
        self._revoke_qualification(
            player_index,
            keep_declared=(
                self.rules.declared_ready_auto_added_kong
                and bool(getattr(player, "ready_locked", False))
            ),
        )
        self.table_claim_or_kong = True
        self.jiagang_tile = normal
        jiagang_mask = self._build_jiagang_mask(
            player_index,
            combination_index,
            actual,
        )
        player.combination_tiles[combination_index] = f"g{normal}"
        player.combination_mask[combination_index] = jiagang_mask
        player_action_record_jiagang(
            self,
            jiagang_tile=normal,
            is_mo_gang=is_mo,
        )
        await broadcast_do_action(
            self,
            action_list=["jiagang"],
            action_player=player_index,
            combination_target=f"k{normal}",
            combination_mask=jiagang_mask,
            is_mo_gang=is_mo,
        )
        self.action_dict = check_action_jiagang(self, normal)
        if any(self.action_dict[index] for index in self.action_dict):
            self.game_status = "waiting_action_qianggang"
        else:
            await self.finalize_jiagang()

    def _build_jiagang_mask(
        self,
        player_index: int,
        combination_index: int,
        actual_tile: int,
    ) -> List[int]:
        player = self.player_list[player_index]
        mask = list(player.combination_mask[combination_index])
        insert_at = next(
            (pos for pos in range(0, len(mask), 2) if mask[pos] == 1),
            len(mask) - 2,
        )
        mask[insert_at:insert_at] = [3, actual_tile]
        return mask

    def _rollback_pending_jiagang(self, *, consume_robbed_tile: bool = False) -> None:
        """撤销暂态加杠；抢杠成立时让第四张离开加杠者手牌。"""

        pending = getattr(self, "_pending_jiagang", None)
        if not pending:
            return
        player = self.player_list[pending["player_index"]]
        current_water = bool(getattr(player, "water", False))
        current_ready = (
            bool(getattr(player, "qualification_alive", False)),
            bool(getattr(player, "qualification_ever", False)),
            bool(getattr(player, "heavenly_ready", False)),
            bool(getattr(player, "earthly_ready", False)),
            bool(getattr(player, "declared_ready", False)),
            bool(getattr(player, "ready_locked", False)),
            tuple(getattr(player, "tag_list", [])),
        )

        player.hand_tiles[:] = pending["hand_tiles"]
        player.combination_tiles[:] = pending["combination_tiles"]
        player.combination_mask[:] = [
            list(mask) if isinstance(mask, (list, tuple)) else mask
            for mask in pending["combination_mask"]
        ]
        player.has_draw_slot = pending["has_draw_slot"]
        player.last_drawn_tile = pending["last_drawn_tile"]
        player.water = pending["water"]
        player.qualification_alive = pending["qualification_alive"]
        player.qualification_ever = pending["qualification_ever"]
        player.heavenly_ready = pending["heavenly_ready"]
        player.earthly_ready = pending["earthly_ready"]
        player.declared_ready = pending["declared_ready"]
        player.ready_locked = pending["ready_locked"]
        player.tag_list[:] = pending["tag_list"]
        self.table_claim_or_kong = pending["table_claim_or_kong"]
        self.jiagang_tile = pending["jiagang_tile"]
        self._pending_jiagang = None

        if consume_robbed_tile:
            robbed_tile = pending.get("actual_tile") or pending["normal"]
            removed = remove_cut_tile(
                player.hand_tiles,
                robbed_tile,
                bool(pending["is_mo_gang"]),
                draw_slot=bool(pending["has_draw_slot"]),
            )
            if removed is None:
                logger.error(
                    "台湾麻将抢杠回滚未能移除被抢牌 player=%s tile=%s hand=%s",
                    pending["player_index"],
                    robbed_tile,
                    player.hand_tiles,
                )
            elif pending["is_mo_gang"]:
                clear_draw_slot(player)
                player.last_drawn_tile = None

        if current_water != bool(player.water):
            self._record_water_state(pending["player_index"])
        restored_ready = (
            bool(getattr(player, "qualification_alive", False)),
            bool(getattr(player, "qualification_ever", False)),
            bool(getattr(player, "heavenly_ready", False)),
            bool(getattr(player, "earthly_ready", False)),
            bool(getattr(player, "declared_ready", False)),
            bool(getattr(player, "ready_locked", False)),
            tuple(getattr(player, "tag_list", [])),
        )
        if current_ready != restored_ready:
            self._record_ready_state(pending["player_index"])

    async def finalize_jiagang(self) -> None:
        pending = getattr(self, "_pending_jiagang", None)
        if pending is not None:
            self._pending_jiagang = None
        self.jiagang_tile = None
        if self._on_kong_established():
            return
        if not self.can_take_supplement_tile():
            self.draw_reason = "terminal_added_kong"
            self.game_status = "END"
            return
        self.next_supplement_kind = "jiagang"
        self.game_status = "deal_card_after_gang"

    @staticmethod
    def _claim_mask(action_type: str, tile: int, relative: str) -> Tuple[str, List[int], List[int]]:
        if action_type == "chi_left":
            return f"s{tile - 1}", [tile - 2, tile - 1], [1, tile, 0, tile - 2, 0, tile - 1]
        if action_type == "chi_mid":
            return f"s{tile}", [tile - 1, tile + 1], [1, tile, 0, tile - 1, 0, tile + 1]
        if action_type == "chi_right":
            return f"s{tile + 1}", [tile + 1, tile + 2], [1, tile, 0, tile + 1, 0, tile + 2]
        if action_type == "peng":
            mask = {
                "left": [1, tile, 0, tile, 0, tile],
                "right": [0, tile, 0, tile, 1, tile],
                "top": [0, tile, 1, tile, 0, tile],
            }[relative]
            return f"k{tile}", [tile, tile], mask
        mask = {
            "left": [1, tile, 0, tile, 0, tile, 0, tile],
            "right": [0, tile, 0, tile, 0, tile, 1, tile],
            "top": [0, tile, 1, tile, 0, tile, 0, tile],
        }[relative]
        return f"g{tile}", [tile, tile, tile], mask

    async def execute_claim(self, player_index: int, action_type: str) -> None:
        discarder = self.current_player_index
        discarder_player = self.player_list[discarder]
        if not discarder_player.discard_tiles:
            self.game_status = "deal_card"
            return
        tile = discarder_player.discard_tiles[-1]
        player = self.player_list[player_index]
        relative = get_index_relative_position(player_index, discarder)
        code, required, mask = self._claim_mask(action_type, tile, relative)
        if any(player.hand_tiles.count(required_tile) < required.count(required_tile) for required_tile in set(required)):
            logger.error("台湾麻将非法鸣牌 player=%s action=%s tile=%s", player_index, action_type, tile)
            self.game_status = "deal_card"
            return
        if action_type == "gang" and (
            not self._can_establish_kong_for_action()
            or not self.can_keep_wall_after_kong()
        ):
            try:
                playable = self.playable_wall_count()
            except (AttributeError, TypeError, ValueError):
                playable = "unknown"
            logger.warning(
                "台湾麻将拒绝牌墙不足的直杠 player=%s playable=%s",
                player_index,
                playable,
            )
            self.game_status = "deal_card"
            return
        for required_tile in required:
            player.hand_tiles.remove(required_tile)
        player.combination_tiles.append(code)
        player.combination_mask.append(mask)
        self._remember_claim_liability(player_index, discarder, tile)
        clear_draw_slot(player)
        player.last_drawn_tile = None
        self._revoke_qualification(player_index)
        self.table_claim_or_kong = True

        discarder_player.discard_tiles.pop()
        discarder_player.discard_origin_tiles.append(tile)
        self.current_player_index = player_index
        player_action_record_chipenggang(
            self,
            action_type=action_type,
            mingpai_tile=tile,
            action_player=player_index,
            combination_mask=mask,
        )
        await broadcast_do_action(
            self,
            action_list=[action_type],
            action_player=player_index,
            cut_tile=tile,
            cut_from_player=discarder,
            combination_target=code,
            combination_mask=mask,
        )

        if action_type == "gang":
            if self._on_kong_established():
                return
            self.next_supplement_kind = "direct_kong"
            self.supplement_win_allowed = (
                self.rules.direct_kong_replacement_win_allowed
            )
            self.game_status = "deal_card_after_gang"
        else:
            kuikae_mode = self.rules.chow_discard_restriction_mode
            if action_type == "peng":
                kuikae_mode = "same_tile" if self.rules.pung_same_tile_discard_forbidden else "none"
            player.kuikae_forbidden_tiles = strict_kuikae_forbidden(
                action_type,
                tile,
                kuikae_mode,
            )
            self.game_status = "onlycut_after_action"

    def _selected_winners(self, selected: List[Tuple[int, str]]) -> List[Tuple[int, str]]:
        selected = sorted(selected, key=lambda item: (item[0] - self.current_player_index) % 4)
        mode = self.rules.multi_win_mode
        if mode == "multiple_winners":
            return selected
        if mode == "head_bump":
            return selected[:1]
        return selected if len(selected) >= 3 else selected[:1]

    async def resolve_discard_responses(self, responses: Dict[int, dict], allowed: Dict[int, list]) -> None:
        discarder = self.current_player_index
        tile = self.player_list[discarder].discard_tiles[-1]
        selected_hu: List[Tuple[int, str]] = []
        ready_tag_changed = False
        for index, actions in allowed.items():
            hu_actions = [action for action in actions if action in HU_ACTIONS]
            chosen = responses.get(index, {}).get("action_type", "pass")
            if (
                hu_actions
                and any(
                    self.result_dict.get(action, {}).get("is_win", True)
                    for action in hu_actions
                )
                and is_forced_ready_win(self, index)
            ):
                chosen = hu_actions[0]
            if chosen in hu_actions:
                selected_hu.append((index, chosen))
            elif any(
                self.result_dict.get(action, {}).get("is_win", True)
                for action in hu_actions
            ):
                ready_tag_changed |= self.enter_water(index)

        if ready_tag_changed:
            await broadcast_refresh_player_tag_list(self)

        winners = self._selected_winners(selected_hu)
        if winners:
            self._queue_winner_resolution([
                self._build_pending_winner(
                    index,
                    "discard",
                    action,
                    self.result_dict[action],
                    tile,
                    discarder,
                )
                for index, action in winners
            ])
            return

        claims: List[Tuple[int, int, str, int]] = []
        for index, data in responses.items():
            action = data.get("action_type")
            if self.player_list[index].water and self.rules.missed_win_blocks_claims:
                continue
            priority = self.action_priority.get(action, 0)
            if action in ("peng", "gang", "chi_left", "chi_mid", "chi_right"):
                claims.append((priority, -((index - discarder) % 4), action, index))
        if claims:
            _, _, action, index = max(claims)
            self.pending_four_winds_abort = False
            await self.execute_claim(index, action)
        elif self.pending_four_winds_abort:
            self._finish_four_winds_abort()
        else:
            self.game_status = "deal_card"

    async def resolve_rob_kong_responses(self, responses: Dict[int, dict], allowed: Dict[int, list]) -> None:
        tile = self.jiagang_tile
        if tile is None:
            self.game_status = "deal_card"
            return
        declarer = self.current_player_index
        selected_hu: List[Tuple[int, str]] = []
        ready_tag_changed = False
        for index, actions in allowed.items():
            hu_actions = [action for action in actions if action in HU_ACTIONS]
            chosen = responses.get(index, {}).get("action_type", "pass")
            if (
                hu_actions
                and any(
                    self.result_dict.get(action, {}).get("is_win", True)
                    for action in hu_actions
                )
                and is_forced_ready_win(self, index)
            ):
                chosen = hu_actions[0]
            if chosen in hu_actions:
                selected_hu.append((index, chosen))
            elif any(
                self.result_dict.get(action, {}).get("is_win", True)
                for action in hu_actions
            ):
                ready_tag_changed |= self.enter_water(index)
        if ready_tag_changed:
            await broadcast_refresh_player_tag_list(self)
        winners = self._selected_winners(selected_hu)
        if winners:
            has_legal_winner = any(
                not self._is_cuohe_detail(self.result_dict.get(action))
                for _, action in winners
            )
            if has_legal_winner:
                self._rollback_pending_jiagang(consume_robbed_tile=True)
            self._queue_winner_resolution([
                self._build_pending_winner(
                    index,
                    "robbing_kong",
                    action,
                    self.result_dict[action],
                    tile,
                    declarer,
                )
                for index, action in winners
            ])
            return
        await self.finalize_jiagang()

    # ------------------------------------------------------------------
    # 主循环与结算
    # ------------------------------------------------------------------

    async def wait_action(self):
        return await wait_action(self)

    def prepare_action_window(self) -> None:
        """在广播启动机器人前清除上一询问的迟到响应。"""
        for index in range(4):
            self.action_events[index].clear()
            queue = self.action_queues[index]
            while not queue.empty():
                try:
                    queue.get_nowait()
                except asyncio.QueueEmpty:
                    break

    async def run_hu_result_ready_phase(self, fan_count: int) -> None:
        self.prepare_action_window()
        await run_synced_hu_ready_phase(
            self,
            fan_count,
            broadcast_ready_status,
        )

    async def _resolve_cuohe(self) -> None:
        info = self.pending_cuohe
        if not info:
            logger.error("台湾麻将进入错和流程但缺少待处理信息")
            self.game_status = "deal_card"
            return

        cuohe_players = info["players"]
        pending_winners = info["winners"]
        source = cuohe_players[0]["source"]
        for cuohe in cuohe_players:
            player_index = cuohe["index"]
            hu_class = cuohe["hu_class"]
            detail = cuohe["detail"]
            tile = cuohe["tile"]
            player = self.player_list[player_index]
            scores_before = self._hand_scores_before or {
                item.original_player_index: item.score
                for item in self.player_list
            }

            player.record_counter.cuohe_times += 1
            if self.cuohe_type == 1:
                cuohe_penalty, others_bonus = 40, 0
            else:
                cuohe_penalty, others_bonus = 30, 10
            for item in self.player_list:
                if item.player_index == player_index:
                    item.score -= cuohe_penalty
                else:
                    item.score += others_bonus

            hu_score = detail.get("capped_tai", detail.get("tai", 0))
            hu_fan = list(detail.get("fan_names", ())) + ["错和"]
            score_changes = build_score_changes_by_seat(
                self.player_list,
                scores_before,
            )
            score_changes_dict = build_score_changes_dict(
                self.player_list,
                scores_before,
            )
            player_action_record_hu(
                self,
                hu_class=hu_class,
                hu_score=hu_score,
                hu_fan=hu_fan,
                hepai_player_index=player_index,
                score_changes=score_changes,
                hepai_tile=tile if source in ("discard", "robbing_kong") else None,
            )

            for item in self.player_list:
                change = item.score - scores_before[item.original_player_index]
                item.score_history.append(
                    f"+{change:02d}"
                    if change > 0
                    else f"-{abs(change):02d}"
                    if change < 0
                    else "0"
                )
                item.round_number_history.append(self.current_round)
                scores_before[item.original_player_index] = item.score
            self._hand_scores_before = scores_before

            display_hand = list(player.hand_tiles)
            if source in ("discard", "robbing_kong") and tile is not None:
                display_hand.append(tile)
            await self.broadcast_result(
                hepai_player_index=player_index,
                player_to_score={
                    item.player_index: item.score
                    for item in self.player_list
                },
                hu_score=hu_score,
                hu_fan=hu_fan,
                hu_class=hu_class,
                hepai_player_hand=display_hand,
                hepai_player_huapai=player.huapai_list,
                hepai_player_combination_mask=player.combination_mask,
                score_changes=score_changes_dict,
                revealed_angang_masks=build_revealed_angang_masks(self.player_list),
                next_status="round_continue",
                hepai_tile=tile if source == "discard" else None,
            )
            await self.run_hu_result_ready_phase(len(hu_fan))

            if "peida" not in player.tag_list:
                player.tag_list.append("peida")
            await self.broadcast_refresh_player_tag_list()

        self.pending_cuohe = None
        self.result_dict = {}

        for item in self.player_list:
            refresh_waiting_tiles(self, item.player_index)

        if pending_winners:
            self.pending_winners = pending_winners
            if source == "robbing_kong":
                self.jiagang_tile = None
            self.hu_class = pending_winners[0]["hu_class"]
            self.game_status = "END"
            return

        self.hu_class = None
        if source == "self_draw":
            self.action_dict = check_action_hand_action(self, self.current_player_index)
            self.game_status = "waiting_hand_action"
            return
        if source == "discard":
            cut_tile = self.player_list[self.current_player_index].discard_tiles[-1]
            self.action_dict = check_action_after_cut(self, cut_tile)
            if any(self.action_dict.values()):
                self.game_status = "waiting_action_after_cut"
            elif self.pending_four_winds_abort:
                self._finish_four_winds_abort()
            else:
                self.game_status = "deal_card"
            return
        if source == "robbing_kong" and self.jiagang_tile is not None:
            self.action_dict = check_action_jiagang(self, self.jiagang_tile)
            if any(self.action_dict.values()):
                self.game_status = "waiting_action_qianggang"
            else:
                await self.finalize_jiagang()
            return

        logger.error("台湾麻将错和续局未知来源 source=%s", source)
        self.game_status = "deal_card"

    def _declared_ready_auto_jiagang_tile(self, player_index: int) -> Optional[int]:
        player = self.player_list[player_index]
        if (
            not self.rules.declared_ready_auto_added_kong
            or not getattr(player, "ready_locked", False)
            or player.last_drawn_tile is None
        ):
            return None
        tile = normalize_tile(player.last_drawn_tile)
        if not any(
            code.startswith("k") and normalize_tile(int(code[1:])) == tile
            for code in player.combination_tiles
        ):
            return None
        can_supplement = self.can_establish_kong()
        can_keep_wall = getattr(self, "can_keep_wall_after_kong", None)
        if callable(can_keep_wall) and not can_keep_wall():
            return None
        if not can_supplement and not self.last_draw_was_last:
            return None
        return tile

    async def _prepare_hand_action_after_draw(self) -> None:
        player_index = self.current_player_index
        self.result_dict = {}
        self.action_dict = check_action_hand_action(self, player_index)
        auto_jiagang_tile = self._declared_ready_auto_jiagang_tile(player_index)
        if (
            auto_jiagang_tile is not None
            and "hu_self" not in self.action_dict[player_index]
        ):
            previous_status = getattr(self, "game_status", "deal_card")
            player = self.player_list[player_index]
            hand_before = tuple(player.hand_tiles)
            combinations_before = tuple(player.combination_tiles)
            await self.execute_jiagang(player_index, auto_jiagang_tile)
            action_established = (
                tuple(player.hand_tiles) != hand_before
                or tuple(player.combination_tiles) != combinations_before
                or getattr(self, "game_status", None)
                not in ("deal_card", "deal_card_after_gang")
            )
            if (
                not action_established
                and getattr(self, "game_status", previous_status)
                in ("deal_card", "deal_card_after_gang")
            ):
                self.game_status = "waiting_hand_action"
                self.action_dict = check_action_hand_action(self, player_index)
            return
        self.game_status = "waiting_hand_action"

    async def _deal_normal(self) -> None:
        if not self.can_take_normal_tile():
            self.draw_reason = "exhaustive"
            self.game_status = "END"
            return
        next_current_index(self)
        player = self.player_list[self.current_player_index]
        if player.normal_draw_count == 0:
            player.pre_first_draw_waiting = bool(refresh_waiting_tiles(self, self.current_player_index))
        tile = self.tiles_list.pop(0)
        player.hand_tiles.append(tile)
        player.has_draw_slot = True
        player.last_drawn_tile = tile
        player.normal_draw_count += 1
        self.last_draw_was_last = not self.can_take_normal_tile()
        player_action_record_deal(self, deal_tile=tile, deal_type="d")
        await broadcast_do_action(
            self,
            action_list=["deal_tile"],
            action_player=self.current_player_index,
            deal_tile=tile,
        )
        if not await self._process_drawn_flowers(self.current_player_index, "normal"):
            return
        await self._prepare_hand_action_after_draw()

    async def _deal_supplement(self) -> None:
        origin = self.next_supplement_kind or "angang"
        self.next_supplement_kind = None
        if not self.can_take_supplement_tile():
            self.draw_reason = "kong_without_replacement"
            self.game_status = "END"
            return
        tile = self._take_supplement_tile()
        player = self.player_list[self.current_player_index]
        player.hand_tiles.append(tile)
        player.has_draw_slot = True
        player.last_drawn_tile = tile
        self.last_draw_was_last = not self.can_take_normal_tile()
        player_action_record_deal(self, deal_tile=tile, deal_type="gd")
        await broadcast_do_action(
            self,
            action_list=["deal_gang_tile"],
            action_player=self.current_player_index,
            deal_tile=tile,
        )
        if not await self._process_drawn_flowers(self.current_player_index, origin):
            return
        await self._prepare_hand_action_after_draw()

    async def _run_hand(self) -> None:
        self.game_status = "playing"
        self.opening_dealer_action = True
        await self._opening_flower_replacement()
        if self.game_status == "END":
            return
        self._register_initial_heavenly_ready()

        self.current_player_index = 0
        self.last_draw_was_last = False
        self.last_draw_after_kong = False
        self.supplement_win_allowed = True
        self.result_dict = {}
        self.action_dict = check_action_hand_action(self, 0)
        self.game_status = "waiting_hand_action"

        while self.game_status != "END":
            await vote_checkpoint(self)
            if self.game_status == "deal_card":
                await self._deal_normal()
            elif self.game_status == "deal_card_after_gang":
                await self._deal_supplement()
            elif self.game_status == "waiting_hand_action":
                self.prepare_action_window()
                await broadcast_ask_hand_action(self)
                await self.wait_action()
            elif self.game_status in ("waiting_action_after_cut", "waiting_action_qianggang"):
                self.prepare_action_window()
                await broadcast_ask_other_action(self)
                await self.wait_action()
            elif self.game_status == "check_cuohe":
                await self._resolve_cuohe()
            elif self.game_status == "onlycut_after_action":
                self.action_dict = {0: [], 1: [], 2: [], 3: []}
                self.action_dict[self.current_player_index] = ["cut"]
                self.game_status = "waiting_hand_action"
            else:
                logger.error("台湾麻将未知状态: %s", self.game_status)
                self.draw_reason = "invalid_state"
                self.game_status = "END"

    def _dealer_continues(self) -> bool:
        continues = (
            any(item["index"] == 0 for item in self.pending_winners)
            if self.pending_winners
            else self.rules.draw_continues_dealer
        )
        if (
            continues
            and self.rules.dealer_streak_limit is not None
            and self.dealer_streak >= self.rules.dealer_streak_limit
        ):
            return False
        return continues

    def _settlement_for_winner(self, item: dict):
        if item.get("seven_robs_mode") == "six_plus_one":
            parts = [
                settle_win(
                    winner=item["index"],
                    hand_tai=item["flower_detail"]["tai"],
                    win_source="seven_flowers_steal_eighth",
                    dealer=0,
                    dealer_streak=self.dealer_streak,
                    rules=self.rules,
                    discarder=item["payer"],
                )
            ]
            normal_detail = item.get("normal_detail")
            if normal_detail:
                parts.append(
                    settle_win(
                        winner=item["index"],
                        hand_tai=normal_detail["tai"],
                        win_source="self_draw",
                        dealer=0,
                        dealer_streak=self.dealer_streak,
                        rules=self.rules,
                    )
                )
            return combine_settlements(*parts)

        detail = item["detail"]
        return settle_win(
            winner=item["index"],
            hand_tai=detail["tai"],
            win_source=item["source"],
            dealer=0,
            dealer_streak=self.dealer_streak,
            rules=self.rules,
            discarder=item["payer"],
            liable_payer=item.get("liable_payer"),
        )

    async def _settle_hand(self, scores_before: Dict[int, int]) -> Tuple[bool, bool]:
        dealer_continues = self._dealer_continues()
        settlements = [
            self._settlement_for_winner(item)
            for item in self.pending_winners
        ]
        projected_scores = {
            index: player.score + sum(
                settlement.score_changes[index]
                for settlement in settlements
            )
            for index, player in enumerate(self.player_list)
        }

        negative_end = self.rules.negative_score_ends_match and any(
            score < 0 for score in projected_scores.values()
        )
        match_end = negative_end or (
            self.current_round >= self.max_round * 4 and not dealer_continues
        )
        next_status = "match_end" if match_end else "round_end_by_ready"

        if self.pending_winners:
            multi_ron = len(self.pending_winners) > 1
            for winner_number, (item, settlement) in enumerate(zip(self.pending_winners, settlements)):
                for index, change in settlement.score_changes.items():
                    self.player_list[index].score += change
                for player in self.player_list:
                    change = settlement.score_changes[player.player_index]
                    player.score_history.append(
                        f"+{change:02d}" if change > 0
                        else f"-{abs(change):02d}" if change < 0
                        else "0"
                    )
                    player.round_number_history.append(self.current_round)
                winner = item["index"]
                detail = item["detail"]

                player = self.player_list[winner]
                fan_names = list(detail["fan_names"])
                display_fan_names = settlement_display_fan_names(fan_names, settlement)
                if item["source"] == "self_draw":
                    player.record_counter.zimo_times += 1
                else:
                    player.record_counter.dianhe_times += 1
                    payer = self.player_list[item["payer"]]
                    payer.record_counter.fangchong_times += 1
                    payer.record_counter.fangchong_score += detail["capped_tai"]
                player.record_counter.recorded_fans.append(fan_names)
                player.record_counter.win_score += detail["capped_tai"]
                player.record_counter.win_turn += self.xunmu

                changes_list = [settlement.score_changes[index] for index in range(4)]
                player_action_record_hu(
                    self,
                    hu_class=item["hu_class"],
                    hu_score=detail["capped_tai"],
                    hu_fan=display_fan_names,
                    hepai_player_index=winner,
                    score_changes=changes_list,
                    hepai_tile=item["tile"],
                    multi_ron=multi_ron if item["source"] == "discard" else None,
                    ron_discarder_index=item["payer"] if item["source"] == "discard" else None,
                    recycle_discard=(winner_number == len(self.pending_winners) - 1) if item["source"] == "discard" else None,
                )

                display_hand = list(player.hand_tiles)
                if item["source"] in ("discard", "robbing_kong"):
                    display_hand.append(item["tile"])
                result_next = next_status if winner_number == len(self.pending_winners) - 1 else "round_continue"
                await broadcast_result(
                    self,
                    hepai_player_index=winner,
                    player_to_score={p.player_index: p.score for p in self.player_list},
                    hu_score=detail["capped_tai"],
                    hu_fan=display_fan_names,
                    hu_class=item["hu_class"],
                    hepai_player_hand=display_hand,
                    hepai_player_huapai=player.huapai_list,
                    hepai_player_combination_mask=player.combination_mask,
                    score_changes={
                        p.original_player_index: settlement.score_changes[p.player_index]
                        for p in self.player_list
                    },
                    revealed_angang_masks=build_revealed_angang_masks(self.player_list),
                    hepai_tile=item["tile"],
                    multi_ron=multi_ron if item["source"] == "discard" else None,
                    is_qianggang=True if item["source"] == "robbing_kong" else None,
                    ron_discarder_index=(
                        item["payer"]
                        if item["source"] in ("discard", "robbing_kong")
                        else None
                    ),
                    recycle_discard=(winner_number == len(self.pending_winners) - 1) if item["source"] == "discard" else None,
                    next_status=result_next,
                )
                if not (match_end and winner_number == len(self.pending_winners) - 1):
                    await self.run_hu_result_ready_phase(len(display_fan_names))
        else:
            self.hu_class = "liuju"
            self._record_liuju(self.draw_reason)
            await broadcast_result(
                self,
                hu_class="liuju",
                score_changes=build_score_changes_dict(self.player_list, scores_before),
                revealed_angang_masks=build_revealed_angang_masks(self.player_list),
                next_status=next_status,
            )
            await asyncio.sleep(liuju_ready_wait_seconds())

        record_fulu_rounds_for_players(self.player_list)
        for player in self.player_list:
            change = player.score - scores_before[player.original_player_index]
            player.record_counter.round_score_total += change
            if not settlements:
                player.score_history.append(
                    f"+{change:02d}" if change > 0
                    else f"-{abs(change):02d}" if change < 0
                    else "0"
                )
                player.round_number_history.append(self.current_round)
        player_action_record_round_end(self)
        return dealer_continues, match_end

    async def game_loop_chinese(self):
        user_seed = self.room_random_seed if self.room_random_seed else None
        self.master_seed, self.salt, self.commitment, self.isPlayerSetRandomSeed = setup_random_seed_system(user_seed)
        capture_player_entry_order(self)
        rng = random.Random(self.master_seed)
        rng.shuffle(self.player_list)
        for index, player in enumerate(self.player_list):
            player.player_index = index
            player.original_player_index = index

        init_game_record(self)
        title = self.game_record["game_title"]
        title.update(self.build_record_title_fields())
        if self.event_id is not None:
            title["event_id"] = self.event_id

        while self.current_round <= self.max_round * 4:
            if self.current_round in (5, 9, 13) and self.current_round not in self._announced_wind_rounds:
                self._announced_wind_rounds.add(self.current_round)
                await broadcast_switch_seat(self)
                await asyncio.sleep(4)

            scores_before = {player.original_player_index: player.score for player in self.player_list}
            self._hand_scores_before = scores_before
            self._reset_hand_runtime()
            init_taiwan_tiles(self)
            self.current_player_index = 0
            await broadcast_game_start(self)
            init_game_round(self)

            await self._run_hand()
            dealer_continues, match_end = await self._settle_hand(scores_before)
            if match_end:
                break

            if dealer_continues:
                if self.pending_winners or self.rules.draw_increments_streak:
                    self.dealer_streak += 1
                next_game_round_classical_switchseat(
                    self,
                    keep_current_round=True,
                    keep_dealer_seat=True,
                )
            else:
                self.dealer_streak = 0
                next_game_round_classical_switchseat(self)

        end_game_record(self)
        assign_competition_final_ranks(self.player_list)
        await broadcast_game_end(self)
        if hasattr(self, "spectator_manager"):
            await self.spectator_manager.send_final_record_and_close()

        store = getattr(self.db_manager, "store_taiwan_game_record", None)
        if store:
            store(self.game_record, self.player_list, self.room_type, f"{self.max_round}/4")

        await self.game_server.gamestate_manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)
        if self.room_type != "match":
            await self.game_server.room_manager.finish_custom_game_room(self.room_id)


# 挂载台湾规则广播方法。
TaiwanGameState.broadcast_game_start = broadcast_game_start
TaiwanGameState.broadcast_ask_hand_action = broadcast_ask_hand_action
TaiwanGameState.broadcast_ask_other_action = broadcast_ask_other_action
TaiwanGameState.broadcast_do_action = broadcast_do_action
TaiwanGameState.broadcast_result = broadcast_result
TaiwanGameState.broadcast_game_end = broadcast_game_end
TaiwanGameState.broadcast_switch_seat = broadcast_switch_seat
TaiwanGameState.broadcast_refresh_player_tag_list = broadcast_refresh_player_tag_list
TaiwanGameState.reconnected_send_pending_ask = reconnected_send_pending_ask
TaiwanGameState.send_realtime_spectator_snapshot = send_realtime_spectator_snapshot
