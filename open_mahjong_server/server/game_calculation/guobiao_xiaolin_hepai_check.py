# -*- coding: utf-8 -*-
"""
国标麻将 - 小林规和牌检查
独立脚本，继承国标检查逻辑，仅修改番数价值表；阻挡字典重写为不求人不阻挡门前清。
"""
from typing import Dict, List, Tuple

from .guobiao_hepai_check import Chinese_Hepai_Check, PlayerTiles


class Xiaolin_Hepai_Check(Chinese_Hepai_Check):
    """小林规和牌检查，继承国标检查逻辑，仅修改番数价值表；不求人只阻挡自摸，不阻挡门前清。"""
    # 重写阻挡字典：不求人仅阻挡自摸，不阻挡门前清（与国标一致但显式保留门前清）
    repel_model_dict: Dict[str, list] = {
        **{k: list(v) for k, v in Chinese_Hepai_Check.repel_model_dict.items()},
        "buqiuren": ["zimo"],
    }
    count_model_dict: Dict[str, int] = {
        "dasixi": 88, "dasanyuan": 64, "lvyise": 88, "jiulianbaodeng": 88, "sigang": 88,
        "lianqidui": 88, "shisanyao": 88,
        "qingyaojiu": 64, "xiaosixi": 64, "xiaosanyuan": 32, "ziyise": 64, "sianke": 64, "yiseshuanglonghui": 64,
        "yisesitongshun": 64, "yisesijiegao": 64, "yisesibugao": 32, "sangang": 32, "hunyaojiu": 32,
        "qiduizi": 24, "qixingbukao": 24, "quanshuangke": 24,
        "qingyise": 32, "yisesantongshun": 24, "yisesanjiegao": 24, "quanda": 24, "quanzhong": 24, "quanxiao": 24,
        "qinglong": 16, "sanseshuanglonghui": 24, "yisesanbugao": 16, "quandaiwu": 16, "santongke": 16, "sananke": 16,
        "quanbukao": 12, "zuhelong": 12, "dayuwu": 12, "xiaoyuwu": 12, "sanfengke": 24,
        "hualong": 8, "tuibudao": 8, "sansesantongshun": 8, "sansesanjiegao": 8, "wufanhe": 8, "miaoshouhuichun": 8, "haidilaoyue": 8,
        "gangshangkaihua": 8, "qiangganghe": 8, "pengpenghe": 6, "hunyise": 12, "sansesanbugao": 6, "wumenqi": 6, "quanqiuren": 6, "shuangangang": 8, "shuangjianke": 12,
        "quandaiyao": 6, "buqiuren": 0, "shuangminggang": 4, "hejuezhang": 0, "jianke": 2, "quanfengke": 2, "menfengke": 2, "menqianqing": 2,
        "pinghe": 2, "siguiyi": 2, "shuangtongke": 2, "shuanganke": 2, "angang": 2, "duanyao": 2, "yibangao": 2, "xixiangfeng": 1,
        "lianliu": 1, "laoshaofu": 1, "yaojiuke": 0, "minggang": 1, "queyimen": 1, "wuzi": 1, "bianzhang": 1,
        "qianzhang": 1, "dandiaojiang": 1, "zimo": 0, "huapai": 1, "mingangang": 5,
    }

    def filter_zero_value_fans(self, fan_score: int, fan_count_list: List[str]) -> Tuple[int, List[str]]:
        """剔除番值=0 的番种后再返回；若无番或剩余番值全为 0 则返回无番和。"""
        cn_to_value = {
            self.eng_to_chinese_dict[k]: self.count_model_dict[k]
            for k in self.eng_to_chinese_dict
            if k in self.count_model_dict
        }
        zero_value_cn = {cn for cn, v in cn_to_value.items() if v == 0}
        wufanhe_value = cn_to_value.get("无番和", 8)

        def parse_item(item: str) -> Tuple[str, int]:
            if "*" in item:
                parts = item.split("*", 1)
                base = parts[0].strip()
                cnt = int(parts[1].strip()) if len(parts) > 1 and parts[1].strip() else 1
                return base, cnt
            return item.strip(), 1

        filtered_list: List[str] = []
        effective_score = 0
        for item in fan_count_list:
            base_cn, count = parse_item(item)
            if base_cn in zero_value_cn:
                continue
            filtered_list.append(item)
            effective_score += cn_to_value.get(base_cn, 0) * count

        if not filtered_list or effective_score == 0:
            return wufanhe_value, ["无番和"]
        return effective_score, filtered_list
