"""
立直麻将 GameState：
- player_index 0 恒为亲家；过庄时 back_current_num 轮转座位（与国标一致）
- 连庄：亲家（index 0）和牌 / 荒牌流局听牌 / 途中特殊流局
- 荒牌流局：本场 +1；亲家不听则过庄并继续 +1 本场
- 特殊流局：九种九牌、四风连打、四杠散了、四人立直、三家和流局（配置允许时）
- 本场+场供：自摸 100/家、荣和 300 由出铳家一次付清
- 使用 mahjong 库计算番/符/点数
- 荒片流局听牌结算：总计 3000 在听牌家均分
"""
import random
import asyncio
import time
import math
import logging
from typing import Dict, List, Optional, Any

from .action_check import (
    check_action_after_cut,
    check_action_hand_action,
    check_action_jiagang,
    refresh_waiting_tiles,
    check_jiuzhongjiupai,
)
from .wait_action import wait_action, _commit_pending_riichi
from ..public.spectator_rules import too_many_ai_for_spectator
from .init_tiles import init_riichi_tiles
from .boardcast import (
    broadcast_game_start,
    broadcast_ask_hand_action,
    broadcast_ask_other_action,
    broadcast_do_action,
    broadcast_result,
    broadcast_game_end,
    broadcast_switch_seat,
    broadcast_refresh_player_tag_list,
    broadcast_ready_status,
    broadcast_update_dora,
    broadcast_declare_riichi,
    reconnected_send_pending_ask,
)
from ..public.logic_common import next_current_num, next_current_index, back_current_num, assign_strict_final_ranks
from ..public.round_end_timing import (
    hu_result_ready_wait_seconds,
    ROUND_END_HAND_REVEAL_SEC,
    liuju_ready_wait_seconds,
)
from ..public.ready_phase import run_hu_result_ready_phase as run_synced_hu_ready_phase
from ..public.game_record_manager import (
    capture_player_entry_order,
    init_game_record,
    init_game_round,
    player_action_record_deal,
    player_action_record_round_end,
    player_action_record_liuju,
    player_action_record_ryuukyoku,
    player_action_record_new_dora,
    player_action_record_hu_riichi,
    end_game_record,
)
from ...game_calculation.game_calculation_service import GameCalculationService
from ...database.db_manager import DatabaseManager
from ..public.random_seed_manager import setup_random_seed_system
from ...database.fulu_utils import record_fulu_rounds_for_players

logger = logging.getLogger(__name__)


def _normalize(tile: int) -> int:
    if tile == 105:
        return 15
    if tile == 205:
        return 25
    if tile == 305:
        return 35
    return tile


def _count_bonus_yaku(names: List[str], base: str) -> int:
    total = sum(1 for y in names if y == base)
    prefix = f"{base}*"
    for y in names:
        if y.startswith(prefix):
            total += int(y.split("*", 1)[1])
    return total


class RiichiRecordCounter:
    def __init__(self):
        self.fulu_times = 0
        self.recorded_fans = []
        self.rank_result = 0
        self.zimo_times = 0
        self.dianhe_times = 0
        self.fangchong_times = 0
        self.fangchong_score = 0
        self.win_turn = 0
        self.win_score = 0


class RiichiPlayer:
    def __init__(self, user_id: int, username: str, tiles: list, remaining_time: int):
        self.user_id = user_id
        self.username = username
        self.is_bot = True if user_id <= 10 else False
        self.hand_tiles = tiles
        self.huapai_list = []
        self.discard_tiles = []
        self.discard_origin_tiles = []  # 理论弃牌：切牌时写入，鸣牌只从 discard_tiles 移除，振听判定仅读此字段
        self.combination_tiles = []
        self.combination_mask = []
        self.score = 25000
        self.remaining_time = remaining_time
        self.player_index = 0
        self.original_player_index = 0
        self.tag_list: List[str] = []

        self.waiting_tiles = set()
        self.record_counter = RiichiRecordCounter()
        self.score_history = []
        self.round_number_history = []

        self.title_used = 0
        self.profile_used = 0
        self.character_used = 0
        self.voice_used = 0

        # 立直相关
        self.pending_riichi = False
        self.pending_daburu = False
        self.riichi_turn: Optional[int] = None
        self.temp_furiten = False  # 同巡振听：自家放过本巡他家弃牌中的听张后，至下一次出牌后不可荣和
        self.riichi_furiten = False  # 立直振听：立直状态下放过荣和后永久振听到本局结束
        self.ryuukyoku_declared_tenpai = True  # 荒牌流局时是否按听牌申报，立直家固定视为听牌
        self.riichi_candidate_cuts: Dict[int, List[int]] = {}
        # 吃牌候选：{"chi_left": [[r1, r2], ...], ...}，含赤 5 真实 ID，供客户端展示可选吃法
        self.chi_candidates: Dict[str, List[List[int]]] = {}
        # 食替禁切：吃/碰后到本家切出前，不可丢回的真实牌 ID 列表（包含吃来源 + 两面搭子筋）
        self.kuikae_forbidden_tiles: List[int] = []
        # 河中每张弃牌是否横置（含立直宣告与"立直牌被吃后下一张续横"两种情形），与 discard_tiles 同序
        self.discard_riichi_flags: List[bool] = []
        # 立直家本应横置的下一张弃牌待标记位：宣告立直时置 True；本次切完归 False；
        # 若立直宣告的横置弃牌被他家吃/碰则在 chi/peng/gang 处理处再次置 True，使下一张续横
        self.riichi_marker_pending: bool = False
        self.has_draw_slot = False

    def get_tile(self, tiles_list, *, mark_draw_slot: bool = True):
        element = tiles_list.pop(0)
        self.hand_tiles.append(element)
        if mark_draw_slot:
            self.has_draw_slot = True

    def get_gang_tile(self, tiles_list, gamestate):
        # 从王牌区取岭上牌（王牌最靠右的那张）；同时从牌山头取一张补到王牌
        element = tiles_list.pop(-1)
        self.hand_tiles.append(element)
        self.has_draw_slot = True
        gamestate.rinshan_count += 1


