from dataclasses import replace

from ...game_calculation.nanjing.rules import VERSION, ScoreItem, payments, score_hand
from .state_machine import Phase as P


class EndOfHand:
    def _payments(self, winner, points, **kwargs):
        order=[p.player_index for p in sorted(self.player_list,key=lambda p:p.original_player_index)]
        return payments(winner,points,seat_order=order,**kwargs)

    def _ledger(self, kind, actor, changes, **details):
        if sum(changes):
            raise ValueError("结算必须为零和")
        self.ledger.append(dict(kind=kind, actor=actor, changes=list(changes), **details))
        for i,delta in enumerate(changes):
            self.round_changes[i] += delta
        self._event("nanjing_score", actor, ledger=self.ledger[-1])

    def settle_kong(self, index, kind, provider=None):
        chain = self.kong_chain_actor == index and self.kong_chain_count > 0
        responsible = self.kong_chain_payer if chain else None
        points = 5 if kind == "concealed" else 10
        payer = provider if kind in ("direct", "added") else None
        changes = self._payments(index, points, payer=payer,
                           responsible=() if responsible is None else (responsible,))
        self._ledger(kind + "_kong", index, changes, provider=provider, responsible=responsible)
        if not chain:
            self.kong_chain_actor, self.kong_chain_count = index, 0
        if self.kong_chain_payer is None and payer is not None:
            self.kong_chain_payer = payer
        self.kong_chain_count += 1

    def settle_follow(self, actor, tile):
        if len(self.discard_log) < 4 or len(self.discard_log)-4 < self.last_follow_end:
            return
        group = self.discard_log[-4:]
        first = group[0][0]
        if all(t == tile and i == (first+n) % 4 for n,(i,t) in enumerate(group)):
            changes = [5]*4
            changes[first] = -15
            self._ledger("follow", first, changes, tile=tile)
            self.last_follow_end = len(self.discard_log)

    def settle_four_discards(self, actor, tile):
        if self.player_list[actor].discard_origin_tiles.count(tile) == 4:
            changes = [5]*4
            changes[actor] = -15
            self._ledger("four_discards", actor, changes, tile=tile)

    def establish_contracts(self, index):
        p = self.player_list[index]
        if len(p.meld_sources) != 3:
            return
        partners = set(range(4)) - {index}
        for provider in p.meld_sources[:3]:
            if provider is not None:
                partners.intersection_update({provider})
        for other in partners:
            self.contracts[index].add(other)
            self.contracts[other].add(index)

    def same_suit_pungs(self, index, count=3):
        p=self.player_list[index]
        codes = p.combination_tiles[:count]
        if len(codes) < count or any((not c.startswith("k") and i not in p.pung_melds) or int(c[1:]) >= 40 for i,c in enumerate(codes)):
            return 0
        suits = {int(c[1:]) // 10 for c in codes}
        return next(iter(suits)) if len(suits) == 1 else 0

    def forced_claim_win(self, index, provider, tile, action):
        p = self.player_list[index]
        if len(p.combination_tiles) != 4:
            return None
        outer = action == "peng" and self.same_suit_pungs(index) == tile // 10
        partners = self.contracts[index]
        if provider in partners or outer:
            patterns = ("全求独钓", "清一色") if outer else ("全求独钓",)
            return self.settle_forced(index, tile, provider, patterns, partners or (provider,))
        return None

    def _finish_round(self, winner=None):
        self.next_dealer = 1
        self.match_finishing = self.current_round >= self.max_round*4
        self.ended_by = "wall" if winner is None else "win"
        self.machine.transition(P.END)

    def _append_win(self, winner, source, tile, payer, quote, changes, *, forced=False, order=1, recycle=True, multi_ron=False):
        p = self.player_list[winner]
        entry = dict(winner=winner, source=source, tile=tile, discarder=payer if source == "discard" else None,
                     kong_player=payer if source == "rob_kong" else None, points=quote.points,
                     fan_ids=quote.fan_ids, fan_names=list(quote.names), fan_details=quote.as_dict(),
                     score_changes=changes, rule_version=VERSION, multi_ron=multi_ron,
                     recycle_discard=recycle, hu_order=order, forced=forced)
        self.deferred_hu_settlements.append(entry)
        if forced:
            entry["fan_ids"].append("NJ|承包自动和|0")
        p.is_hu, p.hu_order, p.has_draw_slot = True, order, False
        self.hu_order_counter = order
        if source != "self_draw" and not forced:
            p.won_tiles.append(tile)
        for i,change in enumerate(changes):
            self.round_changes[i] += change
        self._event(self._hu_action_for_settlement(entry), winner, tile=tile)
        return entry

    def settle_forced(self, winner, tile, payer, patterns, responsible):
        p = self.player_list[winner]
        quote = score_hand(p.hand_tiles, p.combination_tiles, p.huapai_list,
                           winning_tile=tile, forced_patterns=patterns,pung_claimed=p.pung_claimed)
        changes = self._payments(winner, quote.points, payer=payer, responsible=responsible)
        self._append_win(winner, "self_draw" if payer is None else "discard", tile, payer, quote,
                         changes, forced=True, recycle=False)
        self.round_settlement = dict(winners=[winner], source="liability", changes=list(self.round_changes))
        self._finish_round(winner)
        return self._window(P.END)

    def settle_wins(self, winners, source, tile, *, payer=None):
        if self.machine.phase not in (P.TURN, P.RESPONSE, P.KONG) or self.round_settlement is not None:
            raise ValueError("当前阶段不能和牌结算")
        if not winners or len(winners) != len(set(winners)) or source not in ("self_draw", "discard", "rob_kong"):
            raise ValueError("无效的和牌参数")
        if source == "self_draw":
            if winners != [self.current_player_index] or payer is not None or self.machine.phase != P.TURN:
                raise ValueError("自摸和家无效")
        elif payer not in range(4) or payer in winners or payer != self.current_player_index:
            raise ValueError("放铳者无效")
        elif source == "discard":
            river = self.player_list[payer].discard_tiles
            if self.machine.phase != P.RESPONSE or not river or river[-1] != tile:
                raise ValueError("和牌张不属于当前舍牌")
        elif self.machine.phase != P.KONG or not self.pending_kong or self.pending_kong["tile"] != tile or self.pending_kong["concealed"]:
            raise ValueError("只有当前加杠可被抢杠")
        quotes = []
        for winner in winners:
            if winner not in range(4) or not self.can_win(winner, source, tile, payer=payer):
                raise ValueError("和牌资格不成立")
            quote = self.score_win(winner, source, tile, payer=payer)
            partners = self.contracts[winner]
            responsible = set()
            payment_payer = payer
            multiplier = 3 if source == "rob_kong" else 1
            if source == "self_draw" and partners:
                responsible = partners
                if "碰碰和" in quote.names:
                    patterns = tuple(x for x in quote.patterns if x.name != "碰碰和") + (ScoreItem("全求独钓",60),)
                    raw = 10+sum(x.points for x in patterns+quote.flowers)
                    quote = replace(quote,patterns=patterns,points=min(120,raw),uncapped_points=raw)
            elif source != "self_draw" and partners and quote.names.intersection({"碰碰和","清一色","全求独钓"}):
                responsible = partners
            elif source != "self_draw" and "清一色" in quote.names and self.same_suit_pungs(winner):
                responsible = {payer}
                multiplier = 3
            if source == "self_draw" and self.player_list[winner].after_kong and not responsible and self.kong_chain_payer is not None:
                responsible = {self.kong_chain_payer}
            changes = self._payments(winner,quote.points,payer=payment_payer,multiplier=multiplier,responsible=responsible)
            quotes.append((winner,quote,changes))
        if source == "discard":
            self.player_list[payer].discard_tiles.pop()
        elif source == "rob_kong":
            self.player_list[payer].hand_tiles.remove(tile)
            self.player_list[payer].has_draw_slot = False
        for n,(winner,quote,changes) in enumerate(quotes):
            self._append_win(winner,source,tile,payer,quote,changes,order=n+1,
                             recycle=n==0 and source=="discard",multi_ron=len(winners)>1)
        self.round_settlement = dict(winners=winners,source=source,changes=list(self.round_changes))
        self._finish_round(winners[0])
        self.pending_kong = None
        return self._window(P.END)

    def end_draw(self):
        if self.round_settlement is not None:
            raise ValueError("本局已经结算")
        self.round_settlement = dict(winners=[],source="draw",changes=list(self.round_changes))
        self._finish_round()
        return self._window(P.END)

    def apply_deferred_score_changes(self):
        if self.deferred_scores_applied or self.round_settlement is None:
            return
        for p,change in zip(self.player_list,self.round_changes):
            p.score += change
            p.score_history.append(f"{change:+d}" if change else "0")
            p.round_number_history.append(self.current_round)
        self.deferred_scores_applied = True
