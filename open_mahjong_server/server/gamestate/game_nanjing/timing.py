from dataclasses import dataclass
import math


@dataclass
class ActionClock:
    bank: float
    step: float
    started_at: float | None = None
    received_at: float | None = None
    minimum_duration: float = 0.0

    def start(self,now):
        if self.started_at is None:self.started_at=now

    @property
    def deadline(self):
        return None if self.started_at is None else self.started_at+max(self.bank+self.step,self.minimum_duration)

    def remaining_bank(self,now):
        elapsed=0 if self.started_at is None else max(0,now-self.started_at)
        return max(0,self.bank-max(0,elapsed-self.step))

    def parts(self,now):
        elapsed=0 if self.started_at is None else max(0,now-self.started_at)
        total=math.ceil(max(0,self.bank+self.step-elapsed))
        step=min(total,math.ceil(max(0,self.step-elapsed)))
        return total-step,step
