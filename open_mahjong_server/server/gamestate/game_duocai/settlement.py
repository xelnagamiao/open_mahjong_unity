from dataclasses import asdict

from ...game_calculation.duocai import tiles as t
from ...game_calculation.duocai.furiten import logical_waits
from ...game_calculation.duocai.payments import exhaustive_payments, win_payments
from .state_machine import Phase as P


class EndOfHand:
    def settle_win(self, winner, source, tile, *, payer=None):
        if self.round_settlement is not None or type(winner) is not int or winner not in range(4):
            raise ValueError('和牌结算阶段错误')
        if source == 'self_draw':
            if self.machine.phase != P.TURN or winner != self.current_player_index or payer is not None:
                raise ValueError('自摸窗口错误')
        elif self.machine.phase != (P.ROB_KONG if source == 'rob_kong' else P.RESPONSE) or payer != self.current_player_index:
            raise ValueError('荣和来源错误')
        if not self.can_win(winner, source, tile, payer=payer):
            raise ValueError('未满足合法和牌条件')
        p = self.player_list[winner]
        quote = self.score_win(winner, source, tile, payer=payer)
        exposed = {g.logical[0] for g in quote.shape.groups if g.external and not g.concealed and g.kind in ('triplet', 'kong')}
        liabilities = {name: actor for name, actor in p.pao_liability.items()
                       if name == '四杠子' or ({45, 46, 47} if name == '大三元' else {41, 42, 43, 44}) <= exposed}
        changes = win_payments(quote, winner, discarder=payer, honba=self.honba, riichi_sticks=self.riichi_sticks,
                              liabilities=liabilities, rinshan_payer=p.rinshan_payer if source == 'self_draw' else None)
        if source == 'rob_kong':
            declarer = self.player_list[payer]
            drawn = declarer.has_draw_slot and declarer.hand_tiles[-1] == tile
            declarer.hand_tiles.remove(tile)
            declarer.has_draw_slot = False
            self._event('rob_kan', payer, tile=tile, is_mo_gang=drawn)
            self.pending_kong = None
        detail = asdict(quote)
        detail['ura_revealed'] = self.variant == 'riichi' and bool(p.riichi)
        entry = dict(winner=winner, source=source, tile=tile, discarder=payer,
                     points=changes[winner] - (1000 * self.riichi_sticks if self.variant == 'riichi' else 0),
                     fan_ids=list(quote.entries), fan_names=list(quote.entries), fan_details=detail,
                     score_changes=changes, rule_version=self.rule_version, multi_ron=False,
                     recycle_discard=False, hu_order=1)
        self.deferred_hu_settlements.append(entry)
        p.is_hu, p.hu_order, p.has_draw_slot = True, 1, False
        self.hu_order_counter = 1
        self.riichi_sticks = 0
        for player, change in zip(self.player_list, changes):
            player.score += change
        self._event(self._hu_action_for_settlement(entry), winner, tile=tile)
        return self.finish_round(winner=winner, score=detail)

    def finish_round(self, *, winner=None, score=None, reason='win', tenpai=()):
        if self.round_settlement is not None:
            raise ValueError('本副已结算')
        replacement = self.decks.replace_revealed(self.wall.revealed_jokers())
        continued = self.variant == 'riichi' and (winner == 0 or reason not in ('win', 'wall') or reason == 'wall' and 0 in tenpai)
        self.next_dealer = 0 if continued else 1
        self.match_finishing = replacement['exhausted'] or any(p.score < 0 for p in self.player_list) or not continued and self.current_round >= self.max_round * 4
        if self.variant == 'riichi':
            self.honba = self.honba + 1 if continued or reason != 'win' else 0
            if self.match_finishing and self.riichi_sticks:
                top = min(range(4), key=lambda i: (-self.player_list[i].score, self.player_list[i].original_player_index))
                self.player_list[top].score += self.riichi_sticks * 1000
                self.riichi_sticks = 0
        self.round_changes = [p.score - start for p, start in zip(self.player_list, self.round_start_scores)]
        self.round_settlement = dict(winner=winner, score=score, reason=reason, tenpai=list(tenpai),
                                    replacement=replacement, changes=list(self.round_changes))
        self.ended_by = reason
        self.machine.transition(P.END)
        return self._window(P.END)

    def end_draw(self, reason='wall'):
        ready = []
        if self.variant == 'riichi' and reason == 'wall':
            ready = [i for i, p in enumerate(self.player_list) if logical_waits(tuple(p.hand_tiles), tuple(p.melds))]
            nagashi = [i for i, p in enumerate(self.player_list) if p.discard_origin_tiles
                       and len(p.discard_tiles) == len(p.discard_origin_tiles)
                       and all(t.face(v) in t.ORPHANS for v in p.discard_origin_tiles)]
            changes = [0] * 4
            if nagashi:
                for winner in nagashi:
                    for payer in range(4):
                        if payer != winner:
                            amount = (4000 if winner == 0 or payer == 0 else 2000) + 100 * self.honba
                            changes[payer] -= amount
                            changes[winner] += amount
                top = min(nagashi)
                changes[top] += self.riichi_sticks * 1000
                self.riichi_sticks = 0
                ready = nagashi
            else:
                changes = exhaustive_payments(ready)
            for player, change in zip(self.player_list, changes):
                player.score += change
        return self.finish_round(reason=reason, tenpai=ready)

    def apply_deferred_score_changes(self):
        if self.deferred_scores_applied or self.round_settlement is None:
            return
        for p, change in zip(self.player_list, self.round_changes):
            p.score_history.append(f'{change:+d}' if change else '0')
            p.round_number_history.append(self.current_round)
            p.record_counter.round_score_total = getattr(p.record_counter, 'round_score_total', 0) + change
        self.deferred_scores_applied = True
