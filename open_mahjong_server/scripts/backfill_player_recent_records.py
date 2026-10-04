"""独立执行玩家面板历史回填；正常启动 main.py 时也会自动执行。"""
import argparse
import logging
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))


def main():
    parser = argparse.ArgumentParser(description="恢复全部玩家各规则十场顺位与国标历史最高番；支持中断续跑。")
    parser.add_argument("--batch-size", type=int, default=100)
    args = parser.parse_args()
    if args.batch_size < 1:
        parser.error("--batch-size 必须大于零")
    from server.database.db_manager import DatabaseManager
    from server.database.player_recent_records import backfill_player_recent_records
    try:
        from server.local_config import Config
    except ModuleNotFoundError as error:
        if error.name != "server.local_config":
            raise
        from server.test_config import Config
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    manager = DatabaseManager(host=Config.host, user=Config.user, password=Config.password,
                              database=Config.database, port=Config.port)
    try:
        result = backfill_player_recent_records(manager, args.batch_size)
        return 0 if result["complete"] else 1
    finally:
        if manager.pool:
            manager.pool.closeall()


if __name__ == "__main__":
    raise SystemExit(main())
