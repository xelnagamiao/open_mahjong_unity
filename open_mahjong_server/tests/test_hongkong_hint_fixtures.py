"""Shared Python/Unity fixtures for sixteen-tile kongs and menqing.

Run from open_mahjong_server: python -m tests.test_hongkong_hint_fixtures --refresh
Explicit dark-pung tiers and menqing awards guard against an incorrect oracle.
"""

import json
from pathlib import Path

from server.game_calculation.hongkong import HandContext, HongKongRules, score_hand, structural_waits


FIXTURES = Path(__file__).resolve().parents[2] / "open_mahjong_unity/Assets/Tests/Fixtures/LocalHints"
VALUES = {0: 0, 1: 0, 2: 5, 3: 20, 4: 60, 5: 180}
HANDS = {
    2: [11] * 3 + [22, 23, 24, 26, 27, 28, 32, 33, 34, 45, 45],
    3: [11] * 3 + [29] * 3 + [25, 26, 27, 32, 33, 34, 45, 45],
    4: [11] * 3 + [29] * 3 + [32, 33, 34] + [41] * 3 + [45, 45],
    5: [11] * 3 + [29] * 3 + [41] * 3 + [43] * 3 + [45, 45],
}


def calculate(row, *, check_menqing=False):
    c = row["context"]
    rules = HongKongRules(c["profile"], flowers=c.get("flowers_enabled"),
                         new13_version=c.get("new13_version", "gametower"),
                         self_draw_only=c.get("self_draw_only", False))
    ctx = dict(hand=row["hand"], melds=row["melds"], winning_tile=row["win"],
               flowers=c.get("flowers", []), seat_wind=41 + c.get("seat", 0),
               round_wind=c.get("round_wind", 41), dealer=c.get("seat", 0) == 0,
               ready=c.get("ready", ""), dealer_streak=c.get("dealer_streak", 0),
               called_chow_or_pung=c.get("called_chow_or_pung", False))
    scores = [score_hand(HandContext(**ctx, self_draw=self_draw), rules)
              for self_draw in (False, True)]
    if "concealed_counts" in row:
        for score, count in zip(scores, row["concealed_counts"]):
            fans = {fan.id: fan.value for fan in score.fans}
            assert score.is_win, row["case"]
            assert fans.get("concealed_pungs", 0) == VALUES[count], (row["case"], score)
            assert "five_pure_pungs" not in fans, row["case"]
            if count == 5:
                assert "all_pungs" not in fans, row["case"]
    if "concealed_counts" in row or check_menqing:
        # All final kongs qualify, even when a former pung became an added kong.
        # A remaining exposed chow/pung still prevents all three closed awards.
        closed = all(meld[0] in "gG" for meld in row["melds"])
        ready = c.get("ready", "") == "ordinary"
        for self_draw, score in zip((False, True), scores):
            fans = {fan.id: fan.value for fan in score.fans}
            assert score.is_win, row
            assert fans.get("concealed", 0) == (5 if closed and not ready else 0), row
            assert fans.get("concealed_self_draw", 0) == (5 if closed and self_draw else 0), row
            assert fans.get("closed_ready", 0) == (15 if closed and ready else 0), row
            assert fans.get("ready", 0) == (5 if ready and not closed else 0), row
            assert fans.get("self_draw", 0) == (1 if self_draw and not closed else 0), row
    before = list(row["hand"])
    before.remove(row["win"])
    expected = dict(tile=row["win"], ron=scores[0].is_win and not rules.self_draw_only,
                    self_draw=scores[1].is_win, ron_fan=scores[0].fan,
                    self_draw_fan=scores[1].fan)
    return expected, sorted(structural_waits(before, row["melds"], rules))


