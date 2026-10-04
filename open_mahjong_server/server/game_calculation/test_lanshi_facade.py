"""Public service and historical direct-script entry points keep native v4 rules."""
import importlib.util
from pathlib import Path
import sys

import pytest

from .guobiao_lanshi_hepai_check import Lanshi_Hepai_Check


@pytest.mark.parametrize("hand,expected", [
    ([11,11,11,12,13,14,15,16,17,18,19,19,19], set(range(11,20))),
    ([11,14,17,22,25,28,33,36,39,41,41,41,42], set()),
])
def test_service_wait_interface_uses_blue_shapes(hand, expected):
    original=list(hand)
    assert Lanshi_Hepai_Check.tingpai_check(hand, []) == expected
    assert hand == original


@pytest.mark.parametrize("score,expected", [(27,27), (101,100)])
def test_historical_fan_filter_preserves_callers_and_cap(score, expected):
    labels=["一般高*0", "一般高*1", "自摸"]
    assert Lanshi_Hepai_Check.filter_zero_value_fans(score, labels) == (expected,["一般高*1","自摸"])
    assert labels == ["一般高*0", "一般高*1", "自摸"]


def test_direct_script_import_keeps_original_hand_score(monkeypatch):
    """calculation_service supports importing sibling modules outside a package."""
    path=Path(__file__).with_name("guobiao_lanshi_hepai_check.py")
    monkeypatch.syspath_prepend(str(path.parent))
    previous=sys.modules.pop("lanshi_v4", None)
    try:
        spec=importlib.util.spec_from_file_location("lanshi_standalone_compat", path)
        module=importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        hand=[11,11,13,14,15,23,23,24,24,25,25,32,33,34]
        assert module.Lanshi_Hepai_Check().hepai_check(hand, [], ["自摸"], 34) == (
            5,["一般高*1","门前清","喜相逢*1","自摸"])
    finally:
        sys.modules.pop("lanshi_v4", None)
        if previous is not None:
            sys.modules["lanshi_v4"]=previous
