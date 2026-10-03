import copy
import unittest

from server.database.guobiao.backfill_pt_changes import replay, rules_fingerprint


class PtBackfillTests(unittest.TestCase):
    def setUp(self):
        self.baseline = dict(cutoff='2026-09-01 00:00:00', rules_fingerprint=rules_fingerprint(), players={
            '101': dict(rank='10级', score=19, created_at='2026-08-01'),
            '102': dict(rank='2级', score=0, created_at='2026-08-01'),
            '103': dict(rank='2级', score=20, created_at='2026-08-01'),
            '104': dict(rank='十段', score=100, created_at='2026-08-01'),
        })
        self.users = [dict(user_id=uid, created_at='2026-08-01') for uid in range(101, 105)]
        self.games = [dict(game_id='game1', created_at='2026-09-02 12:00:00', queue_type='beginner_quanzhuang', players=[
            dict(user_id=101, score=100, rank=1, rule='guobiao'),
            dict(user_id=102, score=0, rank=2, rule='guobiao'),
            dict(user_id=103, score=0, rank=2, rule='guobiao'),
            dict(user_id=104, score=-100, rank=4, rule='guobiao', pt_change=0),
        ])]
        self.ranks = [dict(user_id=uid, guobiao_rank=rank, guobiao_score=score) for uid, rank, score in
                      [(101, '8级', 3), (102, '2级', 0.75), (103, '2级', 20.75), (104, '十段', 100)]]

    def run_replay(self, audits=None):
        return replay(self.baseline, self.users, self.ranks, self.games, audits or [])

    def test_ties_promotions_and_existing_zero_pt(self):
        changes, problems = self.run_replay()
        self.assertEqual(problems, {})
        self.assertEqual([row['pt_change'] for row in changes], [24, 0.75, 0.75])
        self.assertNotIn(104, [row['user_id'] for row in changes])

    def test_audited_rank_adjustment_is_applied_without_becoming_game_pt(self):
        self.ranks[0].update(guobiao_rank='初段', guobiao_score=200)
        audit = dict(created_at='2026-09-03', target_id='101', payload=dict(
            before=dict(guobiao_rank='8级', guobiao_score=3),
            after=dict(guobiao_rank='初段', guobiao_score=200)))
        changes, problems = self.run_replay([audit])
        self.assertEqual(problems, {})
        self.assertEqual(changes[0]['pt_change'], 24)
        audit['payload']['before']['guobiao_score'] = 100
        changes, problems = self.run_replay([audit])
        self.assertIn(101, problems)
        self.assertNotIn(101, [row['user_id'] for row in changes])

    def test_in_progress_settlement_or_conflicting_saved_pt_is_not_overwritten(self):
        self.ranks[0]['guobiao_score'] = 27
        self.games[0]['players'][1]['pt_change'] = 999
        changes, problems = self.run_replay()
        self.assertEqual(set(problems), {101, 102})
        self.assertEqual([row['user_id'] for row in changes], [103])

    def test_new_players_start_at_ten_kyu_and_reused_ids_are_rejected(self):
        del self.baseline['players']['101']
        self.users[0]['created_at'] = '2026-09-02'
        self.ranks[0].update(guobiao_rank='9级', guobiao_score=4)
        self.users[1]['created_at'] = '2026-09-02'
        changes, problems = self.run_replay()
        self.assertEqual(set(problems), {102})
        self.assertEqual(changes[0]['pt_change'], 24)

    def test_rule_changes_require_a_new_verified_baseline(self):
        baseline = copy.deepcopy(self.baseline)
        baseline['rules_fingerprint'] = 'old-rules'
        with self.assertRaisesRegex(ValueError, 'PT rules changed'):
            replay(baseline, self.users, self.ranks, self.games, [])


if __name__ == '__main__':
    unittest.main()