def regression_rows():
    def make(name, hand, melds, win, counts, prior=False, ready=""):
        row = dict(case=name, rule="hongkong", hand=hand, melds=melds, win=win,
                   context=dict(profile="hongkong/new16", flowers_enabled=True, flowers=[],
                                seat=1, round_wind=41, ready=ready, dealer_streak=0,
                                called_chow_or_pung=prior), concealed_counts=counts)
        row["expected"], row["waits"] = calculate(row)
        return row

    rows = []
    for count, hand in HANDS.items():
        for kong, prior in (("g37", False), ("G37", False), ("g37", True)):
            for win in (45, 11):
                counts = [count if win == 45 else count - 1, count]
                rows.append(make(f"{count}-dark-{kong}-prior-{prior}-win-{win}",
                                 hand, [kong], win, counts, prior))
    for prefixes in ("gg", "gG", "GG", "ggggg", "gGgGg"):
        melds = [f"{p}{t}" for p, t in zip(prefixes, (11, 19, 22, 28, 33))]
        hand = [22, 23, 24, 25, 26, 27, 36, 37, 38, 45, 45] if len(melds) == 2 else [45, 45]
        for prior in (False, True):
            rows.append(make(f"multiple-{prefixes}-prior-{prior}", hand, melds, 45,
                             [len(melds)] * 2, prior))
            rows.append(make(f"multiple-{prefixes}-prior-{prior}-ready", hand, melds, 45,
                             [len(melds)] * 2, prior, ready="ordinary"))
    rows.append(make("pung-does-not-turn-dark", [22, 23, 24, 26, 27, 28, 32, 33, 34, 45, 45],
                     ["g37", "k11"], 45, [1, 1], True))
    for exposed in ("s12", "k11"):
        for prior in (False, True):
            for ready in ("", "ordinary"):
                if exposed == "k11" and prior and not ready:
                    continue  # Covered by the original pung regression above.
                rows.append(make(f"remaining-{exposed}-prior-{prior}-ready-{ready or 'none'}",
                                 [22, 23, 24, 26, 27, 28, 32, 33, 34, 45, 45],
                                 ["g37", exposed], 45, [1, 1], prior, ready))
    return rows


def refresh():
    base = FIXTURES / "hongkong-parity.json"
    rows = json.loads(base.read_text(encoding="utf-8"))
    changed = 0
    for row in rows:
        if row["context"]["profile"] != "hongkong/new16":
            continue
        expected, waits = calculate(row)
        assert waits == row["waits"]
        changed += expected != row["expected"]
        row["expected"] = expected
    base.write_text(json.dumps(rows, ensure_ascii=False), encoding="utf-8", newline="")
    kong_file = FIXTURES / "hongkong-kongs-parity.json"
    original = [r for r in json.loads(kong_file.read_text(encoding="utf-8")) if "case" not in r]
    kong_changed = 0
    for row in original:
        expected, waits = calculate(row, check_menqing=True)
        assert waits == row["waits"]
        kong_changed += expected != row["expected"]
        row["expected"] = expected
    expanded = original + regression_rows()
    kong_file.write_text(json.dumps(expanded, ensure_ascii=False, indent=2) + "\n",
                         encoding="utf-8", newline="\r\n")
    print(f"Refreshed {changed} base and {kong_changed} original kong expectations; {len(expanded)} kong fixtures")


def test_existing_sixteen_hint_fixtures_match_server():
    rows = json.loads((FIXTURES / "hongkong-parity.json").read_text(encoding="utf-8"))
    for index, row in enumerate(rows):
        if row["context"]["profile"] == "hongkong/new16":
            assert calculate(row) == (row["expected"], row["waits"]), index


def test_kong_hint_fixtures_match_server_and_explicit_tiers():
    rows = json.loads((FIXTURES / "hongkong-kongs-parity.json").read_text(encoding="utf-8"))
    for index, row in enumerate(rows):
        assert calculate(row, check_menqing=True) == (row["expected"], row["waits"]), index
    assert [r for r in rows if "case" in r] == regression_rows()


if __name__ == "__main__":
    import sys
    if sys.argv[1:] != ["--refresh"]:
        raise SystemExit("Use --refresh to regenerate the shared fixtures")
    refresh()
