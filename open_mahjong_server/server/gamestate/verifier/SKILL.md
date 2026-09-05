---
name: game-verifier
description: >-
  Runs the local Guobiao game-and-record visual verifier (Python lab plus /2d/verifier).
  Use when verifying GuobiaoGameState, action ticks, Unity client protocol, legal
  action branches, or writing verifier harness tests. Always check the Unity/sim-client
  contract before assuming the simulator matches the client.
disable-model-invocation: true
---

# 国标对局验证器

权威说明在本目录。Cursor 发现入口见仓库 `.cursor/skills/game-verifier/SKILL.md`。

## 每次验证前（必须）

在 `open_mahjong_server` 下运行：

```powershell
python -m server.gamestate.verifier.check_contract
```

失败则停止。先改 [`sim_client/protocol.json`](sim_client/protocol.json) 对齐 Unity `AllowHandActionCheck` / `allowOtherActionCheck`，再：

```powershell
python -m server.gamestate.verifier.check_contract --update-lock
```

不要假设模拟客户端仍等于当前 Unity。lock 里的 `git_head` 只是备注；真正挡漏验的是 **Unity 协议面文件哈希 + 白名单**。

契约文件见 `protocol.json` 的 `unity_contract_files`。

## 启动

两个进程：

```powershell
# 1) lab，只绑本机
cd open_mahjong_server
python -m server.gamestate.verifier.lab

# 2) 网站
cd open_mahjong_web/client
npm run dev
```

打开 `http://localhost:5173/2d/verifier`（不进正式导航，不部署）。

## 用法

- 选场景开局（`scenarios/*.json`，含 `guobiao_debug.py` 四套牌例）。
- 只点当前 `action_list` 经 Unity 白名单过滤后的按钮；切牌点可切手牌。
- 非法动作 API 返回 400，不会入队，避免 `wait_action` 等到超时卡死。
- 回退：操作日志下标 / 书签。实现是 **停掉 loop，按种子重放前缀**，不要 deepcopy GameState。
- 超时按钮才会走超时分支；默认局时/步时拉得很长。
- 听牌工具调用服务端 `GB_tingpai_check`，入参和返回记在 `tool_calls`。

## 和无头测试的关系

- 规则单元测（`action_check`、`test_tactical_claim` 等）保持独立。
- 验证器回归：`python -m pytest server/gamestate/verifier/test_harness.py -q`
- 自战 smoke 仍 `@pytest.mark.selfplay`，不要改成默认 CI。

## 不要做

- 不要把虹雀/立直接到本期 Session。
- 不要在生产 nginx 暴露 8099。
- 不要无预算穷举 `legal.branches`。
