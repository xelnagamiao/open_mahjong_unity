"""Legal phases; a kong commits only after its robbing window has resolved."""

from enum import Enum


class Phase(str, Enum):
    START = "waiting"
    INITIAL_READY = "waiting_initial_ready"
    TURN = "waiting_hand_action"
    DISCARD_ONLY = "onlycut_after_action"
    RESPONSE = "waiting_action_after_cut"
    KONG = "waiting_action_qianggang"
    END = "END"
    READY = "waiting_ready"
    FINISHED = "finished"


P = Phase
TRANSITIONS = {
    P.START: {P.INITIAL_READY, P.TURN, P.END},
    P.INITIAL_READY: {P.TURN},
    P.TURN: {P.RESPONSE, P.KONG, P.END},
    P.DISCARD_ONLY: {P.RESPONSE},
    P.RESPONSE: {P.TURN, P.DISCARD_ONLY, P.END},
    P.KONG: {P.TURN, P.END},
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
            raise RuntimeError(f"非法贵州麻将状态转换：{self.phase.value} -> {target.value}")
        self.phase = target
        self.version += 1
