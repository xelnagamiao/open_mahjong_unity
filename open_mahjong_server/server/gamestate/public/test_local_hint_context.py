"""Local hint packets must own their inputs without invoking server workers."""
import ast
from pathlib import Path
from copy import deepcopy
from unittest.mock import AsyncMock

import pytest

from .local_hint_context import hint_context


def test_nested_inputs_survive_later_draws_claims_and_context_changes():
    hand, melds, cuts = [11, 12, 13, 51, 45], ['s12'], [45, 51]
    records = [dict(code='s12', physical=[11, 12, 13], logical=[11, 12, 13])]
    flowers = [55, 56]
    packet = hint_context('test', hand, melds, cuts=cuts, meld_records=records, flowers=flowers)
    expected = deepcopy(packet)
    hand[0] = 19
    melds.append('k45')
    cuts.clear()
    records[0]['physical'][0] = 51
    flowers.append(57)
    assert packet == expected
    assert set(packet) == {'source_hand_tiles', 'source_melds', 'local_request'}


@pytest.mark.parametrize('sub_rule', ['guobiao/huahun', 'guobiao/four_jokers', 'guobiao/sixteen'])
def test_guobiao_preview_does_not_enqueue_server_cpu(sub_rule, monkeypatch):
    from ..game_guobiao import four_jokers_runtime as runtime
    from ..game_guobiao.test_sanma import sanma
    from ..game_guobiao.boardcast import _build_game_start_payload_for_viewer
    cpu = AsyncMock(side_effect=AssertionError('UI preview must be computed by the client'))
    monkeypatch.setattr(runtime, 'run_room_bot_cpu', cpu)
    state = sanma(sub_rule=sub_rule)
    state.tips = state.count_tips = True
    assert not hasattr(runtime, 'prepare_hints')
    assert not hasattr(runtime, 'cached_hints')
    for index in range(4):
        packet = _build_game_start_payload_for_viewer(state, index)
        assert 'four_jokers_hints' not in packet
    cpu.assert_not_awaited()


def test_runtime_has_no_dependencies_on_offline_hint_oracles():
    root = Path(__file__).resolve().parents[2]
    for path in root.rglob('*.py'):
        if path.name.startswith('test') or path.name == 'conftest.py' or 'tests' in path.parts:
            continue
        for node in ast.walk(ast.parse(path.read_text(encoding='utf-8-sig'))):
            modules = ([node.module or ''] if isinstance(node, ast.ImportFrom) else
                       [alias.name for alias in node.names] if isinstance(node, ast.Import) else [])
            assert not any('local_hint_oracles' in name for name in modules), path


def test_gameplay_does_not_reference_web_preview_entry_points():
    root = Path(__file__).resolve().parents[1]
    removed = {'hint_snapshot', 'calculate_hint', 'calculate_tip_batch'}
    for path in root.rglob('*.py'):
        if path.name.startswith('test') or path.name == 'conftest.py' or 'tests' in path.parts or 'verifier' in path.parts:
            continue
        for node in ast.walk(ast.parse(path.read_text(encoding='utf-8-sig'))):
            if isinstance(node, ast.ImportFrom):
                assert not removed.intersection(alias.name for alias in node.names), path
            elif isinstance(node, ast.Attribute):
                assert node.attr not in removed, path
    from ...response import GameInfo, Ask_hand_action_info
    assert 'four_jokers_hints' not in GameInfo.model_fields
    assert 'four_jokers_hints' not in Ask_hand_action_info.model_fields


@pytest.mark.parametrize('rule', ['fuzhou', 'guangdong', 'hangzhou', 'hongkong', 'hongzhong',
    'jiujiang', 'nanchang', 'nanjing', 'shenyang', 'singapore', 'wuhan'])
def test_removed_server_preview_modules_do_not_reappear(rule):
    root = Path(__file__).resolve().parents[1] / ('game_' + rule)
    assert not (root / 'hints.py').exists()
    assert not (root / 'tips.py').exists()


def test_changsha_exchange_packets_only_copy_own_inputs(monkeypatch):
    from ..game_changsha.test_exchange_six import make_state
    state = make_state()
    flow = state.exchange_six
    state.player_list[0].hand_tiles = [12,12,12,13,14,21,22,23,24,25,26,31,32]
    flow.locked_patterns[0] = {33: ['小胡']}
    def forbidden(*args, **kwargs):
        pytest.fail('Snapshot must not enumerate waits or score hypothetical wins')
    monkeypatch.setattr(flow, 'waits', forbidden)
    monkeypatch.setattr(flow, 'result', forbidden)
    for seat in range(4):
        private = flow.packet(seat).state.private_state
        assert 'hints' not in private
        request = private['local_hint_context']
        assert request['hand'] == state.player_list[seat].hand_tiles
        assert not {'waits', 'discard_waits', 'waiting_details'} & request.keys()
    frozen = flow.client_context(0, [12])
    saved = deepcopy(frozen)
    flow.locked_patterns[0][33].append('明牌')
    state.player_list[0].hand_tiles.append(33)
    assert frozen == saved
    assert not hasattr(flow, 'hand_hints')
    assert not hasattr(flow, 'hint_cache')
