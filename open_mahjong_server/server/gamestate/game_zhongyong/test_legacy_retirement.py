"""Retired Nanque records cannot silently use the new blood-battle replay flow."""
import pytest

from . import NanqueGameState
from ..verifier.record_sim import RecordSim, resolve_record_flags


def test_nanque_entry_points_only_emit_current_records_and_draw_from_tail():
    cls = NanqueGameState
    room = cls._default_room_data()
    state = cls(room_data=room)
    state.initialize_round()
    state.start_game_recording()
    title = state.game_record["game_title"]
    assert (title["rule"], title["sub_rule"]) == ("zhongyong", "zhongyong/nanque")
    assert title["blood_battle"] and title["rule_version"] == "nanque-blood-v1"
    assert state.winner_limit == 3
    state.tiles_list = [11, 12, 13, 19]
    assert state._draw_supplement_tile(0) == 19
    assert state.tiles_list == [11, 12, 13]
    sim = RecordSim(state.game_record)
    assert sim.is_concealed_blood() and sim.flags.replacement_from_tail_end


@pytest.mark.parametrize("sub_rule", ["zhongyong/standard", "zhongyong/nanque"])
def test_current_zhongyong_family_stays_supported(sub_rule):
    flags = resolve_record_flags({"rule": "zhongyong", "sub_rule": sub_rule})
    assert flags.rule_id == "zhongyong" and flags.replacement_from_tail_end
    assert not flags.kong_replacement_from_front
