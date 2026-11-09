"""Persist current Zhongyong-family and related regional replays."""

from __future__ import annotations

import json
import logging
import secrets
import string

from psycopg2 import Error

from ...game_calculation.nanque.fan_definitions import FANS

logger = logging.getLogger(__name__)

_GAME_ID_ALPHABET = string.ascii_letters + string.digits
_GAME_ID_LENGTH = 10
FAN_FIELDS = tuple(FANS.keys())
FAN_NAME_TO_FIELD = {definition.name: fan_id for fan_id, definition in FANS.items()}


def _generate_game_id(length: int = _GAME_ID_LENGTH) -> str:
    return "".join(secrets.choice(_GAME_ID_ALPHABET) for _ in range(length))




def store_zhongyong_game_record(
    db_manager,
    game_record: dict,
    player_list: list,
    room_type: str,
    match_type: str,
):
    """Publish replay, metrics and currency through the shared atomic path."""
    from ..record_persistence import persist_game_record
    return persist_game_record(db_manager, game_record, player_list, room_type, match_type,
                               _generate_game_id)




