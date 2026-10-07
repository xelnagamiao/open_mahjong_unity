"""Authoritative Changchun state; ordinary phases use the Taiwan executor.

The extra pre-draw and final-four windows are explicit substates of the
existing hand-action phase. They never admit a discard or an ordinary kong.
"""
from dataclasses import replace

from ...game_calculation.changchun import rules as book
from ..game_taiwan.TaiwanGameState import TaiwanGameState
from ..game_taiwan import action_check as actions
from ..game_taiwan.wait_action import _collect_responses
from ..game_taiwan.boardcast import broadcast_do_action
from ..public.game_record_manager import append_action_tick
from .bao import BaoFlow
from .special_kongs import SpecialKongFlow
from .lifecycle import ChangchunLifecycle
from .timing import ACTION_PHASES, ChangchunActionClock, TimedActionQueue
from .tactical import ChangchunTacticalFlow


class ChangchunGameState(ChangchunTacticalFlow, BaoFlow, SpecialKongFlow, ChangchunLifecycle, TaiwanGameState):
    flower_tiles = ()
    structure_tiles = book.TILES
    # Online action windows use the room bank plus step, including claims.
    claim_response_seconds = None

    def __init__(self, game_server, room_data, calculation_service, db_manager, gamestate_id):
        from ...room.changchun_room import normalize_changchun_config
        if room_data.get("sub_rule", book.SUB_RULE) != book.SUB_RULE:
            raise ValueError("不支持的长春麻将子规则")
        config = normalize_changchun_config(room_data.get("detailed_config"))
        super().__init__(game_server, {**room_data, "detailed_config": None}, calculation_service, db_manager, gamestate_id)
        self.room_rule, self.sub_rule = "changchun", book.SUB_RULE
        self.rules = replace(self.rules, dead_wall_count=14, ready_qualification_mode="disabled",
            public_ready_enabled=True, declared_ready_win_policy="allow_pass", declared_ready_auto_added_kong=False,
            multi_win_mode="head_bump", chow_discard_restriction_mode="none", pung_same_tile_discard_forbidden=False,
            allow_kong_from_upper_discard=True, direct_kong_replacement_win_allowed=True,
            missed_win_blocks_self_draw=False, missed_win_blocks_claims=False, missed_win_released_by_kong=False,
            seven_flowers_steal_eighth_enabled=False)
        self.rules_dict = config
        # New room defaults are applied by validation; absent legacy flags stay off.
        self.tactical_call = bool(room_data.get('tactical_call', False)) and not any(
            player.is_bot for player in self.player_list)
        self.tactical_commit_lock = False  # MIL VI-4/5 permits higher-priority changes.
        from ..public.tactical_claim import TACTICAL_GRACE_SECONDS
        self.tactical_grace_seconds = TACTICAL_GRACE_SECONDS
        self._cc_tactical_recheck = False
        self.action_priority.update(hu_first=5, hu_second=4, hu_third=3, force_pass=0)
        self.dead_wall_count = 14
        self.hepai_limit = 0
        self.open_cuohe = False
        from .bot import changchun_bot_action
        self.smart_bot_action = changchun_bot_action
        self.action_clock_manager = ChangchunActionClock(self)
        self.action_queues = {i: TimedActionQueue(self) for i in range(4)}

    def _reset_taiwan_players(self):
        super()._reset_taiwan_players()
        self.cc_window = "normal"
        self.cc_turn_serial = 0
        self.cc_resume_draw = "normal"
        self.cc_tail_remaining = None
        self.cc_tail_tiles = []
        self.cc_bao_tile = None
        self.cc_bao_slot = None
        self.cc_bao_owner = None
        self.cc_bao_revision = 0
        self.cc_bao_used_slots = set()
        self.cc_bao_exhausted = False
        self.cc_wall_ids = []
        self.cc_pending_special = None
        self.cc_event = None
        self.kong_ledger = []
        self._terminal_result = None
        for player in self.player_list:
            player.locked_waits = set()
            player.passed_fan = -1
            player.cc_bao_seen = 0
            player.cc_looked_turn = -1
            player.cc_first_from_claim = False
            player.cc_no_kong_until_draw = False

    def action_clock(self, player, reconnecting=False):
        return self.action_clock_manager.clock(player, reconnecting)

    def player_snapshot_remaining_time(self, player):
        from .timing import display_seconds
        return display_seconds(player.remaining_time)

    claim_clock = action_clock

    def claim_clock_fields(self, player, reconnecting=False):
        return self.action_clock_manager.wire_fields(player, reconnecting)

    async def notify_cc_window_closed(self, index, *, include_player=False):
        if not self.realtime_spectators and not include_player:
            return
        from ...response import Response, Ask_other_action_info
        bank, step = self.action_clock(self.player_list[index])
        response = Response(type='gamestate/changchun/ask_closed', success=True,
            message='操作询问已结束', ask_other_action_info=Ask_other_action_info(
                remaining_time=bank, step_remaining=step, action_list=[], cut_tile=0,
                action_tick=self.server_action_tick, player_index=index,
                **self.claim_clock_fields(self.player_list[index])))
        if include_player:
            connection = self.game_server.user_id_to_connection.get(self.player_list[index].user_id)
            if connection is not None:
                await connection.websocket.send_json(response.model_dump(exclude_none=True))
        await self.send_to_realtime_spectators(index, response)

    def on_action_window_broadcast(self):
        self.action_clock_manager.begin()

    def on_action_window_delivered(self, index):
        self.action_clock_manager.delivered(index)

    def is_action_pending(self, index):
        return self.action_clock_manager.is_pending(index)

    async def collect_action_responses(self):
        if self.game_status in ACTION_PHASES:
            return await self.action_clock_manager.collect()
        return await _collect_responses(self)

    def _reset_hand_runtime(self):
        super()._reset_hand_runtime()
        manager = getattr(self, 'action_clock_manager', None)
        if manager is not None:
            manager.reset_round()

    def refresh_waits(self, index):
        player = self.player_list[index]
        player.waiting_tiles = book.waits(player.hand_tiles, player.combination_tiles)
        return player.waiting_tiles

    def score_candidate(self, index, source, tile=None, *, ignore_pass=False, **_):
        player = self.player_list[index]
        if "peida" in player.tag_list:
            return None
        hand = list(player.hand_tiles)
        self_draw = source == "self_draw"
        bao = ""
        if self_draw and self.cc_window == "before_draw":
            if index != self.current_player_index or not self.bao_visible(index):
                return None
            tile = self.cc_bao_tile
            hand.append(tile)
            bao = "chong"
        elif self_draw:
            tile = tile if tile is not None else player.last_drawn_tile
            if tile is None:
                return None
            if player.declared_ready and self.bao_visible(index) and tile == self.cc_bao_tile:
                bao = "mo"
        else:
            if tile is None:
                return None
            hand.append(tile)
        detail = book.score(hand, player.combination_tiles, winning_tile=tile, bao=bao)
        if detail and not self_draw and not ignore_pass and detail["fan"] <= player.passed_fan:
            return None
        return detail

    def score_action_candidate(self, index, source, tile=None):
        return self.score_candidate(index, source, tile)

    def ready_candidate_cuts(self, index):
        player = self.player_list[index]
        if player.ready_locked or self.cc_window != "normal":
            return {}
        result = {}
        for tile in sorted(set(player.hand_tiles)):
            hand = list(player.hand_tiles)
            hand.remove(tile)
            waiting = book.waits(hand, player.combination_tiles, declaration=True)
            if waiting:
                result[tile] = sorted(waiting)
        return result

    def _declare_ready(self, index):
        self.player_list[index].locked_waits = set(self.refresh_waits(index))
        super()._declare_ready(index)

    def _register_initial_heavenly_ready(self):
        pass

    def _revoke_qualification(self, index, **_):
        return False

    def _mark_eight_flowers_if_ready(self, index, **_):
        pass

    def _liability_payer_for_win(self, *args):
        return None

    def _remember_claim_liability(self, *args):
        pass

    async def _opening_flower_replacement(self):
        player = self.player_list[0]
        player.has_draw_slot = True
        player.last_drawn_tile = player.hand_tiles[-1]

    async def _process_drawn_flowers(self, index, origin):
        self.player_list[index].passed_fan = -1
        self.player_list[index].cc_no_kong_until_draw = False
        self.last_draw_after_kong = origin != "normal"
        return True

    def enter_water(self, index):
        if index != self.current_player_index:
            detail = self.result_dict.get(actions.hu_action_for_player(self.current_player_index, index))
            if detail:
                self.player_list[index].passed_fan = max(self.player_list[index].passed_fan, detail["fan"])
        return False

    def _finalize_ready_after_discard(self, index, declared):
        super()._finalize_ready_after_discard(index, declared)
        player = self.player_list[index]
        detail = self.score_candidate(index, "discard", player.last_discarded_tile, ignore_pass=True)
        if detail:
            player.passed_fan = max(player.passed_fan, detail["fan"])

    def check_hand_actions(self, index):
        result = {i: [] for i in range(4)}
        chosen = result[index]
        detail = self.score_candidate(index, "self_draw")
        if detail:
            chosen.append("hu_self")
            self.result_dict["hu_self"] = detail
        if self.cc_window == "before_draw":
            if self.can_change_bao():
                chosen.append("cc_change_bao")
            chosen.append("cc_draw")
            return result
        if self.cc_window == "final_four":
            chosen.append("cc_pass")
            return result
        player = self.player_list[index]
        for action, kind in (("angang", "concealed"), ("jiagang", "added")):
            if any(self.kong_allowed(index, tile, kind) for tile in set(player.hand_tiles)):
                chosen.append(action)
        if self.special_candidates(index):
            chosen.append("cc_special")
        if self.special_added_candidates(index):
            chosen.append("cc_added")
        player.riichi_candidate_cuts = self.ready_candidate_cuts(index)
        if player.riichi_candidate_cuts:
            chosen.append("riichi_cut")
        chosen.append("cut")
        return result

    def after_claim_actions(self):
        return self.check_hand_actions(self.current_player_index)

    def _claim_leaves_legal_hand(self, index, action, tile):
        player = self.player_list[index]
        code, required, _ = self._claim_mask(action, tile, "left")
        has_chow = code.startswith("s") or any(c.startswith("s") for c in player.combination_tiles)
        concealed_kongs = sum(c.startswith("G") for c in player.combination_tiles)
        after = len(player.hand_tiles)-len(required)-(0 if action == "gang" else 1)
        # A directed kong draws one and discards one: net concealed count here.
        return not has_chow or after + 4*concealed_kongs > 1

    def check_discard_actions(self, tile):
        result = actions.check_action_after_cut(self, tile)
        for index, offered in result.items():
            for action in list(offered):
                if action in ("peng", "gang", "chi_left", "chi_mid", "chi_right"):
                    if not self._claim_leaves_legal_hand(index, action, tile) or action == "gang" and not self.kong_allowed(index, tile, "direct"):
                        offered.remove(action)
            player = self.player_list[index]
            if index != self.current_player_index and player.ready_locked and self.kong_allowed(index,tile,"direct"):
                offered.append("gang")
            if offered == ["pass"]:
                offered.clear()
            elif offered and "pass" not in offered:
                offered.append("pass")
        return result

    def check_added_kong_actions(self, tile):
        result = self.check_discard_actions(tile)
        for index, offered in result.items():
            offered[:] = [a for a in offered if not a.startswith("chi_") and not a.startswith("hu_")]
            if index == self.current_player_index:
                continue
            detail = self.score_candidate(index, "robbing_kong", tile)
            if detail:
                action = actions.hu_action_for_player(self.current_player_index,index)
                offered.insert(0,action)
                self.result_dict[action] = detail
            if offered == ["pass"]:
                offered.clear()
            elif offered and "pass" not in offered:
                offered.append("pass")
        return result

    def build_game_info_fields(self):
        return {"detailed_config": self.rules_dict}

    def build_record_title_fields(self):
        return {"detailed_config": self.rules_dict, "rule_version": book.EDITION}

    def build_record_round_fields(self):
        return self.build_record_title_fields()

    def build_private_game_info_fields(self, viewer):
        return {"changchun": self.view_state(viewer)}

    def build_private_hand_action_info(self, index):
        player = self.player_list[index]
        info = {"changchun": self.view_state(index), "riichi_candidate_cuts": player.riichi_candidate_cuts,
                "kong_candidates": {a:[t for t in sorted(set(player.hand_tiles)) if self.kong_allowed(index,t,k)]
                                    for a,k in (("angang","concealed"),("jiagang","added"))}}
        info["changchun"]["special_candidates"] = self.special_candidates(index)
        info["changchun"]["added_candidates"] = self.special_added_candidates(index)
        info.update(self.action_clock_manager.wire_fields(player, reconnecting=True))
        return info

    def build_private_do_action_info(self, action_player, viewer_index):
        result = {"changchun": self.view_state(viewer_index)}
        if self.cc_event:
            event = dict(self.cc_event)
            if (event.get("kind") == "kong_score" and event.get("kong_kind") == "concealed"
                    and viewer_index != event.get("player")):
                event["tile"] = 0
            result["changchun"]["event"] = event
        return result

    async def emit_cc_event(self, event, *, private=False):
        # Private tile identity only appears in view_state for eligible viewers.
        self.cc_event = {k:v for k,v in event.items() if not private or k not in ("tile", "slot", "dice", "old_slot")}
        append_action_tick(self, ["cc", dict(event)])
        tactical_silent = getattr(self, '_tactical_silent_action', False)
        self._tactical_silent_action = False
        try:
            await broadcast_do_action(self, action_list=[], action_player=self.current_player_index)
        finally:
            self._tactical_silent_action = tactical_silent
            self.cc_event = None

    async def wait_action(self):
        if self.game_status in ("waiting_action_after_cut", "waiting_action_qianggang"):
            return await self.wait_claim_action()
        if self.game_status != "waiting_hand_action":
            return await super().wait_action()
        responses, allowed = await self.collect_action_responses()
        index = self.current_player_index
        data = responses.get(index, {})
        action = data.get("action_type")
        if action == "hu_self" and "hu_self" in allowed.get(index, ()):
            self.accept_self_draw(index)
        elif self.cc_window == "before_draw":
            if action == "cc_change_bao" and self.can_change_bao():
                await self.change_bao()
                self.action_dict = self.check_hand_actions(index)
            else:
                await self.draw_current_player()
        elif self.cc_window == "final_four":
            await self.pass_final_tile()
        elif action == "cc_special":
            await self.execute_special(index, data.get("target_tile"))
        elif action == "cc_added":
            await self.execute_special_added(index, data.get("target_tile"))
        elif action == "angang":
            await self.execute_angang(index, data.get("target_tile"))
        elif action == "jiagang":
            await self.execute_jiagang(index, data.get("target_tile"))
        elif action in ("cut", "riichi_cut"):
            await self.execute_cut(index,data,declare_ready=action=="riichi_cut")
        elif action is None:
            await self.execute_timeout_cut(index)
        return True

    async def execute_cut(self,index,data,**kwargs):
        if self.cc_window != "normal":
            return
        await super().execute_cut(index,data,**kwargs)

    def accept_self_draw(self,index):
        if self.cc_window != "before_draw":
            return super().accept_self_draw(index)
        detail = self.score_candidate(index,"self_draw")
        if detail:
            self._queue_winner_resolution([self._build_pending_winner(index,"bao_indicator","hu_self",detail,self.cc_bao_tile)])
