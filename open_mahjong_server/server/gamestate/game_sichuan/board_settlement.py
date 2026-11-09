"""Authoritative, board-end-only presentation data for SBR doubles."""
from copy import deepcopy
import time

from ..public.round_end_timing import ROUND_END_PRESENTATION_FADE_SEC


def event_snapshot(state, seat, *, win_tile=0, hu_order=0):
    """Capture the actor after the action; never reconstruct an earlier hand from its final state."""
    state._sbr_event_order = getattr(state, '_sbr_event_order', 0) + 1
    player = state.player_list[seat]
    return dict(order=state._sbr_event_order, hand=list(player.hand_tiles),
                melds=deepcopy(player.combination_mask), win_tile=win_tile, hu_order=hu_order)


def ready_seconds(state, *, clock=None):
    if not state.is_doubles or getattr(state, 'is_xueliu', False):
        return 8
    duration = ROUND_END_PRESENTATION_FADE_SEC + getattr(state, '_sbr_presentation_ms', 0) / 1000
    started = getattr(state, '_sbr_presentation_started_at', None)
    elapsed = 0 if started is None else max(0, (clock or time.monotonic)() - started)
    return 8 + max(0, duration - elapsed)


class BoardSettlement:
    def __init__(self, state):
        self.before = {p.player_index: p.score for p in state.player_list}
        self.events = []

    def add(self, kind, seat, changes, *, source=None, fans=None, fan=None, points=0, per_payer=False,
            detail='', presentation=None, phase='play'):
        self.events.append(dict(kind=kind, seat=seat, source=source, fans=list(fans or []),
                                fan=fan, points=points, per_payer=per_payer, detail=detail,
                                phase=phase, duration_ms=2500,
                                **deepcopy(presentation or {}),
                                changes={i: changes.get(i, 0) for i in range(4)}))

    def kongs(self, state):
        names = {'guafeng': '明杠', 'xiayu1': '加杠', 'xiayu2': '暗杠', 'non_timely': '加杠'}
        reasons = {'non_timely': '非及时', 'no_payer': '不计分', 'gang_shang_pao': '杠上炮退税',
                   'no_ting': '没叫退税', 'robbed_kong': '被抢杠'}
        for player in state.player_list:
            for rec in player.gang_score_records:
                if not rec.get('deferred'):
                    continue
                delta = {i: 0 for i in range(4)}
                if not rec.get('cancelled'):
                    for payer, amount in rec['payers'].items():
                        delta[payer] -= amount
                        delta[player.player_index] += amount
                detail = names[rec['type']]
                reason = reasons.get(rec.get('void_reason'))
                if reason:
                    detail += ' · ' + reason
                presentation = deepcopy(rec.get('presentation'))
                if presentation and rec.get('cancelled'):
                    # Keep the action hand, but show this kong's adjudicated face-up/pung form.
                    # Other melds stay as they were, without introducing later kongs.
                    final_meld = next((mask for mask in player.combination_mask
                                       if len(mask) in (6, 8) and all(t == rec['tile'] for t in mask[1::2])), None)
                    if final_meld is not None:
                        for index, mask in enumerate(presentation['melds']):
                            if len(mask) == 8 and all(t == rec['tile'] for t in mask[1::2]):
                                presentation['melds'][index] = deepcopy(final_meld)
                self.add('kong', player.player_index, delta,
                         points=max((-value for value in delta.values() if value < 0), default=0),
                         per_payer=sum(value < 0 for value in delta.values()) > 1, detail=detail,
                         presentation=presentation)

    def finish(self, state, statuses, max_fans, skip_chajiao):
        after = {p.player_index: p.score for p in state.player_list}
        changes = {i: after[i] - self.before[i] for i in after}
        players = []
        wins = {r['winner']: r for r in state.deferred_hu_settlements}
        for p in state.player_list:
            win = wins.get(p.player_index)
            status = ('zimo' if win and win['is_zimo'] else 'hu') if p.is_hu else (
                'unfinished' if skip_chajiao else statuses.get(p.player_index, 'no_ting'))
            players.append(dict(seat=p.player_index, original_seat=p.original_player_index,
                                user_id=p.user_id, username=p.username, team_id=p.team_id,
                                status=status, max_fan=max_fans.get(p.player_index, 0), hu_order=p.hu_order,
                                hand=state._reveal_hand_payload(p), melds=deepcopy(p.combination_mask),
                                win_tile=win.get('hepai_tile', 0) if win else 0))
        team_changes = [sum(changes[p.player_index] for p in state.player_list if p.team_id == t) for t in (0, 1)]
        team_totals = [sum(p.score for p in state.player_list if p.team_id == t) for t in (0, 1)]
        duplicate = getattr(state, 'duplicate_match', None)
        display_order = {p['seat']: i for i, p in enumerate(sorted(players, key=lambda p: (p['team_id'], p['original_seat'])))}
        self.events.sort(key=lambda e: (1, display_order[e['seat']]) if e['phase'] == 'check'
                         else (0, e.get('order', 0)))
        summary = dict(version=2, round=state.current_round, dealer_seat=state.dealer_index,
                       max_rounds=len(duplicate.data['boards']) if duplicate else state.max_round * 4,
                       reason='team_finished' if skip_chajiao else 'wall_exhausted',
                       players=players, events=deepcopy(self.events), scores_before=self.before,
                       presentation_ms=sum(e['duration_ms'] for e in self.events),
                       scores_after=after, changes=changes, team_changes=team_changes, team_totals=team_totals)
        # Event rows are the same authoritative transfers, never inferred from labels.
        assert all(sum(e['changes'][i] for e in self.events) == changes[i] for i in after)
        return summary


