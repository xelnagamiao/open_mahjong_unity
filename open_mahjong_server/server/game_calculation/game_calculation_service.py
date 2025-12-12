"""
游戏计算服务类
提供线程安全的和牌检查和听牌检查服务
"""
import threading
from typing import List, Set, Tuple
from .gb_hepai_check import Chinese_Hepai_Check, PlayerTiles
from .gb_tingpai_check import Chinese_Tingpai_Check


class GameCalculationService:
    def __init__(self):
        # 线程锁，确保并发安全
        self._lock = threading.RLock()  # 使用可重入锁，支持嵌套调用
        # 和牌检查实例
        self._hepai_check = Chinese_Hepai_Check()
        # 听牌检查实例
        self._tingpai_check = Chinese_Tingpai_Check()
    
    def GB_hepai_check(self, hand_list: List[int], tiles_combination: List, 
                   way_to_hepai: List[str], get_tile: int) -> Tuple[int, List[str]]:
        """
        检查和牌
        Args:
            hand_list: 手牌列表
            tiles_combination: 组合牌列表
            way_to_hepai: 和牌方式列表
            get_tile: 获得的牌
        Returns:
            (分数, 番种列表) 元组
        """
        with self._lock:
            return self._hepai_check.hepai_check(hand_list, tiles_combination, way_to_hepai, get_tile)
    
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

