# 客户端新增麻将规则 Skill

> 本文档总结了在 Open Mahjong Unity 客户端（C# / Unity）中新增一种麻将规则所需修改的全部文件、修改内容与注意事项。  
> 按实际操作顺序组织，每一步均标注**必改 / 可选**，以及**示例参照文件**。

---

## 术语约定

| 占位符 | 含义 | 示例 |
|--------|------|------|
| `{rule}` | 规则标识（服务端 `room_rule` 字段，全小写） | `guobiao`、`qingque`、`classical`、`riichi` |
| `{subRule}` | 子规则标识（服务端 `sub_rule` 字段） | `guobiao/standard`、`classical/standard` |
| `{Rule}` | 规则名 PascalCase（用于类名和方法名） | `Classical`、`Riichi` |

---

## 一、配置字典（Config）

以下三个静态字典需要为新规则添加条目。

### 1.1 `RuleNameDictionary.cs`
- **路径**: `Assets/Scripts/Config/RuleNameDictionary.cs`
- **操作**: 在 `WholeName` 和 `ShortName` 字典中添加 `{subRule}` 和 `{rule}` 对应的显示名。
- **参照**: 现有的 `{"classical/standard", "古典麻雀"}` 条目。

### 1.2 `RoundTextDictionary.cs`
- **路径**: `Assets/Scripts/Config/RoundTextDictionary.cs`
- **操作**:
  1. 添加新的 `public static readonly Dictionary<int, string> CurrentRoundText{Rule}`。
  2. 在 `GetRoundName(int round, string rule)` 方法中添加 `else if (rule == "{rule}")` 分支。
- **参照**: `CurrentRoundTextClassical` / `CurrentRoundTextQingque`。

### 1.3 `FanTextDictionary.cs`
- **路径**: `Assets/Scripts/Config/FanTextDictionary.cs`
- **操作**: 如果新规则的番种/副种名需要特殊显示映射，添加对应的 `Dictionary` 和查询方法（如 `GetFuDisplayText`）。
- **参照**: `FanToDisplayClassical`、`FuToDisplayClassical`。

---

## 二、计算脚本（Calculation）— 程序集隔离

客户端提示系统（听牌提示、和牌提示）需要本地和牌/听牌计算。采用**独立程序集**隔离，减少编译耦合。

### 2.1 创建内核程序集

- **目录**: `Assets/Scripts/GameScene/Calculation/CalculationScript/{Rule}/{Rule}Calc/`
- **必须文件**:
  - `{Rule}Assembly.asmdef` — 程序集定义，`name` 字段为 `{Rule}Assembly`，无 references。
  - `{Rule}Assembly.asmdef.meta` — 手动或由 Unity 生成，记录 GUID。
  - `{Rule}PlayerTiles.cs` — 手牌数据模型，建议放在 `namespace {Rule}` 下。
  - `{Rule}CombinationSolver.cs` — 组合拆解（面子+雀头搜索）。
  - `{Rule}HepaiCheck.cs` — 和牌检测与计分。
  - `{Rule}TingpaiCheck.cs` — 听牌检测。
- **参照**: `Classical/ClassicalCalc/` 或 `Qingque13/Qingque13Calc/` 目录结构。

### 2.2 创建外壳封装

- **文件**: `Assets/Scripts/GameScene/Calculation/CalculationScript/{Rule}External.cs`
- **所属程序集**: `CalculationScriptAssembly`（与 `Qingque13External.cs` 同级）。
- **操作**: 提供 `public static` 的 `HepaiCheck`、`TingpaiCheck` 等方法，内部转调内核程序集。
- **参照**: `ClassicalExternal.cs`、`Qingque13External.cs`。

### 2.3 更新 `CalculationScriptAssembly.asmdef`

- **路径**: `Assets/Scripts/GameScene/Calculation/CalculationScript/CalculationScriptAssembly.asmdef`
- **操作**: 在 `references` 数组中添加新程序集的 GUID。

---

## 三、网络层（Network）

### 3.1 `Response.cs` — 数据结构
- **路径**: `Assets/Scripts/Network/Serialize/Response.cs`
- **操作**: 如果新规则的结算数据含有额外字段（如古典的 `base_fu`、`fu_fan_list`），需在 `ShowResultInfo` 中添加对应的可空字段。
- **参照**: `public int? base_fu; public string[] fu_fan_list;`

### 3.2 `NetworkManager.cs` — 消息总线
- **路径**: `Assets/Scripts/Network/NetworkManager.cs`
- **操作**: 在 `Get_Message` 的 `switch (response.type)` 中为 `gamestate/{rule}/*` 添加 case，转发给 `GameStateNetworkManager.HandleGameStateMessage`。同时添加 `data/get_{rule}_stats` 转发给 `DataNetworkManager.HandleDataMessage`。
- **参照**: 搜索 `gamestate/classical/` 的 case 块。

