"""Public contract for the single-table Washizu captain/assistant format."""
SUB_RULE = 'riichi/washizu_doubles'
ROLE_LABELS = ('A队主将', 'A队助手', 'B队主将', 'B队助手')
PROFILE = {
    'version': 'washizu-captain-v1', 'format': 'captain_assistant_2v2',
    'seat_order': [0, 1, 2, 3], 'captain_only_tobi': True,
    'teammate_payments': True, 'teammate_hidden_hands': False,
    'end_deposits': 'refund_contributors', 'tie': 'draw',
}
FIXED_OPTIONS = {'pao': False, 'pao_suukantsu': False, 'rank_points': 'none',
                 'return_score': 0, 'end_deposits': 'discard', 'chombo_penalty': 'fixed_9000',
                 'tenpai_renchan': True, 'agari_yame': False, 'tenpai_yame': False,
                 'target_score': 0, 'extension_rounds': 0, 'tie_break': 'shared'}
# end_deposits is overridden by PROFILE, not ordinary individual-rank settlement.


def enabled(sub_rule):
    return sub_rule == SUB_RULE


def valid_roles(roles):
    return (isinstance(roles, (list, tuple)) and len(roles) == 4
            and all(type(role) is int for role in roles) and sorted(roles) == [0, 1, 2, 3])


def captain_scores(players):
    return {p.team_id: p.score for p in players if p.team_role == 'captain'}


def result(scores, busted_scores=None):
    """A recorded bust is final even if unclaimed deposits are later refunded."""
    decision = busted_scores if busted_scores is not None else scores
    busted = [team for team, score in decision.items() if score < 0] if busted_scores is not None else []
    winner = 1 - busted[0] if len(busted) == 1 else (
        None if decision[0] == decision[1] else max(decision, key=decision.get))
    return {'format': PROFILE['format'], 'scores': dict(scores), 'winner_team_id': winner,
            'reason': 'captain_bust' if busted else 'draw' if winner is None else 'captain_score',
            'busted_captain_scores': dict(busted_scores) if busted_scores is not None else None,
            'match_points': {team: .5 if winner is None else int(team == winner) for team in (0, 1)}}