async def publish_board(state, builder, statuses, max_fans, skip_chajiao):
    from .boardcast import broadcast_result
    from ..public.game_record_manager import player_action_record_sichuan_liuju_step
    import json

    summary = builder.finish(state, statuses, max_fans, skip_chajiao)
    state._sbr_presentation_ms = summary['presentation_ms']
    state._sbr_presentation_started_at = time.monotonic()
    state._sichuan_grouped_endgame = False
    state._liuju_final_panel_shown = True
    state.next_status = 'match_end' if state.current_round >= summary['max_rounds'] else 'round_end_by_ready'
    player_action_record_sichuan_liuju_step(state, 'board_summary', json.dumps(summary, ensure_ascii=False))
    await broadcast_result(state, hu_class='liuju', liuju_step='board_summary',
                           sichuan_settlement=summary, score_changes={i: 0 for i in range(4)},
                           player_to_score=summary['scores_after'], liuju_status_final=True,
                           round_continues=False, next_status=state.next_status)


def match_summary(state):
    """Publish both rooms only once the paired match is complete."""
    import json
    duplicate = getattr(state, 'duplicate_match', None)
    if duplicate:
        if duplicate.data['profile']['sub_rule'] != 'sichuan/doubles_duplicate' or not duplicate.completed:
            return None
    elif not state.is_doubles or getattr(state, 'is_xueliu', False):
        return None
    records = ({table: checkpoint['game_record'] for table, checkpoint in duplicate.data['checkpoints'].items()}
               if duplicate else {'single': state.game_record})
    tables = {}
    participants = []
    for table, record in records.items():
        boards = []
        for rd in record.get('game_round', {}).values():
            for tick in rd.get('action_ticks', []):
                if tick[:2] == ['liuju', 'board_summary']:
                    boards.append(json.loads(tick[2]))
        boards.sort(key=lambda b: b['round'])
        tables[table] = boards
        if boards:
            for player in boards[-1]['players']:
                participants.append({**player, 'table': table,
                    'score': boards[-1]['scores_after'][str(player['seat'])],
                    'history': [b['changes'][str(player['seat'])] for b in boards]})
    if not participants:
        return None
    result = state.team_result
    return dict(tables=tables, players=participants, duplicate=bool(duplicate),
                scores=[result['scores'][t] for t in (0, 1)],
                match_points=[result['match_points'][t] for t in (0, 1)], reason=result['reason'],
                duplicate_info=duplicate.public_info() if duplicate else None)
