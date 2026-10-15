"""长沙换六张局内流程；真人、机器人和超时动作使用同一套校验。"""
import asyncio
from collections import Counter
import logging
import math
import random
import time

from pydantic import BaseModel

from .exchange_six_config import RULE_VERSION
from ...game_calculation.changsha.exchange_six import (
    VALID_TILES, evaluate, waiting_tiles, hand_fans, WAY_FANS,
    base_score, big_weight, group_kong_wins,
)
from ..public.ask_timing import begin_ask_round, note_ask_delivered, get_ask_elapsed, reconnect_clock
from ..public.lifecycle import start_owned_task
from ..public.vote_manager import vote_checkpoint
from ..public.game_record_manager import (
    append_action_tick, player_action_record_deal, player_action_record_cut,
    player_action_record_angang, player_action_record_jiagang, player_action_record_chipenggang,
    build_score_changes_dict,
)
from ..public.hand_slot_utils import clear_draw_slot
from ..public.hand_action_notify import apply_player_cut
from ..public.logic_common import get_index_relative_position
from .boardcast import broadcast_result


logger = logging.getLogger(__name__)
EXCHANGE_ACTION = "changsha_exchange_six"


class ExchangeSixState(BaseModel):
    phase: str
    revision: int
    action_tick: int
    remaining_time: int
    step_remaining: int
    public_state: dict
    private_state: dict


class ChangshaExchangeSixResponse(BaseModel):
    type: str = "gamestate/changsha/exchange_six_state"
    success: bool = True
    gamestate_id: str
    state: ExchangeSixState


