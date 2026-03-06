"""
游戏计算服务类
提供线程安全的和牌检查和听牌检查服务
"""
import threading
from typing import List, Set, Tuple
from time import time

# 国标和牌：标准与小林规各独立脚本，外部获取结果后调用实例的剔除方法再 return
try:
    from .guobiao_hepai_check import Chinese_Hepai_Check, PlayerTiles
    from .guobiao_xiaolin_hepai_check import Xiaolin_Hepai_Check
    from .gb_tingpai_check import Chinese_Tingpai_Check
except ImportError:
    from guobiao_hepai_check import Chinese_Hepai_Check, PlayerTiles  # type: ignore
    from guobiao_xiaolin_hepai_check import Xiaolin_Hepai_Check  # type: ignore
    from gb_tingpai_check import Chinese_Tingpai_Check  # type: ignore

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
    - hepai_check: 青雀和牌检查
    - tingpai_check: 青雀听牌检查
    - GetBasePoint: 根据番数计算基础分数
    """
    
    def __init__(self):
        # 线程锁，确保并发安全
        self._lock = threading.RLock()  # 使用可重入锁，支持嵌套调用
        # 国标和牌 / 听牌检查实例（Python 版本）
        self._hepai_check = Chinese_Hepai_Check()
        self._xiaolin_hepai_check = Xiaolin_Hepai_Check()
        self._tingpai_check = Chinese_Tingpai_Check()

    def Qingque_hepai_check(
        self,
        hand_list: List[int],
        tiles_combination: List[str],
        way_to_hepai: List[str],
        get_tile: int,
        debug: bool = False,
    ) -> Tuple[float, List[str]]:
        """
        青雀和牌检查
        
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
            # 从 hand_list 中移除 get_tile（创建副本以避免修改原列表）
            hand_without_tile = hand_list.copy()
            hand_without_tile.remove(get_tile)
            return call_hepai_check(hand_without_tile, tiles_combination, way_to_hepai, get_tile, debug)

    def Qingque_tingpai_check(
        self,
        hand_tile_list: List[int],
        combination_list: List[str],
        debug: bool = False,
    ) -> Set[int]:
        """
        青雀听牌检查
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

    def GB_hepai_check(self, hand_list: List[int], tiles_combination: List,
                   way_to_hepai: List[str], get_tile: int) -> Tuple[int, List[str]]:
        """
        检查和牌；获取结果后调用实例的剔除方法（剔除番值=0）再 return 到服务器。
        """
        with self._lock:
            score, fan_list = self._hepai_check.hepai_check(hand_list, tiles_combination, way_to_hepai, get_tile)
            return self._hepai_check.filter_zero_value_fans(score, fan_list)

    def GB_xiaolin_hepai_check(self, hand_list: List[int], tiles_combination: List,
                   way_to_hepai: List[str], get_tile: int) -> Tuple[int, List[str]]:
        """小林规和牌检查；返回包含 0 分番在内的全部番种，由客户端或上层按需过滤。"""
        with self._lock:
            score, fan_list = self._xiaolin_hepai_check.hepai_check(hand_list, tiles_combination, way_to_hepai, get_tile)
            return score, fan_list
    
    def GB_tingpai_check(self, hand_tile_list: List[int], combination_list: List) -> Set[int]:
        """
        检查听牌
        Args:
            hand_tile_list: 手牌列表
            combination_list: 组合牌列表
        Returns:
            等待牌的集合
        """
        with self._lock:
            return self._tingpai_check.tingpai_check(hand_tile_list, combination_list)


if __name__ == "__main__":
    # 测试代码
    # [16, 45, 34, 16, 47, 21, 31, 38, 22, 31, 39, 24, 23]
    test_save = [["k46","s36"],[17,18,19,24,25,33,33,23], 23, ["点和"]]  # 10

    way_to_hepai = test_save[3]
    hepai_tiles = test_save[2]
    tiles_list = test_save[1]
    combination_list = test_save[0]

    # 开始测试
    for i in range(10):
        check_service = GameCalculationService()
        time_start = time()
        result = check_service.Qingque_hepai_check(tiles_list, combination_list,way_to_hepai,hepai_tiles)
        result = check_service.Qingque_tingpai_check(tiles_list, combination_list)
        print("最终结果(返回最大的牌型):", result)
        time_end = time()
        print("测试用时：", time_end - time_start, "秒")
