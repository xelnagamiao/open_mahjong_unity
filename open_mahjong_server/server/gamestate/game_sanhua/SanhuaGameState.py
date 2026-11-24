from collections import Counter
from dataclasses import dataclass, field

from ...game_calculation.sanhua import rules
from ...game_calculation.sanhua.wall import RED, deal, special_tiles
from ..game_beijing.actions import CHOWS, claim_tiles, empty_actions
from ..game_beijing.runtime import Runtime
from ..game_beijing.settlement import EndOfHand
from ..game_new_chinese.flow import HandFlow as CommonFlow
from ..game_new_chinese.state_machine import Phase as P, StateMachine
from ..public.ai.auto_cut_policy import choose_auto_cut_action
from ..game_zhongyong.ZhongyongGameState import ZhongyongGameState, ZhongyongPlayer
from .protocol import Protocol


@dataclass
class Player(ZhongyongPlayer):
    specials: list = field(default_factory=list)
    added: list = field(default_factory=list)
    riichi_flags: list = field(default_factory=list)
    calls: list = field(default_factory=list)
    baoqing: bool = False
    liable: int | None = None
    after_kong: bool = False

    def reset_for_round(self, round_time):
        super().reset_for_round(round_time)
        self.specials.clear()
        self.added.clear()
        self.riichi_flags.clear()
        self.calls.clear()
        self.baoqing = self.after_kong = False
        self.liable = None


