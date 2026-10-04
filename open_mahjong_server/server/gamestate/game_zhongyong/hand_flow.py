from __future__ import annotations
from typing import Any, Dict, Optional
import logging
from ..public.logic_common import back_current_num
logger = logging.getLogger(__name__)

class HandFlow:
    def begin_hand_action(self, player_index: Optional[int] = None, *, is_get_gang_tile: bool = False) -> dict:
        """Refresh waiting cache and return actions after a draw or supplement draw."""
        player_index = self.current_player_index if player_index is None else player_index
        self.current_player_index = player_index
        self.hand_action_is_gang_draw[player_index] = bool(is_get_gang_tile)
        self.action_policy.refresh_waiting_tiles(self, player_index, exclude_last_tile=True)
        self.game_status = "waiting_hand_action"
        return {
            "status": self.game_status,
            "player": player_index,
            "actions": self.action_policy.check_hand_action(self, player_index, is_get_gang_tile=is_get_gang_tile),
        }


    def begin_only_cut(self, player_index: int) -> dict:
        """Return the forced-discard action window after chi or peng."""
        self.current_player_index = player_index
        self.hand_action_is_gang_draw[player_index] = False
        self.game_status = "onlycut_after_action"
        return {
            "status": self.game_status,
            "player": player_index,
            "actions": self.action_policy.check_only_cut(self, player_index),
        }


    def apply_turn_action(
        self,
        player_index: int,
        action: str,
        *,
        tile: Optional[int] = None,
        settlement: Optional[dict] = None,
    ) -> dict:
        """Apply one selected self/turn action and return the next response window."""
        if action == "cut":
            if tile is None:
                raise ValueError("Discard action requires a tile.")
            self.hand_action_is_gang_draw[player_index] = False
            cut_tile = self.record_discard(player_index, tile)
            for other in self.player_list:
                if other.player_index != player_index and not other.is_hu:
                    self.action_policy.refresh_waiting_tiles(self, other.player_index)
            self.game_status = "waiting_action_after_cut"
            return {
                "status": self.game_status,
                "player": player_index,
                "tile": cut_tile,
                "actions": self.action_policy.check_after_cut(self, cut_tile),
            }

        if action == "hu_self":
            win_tile = tile if tile is not None else self.player_list[player_index].hand_tiles[-1]
            if win_tile is not None and win_tile <= 0:
                win_tile = self.player_list[player_index].hand_tiles[-1]
            self.record_self_draw_win(player_index, win_tile, settlement)
            if self.game_status == "END":
                return {"status": self.game_status, "player": player_index, "tile": win_tile, "actions": None}
            drawn_tile = self.draw_after_discard_resolution(player_index)
            if drawn_tile is None:
                return {"status": self.game_status, "player": None, "tile": None, "actions": None}
            next_window = self.begin_hand_action(self.current_player_index)
            next_window["drawn_tile"] = drawn_tile
            return next_window

        if action == "angang":
            if tile is None:
                raise ValueError("Concealed-kong action requires a tile.")
            result = self.declare_concealed_kong(player_index, tile)
            next_window = self.begin_hand_action(player_index, is_get_gang_tile=True)
            next_window["result"] = result
            next_window["drawn_tile"] = result.get("drawn_tile")
            return next_window

        if action == "jiagang":
            if tile is None:
                raise ValueError("Added-kong action requires a tile.")
            result = self.attempt_added_kong(player_index, tile)
            for other in self.player_list:
                if other.player_index != player_index and not other.is_hu:
                    self.action_policy.refresh_waiting_tiles(self, other.player_index)
            return {
                "status": self.game_status,
                "player": player_index,
                "tile": tile,
                "result": result,
                "actions": self.action_policy.check_jiagang(self, tile),
            }

        raise ValueError(f"Unsupported turn action: {action}")


    def continue_after_discard_responses(
        self,
        discarder_index: int,
        tile: int,
        win_responses: Dict[int, str],
        claim_responses: Optional[Dict[int, str]] = None,
        settlements: Optional[Dict[int, dict]] = None,
    ) -> dict:
        """Resolve a discard response window and return a consistent next window."""
        win_result = self.resolve_discard_win_responses(discarder_index, tile, win_responses, settlements)
        if win_result["winners"]:
            return self._window_after_draw_result(win_result, "discard_win", {"win_result": win_result})
        if self.game_status == "END":
            return self._end_window({"win_result": win_result})

        claim_result = self.resolve_discard_claim_responses(discarder_index, tile, claim_responses or {})
        if not claim_result["claimed"]:
            return self._window_after_draw_result(
                claim_result,
                "discard_no_claim",
                {"win_result": win_result, "claim_result": claim_result},
            )
        if claim_result["action"] == "gang":
            window = self.begin_hand_action(claim_result["claimant"], is_get_gang_tile=True)
            window["reason"] = "discard_gang"
            window["claim_result"] = claim_result
            window["drawn_tile"] = claim_result.get("drawn_tile")
            return window
        window = self.begin_only_cut(claim_result["claimant"])
        window["reason"] = "discard_claim"
        window["claim_result"] = claim_result
        return window


    def continue_after_rob_kong_responses(
        self,
        kong_player_index: int,
        tile: int,
        responses: Dict[int, str],
        settlements: Optional[Dict[int, dict]] = None,
    ) -> dict:
        """Resolve a robbing-kong response window and return a consistent next window."""
        result = self.resolve_added_kong_responses(kong_player_index, tile, responses, settlements)
        if result["robbed"]:
            return self._window_after_draw_result(result, "rob_kong", {"rob_kong_result": result})
        if self.game_status == "END":
            return self._end_window({"rob_kong_result": result})
        window = self.begin_hand_action(kong_player_index, is_get_gang_tile=True)
        window["reason"] = "added_kong"
        window["drawn_tile"] = result.get("drawn_tile")
        window["rob_kong_result"] = result
        return window


    def next_active_index(self, from_index: int) -> Optional[int]:
        if len(self.tiles_list) <= self.dead_wall_count:
            return None
        for offset in range(1, len(self.player_list) + 1):
            player_index = (from_index + offset) % len(self.player_list)
            if not self.player_list[player_index].is_hu:
                return player_index
        return None


    def mark_player_hu(self, player_index: int, settlement: Optional[dict] = None) -> None:
        player = self.player_list[player_index]
        # Ignore repeated or late declarations after this profile closes the hand.
        if player.is_hu or self.game_status == "END" or self.hu_count >= self.winner_limit:
            return
        self.hu_order_counter += 1
        player.is_hu = True
        player.hu_order = self.hu_order_counter
        player.has_draw_slot = False
        player.tag_list.append(("first_hu", "second_hu", "third_hu")[player.hu_order - 1])
        self._mark_opening_action(interrupt=True)
        if settlement is not None:
            self.deferred_hu_settlements.append({**settlement, "winner": player_index, "hu_order": player.hu_order})
        # Exhausting the wall must not close a shared discard-response window
        # before every simultaneous winner is recorded. The draw continuation
        # handles wall exhaustion after all responses have been resolved.
        if self.hu_count >= self.winner_limit:
            self.ended_by = "win"
            self.game_status = "END"


    def apply_deferred_score_changes(self) -> None:
        """Apply one hand's settlements and persist one scoreboard row exactly once."""
        if self.deferred_scores_applied:
            return
        round_score_changes = [0 for _ in self.player_list]
        for settlement in self.deferred_hu_settlements:
            changes = settlement.get("score_changes")
            if not changes:
                continue
            for player_index, change in enumerate(changes):
                if player_index < len(self.player_list):
                    self.player_list[player_index].score += change
                    round_score_changes[player_index] += change
        for player in self.player_list:
            score_change = round_score_changes[player.player_index]
            if score_change > 0:
                score_change_text = f"+{score_change:02d}"
            elif score_change < 0:
                score_change_text = f"-{abs(score_change):02d}"
            else:
                score_change_text = "0"
            player.score_history.append(score_change_text)
            player.round_number_history.append(self.current_round)
        self.deferred_scores_applied = True


    def should_end_hand(self) -> bool:
        return self.hu_count >= self.winner_limit or len(self.tiles_list) <= self.dead_wall_count


    @property
    def hu_count(self) -> int:
        return sum(1 for player in self.player_list if player.is_hu)


    def advance_dealer_after_round(self) -> int:
        # Follow the established Qingque seat model: rotate every identity's
        # wind and sort by the new seat, so the next dealer remains East (0).
        for player in self.player_list:
            player.player_index = back_current_num(player.player_index)
        self.player_list.sort(key=lambda player: player.player_index)
        self.dealer_index = 0
        self.current_round += 1
        self.round_index += 1
        return self.dealer_index


    def clear_lockout_on_discard(self, player_index: int, discarded_tile: int) -> None:
        self.player_list[player_index].discard_win_lockout_tiles = {discarded_tile}


    def add_discard_win_lockout(self, player_index: int, tile: int) -> None:
        self.player_list[player_index].discard_win_lockout_tiles.add(tile)


    def can_win_by_discard(self, player_index: int, tile: int) -> bool:
        return not self.is_nanque or tile not in self.player_list[player_index].discard_win_lockout_tiles


    def record_discard(self, player_index: int, tile: int) -> int:
        """Apply a discard and start the next same-tile discard-win lockout window."""
        player = self.player_list[player_index]
        if player.is_hu:
            raise ValueError("A player who has already won cannot discard.")
        if tile not in player.hand_tiles:
            raise ValueError(f"Player {player_index} cannot discard tile {tile}; it is not in hand.")
        player.hand_tiles.remove(tile)
        player.discard_tiles.append(tile)
        player.discard_origin_tiles.append(tile)
        self.discard_log.append((player_index, tile))
        self.last_discard_offsets[player_index] = len(self.discard_log) - 1
        player.has_draw_slot = False
        self.current_player_index = player_index
        self._mark_opening_action()
        self.clear_lockout_on_discard(player_index, tile)
        return tile


    def record_discard_win_pass(self, player_index: int, tile: int) -> None:
        """Record that a player skipped a currently available discard win."""
        player = self.player_list[player_index]
        if player.is_hu:
            return
        if tile in player.waiting_tiles and self.can_win_by_discard(player_index, tile):
            self.add_discard_win_lockout(player_index, tile)


    def record_discard_win(
        self,
        winner_index: int,
        discarder_index: int,
        tile: int,
        settlement: Optional[dict] = None,
    ) -> None:
        """Record a discard win while deferring fan details until final settlement."""
        if not self.can_win_by_discard(winner_index, tile):
            raise ValueError(f"Player {winner_index} is locked out from winning on tile {tile}.")
        if settlement is None:
            settlement = self.settlement_policy.build(
                self,
                winner_index,
                "discard",
                tile,
                payer_index=discarder_index,
            )
        self.mark_player_hu(
            winner_index,
            {
                "source": "discard",
                "discarder": discarder_index,
                "tile": tile,
                **settlement,
            },
        )


    def record_self_draw_win(self, winner_index: int, tile: int, settlement: Optional[dict] = None) -> None:
        """Record a self-draw win while deferring fan details until final settlement."""
        player = self.player_list[winner_index]
        if player.is_hu:
            return
        if tile not in player.hand_tiles:
            raise ValueError(f"Player {winner_index} cannot self-draw win on tile {tile}; it is not in hand.")
        if settlement is None:
            settlement = self.settlement_policy.build(self, winner_index, "self_draw", tile)
        self.mark_player_hu(
            winner_index,
            {
                "source": "self_draw",
                "tile": tile,
                **settlement,
            },
        )


    def draw_after_discard_resolution(self, from_index: int) -> Optional[int]:
        """Draw for the next active player after a discard-response branch resolves."""
        next_index = self.next_active_index(from_index)
        if next_index is None:
            self.ended_by = self.ended_by or "wall"
            self.game_status = "END"
            return None
        self.current_player_index = next_index
        if self.should_end_hand():
            self.ended_by = self.ended_by or "wall"
            self.game_status = "END"
            return None
        drawn_tile = self.player_list[next_index].get_tile(self.tiles_list)
        self.natural_draw_count[next_index] = self.natural_draw_count.get(next_index, 0) + 1
        return drawn_tile


    def resolve_discard_win_responses(
        self,
        discarder_index: int,
        tile: int,
        responses: Dict[int, str],
        settlements: Optional[Dict[int, dict]] = None,
    ) -> dict:
        """Resolve discard-win/pass choices before lower-priority claims."""
        settlements = settlements or {}
        winners: list[int] = []
        passed: list[int] = []
        for player_index in self._response_order_after(discarder_index, responses):
            action = responses[player_index]
            if player_index == discarder_index or self.player_list[player_index].is_hu:
                continue
            if action == "pass":
                self.record_discard_win_pass(player_index, tile)
                passed.append(player_index)
            elif action == "hu":
                if tile not in self.player_list[player_index].waiting_tiles:
                    raise ValueError(f"Player {player_index} is not waiting on tile {tile}.")
                self.record_discard_win(
                    player_index,
                    discarder_index,
                    tile,
                    settlements.get(player_index),
                )
                winners.append(player_index)
                if not self.is_nanque:
                    break
            elif action in {"", "none"}:
                continue
            else:
                raise ValueError(f"Unsupported discard-win response: {action}")

        if winners:
            discarder = self.player_list[discarder_index]
            if discarder.discard_tiles and discarder.discard_tiles[-1] == tile:
                discarder.discard_tiles.pop()
            for index, winner in enumerate(winners):
                settlement = self._latest_hu_settlement_for(winner)
                settlement["multi_ron"] = len(winners) > 1
                settlement["recycle_discard"] = index == len(winners) - 1
        drawn_tile = None
        draw_player = None
        if self.game_status != "END" and winners:
            drawn_tile = self.draw_after_discard_resolution(winners[-1])
            draw_player = self.current_player_index if drawn_tile is not None else None

        return {
            "winners": winners,
            "passed": passed,
            "draw_player": draw_player,
            "drawn_tile": drawn_tile,
            "ended": self.game_status == "END",
        }


    def resolve_discard_claim_responses(
        self,
        discarder_index: int,
        tile: int,
        responses: Dict[int, str],
    ) -> dict:
        """Resolve chi/peng/gang claims after discard wins are declined."""
        claim = self._select_discard_claim(discarder_index, responses)
        if claim is None:
            drawn_tile = self.draw_after_discard_resolution(discarder_index)
            return {
                "claimed": False,
                "claimant": None,
                "action": None,
                "meld_code": None,
                "draw_player": self.current_player_index if drawn_tile is not None else None,
                "drawn_tile": drawn_tile,
                "next_status": self.game_status,
            }

        claimant_index, action = claim
        player = self.player_list[claimant_index]
        discarder = self.player_list[discarder_index]
        # 与其它规则一致：鸣牌后从打牌者河牌移除被认走张，并记入 discard_origin_tiles
        if discarder.discard_tiles:
            if discarder.discard_tiles[-1] == tile:
                discarder.discard_tiles.pop(-1)
            elif tile in discarder.discard_tiles:
                for i in range(len(discarder.discard_tiles) - 1, -1, -1):
                    if discarder.discard_tiles[i] == tile:
                        discarder.discard_tiles.pop(i)
                        break
        discarder.discard_origin_tiles.append(tile)
        meld_code, combination_mask = self._apply_discard_claim(player, tile, action)
        self.current_player_index = claimant_index

        drawn_tile = None
        if action == "gang":
            if not self.tiles_list:
                raise ValueError("Cannot claim a discard kong when the wall is empty.")
            drawn_tile = self._draw_supplement_tile(claimant_index)
            self.game_status = "waiting_hand_action"
        else:
            player.has_draw_slot = False
            self.game_status = "onlycut_after_action"

        return {
            "claimed": True,
            "claimant": claimant_index,
            "action": action,
            "meld_code": meld_code,
            "combination_mask": combination_mask,
            "draw_player": claimant_index if drawn_tile is not None else None,
            "drawn_tile": drawn_tile,
            "next_status": self.game_status,
        }


    def declare_concealed_kong(self, player_index: int, tile: int) -> dict:
        """Declare a concealed kong, store true tile server-side, and draw a supplement."""
        player = self.player_list[player_index]
        is_mo_gang = bool(player.has_draw_slot and player.hand_tiles and player.hand_tiles[-1] == tile)
        self._remove_tiles(player.hand_tiles, [tile, tile, tile, tile])
        player.has_draw_slot = False
        player.combination_tiles.append(f"G{tile}")
        combination_mask = [2, tile, 2, tile, 2, tile, 2, tile]
        player.combination_mask.append(combination_mask)
        self._mark_opening_action(interrupt=True)
        drawn_tile = self._draw_supplement_tile(player_index)
        return {
            "player": player_index,
            "action": "angang",
            "meld_code": f"G{tile}",
            "public_meld_code": "G0",
            "combination_mask": combination_mask,
            "is_mo_gang": is_mo_gang,
            "drawn_tile": drawn_tile,
            "next_status": self.game_status,
        }


    def attempt_added_kong(self, player_index: int, tile: int) -> dict:
        """Validate an added-kong attempt without mutating state before robbing responses."""
        player = self.player_list[player_index]
        if tile not in player.hand_tiles:
            raise ValueError(f"Player {player_index} cannot add kong tile {tile}; it is not in hand.")
        if f"k{tile}" not in player.combination_tiles:
            raise ValueError(f"Player {player_index} has no exposed triplet k{tile} to upgrade.")
        self.current_player_index = player_index
        self._mark_opening_action(interrupt=True)
        self.game_status = "waiting_action_qianggang"
        is_mo_gang = bool(player.has_draw_slot and player.hand_tiles and player.hand_tiles[-1] == tile)
        try:
            triplet_index = player.combination_tiles.index(f"k{tile}")
            combination_mask = list(player.combination_mask[triplet_index])
        except (ValueError, IndexError):
            combination_mask = [1, tile, 0, tile, 0, tile]
        insert_index = next((idx for idx in range(0, len(combination_mask), 2) if combination_mask[idx] == 1), 0)
        preview_mask = list(combination_mask)
        preview_mask[insert_index:insert_index] = [3, tile]
        return {
            "player": player_index,
            "tile": tile,
            "action": "jiagang",
            "meld_code": f"k{tile}",
            "combination_mask": preview_mask,
            "is_mo_gang": is_mo_gang,
            "next_status": self.game_status,
        }


    def resolve_added_kong_responses(
        self,
        kong_player_index: int,
        tile: int,
        responses: Dict[int, str],
        settlements: Optional[Dict[int, dict]] = None,
    ) -> dict:
        """Resolve robbing-kong responses, or finalize the added kong and supplement draw."""
        settlements = settlements or {}
        winners: list[int] = []
        passed: list[int] = []
        for player_index in self._response_order_after(kong_player_index, responses):
            action = responses[player_index]
            if player_index == kong_player_index or self.player_list[player_index].is_hu:
                continue
            if action == "pass":
                if tile in self.player_list[player_index].waiting_tiles and self.can_win_by_discard(player_index, tile):
                    self.add_discard_win_lockout(player_index, tile)
                passed.append(player_index)
            elif action == "hu":
                if tile not in self.player_list[player_index].waiting_tiles:
                    raise ValueError(f"Player {player_index} is not waiting on added-kong tile {tile}.")
                if not self.can_win_by_discard(player_index, tile):
                    raise ValueError(f"Player {player_index} is locked out from robbing kong tile {tile}.")
                self.mark_player_hu(
                    player_index,
                    {
                        "source": "rob_kong",
                        "kong_player": kong_player_index,
                        "tile": tile,
                        **(
                            settlements.get(player_index)
                            or self.settlement_policy.build(
                                self,
                                player_index,
                                "rob_kong",
                                tile,
                                payer_index=kong_player_index,
                            )
                        ),
                    },
                )
                winners.append(player_index)
                if not self.is_nanque:
                    break
            elif action in {"", "none"}:
                continue
            else:
                raise ValueError(f"Unsupported robbing-kong response: {action}")

        if winners:
            self._consume_robbed_added_kong_tile(kong_player_index, tile)
            for index, winner in enumerate(winners):
                settlement = self._latest_hu_settlement_for(winner)
                settlement["multi_ron"] = len(winners) > 1
                settlement["recycle_discard"] = index == len(winners) - 1
            drawn_tile = None
            draw_player = None
            if self.game_status != "END":
                drawn_tile = self.draw_after_discard_resolution(winners[-1])
                draw_player = self.current_player_index if drawn_tile is not None else None
            return {
                "robbed": True,
                "winners": winners,
                "passed": passed,
                "draw_player": draw_player,
                "drawn_tile": drawn_tile,
                "ended": self.game_status == "END",
            }

        kong_result = self._finalize_added_kong(kong_player_index, tile)
        drawn_tile = self._draw_supplement_tile(kong_player_index)
        return {
            "robbed": False,
            "winners": [],
            "passed": passed,
            **kong_result,
            "draw_player": kong_player_index,
            "drawn_tile": drawn_tile,
            "ended": self.game_status == "END",
        }


    def _select_discard_claim(self, discarder_index: int, responses: Dict[int, str]) -> Optional[tuple[int, str]]:
        candidates: list[tuple[int, int, int, str]] = []
        fixed_next_player = (discarder_index + 1) % 4
        priority = {
            "gang": 2,
            "peng": 2,
            "chi_left": 1,
            "chi_mid": 1,
            "chi_right": 1,
        }
        for player_index, action in responses.items():
            if action not in priority:
                continue
            if player_index == discarder_index or self.player_list[player_index].is_hu:
                continue
            if action.startswith("chi") and player_index != fixed_next_player:
                continue
            distance = (player_index - discarder_index) % 4
            candidates.append((-priority[action], distance, player_index, action))
        if not candidates:
            return None
        _, _, player_index, action = min(candidates)
        return player_index, action


    @staticmethod
    def _response_order_after(from_index: int, responses: Dict[int, str]) -> list[int]:
        return sorted(responses, key=lambda player_index: (player_index - from_index) % 4)


    def _apply_discard_claim(self, player: ZhongyongPlayer, tile: int, action: str) -> tuple[str, list[int]]:
        if action == "peng":
            hand_tiles = [tile, tile]
            self._remove_tiles(player.hand_tiles, hand_tiles)
            meld_code = f"k{tile}"
            combination_mask = [1, tile, 0, hand_tiles[0], 0, hand_tiles[1]]
        elif action == "gang":
            hand_tiles = [tile, tile, tile]
            self._remove_tiles(player.hand_tiles, hand_tiles)
            meld_code = f"g{tile}"
            combination_mask = [1, tile, 0, hand_tiles[0], 0, hand_tiles[1], 0, hand_tiles[2]]
        elif action == "chi_left":
            hand_tiles = [tile - 2, tile - 1]
            self._remove_tiles(player.hand_tiles, hand_tiles)
            meld_code = f"s{tile - 2}"
            combination_mask = [1, tile, 0, hand_tiles[0], 0, hand_tiles[1]]
        elif action == "chi_mid":
            hand_tiles = [tile - 1, tile + 1]
            self._remove_tiles(player.hand_tiles, hand_tiles)
            meld_code = f"s{tile - 1}"
            combination_mask = [1, tile, 0, hand_tiles[0], 0, hand_tiles[1]]
        elif action == "chi_right":
            hand_tiles = [tile + 1, tile + 2]
            self._remove_tiles(player.hand_tiles, hand_tiles)
            meld_code = f"s{tile}"
            combination_mask = [1, tile, 0, hand_tiles[0], 0, hand_tiles[1]]
        else:
            raise ValueError(f"Unsupported discard claim action: {action}")
        player.combination_tiles.append(meld_code)
        player.combination_mask.append(combination_mask)
        self._mark_opening_action(interrupt=True)
        return meld_code, combination_mask


    def _finalize_added_kong(self, player_index: int, tile: int) -> dict:
        player = self.player_list[player_index]
        is_mo_gang = bool(player.has_draw_slot and player.hand_tiles and player.hand_tiles[-1] == tile)
        self._remove_tiles(player.hand_tiles, [tile])
        player.has_draw_slot = False
        try:
            triplet_index = player.combination_tiles.index(f"k{tile}")
        except ValueError as exc:
            raise ValueError(f"Player {player_index} has no exposed triplet k{tile} to upgrade.") from exc
        if triplet_index < len(player.combination_mask):
            combination_mask = list(player.combination_mask[triplet_index])
        else:
            combination_mask = [1, tile, 0, tile, 0, tile]
        insert_index = next((idx for idx in range(0, len(combination_mask), 2) if combination_mask[idx] == 1), 0)
        combination_mask[insert_index:insert_index] = [3, tile]
        player.combination_mask[triplet_index:triplet_index + 1] = [combination_mask]
        player.combination_tiles[triplet_index] = f"g{tile}"
        return {
            "meld_code": f"g{tile}",
            "previous_meld_code": f"k{tile}",
            "combination_mask": combination_mask,
            "is_mo_gang": is_mo_gang,
        }


    def _consume_robbed_added_kong_tile(self, player_index: int, tile: int) -> None:
        """Robbed added-kong tile leaves the kong player's hand, but the meld stays a triplet."""
        player = self.player_list[player_index]
        self._remove_tiles(player.hand_tiles, [tile])
        player.has_draw_slot = False


    def _window_after_draw_result(self, result: dict, reason: str, extra: dict) -> dict:
        if result.get("ended") or self.game_status == "END":
            return self._end_window(extra)
        draw_player = result.get("draw_player")
        if draw_player is None:
            return self._end_window(extra)
        window = self.begin_hand_action(draw_player)
        window["reason"] = reason
        window["drawn_tile"] = result.get("drawn_tile")
        window.update(extra)
        return window


    def _end_window(self, extra: Optional[dict] = None) -> dict:
        payload = {"status": "END", "player": None, "actions": None, "ended_by": self.ended_by}
        if extra:
            payload.update(extra)
        return payload


    def _mark_opening_action(self, *, interrupt: bool = False) -> None:
        self.opening_action_taken = True
        if interrupt:
            self.opening_flow_interrupted = True


    def _pre_win_tiles_for_context(self, source: str, hand_tiles: list[int], winning_tile: int) -> list[int]:
        pre_win_tiles = list(hand_tiles)
        if winning_tile in pre_win_tiles:
            pre_win_tiles.remove(winning_tile)
        return pre_win_tiles


    def _settlement_context(self, winner_index: int, source: str, hand_tiles: list[int], tile: int) -> dict:
        rinshan = source == "self_draw" and bool(self.hand_action_is_gang_draw.get(winner_index))
        context = {
            "win_source": source,
            "pre_win_tiles": self._pre_win_tiles_for_context(source, hand_tiles, tile),
            "heavenly_win": (
                source == "self_draw"
                and winner_index == self.dealer_index
                and not self.opening_action_taken
                and not rinshan
            ),
            "earthly_win": self.is_nanque and (
                source == "self_draw"
                and winner_index != self.dealer_index
                and self.natural_draw_count.get(winner_index, 0) == 1
                and not self.opening_flow_interrupted
                and not rinshan
            ),
            "haitei": source == "self_draw" and len(self.tiles_list) <= self.dead_wall_count,
            "houtei": source == "discard" and len(self.tiles_list) <= self.dead_wall_count,
            "rinshan": rinshan,
            "chankan": source == "rob_kong",
            "seat_wind": 41 + winner_index,
        }

        if not self.is_nanque:
            context["earthly_win"] = (source == "discard" and winner_index != self.dealer_index
                and len(self.discard_log) == 1 and self.discard_log[0][0] == self.dealer_index
                and not self.opening_flow_interrupted)
        return context


    def _draw_supplement_tile(self, player_index: int) -> int:
        if len(self.tiles_list) <= self.dead_wall_count:
            self.ended_by = self.ended_by or "wall"
            self.game_status = "END"
            raise ValueError("Cannot draw a supplement tile when the wall is empty.")
        self.current_player_index = player_index
        drawn_tile = self.tiles_list.pop()
        self.player_list[player_index].hand_tiles.append(drawn_tile)
        self.player_list[player_index].has_draw_slot = True
        self.game_status = "waiting_hand_action"
        return drawn_tile


    @staticmethod
    def _remove_tiles(hand_tiles: list[int], tiles: list[int]) -> None:
        for tile in tiles:
            if tile not in hand_tiles:
                raise ValueError(f"Cannot remove tile {tile}; it is not in hand.")
        for tile in tiles:
            hand_tiles.remove(tile)
