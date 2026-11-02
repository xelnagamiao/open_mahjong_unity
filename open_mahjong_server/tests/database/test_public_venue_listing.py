import datetime
import unittest
from unittest.mock import Mock

from server.database.public_venue_listing import public_venue_rows


class PublicVenueListingTests(unittest.TestCase):
    def test_dates_membership_and_unknown_schedule_are_serializable(self):
        cursor = Mock()
        cursor.fetchall.return_value = [
            {"event_id": "base-a", "description": "社团" * 100, "entry_config": {},
             "created_at": datetime.datetime(2026, 10, 24, 9, 30),
             "planned_start_at": datetime.date(2026, 10, 25), "planned_end_at": None,
             "member_count": 1200, "is_registered": True},
            {"event_id": "base-b", "description": None, "entry_config": None,
             "member_count": 0, "is_registered": False},
        ]
        rows = public_venue_rows(cursor, lambda value: value or {}, "base", 42)
        self.assertEqual(rows[0]["planned_start_at"], "2026-10-25")
        self.assertEqual(rows[0]["planned_end_at"], "")
        self.assertEqual(rows[0]["created_at"], "2026-10-24T09:30:00")
        self.assertEqual(rows[0]["member_count"], 1200)
        self.assertTrue(rows[0]["is_registered"])
        self.assertEqual(len(rows[0]["description"]), 180)
        self.assertEqual(rows[1]["description"], "")
        self.assertEqual(rows[1]["planned_start_at"], "")
        self.assertEqual(rows[1]["member_count"], 0)
        self.assertFalse(rows[1]["is_registered"])
        self.assertIsInstance(cursor.fetchall.return_value[0]["planned_start_at"], datetime.date)

    def test_kind_and_identity_are_bound_parameters(self):
        cursor = Mock()
        cursor.fetchall.return_value = []
        self.assertEqual(public_venue_rows(cursor, lambda value: {}, "base", 42), [])
        self.assertEqual(cursor.execute.call_args.args[1], (42, 42, "base", "base"))
        public_venue_rows(cursor, lambda value: {}, "event", 7)
        self.assertEqual(cursor.execute.call_args.args[1], (7, 7, "event", "event"))
        public_venue_rows(cursor, lambda value: {}, "unexpected", None)
        self.assertEqual(cursor.execute.call_args.args[1], (None, None, None, None))


if __name__ == "__main__":
    unittest.main()
