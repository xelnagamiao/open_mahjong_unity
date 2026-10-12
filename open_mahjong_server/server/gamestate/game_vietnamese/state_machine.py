from enum import Enum


class Phase(str,Enum):
    START='waiting'
    REPLACEMENT='waiting_buhua_action'
    TURN='waiting_hand_action'
    DISCARD_ONLY='onlycut_after_action'
    RESPONSE='waiting_action_after_cut'
    ROB='waiting_action_qianggang'
    SPECIAL='waiting_vietnamese_special'
    END='END'
    READY='waiting_ready'
    FINISHED='finished'


P=Phase
TRANSITIONS={
    P.START:{P.REPLACEMENT,P.TURN,P.END},
    P.REPLACEMENT:{P.REPLACEMENT,P.TURN,P.RESPONSE,P.END},
    P.TURN:{P.REPLACEMENT,P.RESPONSE,P.ROB,P.END},
    P.DISCARD_ONLY:{P.RESPONSE,P.END},
    P.RESPONSE:{P.TURN,P.REPLACEMENT,P.DISCARD_ONLY,P.SPECIAL,P.END},
    P.ROB:{P.REPLACEMENT,P.TURN,P.END},
    P.SPECIAL:{P.REPLACEMENT,P.TURN,P.DISCARD_ONLY,P.END},
    P.END:{P.READY},P.READY:{P.START,P.FINISHED},P.FINISHED:set(),
}


class StateMachine:
    def __init__(self):
        self.phase=P.START;self.version=0

    def transition(self,target):
        target=P(target)
        if target==self.phase:
            return
        if target not in TRANSITIONS[self.phase]:
            raise RuntimeError(f'非法越南状态转换: {self.phase.value} -> {target.value}')
        self.phase=target;self.version+=1
