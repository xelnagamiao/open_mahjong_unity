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
            self.prepare(step, bank)
            self.started = time.monotonic()

    def prepare(self, step, bank):
        if self.started is None and self.elapsed == 0:
            self.step, self.bank = max(0, step), max(0, bank)

    @staticmethod
    def now():
        return time.monotonic()

    def used(self, at=None):
        now = self.now() if at is None else at
        return self.elapsed + (max(0, now-self.started) if self.started is not None else 0)

    def stop(self, at=None):
        self.elapsed = self.used(at)
        self.started = None

    def remaining(self, at=None):
        return max(0, self.step+self.bank-self.used(at))

    def bank_remaining(self):
        # Retain fractions on the server; rounding is only a wire/UI concern.
        return max(0, self.bank-max(0, self.used()-self.step))

    def step_remaining(self):
        return max(0, self.step-self.used())

    def display(self):
        elapsed = self.used()
        return (math.ceil(self.bank_remaining()),
                math.ceil(max(0, self.step-elapsed)))
