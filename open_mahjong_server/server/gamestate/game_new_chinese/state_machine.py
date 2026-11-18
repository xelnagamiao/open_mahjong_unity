from enum import Enum


class Phase(str, Enum):
    START = "waiting"
    EXCHANGE = "waiting_exchange"
    TURN = "waiting_hand_action"
    AFTER_MELD = "onlycut_after_action"
    RESPONSE = "waiting_action_after_cut"
    ROB_KONG = "waiting_action_qianggang"
    BAO = "waiting_tongbao"
    END = "END"
    READY = "waiting_ready"
    FINISHED = "finished"


P = Phase
TRANSITIONS = {
    P.START: {P.EXCHANGE, P.TURN}, P.EXCHANGE: {P.TURN},
    P.TURN: {P.RESPONSE, P.ROB_KONG, P.BAO, P.END}, P.AFTER_MELD: {P.RESPONSE, P.ROB_KONG, P.BAO, P.TURN, P.END},
    P.RESPONSE: {P.TURN, P.AFTER_MELD, P.ROB_KONG, P.BAO, P.END},
    P.ROB_KONG: {P.TURN, P.AFTER_MELD, P.BAO, P.END},
    P.BAO: {P.RESPONSE, P.AFTER_MELD, P.TURN, P.ROB_KONG, P.END},
    P.END: {P.READY}, P.READY: {P.START, P.FINISHED}, P.FINISHED: set(),
}


class StateMachine:
    def __init__(self):
        self.phase = P.START

    def transition(self, value):
        target = P(value)
        if target != self.phase and target not in TRANSITIONS[self.phase]:
            raise ValueError(f"非法新中式麻将状态转换 {self.phase.value} → {target.value}")
        self.phase = target
