from __future__ import annotations


def win_score_changes(winner: int, points: int, discarder: int | None = None) -> dict[str, int]:
    """Settle one hand before combining multi-ron.

    Hongque² v1.7 charges 5 per complete 15 points of each winning hand.
    Self draws and Hongque W do not supply a discarder.
    """
    changes = {str(winner): points}
    if discarder is not None:
        changes[str(discarder)] = -(points // 15) * 5
    return changes
