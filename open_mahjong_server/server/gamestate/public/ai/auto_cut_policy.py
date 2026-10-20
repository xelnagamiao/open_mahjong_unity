"""Shared policy for the sparrow (user_id=0): replace bonuses, discard, pass.

Rule adapters supply their legal discard set. Optional wins, declarations and
melds must never fall through to the efficiency bot's decision function.
"""
from ..hand_slot_utils import has_draw_slot, infer_bot_cut_class


def is_auto_cut_bot(player):
    return getattr(player, "user_id", None) == 0


def choose_auto_cut_action(player, actions, legal_tiles=None):
    for action in ("buhua", "nuki", "ready", "nc_draw",
                   "lajihe_flower_continue", "sy_hide", "sy_pass"):
        if action in actions:
            return dict(action_type=action)
    if "cut" in actions:
        hand = player.hand_tiles
        legal = set(hand if legal_tiles is None else legal_tiles)
        # The draw slot is at the end; when it is forbidden, use the last legal
        # physical tile without optimizing the hand or inventing an illegal cut.
        position = next(i for i in range(len(hand)-1, -1, -1) if hand[i] in legal)
        tile = hand[position]
        return dict(action_type="cut", TileId=tile, cutIndex=position,
                    cutClass=infer_bot_cut_class(hand, tile, position, draw_slot=has_draw_slot(player)))
    if "pass" in actions:
        return dict(action_type="pass")
    raise ValueError(f"摸切机器人没有可用的补牌、出牌或过牌动作: {actions}")
