"""虹雀 bot 与其它模式隔离性 / 接线测试（PR 把关）。

虹雀启发式 bot 是「高性能罗伯特在虹雀模式的特化实现」（user_id=3），
与国标等其它模式的 user_id=3 各自独立。本测试保证：

1. import 任何虹雀 bot 模块都不会拉入其它模式（game_guobiao 等）——
   否则会互相拖累，破坏其它模式的高性能罗伯特。
2. HongqueGameState 的 user_id 接线正确：2 仍走牌效、3 走虹雀启发式 bot，
   且入口确实来自 heuristic_bot（而非误绑其它实现）。
"""
import importlib
from pathlib import Path
import subprocess
import sys

# 仅虹雀目录内的模块允许被虹雀 bot 依赖。
_ALLOWED = ("server.gamestate.game_hongque",)


def _foreign_game_modules() -> set[str]:
    return {
        m for m in sys.modules
        if m.startswith("server.gamestate.game_")
        and not any(m == a or m.startswith(a + ".") for a in _ALLOWED)
    }


def test_bot_modules_do_not_import_other_modes() -> None:
    # 其它测试在收集阶段可能已经加载别的玩法；用全新解释器验证 bot 的真实依赖。
    script = """
import importlib
import sys

allowed = "server.gamestate.game_hongque"
importlib.import_module(allowed + ".efficiency_bot")
importlib.import_module(allowed + ".heuristic_bot")
foreign = sorted(
    module_name
    for module_name in sys.modules
    if module_name.startswith("server.gamestate.game_")
    and module_name != allowed
    and not module_name.startswith(allowed + ".")
)
if foreign:
    raise AssertionError(f"虹雀 bot 拉入了其它模式模块: {foreign}")
"""
    result = subprocess.run(
        [sys.executable, "-c", script],
        cwd=Path(__file__).resolve().parents[3],
        capture_output=True,
        text=True,
        check=False,
    )
    assert result.returncode == 0, result.stderr or result.stdout


def test_hongque_state_user2_stays_on_efficiency() -> None:
    """user_id=2（牌效罗伯特）在虹雀仍走 efficiency_bot，未被改动。"""
    hgs = importlib.import_module("server.gamestate.game_hongque.get_action")
    efficiency = importlib.import_module("server.gamestate.game_hongque.efficiency_bot")

    assert hgs.choose_turn_plan is efficiency.choose_turn_plan
    assert hgs.choose_claim_plan is efficiency.choose_claim_plan


def test_hongque_state_user3_wired_to_v3_turn() -> None:
    """user_id=3（高性能罗伯特）在虹雀走 heuristic_bot 的出牌入口。"""
    hgs = importlib.import_module("server.gamestate.game_hongque.get_action")
    bot = importlib.import_module("server.gamestate.game_hongque.heuristic_bot")

    assert hgs.choose_turn_plan_v3 is bot.choose_turn_plan
    assert hgs.OpponentView is bot.OpponentView


def test_hongque_state_user3_wired_to_v3_claim() -> None:
    """user_id=3 在虹雀的鸣牌入口同样来自 heuristic_bot。"""
    hgs = importlib.import_module("server.gamestate.game_hongque.get_action")
    bot = importlib.import_module("server.gamestate.game_hongque.heuristic_bot")

    assert hgs.choose_claim_plan_v3 is bot.choose_claim_plan
