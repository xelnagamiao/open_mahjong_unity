"""方案A 牌效罗伯特对齐回归测试：覆盖川麻双打无定缺缺一门、立直有役门控、国标/长沙/青雀/武汉/长春/沈阳/新加坡/香港/三花/新中式。"""
from types import SimpleNamespace

from .riichi_smart_bot_ai import (
    _choose_riichi_claim_plan,
    _choose_riichi_cut,
    _has_credible_open_yaku,
    _riichi_visible_tiles,
)
from ...game_sichuan import xueliu_bot
from ...game_changsha import bot as changsha_bot
from ...game_changsha import exchange_six_bot
from ...game_mmcr import bot as qingque_bot
from ...game_guobiao import efficiency_bot as guobiao_eff
from ...game_wuhan import bot as wuhan_bot
from ...game_changchun import bot as changchun_bot
from ...game_shenyang import bot as shenyang_bot
from ...game_singapore import bot as singapore_bot
from ...game_shanghai import qinghunpeng_bot
from ...game_new_chinese import bot as new_chinese_bot


def test_sichuan_doubles_enforces_void_suit_without_explicit_dingque():
    # 川麻双打无显式定缺 (dingque=0)，已副露万+筒两门，不得暗杠第三门（索）
    hand = [11, 12, 13, 22, 22, 35, 35, 35, 35]
    melds = ["k19", "k29"]
    action, tile, _ = xueliu_bot.choose_hand(
        hand, melds, [0] * 34, ("angang", "cut"), meld_count=4, dingque=0, locked=False
    )
    assert action == "cut"
    assert tile == 35  # 必须主动清掉第三门索子，不能暗杠 35

    # 也不得碰第三门
    claim = xueliu_bot.choose_claim(
        [11, 12, 13, 22, 22, 35, 35], melds, [0] * 34, ("peng", "pass"), 35, meld_count=4, dingque=0
    )
    assert claim == "pass"


def test_riichi_blocks_yakuless_open_claim_and_allows_yakuhai_or_kuitan():
    vis = [0] * 34
    # 门清、无役牌、含幺九（1万/9筒）且两花色混杂：吃 3万 会毁掉门清立直且无役，必须 pass
    hand_yakuless = [11, 12, 17, 18, 19, 21, 22, 24, 25, 29, 32, 33, 39]
    assert _choose_riichi_claim_plan(
        hand_yakuless, 0, vis, ("chi_left", "pass"), 13, combs=(), round_wind=41, seat_wind=42, open_tanyao=True
    ) == "pass"

    # 碰中（三元役牌）应允许
    hand_dragon = [12, 13, 14, 24, 25, 26, 33, 34, 37, 38, 45, 45, 19]
    assert _choose_riichi_claim_plan(
        hand_dragon, 0, vis, ("peng", "pass"), 45, combs=(), round_wind=41, seat_wind=42, open_tanyao=True
    ) == "peng"

    # 禁食断时，全中张吃牌无役，必须 pass
    hand_tanyao = [12, 13, 15, 16, 17, 23, 24, 25, 27, 28, 34, 35, 36]
    assert not _has_credible_open_yaku(
        [15, 16, 17, 23, 24, 25, 27, 28, 34, 35], ["s13"], 41, 42, open_tanyao=False, shanten_after=0
    )
    assert _has_credible_open_yaku(
        [15, 16, 17, 23, 24, 25, 27, 28, 34, 35], ["s13"], 41, 42, open_tanyao=True, shanten_after=0
    )

    # 三人立直将 2m-8m 标满 4 张
    sanma_state = SimpleNamespace(sub_rule="riichi/sanma", player_list=[
        SimpleNamespace(discard_tiles=[], combination_tiles=[]),
        SimpleNamespace(discard_tiles=[], combination_tiles=[]),
        SimpleNamespace(discard_tiles=[], combination_tiles=[]),
    ])
    sanma_vis = _riichi_visible_tiles(sanma_state, 0)
    assert all(sanma_vis[i] == 4 for i in range(1, 8))

    # 已副露断幺手：切 1万 保持断幺听牌，绝不切 4万 落入无役听牌
    open_tanyao_hand = [11, 12, 13, 14, 24, 25, 26, 35, 36, 37, 37]
    cut_t, _ = _choose_riichi_cut(open_tanyao_hand, ["s25"], 1, vis, set(), 41, 42, open_tanyao=True)
    assert cut_t == 11

    # 同分切牌保留红5（105）而切普通5（15）
    red5_hand = [105, 15, 22, 23, 24, 33, 34, 35, 37, 38, 39, 45, 45, 41]
    cut_r, _ = _choose_riichi_cut(red5_hand, [], 0, vis, {41}, 41, 42, open_tanyao=True)
    assert cut_r != 105