class RiichiGameState:
    def __init__(self, game_server, room_data: dict, calculation_service: GameCalculationService, db_manager: DatabaseManager, gamestate_id: str):
        self.game_server = game_server
        self.calculation_service = calculation_service
        self.db_manager = db_manager
        self.gamestate_id = gamestate_id
        self.game_record: Dict[str, Any] = {}
        self.game_task: Optional[asyncio.Task] = None

        self.player_list: List[RiichiPlayer] = []
        player_settings = room_data.get("player_settings", {})
        for user_id in room_data["player_list"]:
            setting = player_settings.get(user_id, {})
            if user_id == 0:
                username = "麻雀罗伯特"
            elif user_id == 2:
                username = "牌效罗伯特"
            else:
                username = setting.get("username", f"用户{user_id}")
            p = RiichiPlayer(user_id, username, [], room_data["round_timer"])
            p.title_used = setting.get("title_id", 1)
            p.profile_used = setting.get("profile_image_id", 1)
            p.character_used = setting.get("character_id", 1)
            p.voice_used = setting.get("voice_id", 1)
            self.player_list.append(p)

        self.room_id = room_data["room_id"]
        self.tips = room_data["tips"]
        self.max_round = room_data["game_round"]  # 1=东风 2=半庄 3=东西 4=全庄
        self.step_time = room_data["step_timer"]
        self.round_time = room_data["round_timer"]
        self.room_rule = room_data["room_rule"]
        self.room_type = room_data["room_type"]
        self.sub_rule = room_data.get("sub_rule") or "riichi/standard"

        self.room_random_seed = room_data.get("random_seed", 0)
        self.open_cuohe = room_data.get("open_cuohe", False)
        self.show_moqie_hint = room_data.get("show_moqie_hint", False)
        self.hepai_limit = room_data.get("hepai_limit", 1)
        self.tourist_limit = room_data.get("tourist_limit", False)
        self.allow_spectator_config = room_data.get("allow_spectator", True)

        # 立直专属配置
        self.red_dora: bool = room_data.get("red_dora", True)
        self.allow_kuikae: bool = room_data.get("allow_kuikae", False)  # 允许食替（吃什么打什么）
        self.hepai_way: str = room_data.get("hepai_way", "multi_ron")  # head_bump / multi_ron / three_ron_abort
        self.open_xiru: bool = room_data.get("open_xiru", True)
        self.open_tobi: bool = room_data.get("open_tobi", True)

        self._pending_ron_claims: dict = {}
        self.multi_ron_queue: Optional[list] = None
        self._multi_ron_ready_done: bool = False
        self._multi_ron_any_oya_win: bool = False

        self.isPlayerSetRandomSeed = False

        self.tiles_list = []
        self.current_player_index = 0
        self.xunmu = 1
        self.master_seed: int = 0
        self.commitment: int = 0
        self.salt = ""
        self.round_random_seed = 0
        self.game_status = "waiting"
        self.server_action_tick = 0
        self.player_action_tick = 0
        self.current_round = 1
        self.round_index = 1
        self.result_dict: Dict[str, Any] = {}
        self.hu_class: Optional[str] = None
        self.ron_player_index: Optional[int] = None
        self.jiagang_tile: Optional[int] = None

        self.action_events: Dict[int, asyncio.Event] = {i: asyncio.Event() for i in range(4)}
        self.action_queues: Dict[int, asyncio.Queue] = {i: asyncio.Queue() for i in range(4)}
        self.waiting_players_list: List[int] = []
        self.action_dict: Dict[int, list] = {i: [] for i in range(4)}

        self.action_priority: Dict[str, int] = {
            "hu_self": 6, "hu_first": 5, "hu_second": 4, "hu_third": 3,
            "peng": 2, "gang": 2,
            "chi_left": 1, "chi_mid": 1, "chi_right": 1,
            "jiuzhongjiupai": 6,
            "ready": 0,
            "pass": 0, "cut": 0, "riichi_cut": 0, "angang": 0, "jiagang": 0, "deal_tile": 0, "deal_gang_tile": 0,
        }

        self.backward_tiles_list_type = "single"
        self.dead_wall_count = 14

        # 场况
        self.honba: int = 0
        self.riichi_sticks: int = 0
        self.dora_indicators: List[int] = []
        self.kan_dora_indicators: List[int] = []
        self.ura_dora_indicators: List[int] = []
        self.ura_kan_dora_indicators: List[int] = []
        self.rinshan_count: int = 0

        # 四风连打检测
        self._first_round_discards: List[int] = []
        self._first_round_valid: bool = True
        # 四杠散了检测
        self.total_kans: int = 0
        # 立直规则：明杠/加杠需在打完牌后才翻宝牌指示牌；暗杠立刻翻。该计数记录"已杠但尚未翻开的指示牌数量"。
        self._pending_kan_dora_count: int = 0
        # 刚刚完成的杠的类型："ankan" 立翻；"daiminkan"/"shouminkan" 延后到下张弃牌后翻。
        self._last_kan_type: Optional[str] = None
        # 四杠散了：4 杠且由 ≥2 家开出 → 下一张弃牌无人和牌则流局。
        self._pending_four_kan_abort: bool = False
        # 错和：和牌番数低于 hepai_limit 时触发，向其余 3 家各赔 3000（合计 9000）并重打本局
        self._cuohe_triggered: bool = False

        self.Debug = False

        self.spectator_enabled = self.allow_spectator_config and not too_many_ai_for_spectator(self.player_list)
        from .spectator_manager import SpectatorManager
        self.spectator_manager = SpectatorManager(self, delay=180.0, enabled=self.spectator_enabled)
        # 实时观战者（与传统观战独立的低延迟管道，由 FriendManager 维护）
        self.realtime_spectators = []

    async def send_to_realtime_spectators(self, player_index: int, response):
        """把同一份 Response 转发给挂在 player_index 座位上的所有实时观战者。"""
        from ..public.spectator_rules import deliver_realtime_spectator_message
        await deliver_realtime_spectator_message(self, player_index, response)

    async def player_disconnect(self, user_id: int):
        for p in self.player_list:
            if p.user_id == user_id:
                if "offline" not in p.tag_list:
                    p.tag_list.append("offline")
                    await broadcast_refresh_player_tag_list(self)
                break
        non_ai = [p for p in self.player_list if p.user_id >= 10]
        if non_ai and all("offline" in p.tag_list for p in non_ai):
            await self.game_server.gamestate_manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)

    async def player_reconnect(self, user_id: int):
        for p in self.player_list:
            if p.user_id == user_id:
                if "offline" in p.tag_list:
                    p.tag_list.remove("offline")
                    await broadcast_refresh_player_tag_list(self)
                if user_id in self.game_server.user_id_to_connection:
                    from ...response import Response, GameInfo
                    from .boardcast import _build_base_game_info, _build_player_info
                    conn = self.game_server.user_id_to_connection[user_id]
                    base = _build_base_game_info(self)
                    viewer_pi = next((pp.player_index for pp in self.player_list if pp.user_id == user_id), 0)
                    infos = [_build_player_info(pp, user_id, viewer_pi) for pp in self.player_list]
                    game_info = GameInfo(**{**base, "players_info": infos, "self_hand_tiles": None})
                    response = Response(type="gamestate/riichi/game_start", success=True, message="重连成功", game_info=game_info)
                    await conn.websocket.send_json(response.dict(exclude_none=True))
                    await reconnected_send_pending_ask(self, user_id)
                break

    async def cleanup_game_state(self):
        await self.spectator_manager.cleanup()
        if self.game_task and not self.game_task.done():
            self.game_task.cancel()
            try:
                await self.game_task
            except asyncio.CancelledError:
                pass
            except Exception as e:
                logger.error(f"取消 riichi 游戏循环出错: {e}")

    async def run_game_loop(self):
        try:
            await self.game_loop_riichi()
        except asyncio.CancelledError:
            raise
        except Exception as e:
            logger.error(f"riichi 游戏循环异常: {e}", exc_info=True)
            await self.cleanup_game_state()

    async def game_loop_riichi(self):
        user_seed = self.room_random_seed if self.room_random_seed else None
        self.master_seed, self.salt, self.commitment, self.isPlayerSetRandomSeed = setup_random_seed_system(user_seed)
        capture_player_entry_order(self)
        # 随机座位
        rng = random.Random(self.master_seed)
        rng.shuffle(self.player_list)
        for i, p in enumerate(self.player_list):
            p.player_index = i
            p.original_player_index = i
            p.score = self._starting_score()
        self.player_list.sort(key=lambda x: x.player_index)

        init_game_record(self)
        self.game_record["game_title"]["sub_rule"] = self.sub_rule
        self.game_record["game_title"]["red_dora"] = self.red_dora
        if not self._is_langyong():
            self.game_record["game_title"]["allow_kuikae"] = self.allow_kuikae
        self.game_record["game_title"]["hepai_way"] = self.hepai_way
        self.game_record["game_title"]["open_xiru"] = self.open_xiru
        self.game_record["game_title"]["open_tobi"] = self.open_tobi

        scheduled_rounds = self.max_round * 4
        while True:
            if self.current_round > scheduled_rounds:
                if self.max_round >= 4 or not self.open_xiru:
                    break
            init_riichi_tiles(self)
            self._first_round_discards = []
            self._first_round_valid = True
            self.total_kans = 0
            self._pending_kan_dora_count = 0
            self._last_kan_type = None
            self._pending_four_kan_abort = False
            self._cuohe_triggered = False

            await self.broadcast_game_start()
            await self._broadcast_langyong_tags_if_changed()
            init_game_round(self)

            self.game_status = "waiting_hand_action"
            self.current_player_index = 0
            self.dihe_possible = True
            self.hu_class = None
            self.result_dict = {}
            self.ron_player_index = None
            self.multi_ron_queue = None
            self._multi_ron_ready_done = False
            self._multi_ron_any_oya_win = False

            # 亲家（player_index=0）摸第 14 张
            self.refresh_waiting_tiles(self.current_player_index)
            self.player_list[0].get_tile(self.tiles_list)
            player_action_record_deal(self, deal_tile=self.player_list[0].hand_tiles[-1], deal_type="d")
            await self.broadcast_do_action(
                action_list=["deal_tile"],
                action_player=0,
                deal_tile=self.player_list[0].hand_tiles[-1],
            )

            # 开局先计算常规动作（含天和自摸），再叠加九种九牌选项（若可宣）
            self.action_dict = check_action_hand_action(self, self.current_player_index, is_first_action=True)
            if self._can_declare_kyuushu(0):
                self.action_dict[0].append("jiuzhongjiupai")
            await self.broadcast_ask_hand_action()
            await self.wait_action()

            while self.game_status != "END":
                match self.game_status:
                    case "deal_card":
                        if len(self.tiles_list) <= self.dead_wall_count:
                            self.game_status = "END"
                            self.hu_class = "ryuukyoku"
                            break
                        if await self._check_four_wind_abort():
                            self.game_status = "END"
                            self.hu_class = "four_wind_abort"
                            break
                        if await self._check_four_player_riichi_abort():
                            self.game_status = "END"
                            self.hu_class = "four_riichi_abort"
                            break
                        next_current_index(self)
                        self.refresh_waiting_tiles(self.current_player_index)
                        self.player_list[self.current_player_index].get_tile(self.tiles_list)
                        player_action_record_deal(self, deal_tile=self.player_list[self.current_player_index].hand_tiles[-1], deal_type="d")
                        await self.broadcast_do_action(
                            action_list=["deal_tile"],
                            action_player=self.current_player_index,
                            deal_tile=self.player_list[self.current_player_index].hand_tiles[-1],
                        )
                        self.action_dict = check_action_hand_action(self, self.current_player_index)
                        # 九种九牌：首巡且全员无鸣牌、且当前玩家尚未切过牌
                        if self._can_declare_kyuushu(self.current_player_index):
                            self.action_dict[self.current_player_index].append("jiuzhongjiupai")
                        self.game_status = "waiting_hand_action"

                    case "deal_card_after_gang":
                        self.total_kans += 1
                        # 四杠散了判定：≥2 家合计 4 杠 → 等到本次岭上摸牌并打完后再判定流局
                        if self.total_kans >= 4:
                            players_with_kan = set()
                            for p in self.player_list:
                                for c in p.combination_tiles:
                                    if c and c[0] in ("g", "G"):
                                        players_with_kan.add(p.player_index)
                            if len(players_with_kan) >= 2:
                                self._pending_four_kan_abort = True

                        self.refresh_waiting_tiles(self.current_player_index)
                        self.player_list[self.current_player_index].get_gang_tile(self.tiles_list, self)
                        player_action_record_deal(self, deal_tile=self.player_list[self.current_player_index].hand_tiles[-1], deal_type="gd")
                        await self.broadcast_do_action(
                            action_list=["deal_gang_tile"],
                            action_player=self.current_player_index,
                            deal_tile=self.player_list[self.current_player_index].hand_tiles[-1],
                        )
                        # 暗杠：立即翻宝牌指示牌；明杠/加杠：延后到打完牌后翻，此处仅累加待翻数。
                        if self._last_kan_type == "ankan":
                            await self._reveal_kan_dora()
                        else:
                            self._pending_kan_dora_count += 1
                        self._last_kan_type = None
                        self.action_dict = check_action_hand_action(self, self.current_player_index, is_get_gang_tile=True)
                        self.game_status = "waiting_hand_action"

                    case "waiting_hand_action":
                        await self.broadcast_ask_hand_action()
                        await self.wait_action()

                    case "waiting_action_after_cut":
                        await self.broadcast_ask_other_action()
                        await self.wait_action()

                    case "waiting_action_qianggang":
                        await self.broadcast_ask_other_action()
                        await self.wait_action()

                    case "onlycut_after_action":
                        self.action_dict = {i: [] for i in range(4)}
                        self.action_dict[self.current_player_index] = ["cut"]
                        self.game_status = "waiting_hand_action"

                    case _:
                        logger.error(f"riichi 未知状态: {self.game_status}")
                        break

            # 结算
            scores_before = {p.original_player_index: p.score for p in self.player_list}
            await self._settle_round()

            record_fulu_rounds_for_players(self.player_list)

            for p in self.player_list:
                delta = p.score - scores_before[p.original_player_index]
                if delta > 0:
                    p.score_history.append(f"+{delta}")
                elif delta < 0:
                    p.score_history.append(f"-{abs(delta)}")
                else:
                    p.score_history.append("0")
                p.round_number_history.append(self.current_round)

            player_action_record_round_end(self)

            # 准备阶段
            # 与客户端 RoundEndPresentation / EndLiujuPanel / PenaltyPanel 的演出时长保持同步，
            # 避免动画结束后还要在空白界面再等几秒。和牌画面较长（需阅读番符），其它流局以演出时长 + 0.5s 余量为准。
            if self._multi_ron_ready_done:
                self._multi_ron_ready_done = False
            elif self.hu_class in ("hu_self", "hu_first", "hu_second", "hu_third"):
                settle_result = self.result_dict.get(self.hu_class) or {}
                fan_count = len(settle_result.get("yaku", []))
                await run_synced_hu_ready_phase(self, fan_count, broadcast_ready_status)
            elif self.hu_class == "ryuukyoku":
                tenpai_indexes = [p.player_index for p in self.player_list if self._is_ryuukyoku_tenpai(p)]
                noten_indexes = [p.player_index for p in self.player_list if not self._is_ryuukyoku_tenpai(p)]
                wait_time = liuju_ready_wait_seconds(
                    include_hand_reveal=bool(tenpai_indexes),
                    has_draw_noten_penalty=bool(tenpai_indexes and noten_indexes),
                )
            elif self.hu_class in (
                "jiuzhongjiupai",
                "four_wind_abort",
                "four_kan_abort",
                "four_riichi_abort",
                "three_ron_abort",
            ):
                wait_time = liuju_ready_wait_seconds()
            else:
                wait_time = 8.0

            if self.hu_class not in ("hu_self", "hu_first", "hu_second", "hu_third"):
                deadline = time.time() + wait_time
                self.action_dict = {}
                for p in self.player_list:
                    if p.user_id <= 10:
                        self.action_dict[p.player_index] = []
                    else:
                        self.action_dict[p.player_index] = ["ready"]
                        p.remaining_time = math.ceil(wait_time)
                self.game_status = "waiting_ready"
                await broadcast_ready_status(self)
                while any(self.action_dict[i] for i in self.action_dict):
                    for p in self.player_list:
                        if self.action_dict.get(p.player_index):
                            p.remaining_time = max(0, int(deadline - time.time()))
                    if await wait_action(self) is False:
                        break

            # 决定是否连庄
            last_renchan = False
            if self._cuohe_triggered:
                # 错和：亲家不变、本场不增、仅更新随机种子；等同于"不加本场的连庄"重打
                self.round_index += 1
                last_renchan = True
            else:
                winner_index = self._hu_winner_index()
                oya_win = winner_index == 0 or self._multi_ron_any_oya_win
                self._multi_ron_any_oya_win = False
                oya_tenpai = False
                if self.hu_class == "ryuukyoku":
                    oya_tenpai = self._is_ryuukyoku_tenpai(self.player_list[0])
                renchan = oya_win or (self.hu_class == "ryuukyoku" and oya_tenpai)
                # 特殊流局也连庄
                if self.hu_class in ("jiuzhongjiupai", "four_wind_abort", "four_kan_abort", "four_riichi_abort", "three_ron_abort"):
                    renchan = True
                last_renchan = renchan

                if renchan:
                    self.honba += 1
                    self.round_index += 1
                else:
                    if self.hu_class == "ryuukyoku":
                        self.honba += 1
                    else:
                        self.honba = 0
                    self.current_round += 1
                    self.round_index += 1
                    self._rotate_seats()

            if not self._cuohe_triggered and self._riichi_match_should_end(last_renchan):
                break

            # 清理局内状态
            for p in self.player_list:
                p.hand_tiles = []
                p.huapai_list = []
                p.discard_tiles = []
                p.discard_origin_tiles = []
                p.discard_riichi_flags = []
                p.combination_tiles = []
                p.combination_mask = []
                p.waiting_tiles = set()
                p.remaining_time = self.round_time
                p.pending_riichi = False
                p.pending_daburu = False
                p.riichi_turn = None
                p.temp_furiten = False
                p.riichi_furiten = False
                p.ryuukyoku_declared_tenpai = True
                p.riichi_candidate_cuts = {}
                p.chi_candidates = {}
                p.kuikae_forbidden_tiles = []
                p.riichi_marker_pending = False
                for tag in list(p.tag_list):
                    if tag in ("riichi", "daburu_riichi", "ippatsu", "furiten") or tag.startswith("langyong_"):
                        p.tag_list.remove(tag)

            logger.info(f"进入下一局 current_round={self.current_round} honba={self.honba} riichi_sticks={self.riichi_sticks}")

        end_game_record(self)
        assign_strict_final_ranks(self.player_list)
        await self.broadcast_game_end()

        if hasattr(self, "spectator_manager"):
            await self.spectator_manager.send_final_record_and_close()

        # 数据库保存（简化：复用存储接口扩展后再接入）
        if hasattr(self.db_manager, "store_riichi_game_record"):
            try:
                match_type = f"{self.max_round}/4"
                game_id = self.db_manager.store_riichi_game_record(self.game_record, self.player_list, self.room_type, match_type)
                has_ai = any(p.user_id <= 10 for p in self.player_list)
                if not has_ai and game_id:
                    total_rounds = len(self.game_record.get("game_round", {}))
                    if hasattr(self.db_manager, "store_riichi_game_stats"):
                        self.db_manager.store_riichi_game_stats(game_id, self.player_list, self.room_type, self.max_round, total_rounds)
                    if hasattr(self.db_manager, "store_riichi_fan_stats"):
                        self.db_manager.store_riichi_fan_stats(game_id, self.player_list, self.room_type, self.max_round)
            except Exception as e:
                logger.error(f"riichi 存储牌谱异常: {e}")

        await self.game_server.gamestate_manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)
        if self.room_type == "match":
            await self.game_server.room_manager.destroy_room(self.room_id)
        else:
            await self.game_server.room_manager.finish_custom_game_room(self.room_id)

    # ========== 浪涌麻将（riichi/langyong）子规则 ==========

    def _is_langyong(self) -> bool:
        """浪涌麻将：日麻泛用子规则——可食替、吃碰杠累计影响结算倍数、起始 50000。"""
        return self.sub_rule == "riichi/langyong"

    def _kuikae_enabled(self) -> bool:
        """是否启用食替禁切。浪涌由子规则内置可食替；标准日麻由 allow_kuikae 房间项控制。"""
        if self._is_langyong():
            return False
        return not self.allow_kuikae

    def _can_declare_riichi_by_score(self, score: int) -> bool:
        """击飞开启时立直后点数不得 < 0，须 score >= 1000（恰 1000 可立直变 0 分）；关闭击飞时允许负分立直。"""
        if self.open_tobi:
            return score >= 1000
        return True

    def _starting_score(self) -> int:
        return 50000 if self._is_langyong() else 25000

    def _xiru_target_score(self) -> int:
        """西入延长/终了的目标分，随起始点数等比缩放（25000→30000，50000→60000）。"""
        return 60000 if self._is_langyong() else 30000

    def _langyong_call_count(self, player) -> int:
        """该玩家本局吃/碰/杠（含暗杠）次数；每个副露算一次（加杠由碰升级仍记一次）。"""
        return len(player.combination_tiles)

    def _langyong_surge_active(self) -> bool:
        """浪潮：全场累计吃碰杠次数 ≥ 阈值（日麻 4）时进入，所有人结算倍数再 +1。"""
        total = sum(self._langyong_call_count(p) for p in self.player_list)
        return total >= 4

    def _langyong_multiplier(self, payer_index: int, winner_index: int) -> int:
        """浪涌结算倍数 = 1 + 支付家吃碰杠次数 + 和牌家吃碰杠次数 + 浪潮加成(0/1)。"""
        payer = self.player_list[payer_index]
        winner = self.player_list[winner_index]
        surge = 1 if self._langyong_surge_active() else 0
        return 1 + self._langyong_call_count(payer) + self._langyong_call_count(winner) + surge

    def sync_langyong_tags(self) -> bool:
        """浪涌麻将：同步各玩家鸣牌次数 tag（langyong_N）及浪潮 tag（langyong_wave）。"""
        if not self._is_langyong():
            return False
        changed = False
        surge = self._langyong_surge_active()
        for p in self.player_list:
            count = self._langyong_call_count(p)
            new_count_tag = f"langyong_{count}"
            old_langyong = [t for t in p.tag_list if t.startswith("langyong_")]
            for t in list(old_langyong):
                if t != new_count_tag and t != "langyong_wave":
                    p.tag_list.remove(t)
                    changed = True
            if new_count_tag not in p.tag_list:
                p.tag_list.append(new_count_tag)
                changed = True
            has_wave = "langyong_wave" in p.tag_list
            if surge and not has_wave:
                p.tag_list.append("langyong_wave")
                changed = True
            elif not surge and has_wave:
                p.tag_list.remove("langyong_wave")
                changed = True
        return changed

    async def _broadcast_langyong_tags_if_changed(self) -> None:
        if self.sync_langyong_tags():
            await self.broadcast_refresh_player_tag_list()

    # ========== 对局终了（西入 / 击飞）==========

    def _riichi_oya_rank(self) -> int:
        ranked = sorted(self.player_list, key=lambda p: p.score, reverse=True)
        for i, p in enumerate(ranked):
            if p.player_index == 0:
                return i + 1
        return 4

    def _riichi_match_should_end(self, renchan: bool) -> bool:
        """非全庄西入延长战与击飞：判定整场是否在本局结束后终了。"""
        if self.open_tobi and any(p.score < 0 for p in self.player_list):
            return True
        scheduled = self.max_round * 4
        if self.max_round >= 4 or not self.open_xiru:
            return False
        if self.current_round < scheduled:
            return False
        if self.current_round == scheduled and renchan:
            return False
        oya = self.player_list[0]
        oya_rank = self._riichi_oya_rank()
        target = self._xiru_target_score()
        anyone_target = any(p.score >= target for p in self.player_list)
        if oya_rank == 1 and oya.score >= target:
            return True
        if not renchan and anyone_target:
            return True
        return False

    # ========== 结算 ==========

    def _hu_winner_index(self) -> Optional[int]:
        if self.hu_class == "hu_self":
            return self.current_player_index
        if self.hu_class in ("hu_first", "hu_second", "hu_third"):
            return self.ron_player_index
        return None

    def _rotate_seats(self):
        """过庄：player_index 0 恒为亲家，轮转座位编号。"""
        for p in self.player_list:
            p.player_index = back_current_num(p.player_index)
        self.player_list.sort(key=lambda x: x.player_index)

    async def _settle_round(self):
        self.hepai_player_index = None
        multi_queue = getattr(self, "multi_ron_queue", None)
        if multi_queue and len(multi_queue) > 1:
            await self._settle_multi_ron_sequence(multi_queue)
            self.multi_ron_queue = None
        elif self.hu_class in ("hu_self", "hu_first", "hu_second", "hu_third"):
            result = self.result_dict.get(self.hu_class) or {}
            # 错和：①牌型成立但无役（no_yaku，0番不能正常和牌）②和牌番数低于起和番数。
            # 两者均仅在开启错和（open_cuohe）时才允许宣告并走错和罚分流程。
            needs_cuohe = bool(result.get("no_yaku")) or int(result.get("han", 0)) < int(self.hepai_limit)
            if needs_cuohe and self.open_cuohe:
                await self._settle_cuohe()
            else:
                await self._settle_hu()
        elif self.hu_class == "jiuzhongjiupai":
            await broadcast_result(self, hepai_player_index=None, hu_class="jiuzhongjiupai")
            player_action_record_liuju(self)
        elif self.hu_class == "four_wind_abort":
            await broadcast_result(self, hu_class="four_wind_abort")
            player_action_record_liuju(self)
        elif self.hu_class == "four_kan_abort":
            await broadcast_result(self, hu_class="four_kan_abort")
            player_action_record_liuju(self)
        elif self.hu_class == "four_riichi_abort":
            await broadcast_result(self, hu_class="four_riichi_abort")
            player_action_record_liuju(self)
        elif self.hu_class == "three_ron_abort":
            await broadcast_result(self, hu_class="three_ron_abort")
            player_action_record_liuju(self)
        else:
            # 荒牌流局：听牌家均分 3000 分；供托立直棒留场，先结算尚未提交的立直供托
            _commit_pending_riichi(self)
            self.hu_class = "ryuukyoku"
            changes = await self._settle_ryuukyoku()
            tenpai_indexes = [p.player_index for p in self.player_list if self._is_ryuukyoku_tenpai(p)]
            noten_indexes = [p.player_index for p in self.player_list if not self._is_ryuukyoku_tenpai(p)]
            has_penalty = bool(tenpai_indexes and noten_indexes)
            tenpai_tiles = {p.player_index: sorted(p.waiting_tiles) for p in self.player_list if self._is_ryuukyoku_tenpai(p)}
            tenpai_hands = {p.player_index: list(p.hand_tiles) for p in self.player_list if self._is_ryuukyoku_tenpai(p)}
            await broadcast_result(
                self,
                hu_class="ryuukyoku",
                # player_to_score 按当局座位 player_index 为键，score_changes 按 original_player_index 为键
                # （与国标约定及客户端解析器 TryGetAfterScore/TryGetDelta 的偏好一致），避免过庄后座位错位。
                score_changes={p.original_player_index: changes.get(p.player_index, 0) for p in self.player_list},
                player_to_score={p.player_index: p.score for p in self.player_list},
                tenpai_tiles=tenpai_tiles,
                tenpai_hands=tenpai_hands,
                exhaustive_penalty=has_penalty,
            )

    async def _settle_multi_ron_sequence(self, queue: list) -> None:
        """多家荣和：一起喊荣后按 hu_first → hu_second → hu_third 顺序依次结算，每家确认后再下一家。"""
        from .boardcast import broadcast_do_action

        self._multi_ron_any_oya_win = any(player_index == 0 for player_index, _ in queue)

        for index, (winner_index, hu_class) in enumerate(queue):
            self.hu_class = hu_class
            self.ron_player_index = winner_index
            is_first = index == 0

            await broadcast_do_action(
                self,
                action_list=[hu_class],
                action_player=winner_index,
                silent=True,
            )

            result = self.result_dict.get(hu_class)
            if not result:
                logger.error(f"多家荣和结算缺少结果: {hu_class} winner={winner_index}")
                continue

            fan_count = len(result.get("yaku", []))
            await self._settle_hu(
                apply_honba=is_first,
                apply_riichi_sticks=is_first,
            )
            await run_synced_hu_ready_phase(self, fan_count, broadcast_ready_status)

        self._multi_ron_ready_done = True

    async def _settle_hu(self, apply_honba: bool = True, apply_riichi_sticks: bool = True):
        result = self.result_dict.get(self.hu_class)
        if not result:
            logger.error("和牌结算未获得结果")
            return

        han = result["han"]
        fu = result["fu"]
        yaku = result["yaku"]
        aka_count = result.get("aka_count", 0)
        score_info = result.get("cost") or {}

        if self.hu_class == "hu_self":
            winner_index = self.current_player_index
        else:
            winner_index = self.ron_player_index
        self.hepai_player_index = winner_index
        is_dealer_win = (winner_index == 0)

        # 浪涌麻将：吃碰杠累计影响结算倍数，仅作用于和牌基础点（本场棒/立直棒不乘）
        langyong = self._is_langyong()

        def _mult(payer_index: int) -> int:
            return self._langyong_multiplier(payer_index, winner_index) if langyong else 1

        score_changes = {i: 0 for i in range(4)}
        langyong_multiplier = None
        langyong_scored_points = None
        if self.hu_class == "hu_self":
            # 自摸：每家支付，本场每家 +100
            if is_dealer_win:
                main_each = score_info.get("main", 0)  # 每家分摊
                for i in range(4):
                    if i == winner_index:
                        continue
                    pay = main_each * _mult(i)
                    score_changes[i] -= pay + self.honba * 100
                    score_changes[winner_index] += pay + self.honba * 100
            else:
                main = score_info.get("main", 0)  # 亲家支付
                add = score_info.get("additional", 0)  # 子家支付
                for i in range(4):
                    if i == winner_index:
                        continue
                    base = main if i == 0 else add
                    pay = base * _mult(i)
                    score_changes[i] -= pay + self.honba * 100
                    score_changes[winner_index] += pay + self.honba * 100
        else:
            # 荣和：出铳家全额支付；本场棒仅第一家荣和者收取
            loser_index = self.current_player_index
            total = score_info.get("main", 0) * _mult(loser_index)
            honba_bonus = self.honba * 300 if apply_honba else 0
            score_changes[loser_index] -= total + honba_bonus
            score_changes[winner_index] += total + honba_bonus

        if langyong:
            scored = 0
            max_mult = 1
            if self.hu_class == "hu_self":
                if is_dealer_win:
                    main_each = score_info.get("main", 0)
                    for i in range(4):
                        if i == winner_index:
                            continue
                        m = _mult(i)
                        max_mult = max(max_mult, m)
                        scored += main_each * m
                else:
                    main = score_info.get("main", 0)
                    add = score_info.get("additional", 0)
                    for i in range(4):
                        if i == winner_index:
                            continue
                        base = main if i == 0 else add
                        m = _mult(i)
                        max_mult = max(max_mult, m)
                        scored += base * m
            else:
                loser_index = self.current_player_index
                m = _mult(loser_index)
                max_mult = m
                scored = score_info.get("main", 0) * m
            if max_mult > 1:
                langyong_multiplier = max_mult
                langyong_scored_points = scored

        # 场供立直棒给予第一家荣和胜者
        collected = self.riichi_sticks if apply_riichi_sticks else 0
        if apply_riichi_sticks:
            score_changes[winner_index] += collected * 1000
            self.riichi_sticks = 0

        # 应用
        for p in self.player_list:
            p.score += score_changes[p.player_index]

        winner = self.player_list[winner_index]
        stats_yaku = [y for y in yaku if y != "错和"]
        winner.record_counter.recorded_fans.append(stats_yaku)
        winner.record_counter.win_score += han
        winner.record_counter.win_turn += self.xunmu
        if self.hu_class == "hu_self":
            winner.record_counter.zimo_times += 1
        else:
            winner.record_counter.dianhe_times += 1
            self.player_list[self.current_player_index].record_counter.fangchong_times += 1
            self.player_list[self.current_player_index].record_counter.fangchong_score += han

        # 记录
        player_action_record_hu_riichi(
            self,
            hepai_player_index=winner_index,
            hu_class=self.hu_class,
            han=han,
            fu=fu,
            yaku=yaku,
            score_changes=[score_changes.get(i, 0) for i in range(4)],
            dora_indicators=list(self.dora_indicators) + list(self.kan_dora_indicators),
            ura_dora_indicators=list(self.ura_dora_indicators + self.ura_kan_dora_indicators) if "riichi" in self.player_list[winner_index].tag_list else [],
            aka_count=aka_count,
            honba=self.honba,
            riichi_sticks_collected=collected,
        )

        await broadcast_result(
            self,
            hepai_player_index=winner_index,
            # player_to_score 按当局座位 player_index 为键（与 hepai_player_index / 客户端 indexToPosition 一致）；
            # score_changes 按 original_player_index 为键。与国标 boardcast 约定保持一致，避免过庄后座位错位。
            player_to_score={p.player_index: p.score for p in self.player_list},
            hu_score=result.get("score", 0),
            hu_fan=yaku,
            hu_class=self.hu_class,
            hepai_player_hand=self.player_list[winner_index].hand_tiles,
            hepai_player_combination_mask=self.player_list[winner_index].combination_mask,
            han=han,
            fu=fu,
            aka_count=aka_count,
            dora_count=_count_bonus_yaku(yaku, "宝牌"),
            ura_dora_count=_count_bonus_yaku(yaku, "里宝牌"),
            dora_indicators=list(self.dora_indicators) + list(self.kan_dora_indicators),
            ura_dora_indicators=list(self.ura_dora_indicators + self.ura_kan_dora_indicators) if "riichi" in self.player_list[winner_index].tag_list else [],
            honba=self.honba,
            riichi_sticks_collected=collected,
            score_changes={p.original_player_index: score_changes[p.player_index] for p in self.player_list},
            langyong_multiplier=langyong_multiplier,
            langyong_scored_points=langyong_scored_points,
            silent=True,
        )

    async def _settle_cuohe(self):
        """错和：宣告的和牌番数低于起和番数时，先照常展示牌型与番种再附"错和"标签，
        由错和方对其余 3 家各赔 3000 点（合计 9000）；本局重打，亲家与本场棒不变。"""
        result = self.result_dict.get(self.hu_class) or {}
        no_yaku = bool(result.get("no_yaku"))
        # 无役错和：牌型成立但没有任何役，固定显示"错和1番"，其后再列宝牌/赤宝/里宝（库已写入 yaku）。
        han = 1 if no_yaku else int(result.get("han", 0))
        fu = int(result.get("fu", 0))
        yaku = list(result.get("yaku", []))
        aka_count = int(result.get("aka_count", 0))

        offender_index = self._hu_winner_index()
        if offender_index is None:
            logger.error(f"错和结算无法确定和牌方 hu_class={self.hu_class}")
            return
        self.hepai_player_index = offender_index

        cuohe_total_penalty = 9000
        pay_each = cuohe_total_penalty // 3
        score_changes = {i: 0 for i in range(4)}
        for i in range(4):
            if i == offender_index:
                continue
            score_changes[offender_index] -= pay_each
            score_changes[i] += pay_each

        for p in self.player_list:
            p.score += score_changes[p.player_index]

        # 错和标签：无役错和把"错和"排在最前（其后展示宝牌/红宝），其余情况照旧附在番种末尾。
        if "错和" not in yaku:
            if no_yaku:
                yaku.insert(0, "错和")
            else:
                yaku.append("错和")

        self._cuohe_triggered = True

        # 错和也展示宝牌/里宝指示牌：番数由 yaku 内的"宝牌*N/里宝牌*N"统计得出，立直家才显示里宝。
        cuohe_ura_indicators = (
            list(self.ura_dora_indicators + self.ura_kan_dora_indicators)
            if "riichi" in self.player_list[offender_index].tag_list else []
        )
        cuohe_dora_count = _count_bonus_yaku(yaku, "宝牌")
        cuohe_ura_count = _count_bonus_yaku(yaku, "里宝牌")

        player_action_record_hu_riichi(
            self,
            hepai_player_index=offender_index,
            hu_class=self.hu_class,
            han=han,
            fu=fu,
            yaku=yaku,
            score_changes=[score_changes.get(i, 0) for i in range(4)],
            dora_indicators=list(self.dora_indicators) + list(self.kan_dora_indicators),
            ura_dora_indicators=cuohe_ura_indicators,
            aka_count=aka_count,
            honba=self.honba,
            riichi_sticks_collected=0,
        )

        await broadcast_result(
            self,
            hepai_player_index=offender_index,
            # player_to_score 按当局座位 player_index 为键，score_changes 按 original_player_index 为键（与国标约定一致）。
            player_to_score={p.player_index: p.score for p in self.player_list},
            hu_score=cuohe_total_penalty,
            hu_fan=yaku,
            hu_class=self.hu_class,
            hepai_player_hand=self.player_list[offender_index].hand_tiles,
            hepai_player_combination_mask=self.player_list[offender_index].combination_mask,
            han=han,
            fu=fu,
            aka_count=aka_count,
            dora_count=cuohe_dora_count,
            ura_dora_count=cuohe_ura_count,
            dora_indicators=list(self.dora_indicators) + list(self.kan_dora_indicators),
            ura_dora_indicators=cuohe_ura_indicators,
            honba=self.honba,
            riichi_sticks_collected=0,
            score_changes={p.original_player_index: score_changes[p.player_index] for p in self.player_list},
            silent=True,
        )

    async def _settle_ryuukyoku(self) -> Dict[int, int]:
        """荒牌流局：听牌家均分 3000"""
        for p in self.player_list:
            self.refresh_waiting_tiles(p.player_index)
        tenpai_indexes = [p.player_index for p in self.player_list if self._is_ryuukyoku_tenpai(p)]
        noten_indexes = [p.player_index for p in self.player_list if not self._is_ryuukyoku_tenpai(p)]
        changes = {i: 0 for i in range(4)}
        if tenpai_indexes and noten_indexes:
            total = 3000
            pay_each = total // len(noten_indexes)
            gain_each = total // len(tenpai_indexes)
            for i in noten_indexes:
                changes[i] -= pay_each
            for i in tenpai_indexes:
                changes[i] += gain_each
        for p in self.player_list:
            p.score += changes[p.player_index]
        tenpai_flags = [1 if self._is_ryuukyoku_tenpai(p) else 0 for p in self.player_list]
        player_action_record_ryuukyoku(self, tenpai_flags=tenpai_flags, score_changes=[changes.get(i, 0) for i in range(4)], reason="exhaustive")
        return changes

    def _is_ryuukyoku_tenpai(self, player: RiichiPlayer) -> bool:
        if not player.waiting_tiles:
            return False
        if "riichi" in player.tag_list or "daburu_riichi" in player.tag_list:
            return True
        return bool(player.ryuukyoku_declared_tenpai)

    # ========== 抽宝牌 ==========

    async def _reveal_kan_dora(self):
        """翻开下一张杠宝牌指示牌以及对应位置的里杠宝。

        王牌布局（init_tiles）：tiles_list[-1..-4] 为 4 张岭上牌；
        宝牌指示牌使用原始牌山倒数 6/8/10/12/14，里宝牌使用倒数 5/7/9/11/13。
        每次岭上摸牌会 pop(-1)，因此当前位置 = 原始位置 + rinshan_count。
        """
        next_kan_number = len(self.kan_dora_indicators) + 1
        if next_kan_number > 4:
            return
        idx = -(6 + 2 * next_kan_number) + self.rinshan_count
        if -idx > self.dead_wall_count or -idx > len(self.tiles_list):
            return
        new_ind = self.tiles_list[idx]
        self.kan_dora_indicators.append(new_ind)

        ura_idx = -(5 + 2 * next_kan_number) + self.rinshan_count
        if -ura_idx <= self.dead_wall_count and -ura_idx <= len(self.tiles_list):
            self.ura_kan_dora_indicators.append(self.tiles_list[ura_idx])

        player_action_record_new_dora(self, tile_id=new_ind)
        await broadcast_update_dora(self, new_indicator=new_ind, is_kan_dora=True)

    # ========== 振听 ==========

    def sync_furiten_tags(self) -> bool:
        """根据 永久振听 / 同巡振听 / 立直振听 三态合并出对应 furiten tag。
        永久振听：自家弃牌中含听牌；同巡振听：本巡放过荣和；立直振听：立直后放过荣和（永久至本局结束）。
        三者中任意一种成立即挂 furiten tag。返回是否发生改动（用于决定是否广播）。"""
        changed = False
        for p in self.player_list:
            permanent = any(_normalize(t) in p.waiting_tiles for t in p.discard_origin_tiles)
            is_furiten = permanent or p.temp_furiten or p.riichi_furiten
            has_tag = "furiten" in p.tag_list
            if is_furiten and not has_tag:
                p.tag_list.append("furiten")
                changed = True
            elif not is_furiten and has_tag:
                p.tag_list.remove("furiten")
                changed = True
        return changed

    # ========== 特殊流局检测 ==========

    async def _check_four_wind_abort(self) -> bool:
        """四风连打：首巡 4 家均切出相同风牌且无鸣牌"""
        if self.xunmu > 1 or not self._first_round_valid:
            return False
        first_discards = []
        for p in self.player_list:
            if p.combination_tiles:
                return False
            if len(p.discard_tiles) == 0:
                return False
            first_discards.append(_normalize(p.discard_tiles[0]))
        if len(first_discards) == 4 and first_discards[0] in (41, 42, 43, 44) and all(t == first_discards[0] for t in first_discards):
            return True
        return False

    async def _check_four_player_riichi_abort(self) -> bool:
        if sum(1 for p in self.player_list if "riichi" in p.tag_list) >= 4:
            return True
        return False

    def _can_declare_kyuushu(self, player_index: int) -> bool:
        """九种九牌：首巡、全员无鸣牌、当前玩家尚未切过牌，且手牌 14 张含 ≥9 种幺九"""
        player = self.player_list[player_index]
        if len(player.discard_tiles) > 0:
            return False
        for p in self.player_list:
            if p.combination_tiles:
                return False
        return check_jiuzhongjiupai(player.hand_tiles)

    # ========== 观战 ==========

    async def send_realtime_spectator_snapshot(self, spectator_user_id: int, view_player_index: int):
        """实时观战接入：按被观战座位视角补发 game_start 与当前 pending ask，与断线重连一致。"""
        if spectator_user_id not in self.game_server.user_id_to_connection:
            return
        if view_player_index < 0 or view_player_index >= len(self.player_list):
            return
        from ...response import Response, GameInfo
        from .boardcast import _build_base_game_info, _build_player_info, reconnected_send_pending_ask_for_viewer

        viewer = self.player_list[view_player_index]
        conn = self.game_server.user_id_to_connection[spectator_user_id]
        base = _build_base_game_info(self)
        infos = [_build_player_info(p, viewer.user_id, view_player_index) for p in self.player_list]
        game_info = GameInfo(**{**base, "players_info": infos, "self_hand_tiles": None, "view_player_index": view_player_index})
        await conn.websocket.send_json(Response(
            type="gamestate/riichi/game_start",
            success=True,
            message="实时观战初始化",
            game_info=game_info,
        ).dict(exclude_none=True))
        await reconnected_send_pending_ask_for_viewer(self, spectator_user_id, view_player_index)

    async def add_spectator(self, user_id: int, connection):
        await self.spectator_manager.add_spectator(user_id, connection)

    async def remove_spectator(self, user_id: int):
        await self.spectator_manager.remove_spectator(user_id)


# 挂载方法
RiichiGameState.wait_action = wait_action
RiichiGameState.broadcast_game_start = broadcast_game_start
RiichiGameState.broadcast_ask_hand_action = broadcast_ask_hand_action
RiichiGameState.broadcast_ask_other_action = broadcast_ask_other_action
RiichiGameState.broadcast_do_action = broadcast_do_action
RiichiGameState.broadcast_result = broadcast_result
RiichiGameState.broadcast_game_end = broadcast_game_end
RiichiGameState.broadcast_switch_seat = broadcast_switch_seat
RiichiGameState.broadcast_refresh_player_tag_list = broadcast_refresh_player_tag_list
RiichiGameState.reconnected_send_pending_ask = reconnected_send_pending_ask
RiichiGameState.next_current_index = next_current_index
RiichiGameState.refresh_waiting_tiles = refresh_waiting_tiles
