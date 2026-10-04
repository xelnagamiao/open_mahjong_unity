"""Score calculation and payer responsibility; no mutation of the live hand."""
from ...game_calculation.zhongyong import HandContext, score_hand
from ..game_jiandan.settlement import JiandanSettlementPolicy

class ZhongyongSettlementPolicy:
    def build(self, state, winner_index, source, tile, *, payer_index=None):
        if state.is_nanque:
            return JiandanSettlementPolicy().build(state, winner_index, source, tile, payer_index=payer_index)
        player = state.player_list[winner_index]
        hand = list(player.hand_tiles) + ([] if source == "self_draw" else [tile])
        context = state._settlement_context(winner_index, source, hand, tile)
        detail = score_hand(HandContext(hand, player.combination_tiles, tile, **context))
        if not detail.is_win: raise ValueError("Invalid Zhongyong winning hand")
        responsible = self.responsible_player(state, winner_index, tile, payer_index) if source != "self_draw" else None
        changes = self.score_changes(winner_index, detail.points, responsible)
        return dict(is_win=True, points=detail.points, raw_points=detail.raw_points, fan_ids=list(detail.fan_ids),
                    fan_names=list(detail.fan_names), score_changes=changes, responsible=responsible)

    @staticmethod
    def responsible_player(state, winner, tile, discarder):
        offset = state.last_discard_offsets[winner]
        if offset >= 0 and state.discard_log[offset][1] == tile: return None
        for player, discarded in state.discard_log[max(0, offset + 1):]:
            if discarded == tile: return player
        return discarder

    @staticmethod
    def score_changes(winner, points, responsible=None):
        changes = [-points] * 4
        changes[winner] = points * 3
        if points > 25 and responsible is not None:
            changes = [-25] * 4
            changes[winner] = points * 3
            changes[responsible] = -(points * 3 - 50)
        return changes
