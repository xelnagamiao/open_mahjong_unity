import pytest

from .state_machine import HongKongPhase as P, HongKongStateMachine, TRANSITIONS


@pytest.mark.parametrize("source",list(P))
@pytest.mark.parametrize("target",list(P))
def test_every_phase_pair_accepts_only_documented_transitions(source,target):
    machine = HongKongStateMachine()
    # Seed an isolated phase to test its outbound edges, without a restore API
    # that production code could use to bypass transition validation.
    machine.phase = source
    if source == target:
        machine.transition(target)
        assert machine.version == 0 and machine.history == []
    elif target in TRANSITIONS[source]:
        machine.transition(target)
        assert machine.phase == target and machine.version == 1
        assert machine.history == [(source,target)]
    else:
        with pytest.raises(RuntimeError):
            machine.transition(target)
        assert machine.phase == source and machine.version == 0


def test_unknown_phase_does_not_mutate():
    machine = HongKongStateMachine()
    with pytest.raises(ValueError):
        machine.transition("waiting_for_magic")
    assert machine.phase == P.STARTING
