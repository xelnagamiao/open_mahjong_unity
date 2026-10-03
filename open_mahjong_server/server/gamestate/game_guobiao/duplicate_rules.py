"""The Guobiao duplicate differences; ordinary Guobiao keeps its own rules."""
from ..public.hand_slot_utils import has_draw_slot


class DuplicateGuobiaoRules:
    def __init__(self, state):
        if state.room_rule != "guobiao" or state.sub_rule == "guobiao/blood_battle":
            raise ValueError("国标血战及其他玩法不支持复式牌墙")
        self.state = state

    def can_draw(self, player_index):
        player = self.state.player_list[player_index]
        return bool(self.state.tiles_list.parts[player.original_player_index])

    def is_last_discard(self, discarder_index):
        # The next ordinary draw, including after a chi/peng/kong discard,
        # determines whether only ron is available and whether it is haidi.
        return not self.can_draw((discarder_index + 1) % 4)

    def is_last_draw(self, player_index):
        return has_draw_slot(self.state.player_list[player_index]) and self.is_last_discard(player_index)


def duplicate_rules_for(state):
    if not getattr(state, "duplicate_key", None):
        return None
    rules = getattr(state, "_duplicate_rules", None)
    if rules is None:
        rules = state._duplicate_rules = DuplicateGuobiaoRules(state)
    return rules
