"""Legal action-window transitions; settlement can only advance through ready."""
from enum import Enum


class Phase(str, Enum):
    START = "waiting"
    REPLACEMENT = "waiting_buhua_action"
    TURN = "waiting_hand_action"
    DISCARD_ONLY = "onlycut_after_action"
    RESPONSE = "waiting_action_after_cut"
    END = "END"
    READY = "waiting_ready"
    FINISHED = "finished"


P = Phase
TRANSITIONS = {
    P.START: {P.REPLACEMENT, P.TURN, P.END},
    P.REPLACEMENT: {P.TURN, P.END},
    P.TURN: {P.RESPONSE, P.REPLACEMENT, P.END},
    P.DISCARD_ONLY: {P.RESPONSE, P.END},
    P.RESPONSE: {P.REPLACEMENT, P.TURN, P.DISCARD_ONLY, P.END},
    P.END: {P.READY}, P.READY: {P.START, P.FINISHED}, P.FINISHED: set(),
}


class StateMachine:
    def __init__(self):
        self.phase = P.START
        self.version = 0

    def transition(self, target):
        target = P(target)
        if target == self.phase:
            return
        if target not in TRANSITIONS[self.phase]:
            raise RuntimeError(f"非法菲律宾麻将状态转换: {self.phase.value} -> {target.value}")
        self.phase = target
        self.version += 1
