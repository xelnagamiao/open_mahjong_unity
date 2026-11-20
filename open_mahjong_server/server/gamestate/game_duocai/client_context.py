from dataclasses import asdict

from ..public.local_hint_context import hint_context
from ...game_calculation.duocai import tiles as t
from .actions import legal_cuts
from .state_machine import Phase as P


def private_context(state, index, *, for_record=False):
    if not for_record and not (state.tips or state.count_tips):
        return None
    p = state.player_list[index]
    visible = [v for other in state.player_list for v in other.discard_tiles]
    visible += [v for other in state.player_list for m in other.melds
                if not m.concealed or state.variant == 'riichi' or other is p for v in m.physical]
    visible += state.wall.indicators()
    timing = win_timing(state, index)
    return hint_context('duocai', p.hand_tiles, p.combination_tiles, cuts=legal_cuts(state, index),
        variant=state.variant, physical_melds=[asdict(m) for m in p.melds], visible_tiles=visible,
        seat_wind=41 + index, round_wind=41 + min(3, (state.current_round - 1) // 4),
        dealer=0, player_index=index, honba=state.honba, riichi_sticks=state.riichi_sticks,
        indicators=state.wall.indicators(), riichi=p.riichi, ippatsu=p.ippatsu,
        discards=list(p.discard_origin_tiles), temporary_furiten=p.temporary_furiten,
        riichi_furiten=p.riichi_furiten, has_draw_slot=p.has_draw_slot,
        after_kong=p.after_kong, remaining=len(state.wall.live), rule_version=state.rule_version,
        pending_riichi_value=2 if not p.discard_origin_tiles and not state.opening_flow_interrupted else 1,
        **timing)


def win_timing(state, index):
    p = state.player_list[index]
    source, tile = '', 0
    phase = state.machine.phase
    if phase in (P.START, P.TURN) and state.current_player_index == index and p.has_draw_slot:
        source, tile = 'self_draw', p.hand_tiles[-1]
    if phase in (P.TURN, P.RESPONSE, P.ROB_KONG):
        window = getattr(state, 'live_pending_window', None) or {}
        event = state.domain_events[-1] if state.domain_events else {}
        if event.get('action') in ('cut', 'declare_kan'):
            actor, offered = event['player'], event['tile']
            candidate = 'rob_kong' if event['action'] == 'declare_kan' else 'discard'
        elif phase in (P.RESPONSE, P.ROB_KONG) and window.get('status') == phase.value and event.get('action') in (None, 'riichi'):
            actor, offered = window['player'], window['tile']
            candidate = 'rob_kong' if window['status'] == P.ROB_KONG.value else 'discard'
        else:
            actor, offered, candidate = index, 0, ''
        if actor != index and offered:
            source, tile = candidate, offered
    scoring = state.context(index, source, tile, preview=True) if tile else None
    return dict(win_source=source, current_win_tile=tile,
        chankan=bool(scoring and scoring.chankan), rinshan=bool(scoring and scoring.rinshan),
        last=bool(scoring and scoring.last), tenhou=bool(scoring and scoring.tenhou),
        chiihou=bool(scoring and scoring.chiihou))
