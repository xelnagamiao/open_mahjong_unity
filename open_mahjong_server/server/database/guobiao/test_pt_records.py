"""Settlement PT must survive record storage without being replaced by game score."""
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

from server.database.guobiao.store_guobiao import store_guobiao_game_record


class PtRecordTests(unittest.TestCase):
    def store(self, room_type="match"):
        players = []
        for index, pt in enumerate((11.76, 0, -5.15, None)):
            player = SimpleNamespace(
                user_id=101 + index, username=f"player{index}", score=1000 - index * 100,
                original_player_index=index, record_counter=SimpleNamespace(rank_result=index + 1),
                rank_before="10级", score_before=19, rank_after="9级", score_after=10.76,
            )
            if pt is not None:
                player.pt = pt
            players.append(player)
        cursor = Mock()
        conn = Mock()
        conn.cursor.return_value = cursor
        db = Mock()
        db._get_connection.return_value = conn
        record = {"game_title": {"rule": "guobiao", "sub_rule": "guobiao/standard"}}
        with patch("server.database.player_recent_records.update_player_recent_records"), \
             patch("server.database.duplicate_walls.link_duplicate_record"), \
             patch("server.database.scene_stats.record_game_metrics"):
            game_id = store_guobiao_game_record(db, record, players, room_type, "1/4_rank")
        self.assertIsNotNone(game_id)
        conn.commit.assert_called_once()
        db._put_connection.assert_called_once_with(conn)
        records = []
        for call in cursor.execute.call_args_list:
            sql, params = call.args
            if "INSERT INTO game_player_records" in sql:
                columns = sql.split("(", 1)[1].split(")", 1)[0].split(",")
                records.append(dict(zip((column.strip() for column in columns), params)))
        return records

    def test_persists_settled_pt_including_zero_and_preserves_unknown(self):
        rows = self.store()
        self.assertEqual([row["pt_change"] for row in rows], [11.76, 0, -5.15, None])
        self.assertEqual([row["score"] for row in rows], [1000, 900, 800, 700])

    def test_non_ranked_games_have_no_pt_change(self):
        rows = self.store("custom")
        self.assertEqual([row["pt_change"] for row in rows], [None] * 4)


if __name__ == "__main__":
    unittest.main()