class SanhuaGameState(Protocol, Runtime, ZhongyongGameState):
    def __init__(self, game_server=None, room_data=None, calculation_service=None, db_manager=None, gamestate_id="sanhua-test"):
        data = dict(room_data or dict(room_id=31, player_list=[101, 102, 103, 104], game_round=4))
        if len(data["player_list"]) != 4:
            raise ValueError("散花天才麻将需要四家")
        data.update(room_rule="new_chinese", sub_rule=rules.SUB_RULE)
        self.machine = StateMachine()
        super().__init__(game_server, data, calculation_service, db_manager, gamestate_id)
        self.player_list = [Player(**vars(p)) for p in self.player_list]
        self.rule_version = rules.VERSION
        self.detailed_config = {"rule_version": rules.VERSION}
        self.hepai_limit, self.dead_wall_count = 4, 0
        self.bot_speed = data.get("bot_speed", "normal")

    game_status = property(lambda self: self.machine.phase.value, lambda self, v: self.machine.transition(v))
    window = CommonFlow.window
    event = CommonFlow.event
    apply_deferred_score_changes = EndOfHand.apply_deferred_score_changes

    def initialize_round(self, *, wall=None, dice=None):
        self.reset_round_state()
        hands, self.tiles_list, self.marker, self.indicator, self.dice = deal(
            self.round_random_seed, self.dealer_index, wall=wall, dice=dice)
        self.skin, self.joker = special_tiles(self.indicator)
        for p, hand in zip(self.player_list, hands):
            p.hand_tiles = hand
        self.player_list[self.dealer_index].has_draw_slot = True
        self.pending_kong = self.round_settlement = None
        self.match_finishing = False
        self.round_start_scores = [p.score for p in self.player_list]
        self._action_clocks, self._action_deadlines = {}, {}

    def table(self):
        return [rules.table_fans(p.combination_tiles, p.specials, self.joker, p.added) for p in self.player_list]

    def quote(self, seat, tile, payer=None, *, rob=False):
        p = self.player_list[seat]
        hand = p.hand_tiles if payer is None else [*p.hand_tiles, tile]
        return rules.score(hand, p.combination_tiles, self.indicator, seat, payer, self.table(),
                           declared=p.baoqing, liable=p.liable, replacement=p.after_kong, rob=rob)

    def kong_tiles(self, seat, kind):
        if not self.tiles_list:
            return set()
        p = self.player_list[seat]
        special = {self.skin, self.joker, RED}
        if kind == "angang":
            return {t for t, n in Counter(p.hand_tiles).items() if n == 4 or t in special}
        return {t for t in p.hand_tiles if t not in special and f"k{t}" in p.combination_tiles}

    def opening_window(self):
        return self.turn(self.dealer_index)

    def cut_tiles(self, seat):
        tiles = set(self.player_list[seat].hand_tiles)
        return tiles if self.tiles_list else tiles - {RED, self.skin, self.joker}

    def _build_timeout_action(self, index):
        result = super()._build_timeout_action(index)
        if result and result["action_type"] == "cut" and result["TileId"] not in self.cut_tiles(index):
            result.update(choose_auto_cut_action(self.player_list[index], ["cut"], self.cut_tiles(index)))
        return result

    def turn(self, seat, *, after_chow_or_pung=False):
        p = self.player_list[seat]
        self.current_player_index = seat
        self.hand_action_is_gang_draw[seat] = p.after_kong
        choices = ["cut"] if self.cut_tiles(seat) else []
        if p.has_draw_slot and self.quote(seat, p.hand_tiles[-1]):
            choices.append("hu_self")
        if "cut" not in choices:
            if not choices:
                return self.finish()
            choices.append("pass")
        choices += [k for k in ("angang", "jiagang") if self.kong_tiles(seat, k)]
        if after_chow_or_pung and len(p.calls) == 2 and p.calls[0] == p.calls[1] and p.calls[0] in (1, 2, 3):
            choices.append("baoqing")
        return self.window(P.TURN if p.has_draw_slot else P.AFTER_MELD, seat, actions={seat: choices})

    def claims(self, actor, tile, *, rob=False):
        choices = empty_actions()
        specials = {RED, self.skin, self.joker}
        for seat, p in enumerate(self.player_list):
            if seat == actor:
                continue
            offered = choices[seat]
            if self.quote(seat, tile, actor, rob=rob):
                offered.append("hu")
            if not rob and tile not in specials:
                if p.hand_tiles.count(tile) >= 2:
                    offered.append("peng")
                if p.hand_tiles.count(tile) == 3 and self.tiles_list:
                    offered.append("gang")
                if seat == (actor + 1) % 4:
                    for action in CHOWS:
                        needed = claim_tiles(action, tile)
                        if needed and not specials.intersection(needed) and Counter(needed) <= Counter(p.hand_tiles):
                            offered.append(action)
            if offered:
                offered.append("pass")
        return choices

    def cut_position(self, p, data):
        tile = data.get("TileId")
        if type(tile) != int or tile not in p.hand_tiles:
            raise ValueError("无效出牌")
        if not self.tiles_list and tile in (RED, self.skin, self.joker):
            raise ValueError("牌墙已空，不能单张杠")
        drawn = data.get("cutClass")
        if drawn is None:
            drawn = p.has_draw_slot and tile == p.hand_tiles[-1]
        if type(drawn) != bool:
            raise ValueError("无效出牌类型")
        if drawn and p.has_draw_slot and p.hand_tiles[-1] == tile:
            return len(p.hand_tiles) - 1, True
        if not drawn and tile in (p.hand_tiles[:-1] if p.has_draw_slot else p.hand_tiles):
            return p.hand_tiles.index(tile), False
        raise ValueError("出牌位置不合法")

    def validate_response(self, index, data, offered):
        action = data.get("action_type")
        if action not in offered:
            raise ValueError("当前无此操作")
        if action == "cut":
            self.cut_position(self.player_list[index], data)
        if action in ("angang", "jiagang") and data.get("target_tile") not in self.kong_tiles(index, action):
            raise ValueError("无效杠牌")

    def apply_action_results(self, window, responses, *, settlements=None):
        if window is not self.live_pending_window or window["action_tick"] != self.server_action_tick:
            raise ValueError("过期行动窗口")
        if set(responses) != {i for i, actions in window["actions"].items() if actions}:
            raise ValueError("缺少行动")
        for i, data in responses.items():
            self.validate_response(i, data, window["actions"][i])
        if self.machine.phase in (P.TURN, P.AFTER_MELD):
            seat = window["player"]
            result = self.take_turn(seat, responses[seat])
        else:
            result = self.resolve_claims(window, responses)
        return self.open_action_window(result)

    def take_turn(self, seat, data):
        p = self.player_list[seat]
        kind = data["action_type"]
        if kind == "hu_self":
            return self.settle(seat, p.hand_tiles[-1])
        if kind == "pass":
            return self.finish()
        if kind == "baoqing":
            p.baoqing = True
            self.event(kind, seat)
            return self.turn(seat)
        if kind in ("angang", "jiagang"):
            tile = data["target_tile"]
            if kind == "jiagang" or tile in (RED, self.skin, self.joker):
                return self.offer_kong(seat, tile, kind)
            return self.commit_kong(seat, tile, kind)
        pos, drawn = self.cut_position(p, data)
        tile = p.hand_tiles[pos]
        if tile in (RED, self.skin, self.joker):
            # A special tile is a single-tile concealed kong. Even a stale or
            # automated cut request must not discard it as an ordinary tile.
            return self.offer_kong(seat, tile, "angang")
        tile = p.hand_tiles.pop(pos)
        p.hand_tiles.sort()
        p.discard_tiles.append(tile)
        p.discard_origin_tiles.append(tile)
        p.riichi_flags.append(False)
        self.discard_log.append((seat, tile))
        self.last_discard_offsets[seat] = len(self.discard_log) - 1
        p.has_draw_slot = p.after_kong = False
        self.event("cut", seat, tile=tile, cutIndex=data.get("cutIndex", pos), cutClass=drawn,
                   is_timeout_action=data.get("is_timeout_action", False))
        return self.window(P.RESPONSE, seat, tile, self.claims(seat, tile))

    def offer_kong(self, seat, tile, kind):
        choices = self.claims(seat, tile, rob=True)
        if not any(choices.values()):
            return self.commit_kong(seat, tile, kind)
        self.pending_kong = (seat, tile, kind)
        return self.window(P.ROB_KONG, seat, tile, choices)

    def resolve_claims(self, window, responses):
        source, tile = window["player"], window["tile"]
        rob = self.machine.phase == P.ROB_KONG
        ordered = sorted(responses, key=lambda i: (i - source) % 4)
        if any(responses[i]["action_type"] == "hu" for i in ordered):
            winner = next(i for i in ordered if "hu" in window["actions"][i])
            return self.settle(winner, tile, source, rob=rob)
        if rob:
            return self.commit_kong(*self.pending_kong)
        for kinds in (("gang", "peng"), CHOWS):
            for seat in ordered:
                action = responses[seat]["action_type"]
                if action not in kinds:
                    continue
                p = self.player_list[seat]
                p.calls.append(tile // 10 if action != "gang" else 0)
                if p.baoqing and len(p.calls) == 3:
                    p.liable = source
                CommonFlow.meld(self, seat, source, tile, action)
                return self.draw(seat, kong=True) if action == "gang" else self.turn(seat, after_chow_or_pung=True)
        return self.draw((source + 1) % 4)

    def commit_kong(self, seat, tile, kind):
        self.pending_kong = None
        p = self.player_list[seat]
        drawn = p.has_draw_slot and p.hand_tiles[-1] == tile
        if tile in (RED, self.skin, self.joker):
            p.hand_tiles.pop(-1 if drawn else p.hand_tiles.index(tile))
            p.specials.append(tile)
            p.has_draw_slot = False
            self.event("buhua", seat, tile=tile, is_mo_gang=drawn)
        elif kind == "jiagang":
            p.hand_tiles.remove(tile)
            pos = p.combination_tiles.index(f"k{tile}")
            mask = list(p.combination_mask[pos])
            at = next(i for i in range(0, len(mask), 2) if mask[i] == 1)
            mask[at:at] = [3, tile]
            p.combination_tiles[pos], p.combination_mask[pos] = f"g{tile}", mask
            p.added.append(tile)
            p.has_draw_slot = False
            self.event(kind, seat, tile=tile, meld_code=f"g{tile}", combination_mask=mask, is_mo_gang=drawn)
        else:
            p.hand_tiles = [t for t in p.hand_tiles if t != tile]
            p.calls.append(0)
            mask = [2, tile] * 4
            p.combination_tiles.append(f"G{tile}")
            p.combination_mask.append(mask)
            p.has_draw_slot = False
            self.event(kind, seat, tile=tile, meld_code=f"G{tile}", combination_mask=mask, is_mo_gang=drawn)
        return self.draw(seat, kong=True)

    def draw(self, seat, kong=False):
        if not self.tiles_list:
            return self.finish()
        pos = len(self.tiles_list) - 1 if kong else 0
        revealed = self.marker == pos
        self.marker = -1 if revealed else self.marker - int(pos == 0 and self.marker >= 0)
        tile = self.tiles_list.pop(pos)
        p = self.player_list[seat]
        p.hand_tiles.sort()
        p.hand_tiles.append(tile)
        p.has_draw_slot, p.after_kong = True, kong
        self.current_player_index = seat
        self.event("deal_gang_tile" if kong else "deal_tile", seat, tile=tile)
        if kong and revealed:
            return self.offer_kong(seat, tile, "angang")
        return self.turn(seat)

    def settle(self, seat, tile, payer=None, *, rob=False):
        quote = self.quote(seat, tile, payer, rob=rob)
        if not quote:
            raise ValueError("未满足和牌条件")
        if payer is not None:
            source = self.player_list[payer]
            if rob:
                source.hand_tiles.remove(tile)
                source.has_draw_slot = False
                self.event("sanhua_rob", payer, tile=tile)
                self.pending_kong = None
            else:
                source.discard_tiles.pop()
                source.riichi_flags.pop()
        entry = dict(winner=seat, source="rob_kong" if rob else "self_draw" if payer is None else "discard",
                     discarder=payer, tile=tile, points=quote["points"], fan_ids=quote["fan_names"],
                     fan_names=quote["fan_names"], fan_details=quote, score_changes=quote["changes"],
                     recycle_discard=payer is not None and not rob, multi_ron=False, hu_order=1)
        self.deferred_hu_settlements.append(entry)
        self.player_list[seat].is_hu = True
        self.player_list[seat].hu_order = self.hu_order_counter = 1
        for p, change in zip(self.player_list, quote["changes"]):
            p.score += change
        self.event(self._hu_action_for_settlement(entry), seat, tile=tile)
        return self.finish(seat)

    def finish(self, winner=None):
        self.next_dealer = self.dealer_index if winner is None else winner
        self.ended_by = "wall" if winner is None else "win"
        self.match_finishing = self.current_round >= self.max_round
        self.round_changes = [p.score - start for p, start in zip(self.player_list, self.round_start_scores)]
        self.round_settlement = dict(winner=winner, changes=self.round_changes)
        return self.window(P.END)

    def advance_round_after_ready(self):
        self.dealer_index = self.next_dealer
        self.current_round += 1
        self.round_index += 1
        self.machine.transition(P.START)

    def schedule_bot_actions(self):
        import asyncio
        from .bot import play
        for i in self.waiting_players_list:
            if self.player_list[i].is_bot and self.bot_action_ticks.get(i) != self.server_action_tick:
                self.bot_action_ticks[i] = self.server_action_tick
                task = asyncio.create_task(play(self, i, self.server_action_tick))
                self.bot_tasks.add(task)
                task.add_done_callback(self.bot_tasks.discard)
