"""Room entry validates and preserves the complete riichi rules, including old clients."""
import json
import asyncio
from pathlib import Path

import pytest

from .room_validators import RiichiRoomValidator
from . import test_riichi_starting_score as helpers
from ..game_calculation.riichi.rule_config import catalog, normalize_riichi_config, preset_room_config
from ..gamestate.game_riichi.RiichiGameState import RiichiGameState


def validate(**kwargs):
    return RiichiRoomValidator(room_name='rules', game_round=2, round_timer=20, step_timer=5, **kwargs)


def test_legacy_room_has_complete_independent_defaults():
    first, second = validate(), validate()
    assert first.detailed_config == normalize_riichi_config()
    first.detailed_config['ippatsu'] = False
    assert second.detailed_config['ippatsu'] is True


@pytest.mark.parametrize('bad', ['other', '', 'riichi/missing'])
def test_unknown_subrule_rejected(bad):
    with pytest.raises(ValueError): validate(sub_rule=bad)


@pytest.mark.parametrize('bad', [{'ippatsu': 'false'}, {'ippatsu': 0}, {'yakuman_limit': True}, {'unknown': 1}, [], {'kazoe_limit': 'wrong'}])
def test_invalid_rules_rejected_without_coercion(bad):
    with pytest.raises(ValueError): validate(detailed_config=bad)


@pytest.mark.parametrize('preset_id', ['majsoul','tenhou','jpml_a','mleague'])
def test_presets_are_complete_and_roundtrip(preset_id):
    preset = preset_room_config(preset_id)
    assert set(preset['detailed_config']) == {o['key'] for o in catalog()['options']}
    assert normalize_riichi_config(preset['detailed_config']) == json.loads(json.dumps(preset['detailed_config']))


@pytest.mark.parametrize('preset_id', ['majsoul','tenhou','jpml_a','mleague'])
def test_custom_and_event_rooms_keep_rules(preset_id):
    asyncio.run(check_custom_and_event_rooms(preset_id))


async def check_custom_and_event_rooms(preset_id):
    preset = preset_room_config(preset_id)
    for event in (False, True):
        manager, server = helpers.RiichiStartingScoreTests().manager()
        if event:
            response = await manager.create_empty_event_room('event-test','riichi',preset,broadcast=False)
        else:
            response = await manager.create_Riichi_room('connection','rules',preset['game_round'],'',20,5,False,
                sub_rule=preset['sub_rule'], starting_score=preset['starting_score'],red_dora=preset['red_dora'],
                detailed_config=preset['detailed_config'])
        assert response.success, response.message
        assert response.room_info['detailed_config'] == preset['detailed_config']
        game = RiichiGameState(server,response.room_info,None,None,'rules-test')
        assert game.detailed_config == preset['detailed_config']


def test_unity_catalog_matches_server():
    root = Path(__file__).resolve().parents[3]
    asset = root/'open_mahjong_unity/Assets/Resources/RiichiRuleOptions.json'
    assert asset.is_file(), 'Main Unity catalog must be present in the workspace'
    assert json.loads(asset.read_text(encoding='utf-8')) == catalog()
    web_option = root/'open_mahjong_web/shared/riichiWinRequirements.json'
    assert json.loads(web_option.read_text(encoding='utf-8')) == next(o for o in catalog()['options'] if o['key'] == 'minimum_win_score')


@pytest.mark.parametrize('score', [0,2000,4000,6000,8000,12000,16000,24000,32000])
def test_both_thresholds_roundtrip_through_custom_and_event_rooms(score):
    async def check():
        for event in (False, True):
            manager, server = helpers.RiichiStartingScoreTests().manager()
            detail = {'minimum_win_score': score}
            if event:
                response = await manager.create_empty_event_room('event-test','riichi',
                    dict(hepai_limit=3, detailed_config=detail), broadcast=False)
            else:
                response = await manager.create_Riichi_room('connection','thresholds',2,'',20,5,False,
                    hepai_limit=3, detailed_config=detail)
            assert response.success, response.message
            game = RiichiGameState(server,response.room_info,None,None,'thresholds-test')
            assert game.hepai_limit == 3 and game.detailed_config['minimum_win_score'] == score
    asyncio.run(check())


@pytest.mark.parametrize('score', [True, False, '8000', -1, 1000, 3000, None])
def test_invalid_win_line_rejected(score):
    with pytest.raises(ValueError): validate(detailed_config={'minimum_win_score': score})
