---
name: game-verifier
description: >-
  Runs the local Guobiao Unity-component lab (Python Session + /lab).
  Use when verifying GuobiaoGameState, WS→Unity component fields, legal
  action branches, or writing lab harness tests. Always check the Unity/sim-client
  contract before assuming the simulator matches the client.
disable-model-invocation: true
---

# 国标 Unity 组件集成测试台

牌谱跳转无头测试器（复刻 `ApplyActionToRecordState`、跑本地 jsonc）不在本文件：见 [`record_sim/README.md`](record_sim/README.md)。

权威说明在本目录。Cursor 发现入口见仓库 `.cursor/skills/game-verifier/SKILL.md`。

这是 **组件模拟器**：中间牌桌复制 2D 牌谱阅览的 `MahjongScene`（Pixi 牌面、河牌、副露、吃碰按钮），状态只读 Unity 组件快照翻译，不跑 `recordReplay.ts` / `MahjongScene` 当对局引擎。不要引用 `/2d/verifier`。

## 每次验证前（必须）

在 `open_mahjong_server` 下运行：

```powershell
.\.venv\Scripts\python.exe -m server.gamestate.verifier.check_contract
```

失败则停止。先改 [`sim_client/protocol.json`](sim_client/protocol.json) 对齐 Unity `AllowHandActionCheck` / `allowOtherActionCheck`，再：

```powershell
.\.venv\Scripts\python.exe -m server.gamestate.verifier.check_contract --update-lock
```

不要假设模拟客户端仍等于当前 Unity。lock 里的 `git_head` 只是备注；真正挡漏验的是 **Unity 协议面文件哈希 + 白名单**。

契约文件见 `protocol.json` 的 `unity_contract_files`。

## 启动

两个进程（lab 必须用 venv，系统 Python 通常没有 fastapi）：

```powershell
# 1) lab，只绑本机
cd open_mahjong_server
.\.venv\Scripts\python.exe -m server.gamestate.verifier.lab

# 2) 网站
cd open_mahjong_web/client
npm run dev
```

打开 `http://localhost:5173/lab`（不进正式导航，不部署）。Vite 把 `/verifier-api` 代理到 `127.0.0.1:8099`。

## 用法

- 左侧按规则分组。**打开**只开到第一问；**下一步 / 播放**按序推进；**跑完 / 跑本规则**才自动打到终局。点选不自动跑。
- 中间是 2D 牌谱阅览同款 `MahjongScene`。右下角有他家手牌 / 铳张 / 牌山 / 计分板。逐步测试可点合法分支（含他家切牌）。跑完结论条在牌桌左上角。
- 右侧手风琴用 Unity 脚本名（`NormalGameStateManager` / `ActionButton` 等）。
- 超过 50 条批量会确认。默认 turbo，排除 selfplay。
- 战鸣函数测不创建完整牌局：把 `broadcast_do_action` kwargs 译成 `gamestate/guobiao/do_action` 灌进 UnitySim。
- 真 `GuobiaoGameState` 场景用启发式或简单策略立刻入队，直到终局或 `max_steps`。
- 回退仍是停 loop、按种子重放前缀，不要 deepcopy GameState。
- 听牌工具调用服务端 `GB_tingpai_check`（`TipsSim` 在本家 `do_action` 后也会调）。

## 和无头测试的关系

- 规则单元测保持可单独跑：`python -m pytest server/gamestate/public/test_tactical_claim.py -q`
- 测试台回归：`python -m pytest server/gamestate/verifier/test_harness.py -q`
- 自战 smoke 仍 `@pytest.mark.selfplay`，不要改成默认 CI，也不要默认跑 63 全庄。

## 不要做

- 不要把虹雀/立直接到本期 Session。
- 不要在生产 nginx 暴露 8099。
- 不要无预算穷举 `legal.branches`。
- 不要把 FakeGS 战鸣测改写成完整四家牌局。
