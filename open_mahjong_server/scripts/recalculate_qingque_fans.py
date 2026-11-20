"""一次性按原始牌谱重算 MMCR 14 番种统计（不改牌谱、比分或评级）。

在 open_mahjong_server 下运行：
    python scripts/recalculate_qingque_fans.py          # 只读预览
    python scripts/recalculate_qingque_fans.py --apply  # 备份、重算并原子写入完成标记

使用 server.local_config.Config，未配置时使用 server.test_config.Config。
也可通过环境变量 QINGQUE_RECALCULATE_DATABASE_URL 指定连接。
保留原有字段名；补回漏计的十三幺，清除已取消的风牌五对、风牌四对。
重建逐局 fan_stats 和已结束统计日的青雀汇总；今日数据仍由日结发布。
原谱缺失或玩家身份冲突时整体回滚；重复 --apply 读取完成标记后退出。
备份保存在 statistics_fan_backfill_backup，以本脚本 MIGRATION_KEY 区分。
"""
import argparse
import json
import os
from pathlib import Path
import sys

import psycopg2

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from server.database.statistics_fan_backfill import backfill

MIGRATION_KEY = "qingque_mmcr14_fans_v1"


def recalculate(conn, *, apply=False, batch_size=200):
    return backfill(conn, apply=apply, sub_rules=["qingque/standard"],
                    batch_size=batch_size, migration_key=MIGRATION_KEY)


def connect():
    dsn = os.environ.get("QINGQUE_RECALCULATE_DATABASE_URL")
    if dsn:
        return psycopg2.connect(dsn)
    try:
        from server.local_config import Config
    except ModuleNotFoundError as exc:
        if exc.name != "server.local_config":
            raise
        from server.test_config import Config
    return psycopg2.connect(host=Config.host, port=Config.port, user=Config.user,
                            password=Config.password, database=Config.database)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--apply", action="store_true", help="提交重算结果；默认只读预览")
    parser.add_argument("--batch-size", type=int, default=200)
    args = parser.parse_args()
    conn = connect()
    try:
        report = recalculate(conn, apply=args.apply, batch_size=args.batch_size)
        print(json.dumps(report, ensure_ascii=False, indent=2))
        return 1 if report["error_count"] else 0
    finally:
        conn.close()


if __name__ == "__main__":
    raise SystemExit(main())
