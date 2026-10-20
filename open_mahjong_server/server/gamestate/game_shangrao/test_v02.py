from collections import Counter

import pytest

from .test_state import act, turn, wall, NORMAL
from .actions import claim_actions, kong_tiles
from ...game_calculation.shangrao import rules as r


@pytest.mark.parametrize("positions,allowed", [
    ([(125,12),(126,45)], False),
    ([(126,45),(127,46)], False),
    ([(124,12),(125,22),(126,45)], True),
    ([(125,12),(126,45),(127,46)], True),
    ([(126,45)], False),
])
def test_kong_availability_and_protected_indicator(positions, allowed):
    s = turn([21]*4+NORMAL[4:])
    wall(s, positions)
    assert s.can_draw_kong() == allowed
    assert (21 in kong_tiles(s,0,"angang")) == allowed
    s.player_list[1].hand_tiles = [21]*3+NORMAL[4:]
    assert ("gang" in claim_actions(s,0,21)[1]) == allowed
    before = list(s.tiles_list)
    if allowed:
        drawn, burnt = s.take_kong_tile()
        assert s.indicator in s.tiles_list and s.indicator_position in s.wall_positions
        assert Counter(before) == Counter(s.tiles_list+[drawn]+([] if burnt is None else [burnt]))
    else:
        with pytest.raises(ValueError):
            s.take_kong_tile()
        assert before == s.tiles_list


@pytest.mark.parametrize("holder", range(4))
@pytest.mark.parametrize("winner", [None,0,2])
def test_four_bao_pays_two_from_each_opponent_once_at_end(holder, winner):
    s = turn(index=winner or 0)
    s.detailed_config["four_bao_reward"] = True
    s.player_list[holder].drawn_bao_count = 4
    s.start_game_recording(); s.start_round_recording()
    s.check_four_bao(holder)
    assert not s.payment_ledger and all(p.score == 0 for p in s.player_list)
    if winner is None:
        s.end_draw()
    else:
        s.settle_win(winner,"self_draw",41)
    expected = r.payments(holder,2)
    if winner is not None:
        expected = [a+b for a,b in zip(expected,r.payments(winner,2))]
    for i in range(4):
        for _ in range(2):
            s.restore_payloads(i)
    assert [p.score for p in s.player_list] == expected
    assert len(s.payment_ledger) == 1 and s.payment_ledger[0]["points"] == 2
    assert s.round_changes == expected
    ticks = next(iter(s.game_record["game_round"].values()))["action_ticks"]
    payments = [i for i,t in enumerate(ticks) if t[:2] == ["shangrao","payment"]]
    assert len(payments) == 1
    if winner is not None:
        assert payments[0] < next(i for i,t in enumerate(ticks) if t[0] == "hu_self")


def test_legacy_rooms_preserve_old_reward_and_single_lower_rules():
    s = turn();s.rule_version = r.LEGACY_VERSION;s.detailed_config["rule_version"] = r.LEGACY_VERSION
    s.detailed_config["four_bao_reward"] = True
    s.player_list[0].drawn_bao_count = 4
    s.check_four_bao(0)
    assert s.player_list[0].score == 12
    wall(s,[(125,12),(126,45)])
    assert s.take_kong_tile() == (12,None)
    s.end_draw();assert s.player_list[0].score == 12


@pytest.mark.parametrize("legacy", [False,True])
def test_flying_star_can_be_discarded_before_collecting_remaining_six(legacy):
    s = turn(NORMAL[:-1]+[47])
    if legacy:s.rule_version = r.LEGACY_VERSION
    act(s,action_type="cut",TileId=47,cutClass=True)
    assert s.player_list[0].flying_seven_stars == (not legacy)
    s.player_list[0].hand_tiles = [11,14,17,21,24,27,31,34,41,42,43,44,45,46]
    s.detailed_config["flying_seven_stars"] = True
    quote = s.score_win(0,"self_draw",46)
    assert quote["pattern"] == ("十三烂" if legacy else "七星十三烂")
    assert quote["points"] == (4 if legacy else 8)
