from types import SimpleNamespace

from .rule_logic import apply_daiminkan_pao, record_pao_call


def game():
    players = [SimpleNamespace() for _ in range(4)]
    return SimpleNamespace(
        detailed_config={"pao_daiminkan_rinshan": True, "pao": False},
        hu_class="hu_self",
        player_list=players,
    )


def test_daiminkan_call_records_only_the_discarder_for_the_next_rinshan():
    game_state = game()
    record_pao_call(game_state, caller=1, discarder=3, action="gang", tile=11)
    assert game_state._pending_daiminkan_pao_liable == 3


def test_daiminkan_pao_transfers_all_remaining_tsumo_shares_once():
    game_state = game()
    changes = {0: 4300, 1: -2100, 2: -1100, 3: -1100}
    apply_daiminkan_pao(game_state, changes, winner=0, result={"daiminkan_pao_liable": 2})
    assert changes == {0: 4300, 1: 0, 2: -4300, 3: 0}
    assert sum(changes.values()) == 0
