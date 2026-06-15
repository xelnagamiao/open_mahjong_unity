# -*- coding: utf-8 -*-
"""
国标麻将 - K神规则和牌检查
独立脚本，逻辑与 guobiao_hepai_check-kshen.py 保持一致。
"""
import importlib.util
from pathlib import Path
from typing import List, Tuple

_kshen_module_path = Path(__file__).resolve().parent / "guobiao_hepai_check-kshen.py"
_spec = importlib.util.spec_from_file_location("guobiao_hepai_check_kshen_module", _kshen_module_path)
_kshen_module = importlib.util.module_from_spec(_spec)
assert _spec.loader is not None
_spec.loader.exec_module(_kshen_module)


class Kshen_Hepai_Check(_kshen_module.Chinese_Hepai_Check):
    """K神规则和牌检查；fan_count_output 内已过滤 0 分番并应用复合 100 封顶。"""

    def filter_zero_value_fans(self, fan_score: int, fan_count_list: List[str]) -> Tuple[int, List[str]]:
        return fan_score, fan_count_list
