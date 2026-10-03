"""MIL Hangzhou's explicit, single-winner action phases."""

from enum import Enum


class Phase(str, Enum):
    START = "waiting"
    TURN = "waiting_hand_action"
    DISCARD_ONLY = "onlycut_after_action"
    RESPONSE = "waiting_action_after_cut"
    TEN_WINDS = "waiting_hangzhou_ten_winds"
    END = "END"
    READY = "waiting_ready"
    FINISHED = "finished"


P = Phase
TRANSITIONS = {
    P.START: {P.TURN},
    P.TURN: {P.RESPONSE, P.TEN_WINDS, P.END},
    P.DISCARD_ONLY: {P.TURN, P.RESPONSE, P.END},
    P.RESPONSE: {P.TURN, P.DISCARD_ONLY, P.END},
    P.TEN_WINDS: {P.RESPONSE, P.END},
    P.END: {P.READY},
    P.READY: {P.START, P.FINISHED},
    P.FINISHED: set(),
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
            raise RuntimeError(f"非法杭州麻将状态转换：{self.phase.value} -> {target.value}")
        self.phase = target
        self.version += 1