### 3.3 `GameStateNetworkManager.cs` — 游戏状态路由
- **路径**: `Assets/Scripts/Network/GameStateNetworkManager.cs`
- **操作**: 在 `HandleGameStateMessage` 的 switch 中添加新规则的所有游戏状态消息：
  - `gamestate/{rule}/game_start`
  - `gamestate/{rule}/broadcast_hand_action`
  - `gamestate/{rule}/ask_other_action`
  - `gamestate/{rule}/do_action`
  - `gamestate/{rule}/show_result`
  - `gamestate/{rule}/game_end`
  - `gamestate/{rule}/ready_status`
- **参照**: 现有的 `gamestate/classical/*` 或 `gamestate/qingque/*` 块。

### 3.4 `RoomNetworkManager.cs` — 创建房间
- **路径**: `Assets/Scripts/Network/RoomNetworkManager.cs`
- **操作**: 添加 `public async void Create_{Rule}_Room(...)` 方法。
- **参照**: `Create_Classical_Room`、`Create_Qingque_Room`。

### 3.5 `DataNetworkManager.cs` — 统计数据
- **路径**: `Assets/Scripts/Network/DataNetworkManager.cs`
- **操作**:
  1. 在 `HandleDataMessage` 添加 `case "data/get_{rule}_stats"` 分支。
  2. 添加 `HandleGet{Rule}StatsResponse` 回调方法。
  3. 添加 `public async void Get{Rule}Stats(...)` 请求方法。
- **参照**: `GetClassicalStats`、`HandleGetClassicalStatsResponse`。

---

## 四、房间创建（Room）

### 4.1 `CreatePanel.cs`
- **路径**: `Assets/Scripts/Room/CreateRoomPanel/CreatePanel.cs`
- **操作**:
  1. 在 `_ruleState` 映射中添加新规则的索引。
  2. 在 `SubRuleDescriptions` 字典中添加 `{subRule}` 对应的介绍文本。
  3. 在 `GetCurrentSubRuleKey` 中添加对应返回值。
  4. 在 `CreateRoom()` 中添加分支，调用 `RoomNetworkManager.Instance.Create_{Rule}_Room`。
  5. 添加 `Create{Rule}Room()` 私有方法，构建配置参数。
- **参照**: `CreateClassicalRoom()`。

---

## 五、游戏场景 UI（GameScene）

### 5.1 `TipsBlock.cs` — 听牌提示入口
- **路径**: `Assets/Scripts/GameScene/GameSceneUI/TipsBlock.cs`
- **操作**: 在 `ShowTipsBlock` 中按 `roomRule` 添加分支，调用 `{Rule}External.TingpaiCheck`。

### 5.2 `TipsContainer.cs` — 和牌提示详情
- **路径**: `Assets/Scripts/GameScene/GameSceneUI/TipsContainer.cs`
- **操作**:
  1. 在 `SetTipsWithHand` 的规则分发处添加 `else if (gameManager.roomRule == "{rule}")` 分支。
  2. 添加 `Process{Rule}Tile(...)` 私有方法，调用 `{Rule}External.HepaiCheck` 并显示结果。
  3. 注意: 古典规则的 `wayToHepai` 使用"门风"而非"自风"，如有类似术语差异需在此做转换。

### 5.3 `TileCard.cs` — 切牌听牌提示
- **路径**: `Assets/Scripts/GameScene/CanvasManager/CanvasControl/TileCard.cs`
- **操作**: 在切牌听牌检测的 `roomRule` 分支中添加新规则，调用 `{Rule}External.TingpaiCheck`。

### 5.4 `EndResultPanel.cs` — 结算面板
- **路径**: `Assets/Scripts/GameScene/GameSceneUI/EndResultPanel.cs`
- **操作**: 如果新规则的结算展示格式与现有规则不同（如古典需要显示副种+番种+总计），在 `ShowResult` 中按 `subRule` 添加分支。
- **参照**: `isClassical` 分支逻辑。

### 5.5 `RoundPanel.cs` — 局数信息
- **路径**: `Assets/Scripts/GameScene/GameSceneUI/RoundPanel.cs`
- **操作**: 在 `UpdateRoomInfo` 中按 `roomType` 添加分支，选用对应的 `RoundTextDictionary.CurrentRoundText{Rule}`。

### 5.6 `ScoreHistoryPanel.cs`（原 GameScoreRecord）— 计分板
- **路径**: `Assets/Scripts/GameScene/GameSceneUI/ScoreHistoryPanel.cs`
- **操作**: 在 `RuleToRoundMap` 静态字典中添加 `{"{rule}", RoundTextDictionary.CurrentRoundText{Rule}}`。

### 5.7 `BoardCanvas.cs` — 棋盘信息
- **路径**: `Assets/Scripts/GameScene/BoardManager/BoardCanvas.cs`
- **操作**: 在 `InitializeBoardInfo` 和 `UpdateBoardInfo` 中按 `roomType` 添加分支选用对应局数字典。

### 5.8 `GameSceneUIManager.cs` — UI 管理器
- **路径**: `Assets/Scripts/GameScene/GameSceneUI/GameSceneUIManager.cs`
- **操作**: 如果 `ShowEndResult` 方法签名因新规则而扩展了参数（如 `base_fu`、`fu_fan_list`），需同步更新此处的方法签名和转发调用。

