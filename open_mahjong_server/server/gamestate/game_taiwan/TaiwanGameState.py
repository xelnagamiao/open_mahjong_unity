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
    is_forced_declared_ready_win,
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
        self.pending_eight_immortals = False
        self.eight_immortals_declined = False
        self.declared_ready = False
        self.ready_locked = False
        self.riichi_candidate_cuts = {}
        self.liability_payer = None

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
            player.pending_eight_immortals = False
            player.eight_immortals_declined = False
            player.has_draw_slot = False
            player.declared_ready = False
            player.ready_locked = False
            player.riichi_candidate_cuts = {}
            player.liability_payer = None
            if "declared_ready" in player.tag_list:
                player.tag_list.remove("declared_ready")

    def _reset_hand_runtime(self) -> None:
        self.pending_winners = []
        self.pending_cuohe = None
        self.jiagang_tile = None
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
        mode = getattr(getattr(self, "rules", None), "dead_wall_mode", "fixed_16")
        if mode == "replacement_wall_16":
            remaining = getattr(self, "replacement_wall_remaining", self.dead_wall_count)
            return remaining > 0 and bool(self.tiles_list)
        return len(self.tiles_list) > self.dead_wall_count

    def can_establish_kong(self) -> bool:
        """判断现在成立一杠后是否仍有合法补牌。

        “每杠加一张”会先把普通侧的一张牌划入尾牌，因此必须比当前尾界
        至少多两张。若第四杠会直接触发四杠散了，则按馆规先流局，无需补牌。
        末张暗杠/加杠的无补牌例外由动作检查层单独放行，碰杠不适用。
        """
        if self.rules.four_kongs_abort and self._kong_count() >= 3:
            return True
        if self.rules.dead_wall_mode == "kong_add_one":
            return len(self.tiles_list) > self.dead_wall_count + 1
        return self.can_take_supplement_tile()

    # 公共流程读取此入口；其语义始终是“普通侧仍可摸”。
    def can_take_wall_tile(self) -> bool:
        return self.can_take_normal_tile()

    def playable_wall_count(self) -> int:
        return max(0, len(self.tiles_list) - self.dead_wall_count)

    def _take_supplement_tile(self) -> int:
        tile = self.tiles_list.pop(-1)
        mode = getattr(getattr(self, "rules", None), "dead_wall_mode", "fixed_16")
        if mode == "replacement_wall_16":
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

    def _on_kong_established(self) -> bool:
        """返回是否已按四杠散了结束本手；结束时不得移动尾界。"""
        if self.rules.four_kongs_abort and self._kong_count() >= 4:
            self.draw_reason = "four_kongs_abort"
            self.game_status = "END"
            return True
        if self.rules.dead_wall_mode == "kong_add_one":
            self.dead_wall_count += 1
        return False

    def _round_wind(self) -> int:
        return 41 + min(3, (self.current_round - 1) // 4)

    def _uses_scoring_preset(self, preset: str) -> bool:
        return self.rules.scoring_preset == preset

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
        shenlaiye = self._uses_scoring_preset("shenlaiye")
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
            "after_kong": bool(self.last_draw_after_kong),
            "last_tile": bool(is_self and self.last_draw_was_last),
            "river_bottom": bool(source == "discard" and not self.can_take_wall_tile()),
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
                        or (shenlaiye and not table_concealed_kong)
                    )
                )
                or (opening_flower_timing and player_index != 0)
            ),
            "human_win": bool(
                source == "discard"
                and (
                    discarder_first_cut
                    if shenlaiye
                    else (
                        player_index != 0
                        and player.normal_draw_count == 0
                        and not self.table_claim_or_kong
                    )
                )
            ),
            "eight_immortals_declined": bool(player.eight_immortals_declined),
        }
        if include_special and player.pending_eight_immortals:
            context["eight_immortals"] = True
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
        if source in ("discard", "rob_kong"):
            if tile is None:
                return None
            final_hand.append(tile)
            winning_tile = tile
        else:
            winning_tile = tile or player.last_drawn_tile or (final_hand[-1] if final_hand else None)

        if winning_tile is None and not (include_special and player.pending_eight_immortals):
            return None
        contexts = []
        if include_special and player.pending_eight_immortals:
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
        if self.player_list[player_index].water and self.rules.water_blocks_self_draw:
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

    def _dangerous_pattern(self, player_index: int, tile: int, *, completed: bool) -> bool:
        """判断清一色、四喜或三元的危险弃牌；completed 用于鸣牌后的持续包赔。"""
        normal = normalize_tile(tile)
        codes = [code for code in self.player_list[player_index].combination_tiles if code and code[0] != "G"]
        same_suit = [
            code for code in codes
            if normal < 40
            and all(value < 40 and value // 10 == normal // 10 for value in self._meld_structure_tiles(code))
        ]
        dangerous_meld_count = SIXTEEN_TILE_MAHJONG.meld_count
        if len(same_suit) >= (
            dangerous_meld_count if completed else dangerous_meld_count - 1
        ):
            return True
        triplets = {
            normalize_tile(int(code[1:]))
            for code in codes
            if code[0] in ("k", "g") and code[1:].isdigit()
        }
        if normal in (41, 42, 43, 44):
            return len(triplets.intersection((41, 42, 43, 44))) >= (4 if completed else 3)
        if normal in (45, 46, 47):
            return len(triplets.intersection((45, 46, 47))) >= (3 if completed else 2)
        return False

    def _tile_was_unseen_before_discard(self, discarder: int, tile: int) -> bool:
        normal = normalize_tile(tile)
        visible = 0
        for player in self.player_list:
            visible += sum(normalize_tile(value) == normal for value in player.discard_tiles)
            visible += sum(normalize_tile(value) == normal for value in player.discard_origin_tiles)
            for code in player.combination_tiles:
                if code and code[0] != "G":
                    visible += sum(value == normal for value in self._meld_structure_tiles(code))
        # execute_cut 已把当前弃牌放入牌河，因此只出现这一张即表示此前为生张。
        return visible == 1 and any(
            normalize_tile(value) == normal for value in self.player_list[discarder].discard_tiles[-1:]
        )

    def _liability_payer_for_win(
        self,
        winner: int,
        source: str,
        payer: Optional[int],
        tile: int,
    ) -> Optional[int]:
        if not self.rules.dangerous_discard_liability:
            return None
        persisted = getattr(self.player_list[winner], "liability_payer", None)
        if persisted is not None:
            return persisted
        if source not in ("discard", "rob_kong") or payer is None:
            return None
        if self._dangerous_pattern(winner, tile, completed=False):
            return payer
        if (
            source == "discard"
            and self.playable_wall_count() <= 5
            and self._tile_was_unseen_before_discard(payer, tile)
        ):
            return payer
        return None

    def _remember_claim_liability(self, player_index: int, payer: int, tile: int) -> None:
        if (
            self.rules.dangerous_discard_liability
            and self._dangerous_pattern(player_index, tile, completed=True)
        ):
            self.player_list[player_index].liability_payer = payer

    def _special_flower_detail(
        self,
        player_index: int,
        seven_robs_one: bool = False,
        *,
        opening: bool = False,
    ) -> dict:
        player = self.player_list[player_index]
        context = self._score_context(
            player_index,
            "seven_robs_one" if seven_robs_one else "self_draw",
            include_special=False,
            opening_flower_win=opening,
        )
        context["seven_robs_one" if seven_robs_one else "eight_immortals"] = True
        return self.calculation_service.Taiwan_hepai_detail(
            list(player.hand_tiles),
            player.combination_tiles,
            [],
            player.last_drawn_tile or (player.hand_tiles[-1] if player.hand_tiles else 11),
            context,
        )

    def _combine_flower_details(self, normal: Optional[dict], flower: dict) -> dict:
        if not normal:
            return flower
        result = dict(normal)
        result["tai"] = normal["tai"] + flower["tai"]
        result["capped_tai"] = (
            min(result["tai"], self.rules.tai_cap)
            if self.rules.tai_cap
            else result["tai"]
        )
        for key in ("fan_ids", "fan_names", "fan_detail"):
            result[key] = list(normal.get(key, [])) + list(flower.get(key, []))
        result["decomposition"] = list(normal.get("decomposition", [])) + [
            "special:seven_robs_one"
        ]
        result["special"] = "seven_robs_one"
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
            self._uses_scoring_preset("shenlaiye")
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
        player = self.player_list[player_index]
        waits = self.calculation_service.Taiwan_tingpai_check(
            player.hand_tiles,
            player.combination_tiles,
            self.rules_dict,
        )
        return normalize_tile(discarded_tile) in {normalize_tile(tile) for tile in waits}

    def _kong_fourth_may_win(self, player_index: int, tile: int) -> bool:
        player = self.player_list[player_index]
        pre_win = list(player.hand_tiles)
        normal = normalize_tile(tile)
        remove_index = next(
            (idx for idx, value in enumerate(pre_win) if normalize_tile(value) == normal),
            None,
        )
        if remove_index is None:
            return False
        pre_win.pop(remove_index)
        waits = self.calculation_service.Taiwan_tingpai_check(
            pre_win,
            player.combination_tiles,
            self.rules_dict,
        )
        return normal in {normalize_tile(value) for value in waits}

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
        if not self.rules.heavenly_earthly_ready_enabled:
            return
        if self.rules.scoring_preset in ("star31", "shenlaiye"):
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
        star31_window = (
            self._uses_scoring_preset("star31")
            and not self.table_claim_or_kong
            and not player.qualification_ever
            and total_discards < 8
        )
        shenlaiye_window = (
            self._uses_scoring_preset("shenlaiye")
            and not player.qualification_ever
            and player.discard_count == 0
            and not player.combination_tiles
            and not self._has_table_concealed_kong()
        )

        # 庄家的开局完整手牌没有普通摸牌槽，但第一打仍是合法的天听声明时点。
        if player.ready_locked or (not has_draw_slot(player) and not dealer_opening_cut):
            return {}

        qualification_window = (
            self.rules.heavenly_earthly_ready_enabled
            and bool(self.rules.public_ready_tai)
            and (
                player.qualification_alive
                or star31_window
                or shenlaiye_window
                or (
                    not self.table_claim_or_kong
                    and (
                        (
                            dealer_opening_cut
                            and not self._uses_scoring_preset("cml")
                        )
                        or first_normal_draw_cut
                    )
                )
            )
        )
        if not qualification_window and not self.rules.public_ready_tai:
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
        if self.rules.heavenly_earthly_ready_enabled:
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
                if self._uses_scoring_preset("star31") and not self.table_claim_or_kong:
                    if dealer_opening_cut:
                        return "heavenly"
                    if sum(item.discard_count for item in self.player_list) < 8:
                        return "earthly"
                if (
                    self._uses_scoring_preset("shenlaiye")
                    and player.discard_count == 0
                    and not player.combination_tiles
                    and not self._has_table_concealed_kong()
                ):
                    return "earthly"
                if self.table_claim_or_kong:
                    return "public" if player.declared_ready and self.rules.public_ready_tai else "none"
                if dealer_opening_cut and not self._uses_scoring_preset("cml"):
                    return "heavenly"
                if first_normal_draw_cut:
                    return "earthly"

        if player.declared_ready and self.rules.public_ready_tai:
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
        if not self.rules.heavenly_earthly_ready_enabled:
            return
        player = self.player_list[player_index]
        waits = refresh_waiting_tiles(self, player_index)
        if not waits or player.qualification_ever:
            return
        if self._uses_scoring_preset("star31"):
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
        if self._uses_scoring_preset("shenlaiye"):
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
        if self.table_claim_or_kong:
            return
        if (
            player_index == 0
            and player.discard_count == 1
            and player.normal_draw_count == 0
            and not self._uses_scoring_preset("cml")
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
            self.rules.public_ready_tai
            and player.qualification_alive
            and not player.declared_ready
        ):
            # 公开模式必须在资格产生的当次动作确认；跳过后不能延后补报。
            self._revoke_qualification(player_index)

    def decline_eight_immortals(self, player_index: int) -> None:
        player = self.player_list[player_index]
        if player.pending_eight_immortals:
            player.pending_eight_immortals = False
            player.eight_immortals_declined = True

    async def _ask_eight_immortals(
        self,
        player_index: int,
        *,
        opening: bool = False,
    ) -> None:
        player = self.player_list[player_index]
        if not player.pending_eight_immortals or self.game_status == "END":
            return
        if getattr(player, "user_id", None) == 0:
            # 摸切机器人只负责推进牌局，所有和牌机会均放弃。
            self.decline_eight_immortals(player_index)
            return
        if self.rules.eight_immortals_mode == "forced_separate":
            self.result_dict["hu_self"] = self._special_flower_detail(
                player_index,
                opening=opening,
            )
            self.accept_self_draw(player_index)
            return
        if self.rules.eight_immortals_mode == "compound":
            self.result_dict["hu_self"] = self.score_candidate(
                player_index,
                "self_draw",
                opening_flower_win=opening,
            )
            self.accept_self_draw(player_index)
            return
        if self.rules.eight_immortals_mode != "optional_separate":
            player.pending_eight_immortals = False
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
            self.decline_eight_immortals(player_index)

    # ------------------------------------------------------------------
    # 花牌与统一牌墙
    # ------------------------------------------------------------------

    def _seven_robs_one_candidate(self, owner_index: int, tile: int) -> Optional[dict]:
        """检查本次补花是否产生七抢一机会，不提前改变花牌归属。"""

        if not self.rules.seven_robs_one:
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

    async def _ask_seven_robs_one(self, special: Optional[dict]) -> Optional[dict]:
        """提示七抢一花胡；取消或超时均按普通补花继续。"""

        if special is None:
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
        candidate = self._seven_robs_one_candidate(player_index, flower)

        player.hand_tiles.pop(flower_index)
        self._publish_flower(player_index, flower)
        # 实时对局先看到普通补花；牌谱等选择结束后写入一次最终归属。
        await self._broadcast_flower(
            player_index,
            flower,
            is_drawn=is_drawn,
            record=False,
        )

        special = await self._ask_seven_robs_one(candidate)
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
            await self._complete_seven_robs_one(special, opening=opening)
            return False
        return await self._draw_tail_for_player(recipient, opening=opening) is not None

    async def _draw_tail_for_player(self, player_index: int, *, opening: bool) -> Optional[int]:
        if not self.can_take_supplement_tile():
            self.draw_reason = "flower_or_kong_without_replacement"
            self.game_status = "END"
            return None
        tile = self._take_supplement_tile()
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

    async def _complete_seven_robs_one(self, info: dict, *, opening: bool) -> None:
        winner = info["winner"]
        tile = await self._draw_tail_for_player(winner, opening=opening)
        if tile is None:
            return
        # 八花均已公开，理论上补牌不可能再是花；保留循环用于损坏牌墙的安全裁决。
        while tile in FLOWER_TILES:
            self.player_list[winner].hand_tiles.remove(tile)
            self.player_list[winner].huapai_list.append(tile)
            await self._broadcast_flower(winner, tile, is_drawn=True)
            tile = await self._draw_tail_for_player(winner, opening=opening)
            if tile is None:
                return
        self.player_list[winner].last_drawn_tile = tile
        player = self.player_list[winner]
        self.last_draw_after_kong = True
        flower_detail = self._special_flower_detail(
            winner,
            seven_robs_one=True,
            opening=opening,
        )
        normal_detail = None
        if info["mode"] == "seven_then_last":
            normal_detail = self.score_candidate(
                winner,
                "seven_robs_one",
                include_special=False,
                flowers_override=[],
            )
        else:
            own_flowers = list(player.huapai_list)
            own_flowers.remove(info["stolen"])
            normal_detail = self.score_candidate(
                winner,
                "self_draw",
                include_special=False,
                flowers_override=own_flowers,
            )
        detail = self._combine_flower_details(normal_detail, flower_detail)
        self.pending_winners = [{
            "index": winner,
            "source": "seven_robs_one",
            "payer": info["payer"],
            "tile": info["tile"],
            "hu_class": hu_action_for_player(info["payer"], winner),
            "detail": detail,
            "seven_robs_mode": info["mode"],
            "normal_detail": normal_detail,
            "flower_detail": flower_detail,
        }]
        self.hu_class = self.pending_winners[0]["hu_class"]
        self.game_status = "END"

    def _mark_eight_immortals_if_ready(
        self,
        player_index: int,
    ) -> None:
        player = self.player_list[player_index]
        if player.eight_immortals_declined or len(set(player.huapai_list)) != 8:
            return
        if self.rules.eight_immortals_mode in (
            "optional_separate",
            "forced_separate",
            "compound",
        ):
            player.pending_eight_immortals = True

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

    async def _opening_flower_replacement(self) -> None:
        """庄、南、西、北依次逐张确认、公开并补牌，直到该玩家手中无花。"""

        for owner_index in range(4):
            player = self.player_list[owner_index]
            while True:
                flower = next(
                    (tile for tile in player.hand_tiles if tile in FLOWER_TILES),
                    None,
                )
                if flower is None:
                    break

                await self._request_opening_buhua(owner_index)
                flower_index = player.hand_tiles.index(flower)
                if not await self._replace_one_flower(
                    owner_index,
                    flower_index,
                    is_drawn=False,
                    opening=True,
                ):
                    return

                self._mark_eight_immortals_if_ready(owner_index)
                await self._ask_eight_immortals(owner_index, opening=True)
                if self.game_status == "END":
                    return

        for player in self.player_list:
            player.has_draw_slot = False
            player.last_drawn_tile = None

    async def _process_drawn_flowers(self, player_index: int, origin: str) -> bool:
        player = self.player_list[player_index]
        if (
            origin == "normal"
            and self._uses_scoring_preset("cml")
            and self.last_draw_was_last
            and player.hand_tiles
            and player.hand_tiles[-1] in FLOWER_TILES
        ):
            # CML 末张摸花须公开，但不再补牌或弃牌，直接结束本手。
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
        self.supplement_win_allowed = origin != "direct_kong" or self.rules.kong_discard_self_draw
        if player.hand_tiles and player.hand_tiles[-1] in FLOWER_TILES:
            # 由标准手牌操作窗口提供 buhua；客户端的“自动补花”只决定是否自动提交。
            return True

        self._mark_eight_immortals_if_ready(player_index)
        if player.pending_eight_immortals:
            # 行牌中补齐八花也必须先处理独立花胡选择；放弃后才回到普通动作窗口。
            await self._ask_eight_immortals(player_index)
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

        self._mark_eight_immortals_if_ready(player_index)
        if player.pending_eight_immortals:
            await self._ask_eight_immortals(player_index)
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
        if winners[0]["source"] == "rob_kong":
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
        if (
            player.water
            and self.rules.water_release_by_kong
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
        if (
            player.water
            and self.rules.water_release_by_kong
            and not self._kong_fourth_may_win(player_index, normal)
        ):
            self._clear_water(player_index)
        draw_slot = has_draw_slot(player)
        is_mo = resolve_is_mo_gang(player.hand_tiles, normal, draw_slot=draw_slot)
        actual = remove_cut_tile(player.hand_tiles, target_tile, is_mo, draw_slot=draw_slot)
        if actual is None:
            return
        clear_draw_slot(player)
        player.last_drawn_tile = None
        self._revoke_qualification(
            player_index,
            keep_declared=(
                self._uses_scoring_preset("shenlaiye")
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

    async def finalize_jiagang(self) -> None:
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
            self.supplement_win_allowed = self.rules.kong_discard_self_draw
            self.game_status = "deal_card_after_gang"
        else:
            kuikae_mode = self.rules.strict_kuikae
            if action_type == "peng":
                kuikae_mode = "same_tile" if self.rules.peng_kuikae_forbidden else "none"
            player.kuikae_forbidden_tiles = strict_kuikae_forbidden(
                action_type,
                tile,
                kuikae_mode,
            )
            self.game_status = "onlycut_after_action"

    def _selected_winners(self, selected: List[Tuple[int, str]]) -> List[Tuple[int, str]]:
        selected = sorted(selected, key=lambda item: (item[0] - self.current_player_index) % 4)
        mode = self.rules.multi_win_mode
        if mode == "multi":
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
                and is_forced_declared_ready_win(self, index)
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
            if self.player_list[index].water and self.rules.water_blocks_claims:
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
                and is_forced_declared_ready_win(self, index)
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
                    "rob_kong",
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
                hepai_tile=tile if source in ("discard", "rob_kong") else None,
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
            if source in ("discard", "rob_kong") and tile is not None:
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
            if source == "rob_kong":
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
        if source == "rob_kong" and self.jiagang_tile is not None:
            self.action_dict = check_action_jiagang(self, self.jiagang_tile)
            if any(self.action_dict.values()):
                self.game_status = "waiting_action_qianggang"
            else:
                await self.finalize_jiagang()
            return

        logger.error("台湾麻将错和续局未知来源 source=%s", source)
        self.game_status = "deal_card"

    def _shenlaiye_ready_auto_jiagang_tile(self, player_index: int) -> Optional[int]:
        player = self.player_list[player_index]
        if (
            not self._uses_scoring_preset("shenlaiye")
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
        if not can_supplement and not self.last_draw_was_last:
            return None
        return tile

    async def _prepare_hand_action_after_draw(self) -> None:
        player_index = self.current_player_index
        self.result_dict = {}
        self.action_dict = check_action_hand_action(self, player_index)
        auto_jiagang_tile = self._shenlaiye_ready_auto_jiagang_tile(player_index)
        if (
            auto_jiagang_tile is not None
            and "hu_self" not in self.action_dict[player_index]
        ):
            await self.execute_jiagang(player_index, auto_jiagang_tile)
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
                    win_source="seven_robs_one",
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
            multi = len(self.pending_winners) > 1
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
                    multi_ron=multi if item["source"] == "discard" else None,
                    ron_discarder_index=item["payer"] if item["source"] == "discard" else None,
                    recycle_discard=(winner_number == len(self.pending_winners) - 1) if item["source"] == "discard" else None,
                )

                display_hand = list(player.hand_tiles)
                if item["source"] in ("discard", "rob_kong"):
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
                    hepai_tile=item["tile"] if item["source"] != "rob_kong" else None,
                    multi_ron=multi if item["source"] == "discard" else None,
                    ron_discarder_index=item["payer"] if item["source"] == "discard" else None,
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
