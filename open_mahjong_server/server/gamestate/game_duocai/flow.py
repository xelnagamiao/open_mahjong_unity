from itertools import product

from .actions import KINDS, claim_actions, empty_actions, legal_cuts, riichi_cuts, turn_actions
from .state_machine import Phase as P
from ...game_calculation.duocai import tiles as t
from ...game_calculation.duocai.actions import resolve_response
from ...game_calculation.duocai.solver import Meld, meld_options


class HandFlow:
    def _window(self, phase, actor=None, tile=None, actions=None):
        self.machine.transition(phase)
        return dict(status=phase.value, player=actor, tile=tile, actions=actions or empty_actions())

    def _event(self, action, actor, **fields):
        event = dict(action=action, player=actor, **fields)
        self.domain_events.append(event)
        self.emit_visible_action_payloads(event)

    def opening_window(self):
        return self.begin_turn(0)

    def begin_turn(self, index, after_meld=False):
        self.current_player_index = index
        self.hand_action_is_gang_draw[index] = self.player_list[index].after_kong
        return self._window(P.AFTER_MELD if after_meld else P.TURN, index, actions=turn_actions(self, index, after_meld))

    def selected_choice(self, index, data):
        options = [c for c in self.meld_choices.get(index, ()) if KINDS.get(c.kind, c.kind) == data['action_type']]
        key = data.get('choice_id')
        if key is None and len(options) == 1:
            return options[0]
        match = next((c for c in options if c.key == key), None)
        if match is None:
            raise ValueError('须选择当前可用的实体牌组合')
        return match

    def validate_response(self, index, data, offered):
        if type(index) is not int or index not in range(4) or not isinstance(data, dict) or data.get('action_type') not in offered:
            raise ValueError('行动不在当前窗口内')
        action = data['action_type']
        if action in ('cut', 'riichi_cut'):
            tile, _, _ = self._cut_identity(index, data)
            if action == 'riichi_cut' and tile not in riichi_cuts(tuple(self.player_list[index].hand_tiles), tuple(self.player_list[index].melds)):
                raise ValueError('立直宣言牌不能形成听牌')
        elif action in ('angang', 'jiagang', 'gang', 'peng', 'chi_left'):
            self.selected_choice(index, data)

    def _cut_identity(self, index, data):
        p = self.player_list[index]
        tile, position, drawn = data.get('TileId'), data.get('cutIndex', -1), data.get('cutClass')
        if type(tile) is not int or tile not in legal_cuts(self, index):
            raise ValueError('非法出牌')
        if type(position) is not int or position < -1:
            raise ValueError('出牌索引无效')
        if drawn is not None and type(drawn) is not bool:
            raise ValueError('出牌类型无效')
        if drawn is True:
            if not p.has_draw_slot or p.hand_tiles[-1] != tile:
                raise ValueError('摸切身份错误')
            position = len(p.hand_tiles) - 1
        elif drawn is False:
            held = p.hand_tiles[:-1] if p.has_draw_slot else p.hand_tiles
            if tile not in held:
                raise ValueError('手切牌不在手牌中')
            position = held.index(tile)
        else:
            position = len(p.hand_tiles) - 1 if p.has_draw_slot and p.hand_tiles[-1] == tile else p.hand_tiles.index(tile)
        actual_drawn = p.has_draw_slot and position == len(p.hand_tiles) - 1
        if drawn is False and actual_drawn:
            raise ValueError('手切索引指向摸牌')
        if p.riichi and not actual_drawn:
            raise ValueError('立直后必须摸切')
        return tile, position, actual_drawn

    def apply_action_results(self, window, responses, *, settlements=None):
        if settlements or window is not self.live_pending_window or window.get('action_tick') != self.server_action_tick:
            raise ValueError('客户端结算或过期窗口')
        offered = window['actions']
        if set(responses) != {i for i, a in offered.items() if a}:
            raise ValueError('响应座位集合错误')
        for i, data in responses.items():
            self.validate_response(i, data, offered[i])
        if window['status'] in (P.TURN.value, P.AFTER_MELD.value):
            result = self._turn(window, responses[window['player']])
        elif window['status'] in (P.RESPONSE.value, P.ROB_KONG.value):
            result = self._response(window, responses)
        else:
            raise ValueError('当前阶段不能行牌')
        return self.open_action_window(result)

    def _turn(self, window, data):
        index, action = window['player'], data['action_type']
        p = self.player_list[index]
        if action == 'hu_self':
            return self.settle_win(index, 'self_draw', p.hand_tiles[-1])
        if action == 'jiuzhongjiupai':
            return self.end_draw('nine_terminals')
        if action in ('angang', 'jiagang'):
            choice = self.selected_choice(index, data)
            if choice.meld_index >= 0 and not p.melds[choice.meld_index].concealed:
                self.pending_kong = choice
                self.interrupt_opening()
                self._event('declare_kan', index, tile=choice.take[0], choice=choice.public())
                return self._window(P.ROB_KONG, index, choice.take[0], claim_actions(self, index, choice.take[0], rob=True))
            return self.commit_kong(index, choice)
        tile, position, drawn = self._cut_identity(index, data)
        if p.has_draw_slot:
            p.temporary_furiten = False
        p.ippatsu = False
        p.riichi_pending = action == 'riichi_cut'
        p.hand_tiles.pop(position)
        p.hand_tiles.sort()
        p.has_draw_slot = p.after_kong = False
        p.draw_kind, p.rinshan_payer = 'none', None
        p.discard_tiles.append(tile)
        p.discard_origin_tiles.append(tile)
        p.discard_riichi_flags.append(p.riichi_pending)
        self.discard_log.append((index, tile))
        self.last_discard_offsets[index] = len(self.discard_log) - 1
        self._event('cut', index, tile=tile, cutIndex=data.get('cutIndex', position), cutClass=drawn,
                    is_riichi_horizontal=p.riichi_pending, is_timeout_action=bool(data.get('is_timeout_action')))
        return self._window(P.RESPONSE, index, tile, claim_actions(self, index, tile))

    def complete_riichi(self, index):
        p = self.player_list[index]
        if not p.riichi_pending:
            return
        p.riichi_pending = False
        p.riichi = 2 if len(p.discard_origin_tiles) == 1 and not self.opening_flow_interrupted else 1
        p.ippatsu = True
        p.score -= 1000
        self.riichi_sticks += 1
        p.tag_list.append('riichi')
        self._event('riichi', index, score=p.score, sticks=self.riichi_sticks)

    def _response(self, window, responses):
        actor, tile = window['player'], window['tile']
        rob = window['status'] == P.ROB_KONG.value
        for index, data in responses.items():
            if self.variant == 'riichi' and 'hu' in window['actions'][index] and data['action_type'] != 'hu':
                p = self.player_list[index]
                if p.riichi:
                    p.riichi_furiten = True
                else:
                    p.temporary_furiten = True
        resolution = resolve_response(actor, {i: 'chi' if d['action_type'] == 'chi_left' else 'minggang' if d['action_type'] == 'gang' else d['action_type'] for i, d in responses.items()})
        if resolution and resolution[1] == 'hu':
            return self.settle_win(resolution[0], 'rob_kong' if rob else 'discard', tile, payer=actor)
        if rob:
            choice, self.pending_kong = self.pending_kong, None
            return self.commit_kong(actor, choice)
        self.complete_riichi(actor)
        if self.variant == 'riichi':
            if all(p.riichi for p in self.player_list):
                return self.end_draw('four_riichi')
            if not self.opening_flow_interrupted and all(len(p.discard_origin_tiles) == 1 for p in self.player_list):
                first = [t.face(p.discard_origin_tiles[0]) for p in self.player_list]
                if first[0] in (41, 42, 43, 44) and len(set(first)) == 1:
                    return self.end_draw('four_winds')
            if sum(len(m.physical) >= 4 for p in self.player_list for m in p.melds) == 4:
                if sum(any(len(m.physical) >= 4 for m in p.melds) for p in self.player_list) > 1:
                    return self.end_draw('four_kongs')
        if resolution is None:
            return self.draw_for((actor + 1) % 4)
        index, _ = resolution
        choice = self.selected_choice(index, responses[index])
        p = self.player_list[index]
        self.interrupt_opening()
        for value in choice.take:
            p.hand_tiles.remove(value)
        declared = 'gang' if choice.kind == 'minggang' else choice.kind
        p.melds.append(Meld((*choice.take, tile), False, declared, actor))
        self.player_list[actor].discard_tiles.pop()
        self.player_list[actor].discard_riichi_flags.pop()
        p.has_draw_slot = p.after_kong = False
        p.draw_kind = 'none'
        self.current_player_index = index
        self.update_melds(index)
        self.check_pao(index, actor)
        self._event(KINDS.get(choice.kind, choice.kind), index, tile=tile, meld_index=len(p.melds) - 1,
                    meld_code=p.combination_tiles[-1], combination_mask=p.combination_mask[-1], cut_from_player=actor)
        if choice.kind == 'minggang':
            return self.draw_for(index, kind='kong', fourth=True, payer=actor)
        return self.begin_turn(index, after_meld=True)

    def interrupt_opening(self):
        self.opening_flow_interrupted = True
        for p in self.player_list:
            p.ippatsu = False

    def update_melds(self, index):
        p = self.player_list[index]
        p.combination_tiles = []
        p.combination_mask = []
        for meld in p.melds:
            options = meld_options(meld)
            group = next((g for g in options if (g.kind == 'sequence') == (meld.declared == 'chi')), options[0])
            p.combination_tiles.append(group.code)
            physical = list(meld.physical)
            if meld.concealed:
                if self.variant == 'riichi':
                    special = [v for v in physical[:4] if t.joker(v) or v >= 1000]
                    plain = [v for v in physical[:4] if v not in special]
                    first = plain[:1] + special + plain[1:]
                    mask = [item for v in reversed(physical[4:]) for item in (0, v)]
                    mask += [item for n, v in enumerate(first) for item in (2 if n in (0, 3) and not (t.joker(v) or v >= 1000) else 0, v)]
                else:
                    mask = [item for v in physical for item in (2, v)]
            else:
                base_size = min(4, len(physical))
                called = physical.pop(3 if meld.declared == 'gang' else 2)
                mark = {1: base_size - 1, 2: 1, 3: 0}[(meld.source - index) % 4]
                physical.insert(mark, called)
                mask = [item for n, v in enumerate(physical[:4]) for item in (1 if n == mark else 0, v)]
                extras = [item for v in (reversed(physical[4:]) if self.variant == 'riichi' else physical[4:]) for item in (0, v)]
                mask = extras + mask if self.variant == 'riichi' else mask + extras
            p.combination_mask.append(mask)

    def check_pao(self, index, source):
        if self.variant != 'riichi':
            return
        p = self.player_list[index]
        opened = [m for m in p.melds if not m.concealed]
        faces = [values for m in opened if (values := {g.logical[0] for g in meld_options(m) if g.kind in ('triplet', 'kong')})]
        for name, required in (('大三元', {45, 46, 47}), ('大四喜', {41, 42, 43, 44})):
            if name not in p.pao_liability and len(faces) >= len(required) and any(required <= set(v) for v in product(*faces)):
                p.pao_liability[name] = source
        if sum(len(m.physical) >= 4 for m in p.melds) == 4:
            p.pao_liability.setdefault('四杠子', source)

    def commit_kong(self, index, choice):
        p = self.player_list[index]
        self.interrupt_opening()
        drawn = p.has_draw_slot and p.hand_tiles[-1] in choice.take
        for tile in choice.take:
            p.hand_tiles.remove(tile)
        old = p.melds[choice.meld_index] if choice.meld_index >= 0 else None
        fourth = old is None or len(old.physical) == 3
        if old is None:
            p.melds.append(Meld(choice.take, True, 'angang'))
            position = len(p.melds) - 1
        else:
            position = choice.meld_index
            p.melds[position] = Meld(old.physical + choice.take, old.concealed, old.declared, old.source)
        self.update_melds(index)
        p.has_draw_slot = False
        self._event(choice.kind, index, tile=choice.take[-1], meld_index=position,
                    meld_code=p.combination_tiles[position], combination_mask=p.combination_mask[position], is_mo_gang=drawn)
        return self.draw_for(index, kind='kong', fourth=fourth)

    def draw_for(self, index, kind='normal', fourth=False, payer=None):
        if not self.wall.live:
            return self.end_draw()
        tile, position = self.wall.draw(fourth_kong=fourth)
        self.sync_wall()
        p = self.player_list[index]
        p.hand_tiles.sort()
        p.hand_tiles.append(tile)
        p.has_draw_slot, p.after_kong, p.draw_kind = True, kind == 'kong', kind
        p.rinshan_payer = payer
        self.current_player_index = index
        if kind == 'normal':
            self.natural_draw_count[index] += 1
        self._event('deal_gang_tile' if kind == 'kong' else 'deal_tile', index, tile=tile, wall_position=position)
        return self.begin_turn(index)
