"""
游戏计算服务类
提供线程安全的和牌检查和听牌检查服务
"""
import threading
from typing import List, Set, Tuple
from time import time

# 传统国标 Python 实现
if __name__ == "__main__":
    from gb_hepai_check import Chinese_Hepai_Check
    from gb_tingpai_check import Chinese_Tingpai_Check
else:
    from .gb_hepai_check import Chinese_Hepai_Check
    from .gb_tingpai_check import Chinese_Tingpai_Check

# Qingque13 C# 桥接模块
try:
    if __name__ == "__main__":
        from qingque13_bridge import call_hepai_check, call_tingpai_check, call_get_base_point
    else:
        from .qingque13_bridge import call_hepai_check, call_tingpai_check, call_get_base_point
    _QQ13_AVAILABLE = True
except ImportError:
    _QQ13_AVAILABLE = False
    call_hepai_check = None  # type: ignore
    call_tingpai_check = None  # type: ignore
    call_get_base_point = None  # type: ignore


class GameCalculationService:
    """
    游戏计算服务类
    
    提供以下接口：
    - hepai_check: 青雀13张麻将和牌检查
    - tingpai_check: 青雀13张麻将听牌检查
    - GetBasePoint: 根据番数计算基础分数
    """
    
    def __init__(self):
        # 线程锁，确保并发安全
        self._lock = threading.RLock()  # 使用可重入锁，支持嵌套调用
        # 国标和牌 / 听牌检查实例（Python 版本）
        self._hepai_check = Chinese_Hepai_Check()
        self._tingpai_check = Chinese_Tingpai_Check()

    def hepai_check(
        self,
        hand_list: List[int],
        tiles_combination: List[str],
        way_to_hepai: List[str],
        get_tile: int,
        debug: bool = False,
    ) -> Tuple[float, List[str]]:
        """
        青雀 13 张麻将 和牌检查
        
        Args:
            hand_list: 手牌列表（C# 中为 List<int>）
            tiles_combination: 明刻/明杠/顺子组合（C# 中为 List<string>，如 "k11"、"s12" 等）
            way_to_hepai: 和牌方式列表（C# 中为 List<string>，如 "自摸"、"海底捞月" 等）
            get_tile: 和牌牌编号
            debug: 是否开启 C# 侧调试日志
        Returns:
            (fan_score, fan_names) -> (番数, 中文番名列表)
        """
        if not _QQ13_AVAILABLE or call_hepai_check is None:
            raise RuntimeError(
                "Qingque13 C# 桥接模块未可用，请检查：\n"
                "1) 是否安装了 pythonnet: pip install pythonnet\n"
                "2) 是否已编译并放置了 Qingque13Calc.dll"
            )
        
        with self._lock:
            return call_hepai_check(hand_list, tiles_combination, way_to_hepai, get_tile, debug)

    def tingpai_check(
        self,
        hand_tile_list: List[int],
        combination_list: List[str],
        debug: bool = False,
    ) -> Set[int]:
        """
        青雀 13 张麻将 听牌检查
        
        Args:
            hand_tile_list: 手牌列表（List[int]）
            combination_list: 已完成组合列表（List[str]）
            debug: 是否让 C# 侧输出调试日志
        Returns:
            等待牌集合（Python set[int]）
        """
        if not _QQ13_AVAILABLE or call_tingpai_check is None:
            raise RuntimeError(
                "Qingque13 C# 桥接模块未可用，请检查：\n"
                "1) 是否安装了 pythonnet: pip install pythonnet\n"
                "2) 是否已编译并放置了 Qingque13Calc.dll"
            )
        
        with self._lock:
            return call_tingpai_check(hand_tile_list, combination_list, debug)

    def GetBasePoint(self, fan: float) -> int:
        """
        根据番数计算基础分数
        
        Args:
            fan: 番数
        Returns:
            基础分数（int）
        """
        if not _QQ13_AVAILABLE or call_get_base_point is None:
            raise RuntimeError(
                "Qingque13 C# 桥接模块未可用，请检查：\n"
                "1) 是否安装了 pythonnet: pip install pythonnet\n"
                "2) 是否已编译并放置了 Qingque13Calc.dll"
            )
        
        with self._lock:
            return call_get_base_point(fan)


if __name__ == "__main__":
    # 测试代码
    test_save = [["G21", "g18"], [32, 32, 32, 25, 26, 27, 27], 27, ["自摸"]]  # 10

    way_to_hepai = test_save[3]
    hepai_tiles = test_save[2]
    tiles_list = test_save[1]
    combination_list = test_save[0]

    # 开始测试
    for i in range(10):
        check_service = GameCalculationService()
        time_start = time()
        result = check_service.hepai_check(tiles_list, combination_list, way_to_hepai, hepai_tiles)
        print("最终结果(返回最大的牌型):", result)
        time_end = time()
        print("测试用时：", time_end - time_start, "秒")
