"""Deferred Sichuan payments and active-player order must agree with saved records."""
from types import SimpleNamespace
from unittest.mock import Mock

from .shared_record_metrics import analyze_shared_record_for_player
from .scene_stats import record_game_metrics


def blood_record():
    return {"game_title": {"rule": "sichuan", "sub_rule": "sichuan/standard", "blood_battle": True},
        "game_round": {"round_index_1": {"seats": [0, 1, 2, 3], "start_player_index": 0,
            "action_ticks": [["c", 11, "F"], ["d", 12], ["c", 11, "F"],
                ["p", 11, 2, 11, 11], ["c", 13, "F"],
                ["hu_self", 2, 1, ["平和"], [0, 0, 0, 0], 14],
                ["d", 19], ["c", 19, "F"],
                ["hu_first", 1, 2, ["平和"], [0, 0, 0, 0], 19, 0, 3, 1],
                ["liuju", "settle_hu", "hu_self", 2, 1, ["平和"], [-2, -2, 6, -2], 0],
                ["liuju", "settle_hu", "hu_first", 1, 2, ["平和"], [0, 2, 0, -2], 0],
                ["liuju", "chajiao", 3, "no_ting", "[]", [0, -1, 0, 1], 1], ["end"]]}}}


def test_blood_replay_counts_visible_wins_once_and_deferred_money_once():
    rows = [analyze_shared_record_for_player(blood_record(), i) for i in range(4)]
    assert [r['win_count'] for r in rows] == [0, 1, 1, 0]
    assert [r['total_round_score'] for r in rows] == [-2, -1, 6, -3]
    assert rows[1]['total_win_turn'] == rows[2]['total_win_turn'] == 2
    assert rows[2]['fulu_round_count'] == 1
    assert rows[3]['deal_in_count'] == 1 and rows[3]['total_fangchong_score'] == 2
    assert rows[0]['deal_in_count'] == 0  # hu_first is not distance from payer in Sichuan.


def test_next_draw_skips_retired_winner_and_gang_refunds_cancel_only_money():
    record = blood_record()
    ticks = record['game_round']['round_index_1']['action_ticks']
    ticks[5:5] = [["jg", 11, "T", "gs", -1, -1, 3, -1],
                  ["gr", "gs", 1, 1, -3, 1]]
    rows = [analyze_shared_record_for_player(record, i) for i in range(4)]
    assert [r['total_round_score'] for r in rows] == [-2, -1, 6, -3]
    # After seat 2 retires, the next draw/discard belongs to seat 3.
    assert rows[3]['deal_in_count'] == 1 and rows[2]['deal_in_count'] == 0


def test_live_scene_writer_uses_the_same_completed_blood_replay():
    cur = Mock()
    conn = Mock()
    conn.cursor.return_value = cur
    db = Mock()
    db._get_connection.return_value = conn
    player = SimpleNamespace(user_id=10000003, username='player', original_player_index=2,
        score=6, record_counter=SimpleNamespace(rank_result=1))
    record_game_metrics(db, 'blood', blood_record(), [player], dict(rule='sichuan',
        sub_rule='sichuan/standard', room_type='match', match_tier='elo', match_type='4/4_rank'))
    values = cur.execute.call_args.args[1]
    assert values[13:18] == (1, 1, 0, 1, 2)
    assert values[23] == 1 and values[25] == 6
    conn.commit.assert_called_once()


def test_classical_base_fu_and_payment_are_not_double_counted():
    record = {"game_title": {"rule": "classical"}, "game_round": {"round_index_1": {
        "seats": [0, 1, 2, 3], "action_ticks": [["d", 11], ["c", 11],
            ["d", 12], ["c", 12], ["shuhewei", [22, 0, 2, 0], [132, -46, -40, -46]],
            ["hu_self", 0, 132, ["自摸"], [132, -46, -40, -46], 22, []], ["end"]]}}}
    rows = [analyze_shared_record_for_player(record, i) for i in range(4)]
    assert [r['total_round_score'] for r in rows] == [132, -46, -40, -46]
    assert rows[0]['total_fan_score'] == 22 and rows[0]['total_win_turn'] == 2


def test_classical_draw_counts_shuhewei_payment_without_a_hu_tick():
    record = {"game_title": {"rule": "classical"}, "game_round": {"round_index_1": {
        "seats": [0, 1, 2, 3], "action_ticks": [
            ["shuhewei", [2, 0, 0, 0], [6, -2, -2, -2]], ["liuju"], ["end"]]}}}
    assert analyze_shared_record_for_player(record, 0)['total_round_score'] == 6


def test_qingque_decimal_fans_round_once_per_game_and_loss_uses_payment():
    record = {"game_title": {"rule": "qingque"}, "game_round": {"round_index_1": {
        "seats": [0, 1, 2, 3], "action_ticks": [["d", 11], ["c", 11],
            ["hu_first", 1, 3.60616, ["门前清"], [-39, 39, 0, 0]], ["end"]]},
        "round_index_2": {"seats": [0, 1, 2, 3], "action_ticks": [
            ["d", 11], ["hu_self", 1, 3.60616, ["门前清"], [-13, 39, -13, -13]], ["end"]]}}}
    rows = [analyze_shared_record_for_player(record, i) for i in range(4)]
    assert rows[1]['total_fan_score'] == 7  # not 6 (per-win truncation) or 8 (per-win rounding).
    assert rows[1]['total_win_turn'] == 3
    assert rows[0]['total_fangchong_score'] == 39


def test_xueliu_does_not_retire_a_winner():
    record = blood_record()
    record['game_title'].update(sub_rule='sichuan/xueliu', blood_battle=False)
    ticks = record['game_round']['round_index_1']['action_ticks']
    ticks[5:5] = [["hu_self", 1, 1, [], [0, 3, -1, -2]]]
    assert analyze_shared_record_for_player(record, 1)['win_count'] == 2
