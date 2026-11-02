"""v1.7 examples and settlement boundaries, including live/record snapshots."""
import asyncio

import pytest

from .scoring import score_partition
from .settlement import win_score_changes
from .test_hongque_reconnect import stage_fixture


MIRROR_CASES = [
    (["AX1 AX5 AX9"], True),
    (["AX5 BX5 CX5"], True),
    (["AX1 AX2 AX3", "BY7 BY8 BY9"], True),
    (["AX1 AX2 AX3", "BY7 BY8 BY9", "CX4 CX5 CX6"], True),
    (["AX1 AX2 AX3", "BX1 BX2 BX3", "CY7 CY8 CY9", "DY7 DY8 DY9"], True),
    (["AX1 AX2 AX3", "BX1 BX2 BX3", "CY7 CY8 CY9"], False),
    # The complete hand is symmetric, but its three groups cannot be paired.
    (["AX1 AY1 BX1", "BY1 CY1 DY1", "AX9 AY9 BX9 BY9 CX9 CY9"], False),
    (["AX1 AX2 AX3", "BY6 BY7 BY8 BY9"], False),
    (["AX1 AX2 AX3", "BY7 BY8 BY9", "CX5 CX6 CX7"], False),
]


@pytest.mark.parametrize("groups,mirror", MIRROR_CASES)
@pytest.mark.parametrize("open_hand", [False, True])
def test_mirror_pairs_whole_groups(groups, mirror, open_hand):
    groups = [group.split() for group in groups]
    result = score_partition([] if open_hand else groups,
        [{"tiles": group} for group in groups] if open_hand else [],
        self_draw=False, before_first_discard=False, wall_empty=False)
    fans = {fan["name"]: fan["total"] for fan in result["fans"]}
    assert fans.get("镜像", 0) == (2 if mirror else 0)


@pytest.mark.parametrize("count,name,value", [(2, "双同顺", 1), (3, "三同顺", 3), (4, "四同顺", 6)])
def test_same_sequence_tiers(count, name, value):
    groups = [[f"{colour}X{n}" for n in (1, 2, 3)] for colour in "ABCD"[:count]]
    result = score_partition(groups, [], self_draw=False, before_first_discard=False, wall_empty=False)
    assert [fan for fan in result["fans"] if fan["name"] in ("双同顺", "三同顺", "四同顺")] == [
        {"name": name, "value": value, "count": 1, "total": value}]


@pytest.mark.parametrize("points,loss", [(1, 0), (14, 0), (15, 5), (29, 5), (30, 10), (44, 10), (45, 15), (1008, 335)])
def test_ron_penalty_rounds_each_hand_down(points, loss):
    assert win_score_changes(1, points, 0) == {"1": points, "0": -loss}
    assert win_score_changes(1, points) == {"1": points}


@pytest.mark.parametrize("sub_rule", ["hongque/v1.6", "hongque/w-v1.0"])
@pytest.mark.parametrize("reason", ["ron", "self_draw"])
@pytest.mark.parametrize("dealer", range(4))
def test_settlement_rows_agree_for_live_reconnect_record_and_scoreboard(sub_rule, reason, dealer):
    async def run():
        game, _ = await stage_fixture("turn", dealer=dealer)
        game.sub_rule = sub_rule
        game.last_discard = {"player": dealer, "tile": "AX3"}
        seats = [(dealer + 1) % 4, (dealer + 2) % 4] if reason == "ron" else [dealer]
        points = [29, 44][:len(seats)]
        rows = []
        results = []
        for seat, score in zip(seats, points):
            groups = [["AX1", "AX2", "AX3"]]
            result = dict(points=score, winning_hand=groups[0], partition=groups, groups=groups,
                base=3, fans=[], fan_total=0)
            results.append((game.players[seat], result))
            row = {str(seat): score}
            if reason == "ron" and sub_rule == "hongque/v1.6":
                row[str(dealer)] = -(score // 15) * 5
            rows.append(row)
        try:
            await game._finish_round(results, reason)
            expected = {str(i): sum(row.get(str(i), 0) for row in rows) for i in range(4)}
            assert game.round_result["score_changes"] == expected
            assert [winner["score_changes"] for winner in game.round_result["winners"]] == rows
            assert [player.score for player in game.players] == list(expected.values())
            restored = game.build_state(dealer, sync_mode="reconnect")
            assert restored["round_result"] == game.round_result
            for player in game.players:
                assert [int(value) for value in player.score_history] == [row.get(str(player.index), 0) for row in rows]
            ticks = game.game_record["game_round"]["round_index_1"]["action_ticks"]
            wins = [tick for tick in ticks if tick[0].startswith("hu_")]
            assert [tick[4] for tick in wins] == [[row.get(str(i), 0) for i in range(4)] for row in rows]
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())
