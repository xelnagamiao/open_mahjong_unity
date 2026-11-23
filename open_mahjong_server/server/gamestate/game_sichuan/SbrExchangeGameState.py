from collections import Counter
import asyncio
import time
from random import Random

from .SichuanGameState import SichuanGameState
from .three_tile_opening import ThreeTileOpening
from .boardcast import broadcast_dingque_done, broadcast_xueliu_exchange_animation
from ..public.game_record_manager import init_game_round
from ...game_calculation.sichuan.sbr_exchange import ExchangeChajiao, basic_points
from ...game_calculation.sichuan.xueliu_rules import SBR_EXCHANGE_SUB_RULE


class SbrExchangeGameState(ThreeTileOpening, SichuanGameState):
    is_sbr_exchange = True
    xueliu_exchange = True
    xueliu_opening_action = "xueliu_exchange_three"

    def _configure_rule(self):
        if self.sub_rule != SBR_EXCHANGE_SUB_RULE:
            raise ValueError("此状态机仅支持血战换三张")
        self.xueliu_rule_profile = None
        for player in self.player_list:
            player.passed_pungs = set()

    def _reset_round_state(self):
        super()._reset_round_state()
        self.deal_dice = None
        self.exchange_dice = None
        self.last_cut_after_kong = False
        self.exchange_deadline = 0
        self.recorded_passed_fans = {}
        for player in self.player_list:
            player.passed_pungs.clear()

    def _consume_xueliu_throw_tiles(self, seat, chosen):
        self.player_list[seat].xueliu_throw_tiles = list(chosen)

    async def _opening_phase(self):
        await self._collect_xueliu_selection(range(len(self.player_list)))
        before = [list(p.hand_tiles) for p in self.player_list]
        rng = Random(f"{self.round_random_seed}:sbr-exchange")
        self.exchange_dice = [rng.randint(1, 6), rng.randint(1, 6)]
        red = self.exchange_dice[0]
        direction = 1 + (red - 1) // 2
        self.exchange_direction = direction
        for phase, seconds in (("sbr_exchange_dice", 2.35), ("xueliu_exchange_animation", 1.90)):
            self.game_status = phase
            self.server_action_tick += 1
            self.exchange_deadline = time.monotonic() + seconds
            await broadcast_xueliu_exchange_animation(self)
            await asyncio.sleep(max(0, self.exchange_deadline - time.monotonic()))
        for seat, player in enumerate(self.player_list):
            for tile in player.xueliu_throw_tiles:
                player.hand_tiles.remove(tile)
            sender = self.player_list[(seat - direction) % len(self.player_list)]
            player.hand_tiles.extend(sender.xueliu_throw_tiles)
        init_game_round(self)
        rd = self.game_record["game_round"][f"round_index_{self.round_index}"]
        rd.update(exchange_start_hands=before, deal_dice=self.deal_dice, exchange_dice=list(self.exchange_dice),
                  xueliu_exchange_direction=direction,
                  xueliu_throw_tiles={p.player_index: list(p.xueliu_throw_tiles) for p in self.player_list})
        self.game_status = "sbr_exchange_received"
        await self.broadcast_game_start()
        await asyncio.sleep(0.75)
        await self._dingque_phase()
        rd["dingque_suits"] = {p.player_index: p.dingque_suit for p in self.player_list}
        await broadcast_dingque_done(self)

    def exchange_view(self, seat):
        outgoing = list(self.player_list[seat].xueliu_throw_tiles)
        if self.game_status == "waiting_xueliu_throw" and outgoing:
            return dict(selection_done=True, outgoing_tiles=outgoing)
        if self.game_status == "sbr_exchange_dice":
            return dict(dice=list(self.exchange_dice), direction=0, active_players=[],
                        outgoing_tiles=outgoing, animating=True,
                        animation_seconds=max(0, self.exchange_deadline - time.monotonic()))
        if self.game_status != "xueliu_exchange_animation":
            return None
        return dict(active_players=list(range(4)), outgoing_tiles=outgoing, outgoing_suit=0,
                    teammate_suit=0, direction=self.exchange_direction, cards_face_down=True,
                    animating=True, animation_seconds=max(0, self.exchange_deadline - time.monotonic()))

    def hint_window(self, seat, tile=None):
        from .duplicate_wall import wall_exhausted
        from ..public.hand_draw_source import get_hand_draw_source
        actor = self.current_player_index
        draw = tile is None
        if (seat == actor) != draw:
            return None
        player = self.player_list[seat]
        if draw:
            if not player.hand_tiles or ("cut" not in self.action_dict.get(seat, ())):
                return None
            if not player.has_draw_slot and (player.combination_tiles or self._original_discards(player)):
                return None
            tile = player.hand_tiles[-1]
        robbing = not draw and self.game_status == "waiting_action_qianggang"
        kong = ((get_hand_draw_source(self, actor) == "deal_gang_tile" or self.last_action_was_gang) if draw
                else self.jiagang_after_gang if robbing else getattr(self, "last_cut_after_kong", False))
        method = "handgot" if draw else "qianggang" if robbing else "dianhe"
        way = self.opening_fans(seat, method, kong)
        return dict(tile=tile, draw=draw, kong=kong, robbing=robbing, last=wall_exhausted(self) and not robbing,
                    heaven="天和" in way, earth="地和" in way)

    def record_passed_fans(self):
        rounds = self.game_record.get("game_round", {})
        rd = rounds.get(f"round_index_{self.round_index}")
        if rd is None:
            return
        node = len(rd["action_ticks"]) + 1
        for p in self.player_list:
            cap = p.shunhe_passed_max_fan
            previous = self.recorded_passed_fans.get(p.player_index)
            if cap != previous:
                self.recorded_passed_fans[p.player_index] = cap
                history = rd.setdefault("sbr_passed_fans", {}).setdefault(p.player_index, {})
                history[node] = -1 if cap is None else cap

    def opening_fans(self, seat, method, kong_draw=False):
        player = self.player_list[seat]
        uninterrupted = not kong_draw and not any(p.combination_tiles or p.is_hu for p in self.player_list)
        if method == "handgot":
            if uninterrupted and not self._original_discards(player):
                if seat == self.dealer_index:
                    return ["天和"]
                return ["自摸", "地和"]
            return ["自摸"]
        if (method == "dianhe" and uninterrupted and self.current_player_index == self.dealer_index
                and len(self._original_discards(self.player_list[self.dealer_index])) == 1
                and sum(len(self._original_discards(p)) for p in self.player_list) == 1):
            return ["地和"]
        return []

    def _classify_hua_zhu(self, hand_tiles, combination_tiles, dingque_suit, original_discards):
        if not self._player_has_dingque_tile(hand_tiles, combination_tiles, dingque_suit):
            return None
        if any(int(meld[1:]) // 10 == dingque_suit for meld in combination_tiles):
            return "active"
        return "active" if any(tile // 10 != dingque_suit for tile in original_discards) else "passive"

    def _base_from_fan(self, fan, names):
        return basic_points(fan)

    def _chajiao_fan(self, hand, melds, dingque):
        return ExchangeChajiao().max_hepai_fan(hand, melds, dingque)

    def _active_flower_pig_score_changes(self, player_index):
        return {player_index: -96}

    def _assign_final_ranks(self):
        scores = [p.score for p in self.player_list]
        frequency = Counter(scores)
        for player in self.player_list:
            rank = 1 + sum(score > player.score for score in scores)
            player.record_counter.rank_result = rank
            player.competition_points = 5 - rank - (frequency[player.score] - 1) / 2
        self.game_record["game_title"]["sichuan_final_result"] = {
            str(p.original_player_index): {"competition_points": p.competition_points} for p in self.player_list
        }
