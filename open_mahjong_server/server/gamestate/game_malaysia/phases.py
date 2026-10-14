from enum import Enum


class Phase(str, Enum):
    START = "waiting"
    FLOWER = "waiting_buhua_action"
    TURN = "waiting_hand_action"
    CUT = "onlycut_after_action"
    RESPONSE = "waiting_action_after_cut"
    KONG = "waiting_action_qianggang"
    END = "END"
    READY = "waiting_ready"
    FINISHED = "finished"


P = Phase
TRANSITIONS = {
    P.START: {P.FLOWER, P.TURN, P.END},
    P.FLOWER: {P.FLOWER, P.TURN, P.CUT, P.END},
    P.TURN: {P.FLOWER, P.TURN, P.RESPONSE, P.KONG, P.END},
    P.CUT: {P.FLOWER, P.CUT, P.RESPONSE},
    P.RESPONSE: {P.FLOWER, P.TURN, P.CUT, P.END},
    P.KONG: {P.FLOWER, P.TURN, P.END},
    P.END: {P.READY}, P.READY: {P.START, P.FINISHED}, P.FINISHED: set(),
}


class Machine:
    def __init__(self):
        self.phase = P.START
        self.version = 0

    def transition(self, value):
        target = P(value)
        if target != self.phase:
            if target not in TRANSITIONS[self.phase]:
                raise ValueError(f"非法马来西亚状态转换: {self.phase.value} -> {target.value}")
            self.phase, self.version = target, self.version + 1
