"""Two-team Elo. Team identity and outcome come from the authoritative game."""
import math


def team_elo_deltas(ratings, players, k=32.0):
    if len(ratings) != 4 or len(players) != 4 or any(not math.isfinite(r) for r in ratings):
        raise ValueError('双打评分需要四名有效玩家')
    teams = [p.get('team_id') for p in players]
    if sorted(teams) != [0, 0, 1, 1]:
        raise ValueError('双打评分需要两队各两人')
    points = []
    for team in (0, 1):
        values = {p.get('team_match_points') for p in players if p['team_id'] == team}
        if len(values) != 1 or not values <= {0, .5, 1}:
            raise ValueError('双打胜负数据无效')
        points.append(values.pop())
    if sum(points) != 1:
        raise ValueError('双打双方胜负不一致')
    means = [sum(r for r, t in zip(ratings, teams) if t == team) / 2 for team in (0, 1)]
    expected = 1 / (1 + 10 ** max(-16, min(16, (means[1] - means[0]) / 2400)))
    delta = round(k * (points[0] - expected), 2)
    return [delta if t == 0 else -delta for t in teams]
