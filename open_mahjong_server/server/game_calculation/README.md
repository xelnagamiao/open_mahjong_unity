# `game_calculation` 统一多核算牌约定（Python 3.14+）

本目录承载全平台麻将规则的听牌、和牌与番种计算。项目基准运行环境为 **Python 3.14+**，所有规则统一遵循以下规范：

## 1. 核心与池子分配

- 在 `server/test_config.py` / `server/local_config.py` 中通过单一配置项 `cpu_budget = None` 控制 CPU 核数预算（`None` = 自动探测当前机器/容器可用核数，支持 1 核 ~ 64+ 核动态分配）。
- 运行时由 `server/runtime/cpu_allocation.py` 统一拆分为两个物理隔离的算力池：
  1. **`CalcExecutor`（`calc_executor.py`）**：专职实时听牌/和牌判定。多核使用 Python 3.14 标准库进程内独立 GIL 池（`InterpreterPoolExecutor`），单核自动走 `inline`。
  2. **`BotExecutor`（`gamestate/public/ai/bot_executor.py`）**：专职机器人决策（`ProcessPoolExecutor(spawn)`），与实时算牌彻底分池，避免机器人长考堵塞真人对局。

## 2. 新增或修改规则的唯一标准写法

- **禁止**在 `gamestate` 中直接借用 `bot_executor` 执行非机器人实时听/和牌判定。
- **统一入口**：
  - 将规则的纯 Python 听牌/和牌函数注册到 `registry.py`（入参/出参只用基础不可变类型 `int`/`str`/`bool`/`float`/`tuple` 或 JSON 字符串，保证跨独立 GIL 零拷贝兼容）。
  - 在 `gamestate` 中统一通过 `server/gamestate/public/async_tingpai.py`（`async_refresh_waiting_tiles` / `sync_refresh_waiting_tiles_cached`）或 `AsyncCalculationService` 调用，自动享受 **L1 玩家手牌脏标记缓存** 与 `CalcExecutor` 多核卸载。
- **青雀（`qingque/`）**：已采用纯 Python 实现，原 C# `Qingque13Calc.dll` 与 `qingque13_bridge.py` 存档于 `Qingque13Calc/archive/`，无需 `.NET` / `pythonnet` 依赖。