### 5.9 `NormalGameStateManager.cs` — 状态管理
- **路径**: `Assets/Scripts/GameScene/GameStateManager/NormalGameStateManager.cs`
- **操作**: 如果 `ShowResult` 方法签名扩展，需同步更新。通常无需修改规则分支（`roomRule` 字段自动由服务端赋值）。

---

## 六、统计显示（PlayerInfo）

### 6.1 `PlayerInfoPanel.cs`
- **路径**: `Assets/Scripts/UI/PlayerInfo/PlayerInfoPanel.cs`
- **操作**:
  1. 添加 `private PlayerStatsInfo[] {rule}Stats;` 和 `private Dictionary<string, int> {rule}TotalFanStats;` 字段。
  2. 在 `ShowPlayerInfo` 中初始化为 null。
  3. 添加 `public void On{Rule}StatsReceived(...)` 回调。
  4. 在 `OnSwitchRuleButtonClick` 中添加数据请求。
  5. 在 `RefreshCurrentRuleDisplay` 中添加显示逻辑。
- **参照**: `classicalStats` / `OnClassicalStatsReceived` 的实现。

---

## 七、牌谱回放（GameRecord）

### 7.1 `GameRecordManager.cs`
- **路径**: `Assets/Scripts/GameScene/GameStateManager/GameRecordManager/GameRecordManager.cs`
- **操作**:
  1. 在 `GetGameTitleText` 中添加规则名映射（fallback 逻辑）。
  2. 在 `RefreshRecordScoreTable` 中确保 `rule` 字符串能匹配到 `ScoreHistoryPanel` 的字典。
  3. 在 `ShowRecordResult` 中，如有新字段需解析结算数据。

### 7.2 `RecordRoundItem.cs`
- **路径**: `Assets/Scripts/GameScene/GameStateManager/Prefab/RecordRoundItem.cs`
- **操作**: 如果新规则在局跳转时需要显示额外信息（如立直的本场数），添加分支。

---

## 八、其他可能涉及的文件

| 文件 | 条件 |
|------|------|
| `RoomItem.cs` | 如果新规则在房间列表中需要显示起和番等特殊信息 |
| `RoomConfigContainer.cs` | 房间内规则配置文案展示 |
| `SpectatorPrefab.cs` | 观战列表中的规则名显示 |
| `RecordPrefab.cs` | 牌谱列表中的规则名显示 |
| `PlayerInfoEntry.cs` | 统计条目按规则分支显示 |
| `TestPanel.cs` | 调试面板添加新规则的测试用例 |

---

## 核心检查清单（Checklist）

添加新规则时，按以下清单逐项确认：

- [ ] `RuleNameDictionary` 添加 WholeName + ShortName
- [ ] `RoundTextDictionary` 添加局数字典 + GetRoundName 分支
- [ ] `FanTextDictionary` 添加番/副显示映射（如需）
- [ ] 计算脚本：内核程序集 + External 封装 + asmdef 引用
- [ ] `Response.cs` 扩展结算字段（如需）
- [ ] `NetworkManager.cs` 添加 gamestate 和 data 路由
- [ ] `GameStateNetworkManager.cs` 添加 7 个 gamestate case
- [ ] `RoomNetworkManager.cs` 添加 Create 方法
- [ ] `DataNetworkManager.cs` 添加统计请求/响应
- [ ] `CreatePanel.cs` 添加规则选项和创建逻辑
- [ ] `TipsBlock.cs` 添加听牌分支
- [ ] `TipsContainer.cs` 添加和牌提示分支
- [ ] `TileCard.cs` 添加切牌听牌分支
- [ ] `EndResultPanel.cs` 添加结算显示分支（如需）
- [ ] `RoundPanel.cs` 添加局数显示分支
- [ ] `ScoreHistoryPanel.cs` 添加 RuleToRoundMap 条目
- [ ] `BoardCanvas.cs` 添加棋盘局数分支
- [ ] `GameSceneUIManager.cs` / `NormalGameStateManager.cs` 同步方法签名（如需）
- [ ] `PlayerInfoPanel.cs` 添加统计显示
- [ ] `GameRecordManager.cs` 添加牌谱规则映射

---

## 注意事项

1. **规则标识一致性**: 服务端的 `room_rule` 和 `sub_rule` 字段值必须与客户端各处的字符串匹配完全一致。注意拼写（如 `riichi` vs `ricchi`）。
2. **程序集隔离**: 计算脚本放在独立 asmdef 下，修改计算逻辑时不会触发全量编译。External 封装类放在 `CalculationScriptAssembly` 内，游戏层通过 External 间接引用。
3. **wayToHepai 参数差异**: 不同规则可能使用不同术语（如"自风"vs"门风"、"抢杠"vs"金鸡夺食"），在客户端提示层做好转换。
4. **大括号风格**: C# 代码大括号不换行。
5. **不添加时间线注释**: 注释应描述功能，不应包含"现在改为"等时间线描述。
