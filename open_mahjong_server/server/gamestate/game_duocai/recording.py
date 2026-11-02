from ..public import game_record_manager as record
from .client_context import private_context


class Recording:
    def _update_record_counter_for_settlement(self, settlement, fan_ids):
        winner = self.player_list[settlement['winner']]
        winner.record_counter.win_turn += len(winner.discard_origin_tiles) + 1
        winner.record_counter.win_score += settlement['points']
        if settlement['source'] == 'self_draw':
            winner.record_counter.zimo_times += 1
        else:
            winner.record_counter.dianhe_times += 1
            payer = self.player_list[settlement['discarder']]
            payer.record_counter.fangchong_times += 1
            payer.record_counter.fangchong_score += -settlement['score_changes'][settlement['discarder']]

    def start_game_recording(self):
        record.init_game_record(self)
        self.game_record['game_title'].update(sub_rule=self.sub_rule, rule_version=self.rule_version,
            detailed_config=dict(self.detailed_config), starting_score=40000, return_score=40000)
        self.spectator_manager.record_game_title()

    def build_record_round_fields(self):
        self._record_hints = {}
        return dict(duocai=self.duocai_info(), duocai_hints={i:private_context(self, i, for_record=True) for i in range(4)})

    def record_visible_action(self, event):
        if not self._has_active_round_record():
            return
        action, actor = event['action'], event['player']
        if action in ('cut', 'angang', 'jiagang', 'rob_kan'):
            record.append_action_tick(self, ['reset', actor])
        if action == 'cut':
            record.player_action_record_cut(self, event['tile'], bool(event.get('cutClass')), bool(event.get('is_riichi_horizontal')))
        elif action in ('riichi', 'declare_kan', 'rob_kan', 'angang', 'jiagang', 'peng', 'gang', 'chi_left'):
            record.append_action_tick(self, ['duocai', 'action', event])
        else:
            super().record_visible_action(event)
        record.append_action_tick(self, ['duocai', 'state', self.duocai_info(), [p.score for p in self.player_list]])

    def finalize_round_recording(self):
        if not self._has_active_round_record():
            return
        self.apply_deferred_score_changes()
        for p in self.player_list:
            if any(not m.concealed for m in p.melds):
                p.record_counter.fulu_times += 1
        if not self.deferred_hu_settlements:
            record.player_action_record_liuju(self)
        record.append_action_tick(self, ['duocai', 'state', self.duocai_info(), [p.score for p in self.player_list]])
        record.player_action_record_round_end(self)
        self.round_record_finalized = True

    def finalize_match_result(self):
        order = sorted(range(4), key=lambda i: (-self.player_list[i].score, self.player_list[i].original_player_index))
        results = []
        for p in self.player_list:
            rank = order.index(p.player_index) + 1
            p.record_counter.rank_result = rank
            results.append(dict(rank=rank, score=p.score, points=(p.score - 40000) / 1000))
        if self.game_record.get('game_title') is not None:
            self.game_record['game_title']['duocai_final_result'] = {p.original_player_index:r for p, r in zip(self.player_list, results)}
        return results

    def finalize_game_recording(self):
        self.finalize_match_result()
        super().finalize_game_recording()

    def persist_game_record(self):
        self.finalize_match_result()
        if self.db_manager is None or not self.game_record:
            return None
        return self.db_manager.store_duocai_game_record(self.game_record, self.player_list, self.room_type, f'{self.max_round}/4')
