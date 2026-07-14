from __future__ import annotations

from typing import Any, Optional

class JiandanSettlementPolicy:
    """Jiandan scoring adapter, separate from hand flow and action handling."""

    def build(
        self,
        game_state: Any,
        winner_index: int,
        source: str,
        tile: int,
        *,
        payer_index: Optional[int] = None,
    ) -> dict:
        player = game_state.player_list[winner_index]
        hand_tiles = list(player.hand_tiles)
        if source != "self_draw":
            hand_tiles.append(tile)
        context = game_state._settlement_context(winner_index, source, hand_tiles, tile)

        detail = game_state.calculation_service.Jiandan_hepai_detail(
            hand_tiles,
            player.combination_tiles,
            [],
            tile,
            context,
        )

        if not detail["is_win"]:
            raise ValueError(f"Confirmed win for player {winner_index} is not a valid Jiandan hand.")
        return {
            "is_win": detail["is_win"],
            "points": detail["points"],
            "raw_points": detail["raw_points"],
            "fan_ids": list(detail["fan_ids"]),
            "fan_names": list(detail["fan_names"]),
            "score_changes": self.score_changes(
                game_state,
                winner_index,
                source,
                detail["points"],
                payer_index,
            ),
        }

    @staticmethod
    def score_changes(
        game_state: Any,
        winner_index: int,
        source: str,
        points: int,
        payer_index: Optional[int] = None,
    ) -> list[int]:
        changes = [0 for _ in game_state.player_list]
        if points <= 0:
            return changes
        if source == "self_draw":
            payers = [
                player.player_index
                for player in game_state.player_list
                if player.player_index != winner_index and not player.is_hu
            ]
            if not payers:
                return changes
            payment = points * {3: 2, 2: 3}.get(len(payers), 6)
            for payer in payers:
                changes[payer] -= payment
                changes[winner_index] += payment
            return changes

        if payer_index is None:
            raise ValueError(f"{source} win needs a payer index for score changes.")
        payment = points * 6
        changes[payer_index] -= payment
        changes[winner_index] += payment
        return changes
