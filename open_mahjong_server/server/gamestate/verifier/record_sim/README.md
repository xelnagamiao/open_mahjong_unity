# 牌谱跳转测试器（record_sim）

无头复刻 Unity `GameRecordManager.ApplyActionToRecordState`（[`GameRecordManager.GotoAction.cs`](../../../../../open_mahjong_unity/Assets/Scripts/GameScene/Record/GameRecordManager.GotoAction.cs)）。用来逐步/跳转推演本地 jsonc 牌谱，**不是**国标 `/lab` 组件台。组件台说明见上一级 [`../SKILL.md`](../SKILL.md)。

## 怎么跑

在 `open_mahjong_server` 下：

```powershell
.\.venv\Scripts\python.exe -m pytest server/gamestate/verifier/test_record_replay.py -q
```

venv 若没有 pytest：

```powershell
.\.venv\Scripts\python.exe -m pip install pytest
```

测的是 [`public/game_record_example_*.jsonc`](../../public/) 全规则示例，外加内联小局（川麻杠后从头摸、台湾补花从尾取、虹雀加杠/补牌等）。

## 对照 C# 翻译

Python 文件是 C# 的行为翻译，不是另一套规则引擎。改 Unity 回放时同步改这里。

| Python | 对照的 C# |
|---|---|
| [`goto_action.py`](goto_action.py) `RecordSim.apply_tick` | `GameRecordManager.GotoAction.cs` 的 `ApplyActionToRecordState` |
| 同上，川麻血战/杠后摸牌 | [`Rules/Sichuan/SichuanReplay.cs`](../../../../../open_mahjong_unity/Assets/Scripts/GameScene/Rules/Sichuan/SichuanReplay.cs) |
| 同上，台湾死墙/`state` | [`Rules/Taiwan/TaiwanReplay.cs`](../../../../../open_mahjong_unity/Assets/Scripts/GameScene/Rules/Taiwan/TaiwanReplay.cs) |
| 同上，立直场供 | [`Rules/Riichi/RiichiReplay.cs`](../../../../../open_mahjong_unity/Assets/Scripts/GameScene/Rules/Riichi/RiichiReplay.cs) |
| [`decoder.py`](decoder.py) | [`GameRecordJsonDecoder.cs`](../../../../../open_mahjong_unity/Assets/Scripts/GameScene/Record/GameRecordJsonDecoder.cs) |
| [`meld_codec.py`](meld_codec.py) | [`GameRecordMeldCodec.cs`](../../../../../open_mahjong_unity/Assets/Scripts/GameScene/Record/GameRecordMeldCodec.cs) |
| [`hu_hand.py`](hu_hand.py) | [`RecordHuHandBuilder.cs`](../../../../../open_mahjong_unity/Assets/Scripts/GameScene/Record/RecordHuHandBuilder.cs) |
| [`flags.py`](flags.py) | [`RuleManifest.cs`](../../../../../open_mahjong_unity/Assets/Scripts/GameScene/Contracts/RuleManifest.cs) 牌谱回放旗标 + 各族 `*RuleBootstrap` |

对照版本以**工作区当前 C# 源文件**为准（Manifest 旗标，而不是规则名 `startswith`）。最近一次提交触及这些 C# 文件的是 `70f6907e`；未提交的回放改动也必须先改 Python 再跑上面的测试。

`flags.py` 必须与 Bootstrap 登记一致：

| 旗标 | 规则 | C# 行为 |
|---|---|---|
| `KongReplacementFromFront` | 四川 | `gd` 与普通摸牌一样从头取 |
| `JiagangExtendsLastMeld` | 虹雀 | 加杠追加到最后一组副露 mask |
| `FaceDownAnkan` | 长沙 | 暗杠 mask 四张全暗 |
| `PeekAnkan` | 日麻 / 四川 | 暗杠两侧暗、中间明 |
| `RecordHuTileTickIndex` | 古典 7，其余 5 | `hu_*` 和牌张字段下标 |
| `InfersDingqueFromDiscards` | 四川 | 切牌后按手牌+副露缺一门推断定缺 |

## 测试须知

- 示例必须覆盖该规则特殊 tick，不能只「不抛异常」。`test_example_jsonc_replays_without_throwing` 会扫全部 `game_record_example_*.jsonc`，逐步推演与 `goto_action` 终局快照必须一致。
- **四川定缺**：服务端牌谱没有独立 dingque tick。回放对齐 C# `RecordChongHintCalculator.TryInferRecordDingqueSuit`：切牌后若手牌+副露恰好缺一门，写入 `dingque_suit`。四川示例子 `p0` 起手无条，切一张后应为缺条（3）。
- **虹雀补牌**：服务端 `supplement` 写成 Unity tick `bd`。`tiles_list` 是剩余牌山的反转，补牌从岭上方向取（首张 `bd` 走 double 槽，不是 `tiles_list[0]`）。
- **台湾**：`bh` / `bd` / `state ready`；死墙从尾消耗。
- **古典**：和牌张在 tick 下标 7（`RecordHuTileTickIndex`）。
- 新增规则：在 `flags.py` 登记旗标、补一份 `game_record_example_{rule}.jsonc`、保证 `remember_local_record_detail` 挂在终局路径上。
