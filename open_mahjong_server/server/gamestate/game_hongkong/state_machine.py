"""Explicit legal phases, following the project's Hongque FSM convention.

Flower declarations and the initial sixteen-tile ready declaration are actual
action windows. They may not be hidden in a scoring callback or inferred by a
client. The hand-end phase never advances directly to a player's turn.
"""

from enum import Enum


class HongKongPhase(str, Enum):
    STARTING = "waiting"
    INITIAL_READY = "waiting_initial_ready"
    REPLACEMENT = "waiting_buhua_round"
    TURN = "waiting_hand_action"
    DISCARD_ONLY = "onlycut_after_action"
    DISCARD_RESPONSE = "waiting_action_after_cut"
    KONG_RESPONSE = "waiting_action_qianggang"
    FLOWER = "waiting_flower_choice"
    FLOWER_RESPONSE = "waiting_flower_claim"
    END = "END"
    READY = "waiting_ready"
    FINISHED = "finished"


P = HongKongPhase
TRANSITIONS = {
    P.STARTING: {P.INITIAL_READY,P.REPLACEMENT,P.TURN,P.FLOWER,P.FLOWER_RESPONSE,P.END},
    P.INITIAL_READY: {P.REPLACEMENT,P.TURN,P.FLOWER,P.FLOWER_RESPONSE,P.END},
    P.REPLACEMENT: {P.INITIAL_READY,P.TURN,P.FLOWER,P.FLOWER_RESPONSE,P.END},
    P.TURN: {P.REPLACEMENT,P.DISCARD_RESPONSE,P.KONG_RESPONSE,P.FLOWER,P.FLOWER_RESPONSE,P.END},
    P.DISCARD_ONLY: {P.DISCARD_RESPONSE,P.END},
    P.DISCARD_RESPONSE: {P.REPLACEMENT,P.TURN,P.DISCARD_ONLY,P.FLOWER,P.FLOWER_RESPONSE,P.END},
    P.KONG_RESPONSE: {P.REPLACEMENT,P.TURN,P.FLOWER,P.FLOWER_RESPONSE,P.END},
    P.FLOWER: {P.REPLACEMENT,P.TURN,P.FLOWER_RESPONSE,P.INITIAL_READY,P.END},
    P.FLOWER_RESPONSE: {P.REPLACEMENT,P.TURN,P.FLOWER,P.INITIAL_READY,P.END},
    P.END: {P.READY},
    P.READY: {P.STARTING,P.FINISHED},
    P.FINISHED: set(),
}


class HongKongStateMachine:
    def __init__(self):
        self.phase = P.STARTING
        self.version = 0
        self.history: list[tuple[P,P]] = []

    def transition(self,target):
        target = P(target)
        if target == self.phase:
            return
        if target not in TRANSITIONS[self.phase]:
            raise RuntimeError(f"Invalid Hong Kong transition: {self.phase.value} -> {target.value}")
        self.history.append((self.phase,target))
        self.phase = target
        self.version += 1
