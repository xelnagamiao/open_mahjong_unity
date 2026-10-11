"""Normal daily aggregation never finalizes an unfinished statistical day."""
from datetime import date
from unittest.mock import Mock

import psycopg2
import pytest
from . import daily_aggregator as daily


def database():
    cur, conn, db = Mock(), Mock(), Mock()
    conn.cursor.return_value = cur
    db._get_connection.return_value = conn
    return db, conn, cur


def test_catchup_reaggregates_existing_closed_buckets_only(monkeypatch):
    db, conn, cur = database()
    cur.fetchall.return_value = [(date(2026, 10, 5),)]
    monkeypatch.setattr(daily, 'current_stat_date', lambda: date(2026, 10, 6))
    aggregate = Mock()
    monkeypatch.setattr(daily, 'run_daily_aggregation', aggregate)
    daily.run_catchup_aggregation(db, since_date=date(2026, 10, 4), date_to=date(2026, 10, 6))
    statement, params = cur.execute.call_args.args
    assert params == (date(2026, 10, 4), date(2026, 10, 5))
    assert 'NOT IN' not in statement and 'generate_series' not in statement
    aggregate.assert_called_once_with(db, date(2026, 10, 5))
    db._put_connection.assert_called_once_with(conn)


def test_empty_source_dates_do_not_create_old_phantom_buckets(monkeypatch):
    db, _, cur = database()
    cur.fetchall.return_value = []
    monkeypatch.setattr(daily, 'current_stat_date', lambda: date(2026, 10, 6))
    aggregate = Mock()
    monkeypatch.setattr(daily, 'run_daily_aggregation', aggregate)
    daily.run_catchup_aggregation(db)
    aggregate.assert_not_called()


def test_database_failure_is_reported_and_rolled_back():
    db, conn, cur = database()
    cur.execute.side_effect = psycopg2.OperationalError('isolated failure')
    with pytest.raises(psycopg2.Error):
        daily.aggregate_daily_stats(db, date(2026, 10, 5))
    conn.rollback.assert_called_once()
    conn.commit.assert_not_called()


def test_elo_scene_is_an_ordinary_daily_bucket():
    assert 'elo' in daily.MATCH_TIERS


def test_daily_refresh_requires_one_explicit_date():
    cur = Mock()
    with pytest.raises(ValueError):
        daily._write_scene_statistics(cur, None)
    cur.execute.assert_not_called()
    day = date(2026, 10, 5)
    daily._write_scene_statistics(cur, day)
    deletes = [call.args for call in cur.execute.call_args_list if call.args[0].startswith('DELETE')]
    assert len(deletes) == 2
    assert all('WHERE stat_date=%s' in statement and params == (day,) for statement, params in deletes)


def test_startup_only_catches_up_closed_days_using_existing_metrics(monkeypatch):
    db = Mock()
    monkeypatch.setattr(daily, 'current_stat_date', lambda: date(2026, 10, 6))
    monkeypatch.setattr(daily, '_get_meta_value', lambda *a: '2026-10-04')
    aggregate, save = Mock(), Mock()
    monkeypatch.setattr(daily, 'run_catchup_aggregation', aggregate)
    monkeypatch.setattr(daily, '_set_meta_value', save)
    daily.run_startup_stats_restore(db)
    aggregate.assert_called_once_with(db, since_date=date(2026, 10, 3), date_to=date(2026, 10, 5))
    save.assert_called_once_with(db, daily.STATS_CATCHUP_THROUGH_META_KEY, '2026-10-05')
    db._get_connection.assert_not_called()