def test_changsha_standard_demotes_non_258_pair_fake_tenpai():
    vis = [0] * 34
    # 非 258 将的平胡结构听（11作将）：标准长沙不能小胡，应被降级为 < 0
    fake_tenpai = [11, 11, 12, 13, 14, 23, 24, 25, 34, 35, 36, 37, 38]
    real_tenpai = [12, 12, 12, 13, 14, 23, 24, 25, 34, 35, 36, 37, 38]
    assert changsha_bot._score(fake_tenpai, [], vis)[0] < 0
    assert changsha_bot._score(real_tenpai, [], vis)[0] == 0
    # 换六张共用缓存路径正常运行
    assert exchange_six_bot._score(real_tenpai, [], vis, False)[0] == 0


def test_qingque_demotes_zero_fan_tenpai():
    vis = [0] * 34
    # 清一色副露听牌（有番）应为 0；普通无番副露听牌应被降级为 -1
    hand_flush = [11, 11, 12, 13, 14, 15, 16, 17, 18, 19]
    assert qingque_bot._score(hand_flush, ["s13"], vis, 0)[0] == 0
    hand_zero = [11, 12, 13, 22, 23, 24, 36, 37, 29, 29]
    assert qingque_bot._score(hand_zero, ["s17"], vis, 0)[0] == -1


def test_guobiao_efficiency_prefers_legal_8fan_tenpai_over_cheap_tenpai():
    vis = [0] * 34
    # 混一色 10 张（有 6 番混一色+箭刻 2 番 >= 8 番）vs 普通低番副露假听（平和2+喜相逢1=3番 < 8 番）
    legal_hand = [11, 11, 12, 13, 14, 15, 16, 17, 18, 19]
    cheap_hand = [11, 12, 13, 21, 22, 23, 35, 36, 39, 39]
    assert guobiao_eff._score(legal_hand, ["k45"], vis, 8, 0, 0)[0] == 0
    assert guobiao_eff._score(cheap_hand, ["s17"], vis, 8, 0, 0)[0] == -1


def test_wuhan_discards_skin_first_and_scores_cleanly():
    # indicator=11 -> joker=12, skins 含 11 与 45(红中)
    hand = [11, 12, 15, 15, 22, 23, 24, 34, 35, 36, 37, 38, 39, 25]
    tile, _ = wuhan_bot._choose_cut(hand, [], 11, {})
    assert tile == 11  # 必须先清皮子


def test_changchun_and_shenyang_respect_three_suits_and_orphans():
    vis = [0] * 34
    # 含三门 + 幺九 + 刻子听牌（454545 刻子）
    valid_hand = [11, 12, 13, 22, 23, 24, 35, 36, 45, 45, 45, 28, 28]
    # 缺索子（仅万+筒），在长春/沈阳均不满足三门齐
    missing_suit = [11, 12, 13, 22, 23, 24, 25, 26, 45, 45, 45, 28, 28]
    assert changchun_bot._score(valid_hand, [], vis)[0] == 0
    assert changchun_bot._score(missing_suit, [], vis)[0] < 0
    assert shenyang_bot._score(valid_hand, [], vis)[0] == 0
    assert shenyang_bot._score(missing_suit, [], vis)[0] < 0


def test_singapore_blocks_zero_tai_pung_and_allows_dragon_pung():
    vis = [0] * 34
    # 0花无役牌，碰 22 会毁掉平胡且无台
    hand_no_tai = [12, 13, 14, 22, 22, 25, 26, 32, 33, 34, 37, 38, 39]
    assert singapore_bot._choose_claim(hand_no_tai, [], (), vis, ("peng", "pass"), 22, 0, 41) == "pass"
    # 碰白板（三元刻 1 台）允许
    hand_dragon = [12, 13, 14, 25, 26, 32, 33, 34, 37, 38, 47, 47, 29]
    assert singapore_bot._choose_claim(hand_dragon, [], (), vis, ("peng", "pass"), 47, 0, 41) == "peng"


def test_pruned_qinghunpeng_and_new_chinese_efficiency():
    vis = [0] * 34
    tile, _ = qinghunpeng_bot.best_cut([11, 12, 13, 15, 16, 17, 19, 19, 41, 41, 41, 25, 38, 14], [], vis)
    assert tile in (25, 38)  # 应切掉异花色杂牌
    q, _, cut_t = new_chinese_bot.best_cut(
        [11, 12, 13, 22, 23, 24, 34, 35, 36, 45, 45, 45, 28, 39], 0,
        {11, 12, 13, 22, 23, 24, 34, 35, 36, 45, 28, 39}, {t: 0 for t in range(11, 48)}, (), 0
    )
    assert cut_t in (28, 39)
    assert q[0] == 0
