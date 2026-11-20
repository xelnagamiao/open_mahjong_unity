"""Capture the participant and metrics SQL of a first-time replay publication."""
from contextlib import contextmanager
from unittest.mock import MagicMock, Mock, patch

from server.database.game_rewards import build_currency_reward


@contextmanager
def capture_record_write():
    cursor = MagicMock()
    cursor.__enter__.return_value = cursor
    # The table adapter uses INSERT ... ON CONFLICT ... RETURNING game_id.
    cursor.fetchone.return_value = ("inserted",)
    connection = Mock()
    connection.cursor.return_value = cursor
    db = Mock()
    db._get_connection.return_value = connection

    def reward(cursor, record, indexed_ids, *, game_id):
        return build_currency_reward(record, indexed_ids, [])

    with patch("server.database.commerce.prepare_game_reward", side_effect=reward), \
         patch("server.database.commerce.settle_game_reward"), \
         patch("server.records.store.lock_record_ids"), \
         patch("server.records.store.publish_table", return_value=True) as published:
        cursor.published = published
        yield db, connection, cursor
