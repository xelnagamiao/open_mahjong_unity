import pytest

from .unity_sim.labels import build_action_buttons
from .unity_sim import UnitySim
from .test_spectator_visual_hand import _spectator_game_start


@pytest.mark.parametrize('rule,expected', [
    ('riichi', '九种九牌'), ('riichi/sanma', '九种九牌'),
    ('riichi/standard', '九种九牌'), ('classical', '九老峰回'),
    ('classical/standard', '九老峰回'),
])
def test_shared_nine_terminals_action_uses_rule_specific_caption(rule, expected):
    assert build_action_buttons(['jiuzhongjiupai', 'pass'], kind='hand', room_rule=rule) == [
        {'text': expected, 'actions': ['jiuzhongjiupai']},
        {'text': '取消', 'actions': ['pass']},
    ]


def test_sea_bottom_and_north_actions_keep_distinct_captions():
    assert build_action_buttons(['sea_bottom', 'pass'], kind='hand', room_rule='changsha') == [
        {'text': '要海底', 'actions': ['sea_bottom']}, {'text': '不要', 'actions': ['pass']},
    ]
    assert build_action_buttons(['nuki', 'buhua'], kind='hand', room_rule='riichi') == [
        {'text': '拔北', 'actions': ['nuki']}, {'text': '补花', 'actions': ['buhua']},
    ]


@pytest.mark.parametrize('rule,expected', [('jiujiang', '补发财'), ('wuhan', '杠'), ('classical', '补花')])
def test_replacement_action_uses_the_rule_caption(rule, expected):
    assert build_action_buttons(['buhua'], kind='hand', room_rule=rule) == [
        {'text': expected, 'actions': ['buhua']},
    ]


@pytest.mark.parametrize('rule,expected', [('riichi', '九种九牌'), ('classical', '九老峰回')])
def test_simulated_network_ask_uses_current_room_caption_after_switch(rule, expected):
    sim = UnitySim(viewer_user_id=999)
    for current in ['classical', 'riichi', rule]:
        sim.apply(_spectator_game_start(current, 1))
        sim.apply({'type': f'gamestate/{current}/broadcast_hand_action', 'ask_hand_action_info': {
            'player_index': 1, 'action_tick': 1, 'action_list': ['jiuzhongjiupai', 'pass'],
        }})
    assert sim.snapshot()['ActionButtonSim']['buttons'][0]['text'] == expected
