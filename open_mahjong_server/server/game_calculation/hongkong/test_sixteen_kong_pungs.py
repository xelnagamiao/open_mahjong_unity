"""The sixteen-tile X暗刻/杠 awards count both exposed and concealed kongs."""

from dataclasses import replace

import pytest

from .models import HandContext, HongKongRules, GAMETOWER, QINGZHANG_REMIX
from .scoring import score_hand
from .test_hongkong_calculation import tiles


RULES = HongKongRules("hongkong/new16")
HANDS = {
    2: "111m 234678p 234s 55z",
    3: "111m 999567p 234s 55z",
    4: "111m 999p 234s 11155z",
    5: "111m 999p 11133355z",
}
VALUES = {2: 5, 3: 20, 4: 60, 5: 180}


def awards(ctx, rules=RULES):
    result = score_hand(ctx, rules)
    assert result.is_win
    return {fan.id: fan for fan in result.fans}


@pytest.mark.parametrize("count", [2, 3, 4, 5])
@pytest.mark.parametrize("kong", ["g37", "G37"])
@pytest.mark.parametrize("self_draw", [False, True])
@pytest.mark.parametrize("prior_pung", [False, True])
def test_all_kong_types_count_in_each_concealed_pung_tier(count, kong, self_draw, prior_pung):
    ctx = HandContext(tiles(HANDS[count]), (kong,), 45, self_draw,
                      called_chow_or_pung=prior_pung)
    fans = awards(ctx)
    assert fans["concealed_pungs"].name == f"{count}暗刻"
    assert fans["concealed_pungs"].value == VALUES[count]
    assert "five_pure_pungs" not in fans  # 坎坎糊 still forbids every kind of kong.
    assert "concealed" in fans  # Historical pung flags do not veto final kongs.
    if count == 5:
        assert "all_pungs" not in fans


@pytest.mark.parametrize("count", [2, 3, 4, 5])
def test_ron_completed_triplet_is_not_promoted_by_a_kong(count):
    ctx = HandContext(tiles(HANDS[count]), ("g37",), 11)
    ron, tsumo = awards(ctx), awards(replace(ctx, self_draw=True))
    assert tsumo["concealed_pungs"].value == VALUES[count]
    if count == 2:
        assert "concealed_pungs" not in ron
    else:
        assert ron["concealed_pungs"].name == f"{count - 1}暗刻"
        assert ron["concealed_pungs"].value == VALUES[count - 1]


@pytest.mark.parametrize("prefixes", ["gg", "gG", "GG", "ggggg", "gGgGg"])
def test_multiple_kongs_each_count_once(prefixes):
    codes = tuple(f"{prefix}{tile}" for prefix, tile in zip(prefixes, (11, 19, 22, 28, 33)))
    hand = tiles("234567p 678s 55z") if len(codes) == 2 else tiles("55z")
    fans = awards(HandContext(hand, codes, 45, True))
    assert fans["concealed_pungs"].value == VALUES[len(codes)]
    assert fans["kongs"].value == 2 * len(codes)
    assert "five_pure_pungs" not in fans


def test_exposed_pung_stays_exposed_next_to_a_kong():
    ctx = HandContext(tiles("234678p 234s 55z"), ("g37", "k11"), 45, True,
                      called_chow_or_pung=True)
    fans = awards(ctx)
    assert not {"concealed_pungs", "concealed", "concealed_self_draw"} & fans.keys()


@pytest.mark.parametrize("self_draw", [False, True])
@pytest.mark.parametrize("ready", ["", "ordinary", "closed"])
@pytest.mark.parametrize("replacement", ["", "kong", "flower"])
def test_added_kong_restores_closed_awards_for_ron_self_draw_and_ready(self_draw, ready, replacement):
    ctx = HandContext(tiles(HANDS[2]), ("g37",), 45, self_draw,
                      ready=ready, replacement=replacement, called_chow_or_pung=True)
    before = awards(replace(ctx, melds=("k37",)))
    assert not {"concealed", "concealed_self_draw", "closed_ready"} & before.keys()
    fans = awards(ctx)
    assert ("concealed_self_draw" in fans) == self_draw
    if ready:
        assert fans["closed_ready"].value == 15
        assert not {"concealed", "ready"} & fans.keys()
    else:
        assert fans["concealed"].value == 5
        assert "closed_ready" not in fans
    # Neither replacement source nor old historical marker changes menqing.
    assert fans == awards(replace(ctx, called_chow_or_pung=False))


@pytest.mark.parametrize("open_meld", ["s32", "k41"])
@pytest.mark.parametrize("self_draw", [False, True])
@pytest.mark.parametrize("prior_pung", [False, True])
def test_remaining_chow_or_pung_blocks_closed_awards_even_after_added_kong(open_meld, self_draw, prior_pung):
    ctx = HandContext(tiles("123567m 234p 55z"), ("g37", open_meld), 45, self_draw,
                      ready="ordinary", called_chow_or_pung=prior_pung)
    fans = awards(ctx)
    assert not {"concealed", "concealed_self_draw", "closed_ready"} & fans.keys()
    assert fans["ready"].value == 5


def test_no_kong_five_concealed_pungs_still_use_kankan_award():
    ctx = HandContext(tiles("111m 999p 777s 11133355z"), winning_tile=45)
    fans = awards(ctx)
    assert fans["five_pure_pungs"].value == 240
    assert not {"concealed_pungs", "all_pungs", "concealed"} & fans.keys()


@pytest.mark.parametrize("profile", [GAMETOWER, QINGZHANG_REMIX])
def test_thirteen_tile_profiles_do_not_promote_exposed_kongs(profile):
    ctx = HandContext(tiles("111m 999p 234s 55z"), ("g37",), 45, True)
    exposed = awards(ctx, HongKongRules(profile))
    concealed = awards(replace(ctx, melds=("G37",)), HongKongRules(profile))
    if profile == GAMETOWER:
        assert "concealed_pungs" not in exposed
    else:
        assert exposed["concealed_pungs"].value == 1  # Only the two actual暗刻.
    assert concealed["concealed_pungs"].value == (2 if profile == GAMETOWER else 3)
