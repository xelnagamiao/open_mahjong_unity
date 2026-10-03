"""One action budget, retained while choosing/cancelling a ready discard."""
import math
import time


class ActionClock:
    def __init__(self, step, bank):
        self.step, self.bank = max(0, step), max(0, bank)
        self.elapsed = 0.0
        self.started = None

    def start(self, step, bank):
        if self.started is None:
            if self.elapsed == 0:
                self.step, self.bank = max(0, step), max(0, bank)
            self.started = time.monotonic()

    def used(self):
        return self.elapsed + (max(0, time.monotonic()-self.started) if self.started is not None else 0)

    def stop(self):
        self.elapsed = self.used()
        self.started = None

    def remaining(self):
        return max(0, self.step+self.bank-self.used())

    def display(self):
        elapsed = self.used()
        return (math.ceil(max(0, self.bank-max(0, elapsed-self.step))),
                math.ceil(max(0, self.step-elapsed)))
