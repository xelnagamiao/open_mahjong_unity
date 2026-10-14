"""SBR 2025 ordinary, single-table 2V2. Duplicate play is a separate format."""

DOUBLES_SUB_RULE = "sichuan/doubles"
XUELIU_DOUBLES_SUB_RULE = "sichuan/xueliu_doubles"
from .duplicate_rules import SBR_DUPLICATE, XUELIU_DUPLICATE


def is_team_sub_rule(sub_rule):
    return sub_rule in (DOUBLES_SUB_RULE, XUELIU_DOUBLES_SUB_RULE, SBR_DUPLICATE, XUELIU_DUPLICATE)
DOUBLES_RULE_PROFILE = {
    "version": "sbr2025-doubles-v2",
    "format": "single_table_2v2",
    "dealer_rotation": "each_board",
    "choose_missing_suit": False,
    "require_missing_suit_to_win": True,
    "shunhe": False,
    "team_first_two_end": True,
    "flower_pig_penalty": 24,
    "no_discard_flower_pig": "passive",
    "unresolved_tie": "half_point_each",
}


def is_doubles(sub_rule):
    return sub_rule in (DOUBLES_SUB_RULE, SBR_DUPLICATE)


def tile_suits(hand, melds=()):
    """Count physical suits, including exposed and concealed kongs."""
    suits = {int(t) // 10 for t in hand}
    for meld in melds:
        if isinstance(meld, str) and len(meld) >= 3 and meld[1:].isdigit():
            suits.add(int(meld[1:]) // 10)
    return suits


def has_missing_suit(hand, melds=()):
    suits = tile_suits(hand, melds)
    return bool(suits) and suits <= {1, 2, 3} and len(suits) <= 2


def classify_flower_pig(hand, melds, original_discards):
    if len(tile_suits(hand, melds)) < 3:
        return None
    return "passive" if len(tile_suits(original_discards)) <= 1 else "active"


def team_scores(players):
    return {team: sum(p.score for p in players if p.team_id == team) for team in (0, 1)}


def team_first_two_finished(players):
    winners = [p for p in players if p.is_hu]
    return len(winners) == 2 and winners[0].team_id == winners[1].team_id


def match_result(scores, round_history):
    winner = None
    reason = "draw"
    if scores[0] != scores[1]:
        winner = 0 if scores[0] > scores[1] else 1
        reason = "total_score"
    else:
        for record in reversed(round_history):
            changes = record["score_changes"]
            a, b = changes.get(0, changes.get("0", 0)), changes.get(1, changes.get("1", 0))
            if a == b:
                continue
            # A penalty-only final non-tied board need not have a positive side.
            # The room profile explicitly defines an unresolved tie as a draw.
            if (a > 0) != (b > 0):
                winner = 0 if a > 0 else 1
                reason = "last_positive_board"
            break
    return {
        "scores": dict(scores), "winner_team_id": winner, "reason": reason,
        "match_points": {team: .5 if winner is None else int(team == winner) for team in (0, 1)},
    }