class ExchangeSixFlow:
    def __init__(self, state):
        self.state = state
        self.reset()

    def reset(self):
        self.phase = "playing"
        self.revision = 0
        self.dice = []
        self.source_offset = 0
        self.exchange_round = 0
        self.completed_exchange_rounds = 0
        self.exchange_closed = False
        self.submissions = {}
        self.disqualified = set()
        self.reported_ready = set()
        self.open_hands = set()
        self.four_joy_paid = {seat: set() for seat in range(4)}
        self.four_joy_revealed = {}
        self.locked_patterns = {}
        self.greedy_pass_patterns = {seat: {} for seat in range(4)}
        self.observed_waits = {}
        self.targets = {seat: {} for seat in range(4)}
        self.claim_tiles = []
        self.opening_done = False
        self.actual_draw_counts = {seat: 0 for seat in range(4)}
        self.first_draw_pending = set()
        self.reported_ready_pass_base = {seat: 0 for seat in range(4)}
        self.deal_dice = []
        self.small_hu_forbidden_seat = None
        self.pending_ready_discard = None
        self.hidden_draw = None
        self.private_reveal_pending = None
        self.public_kong_tiles = []
        self.kong_sequence = 0
        self.blind_kong = False
        self.wins = {}
        self.payer = None
        self.birds = None
        self.next_dealer = None
        self.sea_tile = None
        self.settlements = []
        self.kong_dice = []
        self.kong_positions = []
        self.hint_cache = {}
        self.round_scores_before = {p.original_player_index: p.score for p in self.state.player_list}

    def waits(self, seat, hand=None, melds=None, *, for_report=False):
        player = self.state.player_list[seat]
        if seat in self.disqualified:
            return set()
        hand = list(player.hand_tiles if hand is None else hand)
        melds = list(player.combination_tiles if melds is None else melds)
        waits = waiting_tiles(hand, melds, reported_ready=for_report or seat in self.reported_ready)
        if seat in self.locked_patterns:
            waits &= self.locked_patterns[seat].keys()
        return waits

    def result(self, seat, tile, *, hand=None, ways=(), ignore_small_hu_ban=False):
        player = self.state.player_list[seat]
        if seat in self.disqualified or "peida" in player.tag_list:
            return 0, []
        hand = list(player.hand_tiles + [tile] if hand is None else hand)
        score, fans = evaluate(hand, player.combination_tiles, ways, reported_ready=seat in self.reported_ready)
        if not score:
            return 0, []
        if seat in self.locked_patterns:
            if tile not in self.locked_patterns[seat]:
                return 0, []
            # 每张听牌沿用开杠前对应的牌型，不能靠新副露再增加手牌大胡。
            fans = list(self.locked_patterns[seat][tile]) + [fan for fan in fans if fan in WAY_FANS]
            if not fans:
                fans = ["小胡"]
        if seat in self.reported_ready:
            fans = [fan for fan in fans if fan != "小胡"] + ["报听"]
        if seat in self.open_hands:
            fans = [fan for fan in fans if fan != "小胡"] + ["明牌"]
        if not ignore_small_hu_ban and seat == self.small_hu_forbidden_seat and big_weight(fans) == 0:
            return 0, []
        return base_score(big_weight(fans)), fans

    def prepare_deal(self):
        """开局抓牌骰子与换牌骰子分开记录，座位0始终为本局庄家。"""
        rng = random.Random(f"{self.state.round_random_seed}:deal_dice")
        self.deal_dice = [rng.randint(1, 6), rng.randint(1, 6)]
        self.small_hu_forbidden_seat = (sum(self.deal_dice) - 1) % 4

    def note_actual_draw(self, seat):
        self.actual_draw_counts[seat] += 1
        if self.actual_draw_counts[seat] == 1:
            self.first_draw_pending.add(seat)
        else:
            self.first_draw_pending.discard(seat)
        append_action_tick(self.state, ["changsha_actual_draw", seat, self.actual_draw_counts[seat]])

    def lock_patterns(self, seat):
        if seat in self.locked_patterns:
            return
        player = self.state.player_list[seat]
        self.locked_patterns[seat] = {
            tile: hand_fans(player.hand_tiles + [tile], player.combination_tiles)
            for tile in self.waits(seat)
        }
        append_action_tick(self.state, ["changsha_lock_patterns", seat, self.locked_patterns[seat]])

    def validate_selection(self, seat, tiles):
        if not isinstance(tiles, (list, tuple)) or len(tiles) != 6:
            return None
        if any(type(tile) is not int or tile not in VALID_TILES for tile in tiles):
            return None
        hand = Counter(self.state.player_list[seat].hand_tiles)
        selected = Counter(tiles)
        if any(hand[tile] < count for tile, count in selected.items()):
            return None
        return list(tiles)

    def default_selection(self, seat):
        hand = sorted(self.state.player_list[seat].hand_tiles)
        # 先尝试从张数最多的一门以外选六张；不够时，改从张数最多的两门里选。
        counts = Counter(tile // 10 for tile in hand)
        retained = max(counts, key=lambda suit: (counts[suit], -suit))
        candidates = [tile for tile in hand if tile // 10 != retained]
        if len(candidates) >= 6:
            return candidates[:6]
        allowed = set(sorted(counts, key=lambda suit: (-counts[suit], suit))[:2])
        return [tile for tile in hand if tile // 10 in allowed][:6]

    async def submit(self, seat, action, *, action_tick, selected_tiles=None, target_tile=None, tile_id=None):
        state = self.state
        if action_tick != state.server_action_tick or seat not in state.waiting_players_list:
            return False
        if action not in state.action_dict.get(seat, []):
            return False
        if action == EXCHANGE_ACTION:
            if get_ask_elapsed(state, seat) >= state.exchange_selection_seconds:
                return False
            chosen = self.validate_selection(seat, selected_tiles)
            if chosen is None:
                return False
            data = {"action_type": action, "selected_tiles": chosen, "_action_tick": action_tick}
        else:
            targets = self.targets[seat].get(action)
            if targets:
                target_tile = targets[0] if not target_tile else target_tile
                if target_tile not in targets:
                    return False
            if action == "cut":
                player = state.player_list[seat]
                if tile_id not in player.hand_tiles:
                    return False
                opening_ready_cut = seat == 0 and self.pending_ready_discard is not None
                if not opening_ready_cut and (player.open_kong_locked or seat in self.reported_ready) and tile_id != player.hand_tiles[-1]:
                    return False
                if seat == 0 and self.pending_ready_discard is not None and tile_id != self.pending_ready_discard:
                    return False
            data = {"action_type": action, "target_tile": target_tile, "TileId": tile_id, "_action_tick": action_tick}
        await state.action_queues[seat].put(data)
        state.action_events[seat].set()
        return True

    def packet(self, seat):
        state = self.state
        bank, step = reconnect_clock(state, state.player_list[seat])
        if self.phase == "exchange":
            bank = math.ceil(max(0, state.exchange_selection_seconds - get_ask_elapsed(state, seat))) if seat in state.waiting_players_list else 0
            step = 0
        player = state.player_list[seat]
        cut_tiles = []
        if "cut" in state.action_dict.get(seat, []) and seat in state.waiting_players_list:
            if seat == 0 and self.pending_ready_discard is not None:
                cut_tiles = [self.pending_ready_discard]
            elif player.open_kong_locked or seat in self.reported_ready:
                cut_tiles = player.hand_tiles[-1:]
            else:
                cut_tiles = sorted(set(player.hand_tiles))
        return ChangshaExchangeSixResponse(
            gamestate_id=state.gamestate_id,
            state=ExchangeSixState(
                phase=self.phase, revision=self.revision, action_tick=state.server_action_tick,
                remaining_time=bank, step_remaining=step,
                public_state={
                    "rule_version": RULE_VERSION, "dice": list(self.dice), "source_offset": self.source_offset,
                    "exchange_round": self.exchange_round, "completed_exchange_rounds": self.completed_exchange_rounds,
                    "exchange_selection_seconds": state.exchange_selection_seconds,
                    "deal_dice": list(self.deal_dice), "small_hu_forbidden_seat": self.small_hu_forbidden_seat,
                    "submitted_seats": sorted(self.submissions), "disqualified_seats": sorted(self.disqualified),
                    "reported_ready_seats": sorted(self.reported_ready), "open_hand_seats": sorted(self.open_hands),
                    "open_hands": {str(index): list(state.player_list[index].hand_tiles[:-1]
                                    if index == self.private_reveal_pending else state.player_list[index].hand_tiles)
                                   for index in self.open_hands},
                    "four_joy_revealed": {str(index): list(tiles) for index, tiles in self.four_joy_revealed.items()},
                    "claim_tiles": list(self.claim_tiles), "kong_tiles": list(self.public_kong_tiles),
                    "blind_kong": self.blind_kong, "current_player_index": state.current_player_index,
                    "kong_dice": list(self.kong_dice), "kong_positions": list(self.kong_positions),
                    "hidden_draw_pending": self.hidden_draw is not None or self.private_reveal_pending is not None,
                    "settlements": list(self.settlements),
                    "hand_counts": {str(p.player_index): len(p.hand_tiles) + int(p.player_index == state.current_player_index and self.hidden_draw is not None) for p in state.player_list},
                    "remaining_tiles": len(state.tiles_list),
                },
                private_state={"hand_tiles": list(state.player_list[seat].hand_tiles),
                               "selected_tiles": list(self.submissions.get(seat, [])),
                               "action_list": list(state.action_dict.get(seat, [])) if seat in state.waiting_players_list else [],
                               "targets": dict(self.targets[seat]), "cut_tiles": cut_tiles,
                               "has_draw_slot": player.has_draw_slot, "hints": self.hand_hints(seat, cut_tiles)},
            ),
        )

    def hand_hints(self, seat, cut_tiles):
        """只给本人发送听牌提示，按服务器的报听、锁手和胡牌资格计算。"""
        player = self.state.player_list[seat]
        key = (tuple(player.hand_tiles), tuple(player.combination_tiles), tuple(cut_tiles),
               seat in self.reported_ready, seat in self.open_hands, seat in self.disqualified,
               "peida" in player.tag_list,
               self.small_hu_forbidden_seat,
               tuple((tile, tuple(fans)) for tile, fans in self.locked_patterns.get(seat, {}).items()))
        if seat in self.hint_cache and self.hint_cache[seat][0] == key:
            return self.hint_cache[seat][1]
        def describe(hand):
            return [{"tile": tile, "score": score, "fans": fans,
                     "blocked_small_hu": seat == self.small_hu_forbidden_seat and big_weight(fans) == 0}
                    for tile in sorted(self.waits(seat, hand))
                    for score, fans in [self.result(seat, tile, hand=list(hand) + [tile], ignore_small_hu_ban=True)]
                    if score]
        hints = {"hand": list(player.hand_tiles), "melds": list(player.combination_tiles),
                 "waits": describe(player.hand_tiles), "discard_waits": {}}
        if len(player.hand_tiles) % 3 == 2:
            for tile in cut_tiles:
                hand = list(player.hand_tiles)
                hand.remove(tile)
                hints["discard_waits"][str(tile)] = describe(hand)
        self.hint_cache[seat] = (key, hints)
        return hints

    async def send_snapshot(self, connection_user_id, view_seat):
        connection = self.state.game_server.user_id_to_connection.get(connection_user_id)
        if connection is None:
            return
        response = self.packet(view_seat)
        try:
            await connection.websocket.send_json(response.dict(exclude_none=True))
        except Exception:
            logger.warning("换六张状态发送失败 user=%s", connection_user_id, exc_info=True)

    async def broadcast(self):
        self.revision += 1
        for player in self.state.player_list:
            await self.send_snapshot(player.user_id, player.player_index)
            await self.state.send_to_realtime_spectators(player.player_index, self.packet(player.player_index))

    async def run_stage(self, phase, actions, default_action):
        """换牌独立计时；其余阶段沿用步时和局时，暂停及重连不重置预算。"""
        state = self.state
        self.phase = phase
        state.game_status = "waiting_changsha_exchange_six"
        state.server_action_tick += 1
        state.action_dict = {seat: list(actions.get(seat, [])) for seat in range(4)}
        state.waiting_players_list = [seat for seat, choices in state.action_dict.items() if choices]
        for seat in range(4):
            while not state.action_queues[seat].empty():
                state.action_queues[seat].get_nowait()
            state.action_events[seat].clear()
        begin_ask_round(state)
        await self.broadcast()
        for seat in state.waiting_players_list:
            note_ask_delivered(state, seat)
        answers = {}
        try:
            while state.waiting_players_list:
                before_pause = time.time()
                await vote_checkpoint(state)
                pause_seconds = time.time() - before_pause
                if pause_seconds > 0.1:
                    for seat in state.waiting_players_list:
                        state._ask_delivered_at[seat] += pause_seconds
                for seat in list(state.waiting_players_list):
                    player = state.player_list[seat]
                    elapsed = get_ask_elapsed(state, seat)
                    budget = state.exchange_selection_seconds if phase == "exchange" else max(0, player.remaining_time) + state.step_time
                    expired = elapsed >= budget
                    if player.is_bot or "offline" in player.tag_list or expired:
                        if expired:
                            # 预算耗尽时先丢弃排队的晚到动作，不能让它抢在服务器超时决策之前。
                            while not state.action_queues[seat].empty():
                                state.action_queues[seat].get_nowait()
                        data = default_action(seat)
                        if expired and phase == "exchange":
                            # 晚到客户端请求被拒绝；服务器默认选牌仍走下方的牌归属校验。
                            await state.action_queues[seat].put({**data, "_action_tick": state.server_action_tick})
                        else:
                            await self.submit(seat, data["action_type"], action_tick=state.server_action_tick,
                                              selected_tiles=data.get("selected_tiles"), target_tile=data.get("target_tile"),
                                              tile_id=data.get("TileId"))
                    while not state.action_queues[seat].empty():
                        data = state.action_queues[seat].get_nowait()
                        if data.get("_action_tick") != state.server_action_tick:
                            continue
                        if data["action_type"] == EXCHANGE_ACTION:
                            chosen = self.validate_selection(seat, data.get("selected_tiles"))
                            if chosen is None:
                                continue
                            self.submissions[seat] = chosen
                        answers[seat] = data
                        if phase != "exchange":
                            player.remaining_time = max(0, player.remaining_time - max(0, int(elapsed - state.step_time)))
                        state.waiting_players_list.remove(seat)
                        state.action_dict[seat] = []
                        state.action_events[seat].clear()
                        await self.broadcast()
                        break
                if not state.waiting_players_list:
                    break
                tasks = [start_owned_task(state, state.action_events[seat].wait()) for seat in state.waiting_players_list]
                try:
                    await asyncio.wait(tasks, timeout=1, return_when=asyncio.FIRST_COMPLETED)
                finally:
                    for task in tasks:
                        if not task.done():
                            task.cancel()
                    await asyncio.gather(*tasks, return_exceptions=True)
            return answers
        finally:
            state.waiting_players_list = []
            state.action_dict = {seat: [] for seat in range(4)}
            for seat in range(4):
                state.action_events[seat].clear()
                while not state.action_queues[seat].empty():
                    state.action_queues[seat].get_nowait()

    def apply_exchange(self):
        if set(self.submissions) != set(range(4)):
            raise ValueError("换六张必须收齐四家提交")
        outgoing = {seat: self.validate_selection(seat, tiles) for seat, tiles in self.submissions.items()}
        if any(tiles is None for tiles in outgoing.values()):
            raise ValueError("换六张提交已不属于起手牌")
        new_hands = []
        for seat, player in enumerate(self.state.player_list):
            hand = list(player.hand_tiles)
            for tile in outgoing[seat]:
                hand.remove(tile)
            hand.extend(outgoing[(seat + self.source_offset) % 4])
            new_hands.append(hand)
        # 全部算好后才更新手牌，禁止出现仅一两家已经换完的中间状态。
        for seat, hand in enumerate(new_hands):
            self.state.player_list[seat].hand_tiles = hand
            if len({tile // 10 for tile in outgoing[seat]}) == 3:
                self.disqualified.add(seat)

    def second_exchange_dice(self):
        """预留服务端条件入口；条件及第二轮骰子办法确认前不启动第二轮。"""
        return None

    async def exchange(self, *, dice=None):
        if self.exchange_closed or self.completed_exchange_rounds >= 2:
            raise ValueError("换牌仅限开局，最多两轮")
        if self.exchange_round != self.completed_exchange_rounds:
            raise ValueError("上一轮换牌尚未完成")
        if dice is None:
            if self.completed_exchange_rounds:
                raise ValueError("第二轮需由服务端条件入口明确提供骰子")
            rng = random.Random(f"{self.state.round_random_seed}:exchange_six")
            dice = [rng.randint(1, 6), rng.randint(1, 6)]
        if not isinstance(dice, (list, tuple)) or len(dice) != 2 or any(type(die) is not int or not 1 <= die <= 6 for die in dice):
            raise ValueError("换牌骰子必须为两个1至6的整数")
        # 每轮重新收齐提交，第一轮相公保留；第二轮始终以换完后的当前手牌为准。
        self.submissions = {}
        self.exchange_round += 1
        self.dice = []
        self.source_offset = 0
        append_action_tick(self.state, ["changsha_exchange_start", self.exchange_round, [], 0])
        await self.run_stage("exchange", {seat: [EXCHANGE_ACTION] for seat in range(4)},
                             lambda seat: {"action_type": EXCHANGE_ACTION, "selected_tiles": self.default_selection(seat)})
        # 四家都选定后才公开骰子，客户端按快照队列播完骰子再展示换后的手牌。
        self.dice = list(dice)
        total = sum(self.dice)
        self.source_offset = 2 if total % 2 else 3 if total % 4 == 0 else 1
        self.phase = "exchange_dice"
        append_action_tick(self.state, ["changsha_exchange_roll", self.exchange_round, list(self.dice), self.source_offset])
        await self.broadcast()
        self.apply_exchange()
        self.completed_exchange_rounds = self.exchange_round
        append_action_tick(self.state, ["changsha_exchange_six", list(self.dice), self.source_offset,
                                       [self.submissions[seat] for seat in range(4)], sorted(self.disqualified), self.exchange_round])
        self.phase = "playing"
        await self.broadcast()

    def ways(self, seat, *, first=False, self_draw=False, kong=False, rob=False, sea=False):
        ways = []
        if first and seat == 0:
            ways.append("天胡")
        elif seat != 0 and (self.actual_draw_counts[seat] == 0
                           or self_draw and seat in self.first_draw_pending):
            ways.append("地胡")
        if kong:
            ways.append("杠上开花" if seat == self.state.current_player_index else "杠上炮")
        if rob:
            ways.append("抢杠胡")
        if sea:
            ways.append("海底")
        return ways

    async def four_joy(self, seat, quad_tiles):
        state = self.state
        player = state.player_list[seat]
        if seat in self.disqualified:
            return
        pending = [tile for tile in sorted(set(quad_tiles)) if player.hand_tiles.count(tile) == 4
                   and tile not in self.four_joy_paid[seat]]
        while pending:
            self.targets[seat] = {"changsha_four_joy": pending}
            answers = await self.run_stage("four_joy", {seat: ["changsha_four_joy", "pass"]},
                lambda index: {"action_type": "changsha_four_joy" if state.player_list[index].is_bot else "pass"})
            data = answers[seat]
            if data["action_type"] == "pass":
                break
            tile = data["target_tile"]
            self.four_joy_paid[seat].add(tile)
            pending.remove(tile)
            rng = random.Random(f"{state.round_random_seed}:four_joy:{seat}:{tile}")
            dice = [rng.randint(1, 6), rng.randint(1, 6)]
            bird_seats = [(seat + die - 1) % 4 for die in dice]
            before = {p.original_player_index: p.score for p in state.player_list}
            details = []
            for payer in range(4):
                if payer == seat:
                    continue
                hits = sum(position in (seat, payer) for position in bird_seats)
                payment = 2 * 2 ** hits
                player.score += payment
                state.player_list[payer].score -= payment
                details.append({"payer": payer, "base": 2, "hit_count": hits, "multiplier": 2 ** hits, "payment": payment})
            self.four_joy_revealed.setdefault(seat, []).extend([tile] * 4)
            changes = build_score_changes_dict(state.player_list, before)
            append_action_tick(state, ["changsha_four_joy", seat, tile, dice, changes])
            player.record_counter.zimo_times += 1
            player.record_counter.recorded_fans.append(["四喜"])
            total = sum(detail["payment"] for detail in details)
            player.record_counter.win_score += total
            await broadcast_result(state, hepai_player_index=seat,
                player_to_score={p.player_index: p.score for p in state.player_list}, hu_score=total,
                hu_fan=["四喜"], hu_class="initial_hu", hepai_player_hand=[tile] * 4,
                hepai_player_huapai=[], hepai_player_combination_mask=player.combination_mask,
                score_changes=changes, initial_hu_dice=dice, initial_hu_bird_seats=bird_seats,
                initial_hu_payer_details=details, round_continues=True)
            await self.broadcast()
        self.targets[seat] = {}

    async def opening(self):
        self.round_scores_before = {p.original_player_index: p.score for p in self.state.player_list}
        append_action_tick(self.state, ["changsha_deal_dice", list(self.deal_dice), self.small_hu_forbidden_seat])
        await self.exchange()
        second_dice = self.second_exchange_dice()
        if second_dice is not None:
            await self.exchange(dice=second_dice)
        self.exchange_closed = True
        for seat, player in enumerate(self.state.player_list):
            await self.four_joy(seat, [tile for tile, count in Counter(player.hand_tiles).items() if count == 4])
        pending = set(range(4))
        while pending:
            actions = {}
            for seat in sorted(pending):
                player = self.state.player_list[seat]
                choices = ["pass"]
                self.targets[seat] = {}
                if seat not in self.open_hands:
                    choices.insert(0, "changsha_open_hand")
                if seat not in self.reported_ready and seat not in self.disqualified:
                    if seat == 0:
                        discards = [tile for tile in sorted(set(player.hand_tiles))
                                    if self.waits(seat, player.hand_tiles[:player.hand_tiles.index(tile)] +
                                                  player.hand_tiles[player.hand_tiles.index(tile) + 1:], for_report=True)]
                        if discards:
                            self.targets[seat]["changsha_report_ready"] = discards
                            choices.insert(0, "changsha_report_ready")
                    elif self.waits(seat, for_report=True):
                        choices.insert(0, "changsha_report_ready")
                actions[seat] = choices
            answers = await self.run_stage("declarations", actions, lambda seat: {"action_type": "pass"})
            for seat, data in answers.items():
                if data["action_type"] == "changsha_open_hand":
                    self.open_hands.add(seat)
                elif data["action_type"] == "changsha_report_ready":
                    self.reported_ready.add(seat)
                    if seat == 0:
                        self.pending_ready_discard = data["target_tile"]
                else:
                    pending.discard(seat)
                append_action_tick(self.state, ["changsha_declaration", seat, data["action_type"], data.get("target_tile")])
                if seat in self.open_hands and seat in self.reported_ready:
                    pending.discard(seat)
            await self.broadcast()
        self.targets = {seat: {} for seat in range(4)}
        self.opening_done = True
        self.state.current_player_index = 0
        self.phase = "playing"

    def update_waits(self, seat):
        waits = self.waits(seat)
        if seat in self.observed_waits and waits != self.observed_waits[seat]:
            self.greedy_pass_patterns[seat].clear()
        self.observed_waits[seat] = set(waits)
        self.state.player_list[seat].waiting_tiles = waits

    @staticmethod
    def hand_pattern(fans):
        return tuple(sorted(fan for fan in fans if fan not in WAY_FANS and fan not in ("报听", "明牌", "小胡", "闷杠")))

    async def claims(self, tiles, *, kong=False, rob=False, only_thirteen=False, allow_meld=True, sea=False, suppress_hu=False):
        """保留每张公开牌的全部赢家；没人胡时按动作优先级及座位距离选一次副露。"""
        state = self.state
        opener = state.current_player_index
        self.claim_tiles = list(tiles)
        results = {}
        actions = {}
        self.targets = {seat: {} for seat in range(4)}
        for distance in (1, 2, 3):
            seat = (opener + distance) % 4
            player = state.player_list[seat]
            self.update_waits(seat)
            valid = []
            for tile in tiles:
                if suppress_hu:
                    continue
                score, fans = self.result(seat, tile, ways=self.ways(seat, kong=kong, rob=rob, sea=sea))
                if only_thirteen and not set(fans).intersection(("十三将", "无对十三将")):
                    continue
                if score:
                    valid.append((tile, fans))
            groups = (group_kong_wins(valid, "杠上炮") if kong else
                      [{"patterns": list(fans), "fans": list(fans), "tiles": [tile]} for tile, fans in valid])
            if kong and self.blind_kong:
                for group in groups:
                    group["fans"].append("闷杠")
            # 先按完整牌型分组算总分，再判断是否比上次过胡高；逐张比会漏掉累计升番。
            if seat in self.open_hands:
                groups = [group for group in groups if base_score(big_weight(group["fans"])) >
                          self.greedy_pass_patterns[seat].get(self.hand_pattern(group["fans"]), 0)]
            passed = max(state.player_passed_hu_base.get(seat, 0), self.reported_ready_pass_base[seat])
            if sum(base_score(big_weight(group["fans"])) for group in groups) <= passed:
                groups = []
            results[seat] = groups
            choices = [f"hu_{('first', 'second', 'third')[distance - 1]}"] if groups else []
            if allow_meld and len(state.tiles_list) > 1 and "peida" not in player.tag_list and seat not in self.reported_ready and not player.open_kong_locked:
                for tile in tiles:
                    if player.hand_tiles.count(tile) >= 3:
                        remaining = list(player.hand_tiles)
                        for _ in range(3):
                            remaining.remove(tile)
                        if self.waits(seat, remaining, player.combination_tiles + [f"g{tile}"]):
                            self.targets[seat].setdefault("gang", []).append(tile)
                        self.targets[seat].setdefault("buzhang", []).append(tile)
                    if player.hand_tiles.count(tile) >= 2:
                        self.targets[seat].setdefault("peng", []).append(tile)
                    if distance == 1:
                        for action, needed in (("chi_left", [tile - 2, tile - 1]), ("chi_mid", [tile - 1, tile + 1]), ("chi_right", [tile + 1, tile + 2])):
                            if all(value in VALID_TILES and value // 10 == tile // 10 for value in needed):
                                if all(player.hand_tiles.count(value) >= needed.count(value) for value in needed):
                                    self.targets[seat].setdefault(action, []).append(tile)
                choices.extend(self.targets[seat])
            if choices:
                actions[seat] = choices + ["pass"]
        if not actions:
            self.claim_tiles = []
            if not rob:
                state.game_status = "END" if not state.tiles_list else "deal_card"
            return False
        def default(seat):
            if state.player_list[seat].is_bot and results[seat]:
                return {"action_type": actions[seat][0]}
            return {"action_type": "pass"}
        answers = await self.run_stage("claims", actions, default)
        winners = sorted((seat for seat, data in answers.items() if data["action_type"].startswith("hu_")),
                         key=lambda seat: (seat - opener) % 4)
        if winners:
            self.wins = {}
            for seat in sorted(winners, key=lambda index: (index - opener) % 4):
                groups = results[seat]
                for group in groups:
                    group["hand"] = list(state.player_list[seat].hand_tiles) + [group["tiles"][0]]
                self.wins[seat] = groups
            self.payer = opener
            state.hu_class = f"hu_{('first', 'second', 'third')[(winners[0] - opener) % 4 - 1]}"
            state.game_status = "END"
            self.claim_tiles = []
            return True
        for seat, groups in results.items():
            if groups:
                passed_base = sum(base_score(big_weight(group["fans"])) for group in groups)
                state.player_passed_hu_base[seat] = max(state.player_passed_hu_base.get(seat, 0),
                                                     passed_base)
                if seat in self.reported_ready:
                    self.reported_ready_pass_base[seat] = max(self.reported_ready_pass_base[seat],
                                                             passed_base)
                if seat in self.open_hands:
                    for group in groups:
                        fans = group["fans"]
                        pattern = self.hand_pattern(fans)
                        self.greedy_pass_patterns[seat][pattern] = max(self.greedy_pass_patterns[seat].get(pattern, 0),
                                                                      base_score(big_weight(fans)))
        melds = []
        for seat, data in answers.items():
            action = data["action_type"]
            priority = 3 if action == "gang" else 2 if action in ("buzhang", "peng") else 1 if action.startswith("chi_") else 0
            if priority:
                melds.append((-priority, (seat - opener) % 4, tiles.index(data["target_tile"]), seat, data))
        self.claim_tiles = []
        if melds:
            _, _, _, seat, data = min(melds, key=lambda item: item[:3])
            if suppress_hu:
                await state.broadcast_do_action(action_list=["changsha_batch_claim"], action_player=seat,
                                                cut_tile=data["target_tile"], cut_from_player=opener)
            await self.meld(seat, data["action_type"], data["target_tile"], opener)
        elif not rob:
            state.game_status = "END" if not state.tiles_list else "deal_card"
        self.phase = "playing"
        return False

    async def meld(self, seat, action, tile, discarder):
        state = self.state
        if action in ("gang", "buzhang"):
            await self.declare_kong(seat, tile, formal=action == "gang", claimed_from=discarder)
            return
        player = state.player_list[seat]
        needed = {"peng": [tile, tile], "chi_left": [tile - 2, tile - 1],
                  "chi_mid": [tile - 1, tile + 1], "chi_right": [tile + 1, tile + 2]}[action]
        for value in needed:
            player.hand_tiles.remove(value)
        center = tile - 1 if action == "chi_left" else tile + 1 if action == "chi_right" else tile
        combination = f"k{tile}" if action == "peng" else f"s{center}"
        relative = get_index_relative_position(seat, discarder)
        tiles = [tile] * 3 if action == "peng" else sorted(needed + [tile])
        claimed_position = 0 if action != "peng" or relative == "left" else 1 if relative == "top" else 2
        if action != "peng":
            tiles.remove(tile)
            tiles.insert(0, tile)
        mask = [value for index, value in enumerate(tiles) for value in (1 if index == claimed_position else 0, value)]
        player.combination_tiles.append(combination)
        player.combination_mask.append(mask)
        river = state.player_list[discarder].discard_tiles
        river.pop(len(river) - 1 - river[::-1].index(tile))
        state.player_list[discarder].discard_origin_tiles.append(tile)
        clear_draw_slot(player)
        state.current_player_index = seat
        state.current_claim_cut_tile = None
        player_action_record_chipenggang(state, action_type=action, mingpai_tile=tile, action_player=seat, combination_mask=mask)
        await state.broadcast_do_action(action_list=[action], action_player=seat, combination_mask=mask,
                                        combination_target=combination, cut_from_player=discarder, cut_tile=tile)
        state.game_status = "waiting_hand_action"

    async def declare_kong(self, seat, tile, *, formal, claimed_from=None, defer_lock=False):
        state = self.state
        player = state.player_list[seat]
        added_index = next((index for index, meld in enumerate(player.combination_tiles) if meld == f"k{tile}"), None)
        concealed = claimed_from is None and added_index is None
        state.current_player_index = seat
        if formal or added_index is not None:
            # 抢胡检查发生在移牌前。大明杠由准备开杠者承担付款，取消后不取补牌。
            if await self.claims([tile], rob=formal, only_thirteen=concealed, allow_meld=False):
                return
        if claimed_from is not None:
            for _ in range(3):
                player.hand_tiles.remove(tile)
            relative = get_index_relative_position(seat, claimed_from)
            claimed_position = {"left": 0, "top": 1, "right": 3}[relative]
            mask = [value for index in range(4) for value in (1 if index == claimed_position else 0, tile)]
            player.combination_tiles.append(f"g{tile}")
            player.combination_mask.append(mask)
            river = state.player_list[claimed_from].discard_tiles
            river.pop(len(river) - 1 - river[::-1].index(tile))
            state.player_list[claimed_from].discard_origin_tiles.append(tile)
            player_action_record_chipenggang(state, action_type="gang", mingpai_tile=tile, action_player=seat, combination_mask=mask)
            await state.broadcast_do_action(action_list=["gang"], action_player=seat, combination_mask=mask,
                                            combination_target=f"g{tile}", cut_from_player=claimed_from, cut_tile=tile)
        elif added_index is not None:
            player.hand_tiles.remove(tile)
            player.combination_tiles[added_index] = f"g{tile}"
            mask = player.combination_mask[added_index]
            sideways = mask.index(1)
            mask[sideways:sideways] = [3, tile]
            player_action_record_jiagang(state, jiagang_tile=tile, is_mo_gang=player.has_draw_slot)
            await state.broadcast_do_action(action_list=["jiagang" if formal else "buzhang"], action_player=seat,
                                            combination_mask=mask, combination_target=f"k{tile}")
        else:
            for _ in range(4):
                player.hand_tiles.remove(tile)
            mask = [0, tile] * 4
            player.combination_tiles.append(f"G{tile}")
            player.combination_mask.append(mask)
            player_action_record_angang(state, angang_tile=tile, is_mo_gang=player.has_draw_slot, combination_mask=mask)
            await state.broadcast_do_action(action_list=["angang" if formal else "buzhang"], action_player=seat,
                                            combination_mask=mask, combination_target=f"G{tile}")
        clear_draw_slot(player)
        if formal:
            if not defer_lock:
                self.lock_patterns(seat)
            player.open_kong_locked = True
        state.prepare_gang_replacement(4 if formal else 1, formal)
        state.current_claim_cut_tile = None
        state.game_status = "deal_card_after_gang"

    def take_single_replacement(self):
        state = self.state
        position = -1 if len(state.tiles_list) <= 1 or state.backward_tiles_list_type == "single" else -2
        tile = state.tiles_list.pop(position)
        state.backward_tiles_list_type = "single" if state.backward_tiles_list_type == "double" else "double"
        return tile

    def take_open_kong_tiles(self):
        """骰子和定位，起始不存在才退小骰子；全取时留末次所取墩的下层作海底。"""
        state = self.state
        self.kong_sequence += 1
        rng = random.Random(f"{state.round_random_seed}:{state.current_round}:open_kong:{self.kong_sequence}")
        self.kong_dice = [rng.randint(1, 6), rng.randint(1, 6)]
        stacks = []
        end = len(state.tiles_list)
        if state.backward_tiles_list_type == "single" and end:
            stacks.append([end - 1])
            end -= 1
        while end > 0:
            stacks.append(list(range(max(0, end - 2), end)))
            end -= 2
        start = sum(self.kong_dice)
        if start > len(stacks):
            start = min(self.kong_dice)
        selected = stacks[start - 1:start + 1] if start <= len(stacks) else []
        positions = [position for stack in selected for position in stack]
        if positions and len(positions) == len(state.tiles_list):
            # 每墩按墙索引升序为下层、上层。海底来自末次所取墩，不能固定排除墙尾。
            positions.remove(selected[-1][0])
        self.kong_positions = list(positions)
        tiles = [state.tiles_list[position] for position in positions]
        if state.backward_tiles_list_type == "double" and len(state.tiles_list) - 2 in positions:
            state.backward_tiles_list_type = "single"
        for position in sorted(positions, reverse=True):
            state.tiles_list.pop(position)
        append_action_tick(state, ["changsha_open_kong", state.current_player_index,
                                  list(self.kong_dice), positions, tiles])
        return tiles

    async def before_draw(self, source):
        state = self.state
        seat = state.current_player_index
        self.public_kong_tiles = []
        self.four_joy_revealed.pop(seat, None)
        self.update_waits(seat)
        candidates = {tile: action for tile, (action, ready) in self.kong_candidates(seat, blind=True).items() if ready}
        if not candidates:
            return False
        self.targets[seat] = {"changsha_blind_kong": sorted(candidates)}
        answers = await self.run_stage("before_draw", {seat: ["changsha_blind_kong", "pass"]}, lambda index: {"action_type": "pass"})
        data = answers[seat]
        self.targets[seat] = {}
        self.phase = "playing"
        if data["action_type"] == "pass":
            return False
        self.blind_kong = True
        await self.declare_kong(seat, data["target_tile"], formal=True, defer_lock=True)
        if state.game_status == "END":
            return True
        self.phase = "blind_kong_declared"
        await self.broadcast()
        # 声明先公开，隐藏摸牌先从墙移出，再按新牌墙定位四张杠牌。
        self.hidden_draw = state.tiles_list.pop(0) if source == "normal" else self.take_single_replacement()
        append_action_tick(state, ["changsha_hidden_draw", seat, source, self.hidden_draw])
        self.public_kong_tiles = self.take_open_kong_tiles()
        self.phase = "blind_kong_public"
        await self.broadcast()
        player = state.player_list[seat]
        player.hand_tiles.append(self.hidden_draw)
        player.has_draw_slot = True
        self.note_actual_draw(seat)
        hidden = self.hidden_draw
        self.hidden_draw = None
        if seat in self.open_hands:
            # 明牌者也先私下查看；确认或超时前，公开手牌仍裁掉新摸牌，重连沿同一裁剪。
            self.private_reveal_pending = seat
            await self.run_stage("blind_kong_view", {seat: ["changsha_blind_reveal"]},
                                 lambda index: {"action_type": "changsha_blind_reveal"})
            self.private_reveal_pending = None
        self.phase = "blind_kong_revealed"
        append_action_tick(state, ["changsha_hidden_reveal", seat])
        await self.broadcast()
        await self.four_joy(seat, [hidden])
        self.lock_patterns(seat)
        await self.resolve_kong_batch(self.public_kong_tiles)
        return True

    async def after_normal_draw(self, tile):
        seat = self.state.current_player_index
        self.note_actual_draw(seat)
        await self.four_joy(seat, [tile])
        self.state.game_status = "waiting_hand_action"
        self.phase = "playing"
        await self.broadcast()

    async def draw_after_kong(self):
        state = self.state
        seat = state.current_player_index
        self.blind_kong = False
        if not state.pending_gang_forced_discard:
            if len(state.tiles_list) <= 1:
                await self.sea_bottom(start=seat)
                return
            if await self.before_draw("buzhang"):
                return
            tile = self.take_single_replacement()
            player = state.player_list[seat]
            player.hand_tiles.append(tile)
            player.has_draw_slot = True
            state.pending_gang_replacement_count = 0
            player_action_record_deal(state, deal_tile=tile, deal_type="gd")
            await state.broadcast_do_action(action_list=["deal_gang_tile"], action_player=seat, deal_tile=tile)
            await self.after_normal_draw(tile)
            return
        self.public_kong_tiles = self.take_open_kong_tiles()
        if self.public_kong_tiles:
            # 一次公开杠牌批次算一次实际取牌，不能让后续续杠仍冒充首摸地胡。
            self.note_actual_draw(seat)
        self.phase = "open_kong_public"
        await self.broadcast()
        await self.resolve_kong_batch(self.public_kong_tiles)

    async def discard_batch(self, tiles, *, clear_pass=True):
        state = self.state
        seat = state.current_player_index
        for tile in tiles:
            state.player_list[seat].discard_tiles.append(tile)
        if tiles:
            self.first_draw_pending.discard(seat)
            append_action_tick(state, ["changsha_batch_discard", seat, list(tiles)])
            await state.broadcast_do_action(action_list=["cut"], action_player=seat,
                cut_tile=tiles[-1], cut_tiles=list(tiles), cut_class=True)
            if clear_pass:
                state.clear_hu_pass_after_own_discard(seat)

    async def resolve_kong_batch(self, tiles):
        state = self.state
        seat = state.current_player_index
        player = state.player_list[seat]
        state.pending_gang_replacement_count = 0
        state.pending_gang_forced_discard = False
        if not tiles:
            state.game_status = "deal_card"
            self.phase = "playing"
            return
        rows = []
        for tile in tiles:
            score, fans = self.result(seat, tile, ways=self.ways(seat, self_draw=True, kong=True))
            if score:
                rows.append((tile, fans))
        if rows:
            answers = await self.run_stage("kong_self_win", {seat: ["hu_self", "pass"]},
                lambda index: {"action_type": "hu_self" if player.is_bot else "pass"})
            if answers[seat]["action_type"] == "hu_self":
                groups = group_kong_wins(rows, "杠上开花")
                for group in groups:
                    if self.blind_kong:
                        group["fans"].append("闷杠")
                    group["hand"] = list(player.hand_tiles) + [group["tiles"][0]]
                self.wins = {seat: groups}
                self.payer = None
                state.hu_class = "hu_self"
                state.game_status = "END"
                return
        await self.discard_batch(tiles, clear_pass=False)
        if await self.claims(tiles, kong=True, allow_meld=False):
            return
        # 胡窗口全部结束后才提供续杠；每个候选只可使用一张补牌，不能改原手。
        continuation = {}
        for tile in tiles:
            for target, (action, ready) in self.kong_candidates(seat, player.hand_tiles + [tile]).items():
                if ready:
                    continuation.setdefault(target, (tile, action))
        if continuation:
            self.targets[seat] = {"buzhang": sorted(continuation)}
            for target, (_, action) in sorted(continuation.items()):
                self.targets[seat].setdefault(action, []).append(target)
            answers = await self.run_stage("kong_continue", {seat: list(self.targets[seat]) + ["pass"]}, lambda index: {"action_type": "pass"})
            data = answers[seat]
            if data["action_type"] in self.targets[seat]:
                target = data["target_tile"]
                used_tile, _ = continuation[target]
                river = player.discard_tiles
                river.pop(len(river) - 1 - river[::-1].index(used_tile))
                player.hand_tiles.append(used_tile)
                player.has_draw_slot = True
                append_action_tick(state, ["changsha_kong_continue", seat, target, used_tile])
                await state.broadcast_do_action(action_list=["changsha_kong_continue"], action_player=seat,
                                                cut_tile=used_tile, cut_from_player=seat)
                if len(tiles) > 1:
                    state.clear_hu_pass_after_own_discard(seat)
                self.blind_kong = False
                await self.declare_kong(seat, target, formal=data["action_type"] != "buzhang")
                return
        # 鸣牌阶段不再开启胡窗；按杠、碰、吃优先只执行一个副露。
        state.clear_hu_pass_after_own_discard(seat)
        await self.claims(tiles, allow_meld=True, suppress_hu=True)
        self.phase = "playing"
        await self.broadcast()

    async def sea_bottom(self, *, start):
        state = self.state
        if not state.tiles_list:
            state.game_status = "END"
            return
        for offset in range(4):
            seat = (start + offset) % 4
            if not self.waits(seat):
                continue
            state.current_player_index = seat
            answers = await self.run_stage("sea_bottom", {seat: ["sea_bottom", "pass"]}, lambda index: {"action_type": "pass"})
            if answers[seat]["action_type"] != "sea_bottom":
                continue
            tile = state.tiles_list.pop(0)
            self.four_joy_revealed.pop(seat, None)
            self.sea_tile = tile
            state.sea_bottom_player_index = seat
            player = state.player_list[seat]
            self.note_actual_draw(seat)
            score, fans = self.result(seat, tile, ways=self.ways(seat, self_draw=True, sea=True))
            append_action_tick(state, ["changsha_sea_draw", seat, tile])
            player.hand_tiles.append(tile)
            player.has_draw_slot = True
            await state.broadcast_do_action(action_list=["deal_tile"], action_player=seat, deal_tile=tile)
            # 海底也是实际摸入手牌，凑齐第四张时先处理四喜，再决定终局胡牌。
            await self.four_joy(seat, [tile])
            if score:
                answers = await self.run_stage("sea_self_win", {seat: ["hu_self", "pass"]},
                    lambda index: {"action_type": "hu_self" if player.is_bot else "pass"})
                if answers[seat]["action_type"] == "hu_self":
                    self.wins = {seat: [{"patterns": fans, "fans": fans, "tiles": [tile], "hand": list(player.hand_tiles)}]}
                    self.payer = None
                    state.hu_class = "hu_self"
                    state.game_status = "END"
                    return
            player.hand_tiles.pop()
            clear_draw_slot(player)
            append_action_tick(state, ["changsha_sea_release", seat, tile])
            await self.discard_batch([tile])
            await self.claims([tile], allow_meld=False, sea=True)
            state.game_status = "END"
            return
        state.game_status = "END"

    async def settle(self):
        state = self.state
        winners = list(self.wins)
        self.next_dealer = winners[0] if len(winners) == 1 else self.payer
        birds = state._draw_changsha_birds(2)
        if len(birds) == 1:
            birds *= 2
        elif not birds and self.sea_tile is not None:
            birds = [self.sea_tile, self.sea_tile]
        self.birds = birds
        origin = winners[0] if len(winners) == 1 else self.payer
        bird_seats = [state._changsha_bird_seat(tile, origin) for tile in birds]
        settlements = []
        for winner, groups in self.wins.items():
            payers = [seat for seat in range(4) if seat != winner] if self.payer is None else [self.payer]
            details = []
            total = 0
            for payer in payers:
                # 付款者自己的报听、明牌只增加这一笔，不能让另外两家也一起升番。
                payer_weight = int(payer in self.reported_ready) + int(payer in self.open_hands)
                bases = [base_score(big_weight(group["fans"]) + payer_weight) for group in groups]
                base = sum(bases)
                hits = sum(position in (winner, payer) for position in bird_seats)
                payment = base * 2 ** hits
                state.player_list[winner].score += payment
                state.player_list[payer].score -= payment
                details.append({"payer": payer, "group_bases": bases, "base": base, "hit_count": hits, "payment": payment})
                total += payment
            fans = [" + ".join(group["fans"]) for group in groups]
            counter = state.player_list[winner].record_counter
            if self.payer is None:
                counter.zimo_times += 1
            else:
                counter.dianhe_times += 1
                state.player_list[self.payer].record_counter.fangchong_times += 1
                state.player_list[self.payer].record_counter.fangchong_score += total
            counter.recorded_fans.append(fans)
            counter.win_score += total
            counter.win_turn += state.xunmu
            settlements.append({"winner": winner, "groups": groups, "payer_details": details, "payment": total,
                                "combination_masks": [list(mask) for mask in state.player_list[winner].combination_mask]})
        append_action_tick(state, ["changsha_exchange_six_win", settlements, birds, origin, self.next_dealer])
        winner = winners[0]
        fan_display = []
        for item in settlements:
            for group_index, group in enumerate(item["groups"], start=1):
                fan_display.append(f"玩家{item['winner'] + 1} 第{group_index}组: " + " + ".join(group["fans"]))
            for detail in item["payer_details"]:
                bases = " + ".join(str(value) for value in detail["group_bases"])
                fan_display.append(f"玩家{detail['payer'] + 1}付给玩家{item['winner'] + 1}: "
                    f"{bases} = {detail['base']}分，{detail['hit_count']}鸟乘{2 ** detail['hit_count']} = {detail['payment']}分")
        await broadcast_result(state, hepai_player_index=winner,
            player_to_score={p.player_index: p.score for p in state.player_list},
            hu_score=sum(item["payment"] for item in settlements),
            hu_fan=fan_display,
            hu_class=state.hu_class, hepai_player_hand=self.wins[winner][0]["hand"],
            hepai_player_huapai=[], hepai_player_combination_mask=state.player_list[winner].combination_mask,
            score_changes=build_score_changes_dict(state.player_list, self.round_scores_before), bird_tiles=birds,
            hepai_tile=self.wins[winner][0]["tiles"][0], multi_ron=len(winners) > 1,
            simultaneous_hu_hands={index: groups[0]["hand"] for index, groups in self.wins.items()} if len(winners) > 1 else None,
            ron_discarder_index=self.payer,
            next_status="match_end" if state.current_round >= state.max_round * 4 else "round_end_by_ready")
        self.phase = "settlement"
        self.settlements = settlements
        await self.broadcast()
        return winners[0], sum(item["payment"] for item in settlements), fan_display, birds

    def kong_candidates(self, seat, hand=None, *, blind=False):
        player = self.state.player_list[seat]
        hand = list(player.hand_tiles if hand is None else hand)
        candidates = set(tile for tile, count in Counter(hand).items() if count == 4)
        candidates.update(int(meld[1:]) for meld in player.combination_tiles
                          if meld.startswith("k") and int(meld[1:]) in hand)
        result = {}
        for tile in sorted(candidates):
            remaining = list(hand)
            melds = list(player.combination_tiles)
            added = next((index for index, meld in enumerate(melds) if meld == f"k{tile}"), None)
            if added is not None:
                remaining.remove(tile)
                melds[added] = f"g{tile}"
                action = "jiagang"
            else:
                for _ in range(4):
                    remaining.remove(tile)
                melds.append(f"G{tile}")
                action = "angang"
            if blind:
                ready = any(self.waits(seat, remaining + [draw], melds) for draw in VALID_TILES
                            if Counter(remaining)[draw] < 4)
            else:
                ready = bool(self.waits(seat, remaining, melds))
            result[tile] = (action, ready)
        return result

    async def hand_action(self, *, first=False):
        state = self.state
        seat = state.current_player_index
        player = state.player_list[seat]
        self.targets[seat] = {}
        actions = ["cut"]
        result = (self.result(seat, player.hand_tiles[-1], hand=player.hand_tiles, ways=self.ways(seat, first=first, self_draw=True))
                  if first or player.has_draw_slot else (0, []))
        if result[0]:
            actions.insert(0, "hu_self")
        if len(state.tiles_list) > 1:
            for tile, (action, ready) in self.kong_candidates(seat).items():
                if ready:
                    self.targets[seat].setdefault(action, []).append(tile)
                if (not player.open_kong_locked and seat not in self.reported_ready
                        or player.open_kong_locked and ready):
                    self.targets[seat].setdefault("buzhang", []).append(tile)
        actions.extend(self.targets[seat])
        def default(index):
            if player.is_bot and "hu_self" in actions:
                return {"action_type": "hu_self"}
            discard = self.pending_ready_discard if seat == 0 and self.pending_ready_discard is not None else player.hand_tiles[-1]
            return {"action_type": "cut", "TileId": discard}
        answers = await self.run_stage("hand", {seat: actions}, default)
        data = answers[seat]
        if data["action_type"] == "hu_self":
            self.wins = {seat: [{"patterns": list(result[1]), "fans": list(result[1]), "tiles": [player.hand_tiles[-1]], "hand": list(player.hand_tiles)}]}
            self.payer = None
            state.hu_class = "hu_self"
            state.game_status = "END"
        elif data["action_type"] in ("angang", "jiagang", "buzhang"):
            await self.declare_kong(seat, data["target_tile"], formal=data["action_type"] != "buzhang")
        else:
            tile = data["TileId"]
            cut_data = {"TileId": tile, "cutClass": tile == player.hand_tiles[-1] and player.has_draw_slot,
                        "cutIndex": player.hand_tiles.index(tile)}
            cut = await apply_player_cut(state, seat, cut_data)
            if cut is None:
                raise ValueError("服务器已验证的换六张出牌无法执行")
            tile, is_draw_cut, cut_index = cut
            player.discard_tiles.append(tile)
            player_action_record_cut(state, cut_tile=tile, is_moqie=is_draw_cut)
            state.clear_hu_pass_after_own_discard(seat)
            self.first_draw_pending.discard(seat)
            if seat == 0:
                self.pending_ready_discard = None
                state.xunmu += 1
            await state.broadcast_do_action(action_list=["cut"], action_player=seat, cut_tile=tile,
                                            cut_class=is_draw_cut, cut_tile_index=cut_index)
            state.current_claim_cut_tile = tile
            state.game_status = "waiting_action_after_cut"
        self.targets[seat] = {}
        self.phase = "playing"
        await self.broadcast()
