# 测试约定：启发式 AI 自战（self-play）测试必须排除在全量测试之外

## 为什么

高性能罗伯特（国标启发式 AI，`guobiao_heuristic_ai.py`）的自战对局测试（如
`server/gamestate/public/ai/test_guobiao_heuristic_smoke.py`）是**调参 / 找参数用的实验**，
不是回归测试。单场全庄自战优化后约 30 秒，63 全庄约 30-40 分钟。

**自战脚本（如实验分支的 `hongque_selfplay.py`、`_bench_*.py`）不是测试，pytest 不会收集，无需处理。**
需要处理的是**以 `test_*` 命名、内容却是跑自战对局的测试文件**——它们会被全量 pytest
收集并真实执行，一跑就是几十分钟（63 全庄约 30-40 分钟）。

## 规则（必须遵守）

**所有启发式算法的自战测试必须打上 `@pytest.mark.selfplay` 标记**，否则会被全量测试误收集：

```python
@pytest.mark.selfplay
@pytest.mark.slow          # 长耗时类自战额外保留 slow
def test_xxx_self_play():
    ...
```

新增任何启发式 AI 自战测试时，**务必**同时添加 `@pytest.mark.selfplay`。

## 机制

`server/pytest.ini` 已配置默认排除：

```ini
[pytest]
addopts = -m "not selfplay"
markers =
    slow: long-running full self-play smoke (63 quanzhuang); skip by default in short CI
    selfplay: heuristic AI self-play regression/lab runs; excluded from full test runs by default (addopts -m "not selfplay")
```

因此：

- **全量测试**（`pytest` 不带 `-m`，且**从 open_mahjong_server 根目录带路径运行**，如 `pytest server/`）：自战测试被自动排除，不会执行。
- ⚠️ **注意**：pytest 只会在**带路径参数**（如 `pytest server/...`）时向上找到 `server/pytest.ini`。若在根目录**裸跑 `pytest`（无参数）**，不会加载该配置，排除不生效——请始终带路径运行（仓库现有文档均为此用法）。
- **手动运行自战测试**：`-m` 在命令行出现时会**覆盖** `addopts`，必须显式加 `-m selfplay`：

  ```bash
  python -m pytest server/gamestate/public/ai/test_guobiao_heuristic_smoke.py -v -m selfplay
  python -m pytest server/gamestate/public/ai/test_guobiao_heuristic_smoke.py -v -k two_quanzhuang -m selfplay
  ```

## 检查清单（Review / PR 时）

- [ ] 新自战测试是否加了 `@pytest.mark.selfplay`？
- [ ] 全量 pytest 的 collect 结果里是否没有 `test_*_smoke` 自战测试？

可用 `pytest --collect-only` 确认自战测试被排除，或用
`pytest --collect-only -m selfplay` 确认它们仍能被手动收集。
