"""Book-audit regressions; page numbers count the PDF cover as page 1."""
from .test_flow import act, state, turn
from ...game_calculation.vietnamese import rules as book
from ...game_calculation.vietnamese.test_rules import NORMAL
from ...game_calculation.vietnamese.tiles import ANY, DOT, WIND, BIG_FLOWER, BONUS, JOKERS


def test_pdf46_dealer_must_discard_the_single_dealt_flower_on_first_turn():
    s = turn(list(NORMAL[:-1]) + [51])
    p = s.player_list[0]
    p.nfnl_eligible = True
    act(s, {0: dict(action_type='cut', TileId=11, cutClass=False)})
    assert not p.nfnl_eligible


def test_pdf48_discarded_jokers_score_only_for_true_no_flowers_no_leaves():
    hand = (11,12,13,21,22,23,31,32,33,41,41,41,25,25)
    q = book.score(hand, winning_tile=25, nfnl_eligible=True, discarded_bonus=(ANY,))
    assert 'true_no_flowers_leaves' not in dict(q.patterns)
    assert 'replaced_71' not in dict(q.patterns)
    q = book.score(NORMAL, winning_tile=25, nfnl_eligible=True, discarded_bonus=(ANY,))
    assert dict(q.patterns)['true_no_flowers_leaves'] == 6
    assert dict(q.patterns)['replaced_71'] == 3


def test_pdf37_single_wait_cannot_count_fifth_copy_already_in_own_quad():
    hand = (12,13,14,21,22,23,31,32,33,25,25)
    q = book.score(hand, ('G11',), winning_tile=14, self_draw=True)
    assert dict(q.patterns)['single_wait'] == 1


def test_pdf17_50_big_flower_counts_as_additional_flower_for_optional_eight_flowers():
    for count, expected in ((7,6), (9,12)):
        patterns = book.bonus_score(range(51,51+count),
            [dict(tile=BIG_FLOWER,batch=1,opening=False)], (), 0, book.Rules(eight_flowers=True))
        assert patterns['eight_flowers'] == expected
        assert patterns['replaced_75'] == 2


def test_pdf58_safe_joker_is_scoped_and_must_be_legal_to_discard():
    s = turn([45, DOT])
    target = s.player_list[1]
    target.combination_tiles = ['k45','k46']
    target.combination_mask = [[0,45]*3, [0,46]*3]
    assert DOT in s.safe_tiles_by_threat(0)[(1,'big_three_dragons')]
    s.player_list[0].huapai_list = [51]
    assert DOT not in s.safe_tiles_by_threat(0)[(1,'big_three_dragons')]


def test_pdf57_dragon_feeder_liability_requires_self_draw_or_the_third_dragon():
    s = turn(NORMAL)
    winner = s.player_list[1]
    winner.combination_tiles = ['k45','k46','k47']
    winner.combination_mask = [[0,t]*3 for t in (45,46,47)]
    winner.liability_feeders['big_three_dragons'] = 2
    s.discard_safety = {(1,'big_three_dragons'): True}
    quote = book.Score(9, (('big_three_dragons',9),))
    assert s.liability(1,'self_draw',25,None,quote)[0] == 2
    assert s.liability(1,'discard',25,0,quote)[0] is None


def test_pdf25_26_opening_flower_replacement_cycles_do_not_take_retained_jokers():
    s = state()
    s.initialize_round()
    for p in s.player_list: p.hand_tiles = list(NORMAL[:-1])
    s.player_list[0].hand_tiles = list(NORMAL[:-2]) + [51, ANY]
    s.player_list[1].hand_tiles[-1] = 52
    s.tiles_list[-2:] = [11,53]
    s.opening_queue = [0,1]
    s.open_action_window(s.opening_window())
    act(s, {0: dict(action_type='buhua')})
    assert s.current_player_index == 1 and 53 in s.player_list[0].hand_tiles
    act(s, {1: dict(action_type='buhua')})
    assert s.current_player_index == 0
    assert ANY in s.player_list[0].hand_tiles
    assert all(t in BONUS for t in s.player_list[0].huapai_list)
