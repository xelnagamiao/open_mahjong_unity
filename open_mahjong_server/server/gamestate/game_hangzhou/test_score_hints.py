"""Wait labels quote the same capped basic score used by actual self-draw wins."""

import asyncio

import pytest

from tests.local_hint_oracles import hangzhou as hints
from .test_flow import FLOAT, PLAIN, act, turn
from ...game_calculation.hangzhou.rules import RULE_VERSION
from ...response import Response


def resolve_in_test(wire):
    """Independent server oracle for the inputs now evaluated by the client."""
    assert not {"waiting_tiles", "waiting_details_by_discard"} & wire.keys()
    request = wire["local_request"]
    frozen = hints.HintRequest(tuple(request["hand"]), tuple(request["melds"]),
                               tuple(request["cuts"]), RULE_VERSION,
                               request["context"]["cai_piao_count"])
    return hints.to_wire(frozen, hints.compute(frozen))


@pytest.mark.parametrize("hand,cai,tile,fan,points", [
    (PLAIN[:-1], 0, 42, 1, 2),
    (PLAIN[:-1], 0, 46, 0, 1),
    (FLOAT[:-1], 0, 47, 1, 2),
    (FLOAT[:-1], 1, 47, 2, 4),
    (FLOAT[:-1], 3, 47, 4, 16),
    ([11,11,12,12,21,21,22,22,31,31,32,32,41], 0, 41, 3, 8),
    ([11,11,12,12,21,21,22,22,31,31,32,32,41], 0, 46, 2, 4),
    ([11,11,12,12,21,21,22,22,31,31,32,32,46], 2, 47, 4, 16),
    ([11,11,11,11,21,21,22,22,31,31,32,32,46], 0, 46, 4, 16),
])
def test_wait_scores_cover_plain_jokers_pairs_piao_and_cap(hand, cai, tile, fan, points):
    request = hints.HintRequest(tuple(hand), (), (), RULE_VERSION, cai)
    result = hints.to_wire(request, hints.compute(request))
    assert tile in result["waiting_tiles"]
    assert result["waiting_details"][tile] == dict(fan=fan, base_score=points)
    assert set(result["waiting_details"]) == set(result["waiting_tiles"])


def test_white_cut_preview_matches_post_cut_hint_and_actual_next_draw():
    s = turn(FLOAT, tips=True)
    preview = resolve_in_test(hints.private_hints(s, 0))["waiting_details_by_discard"][46]
    assert preview[47] == dict(fan=2, base_score=4)
    act(s, 0, "cut", TileId=46, cutClass=True)
    assert resolve_in_test(hints.private_hints(s, 0))["waiting_details"] == preview
    s.tiles_list.remove(47)
    s.tiles_list.insert(0, 47)
    s.open_action_window(s.draw_for(0))
    actual = s.score_win(0, "self_draw", 47)
    assert preview[47] == dict(fan=actual.fan_total, base_score=actual.points)


def test_discard_preview_clears_previous_kong_bonus_and_only_white_extends_piao():
    s = turn(FLOAT[3:], ("G11",), tips=True)
    p = s.player_list[0]
    p.after_kong = p.drawn_after_cai = True
    p.cai_piao_count = 1
    rows = resolve_in_test(hints.private_hints(s, 0))["waiting_details_by_discard"]
    assert rows[46][47] == dict(fan=3, base_score=8)
    assert rows[41][47] == dict(fan=1, base_score=2)
    act(s, 0, "cut", TileId=41, cutClass=False)
    assert not p.after_kong and p.cai_piao_count == 0
    assert resolve_in_test(hints.private_hints(s, 0))["waiting_details"] == rows[41]


def test_async_snapshot_freezes_its_cai_history_and_wire_mutation_does_not_poison_cache():
    async def run():
        s = turn(FLOAT, tips=True)
        s._client_context_enabled = True
        s.player_list[0].cai_piao_count = 1
        before = s.build_game_start_payload(0)
        s.player_list[0].cai_piao_count = 0
        assert await hints.prepare(s, payloads=[before])
        wire = Response(**before).model_dump(exclude_none=True)["game_info"]["hangzhou_info"]
        assert wire["source_cai_piao_count"] == 1
        assert resolve_in_test(wire)["waiting_details_by_discard"][46][47] == dict(fan=3, base_score=8)
        current = hints.private_hints(s, 0)
        assert resolve_in_test(current)["waiting_details_by_discard"][46][47] == dict(fan=2, base_score=4)
        current["local_request"]["context"]["cai_piao_count"] = 99
        assert resolve_in_test(hints.private_hints(s, 0))["waiting_details_by_discard"][46][47]["fan"] == 2
        assert all(p.get("hand_tiles") is None for p in before["game_info"]["players_info"][1:])
    asyncio.run(run())


def test_replay_records_changed_scores_even_with_identical_hand_and_waits():
    s = turn(FLOAT, tips=True)
    s._recorded_hint_keys = {}
    ticks = s.game_record["game_round"]["round_index_1"]["action_ticks"]
    def snapshots():
        return [tick[3] for tick in ticks if tick[:3] == ["hangzhou", "hints", 0]]
    s.record_hints(0, hints.private_hints(s, 0))
    first = snapshots()[-1]
    count = len(snapshots())
    s.record_hints(0, hints.private_hints(s, 0))
    assert len(snapshots()) == count
    s.player_list[0].cai_piao_count = 1
    s.record_hints(0, hints.private_hints(s, 0))
    second = snapshots()[-1]
    assert len(snapshots()) == count + 1
    assert resolve_in_test(first)["waiting_by_discard"] == resolve_in_test(second)["waiting_by_discard"]
    assert resolve_in_test(first)["waiting_details_by_discard"][46][47] == dict(fan=2, base_score=4)
    assert resolve_in_test(second)["waiting_details_by_discard"][46][47] == dict(fan=3, base_score=8)
