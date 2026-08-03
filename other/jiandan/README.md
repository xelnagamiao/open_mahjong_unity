# 简单麻将（Jiandan）

`jiandan` 是简单麻将的正式规则标识。本目录记录 PR A 实际提交的规则、模块边界和番种说明。

## 本次范围

- 固定“一人和牌即止”，不提供第二/第三赢家续打选项。
- 服务端负责合法动作、和牌判定、番种、支付与最终结算。
- Unity 只增加建房/网络与统计路由、轮数与计分板等静态显示分派、玩家资料页统计，以及独立计算程序集中的本地听牌与悬停提示。
- 本地提示不包含海底、河底、杠上、抢杠、天和、地和等局面番，最终结果始终以服务端为准。

## 文档

- [规则定义](rule_reference.md)
- [实现边界](architecture.md)
- [番种计分表（中文）](fan_scoring.zh-CN.md) / [English](fan_scoring.md)
- [逐番详细说明（中文）](fan_details.zh-CN.md) / [English](fan_details.md)

## 代码入口

服务端：

- `server/game_calculation/jiandan`：牌型分解、番种、计分和听牌。
- `server/gamestate/game_jiandan`：简单动作、首家和牌终局、广播与对局生命周期。
- `server/database/jiandan`：规则专用的牌谱适配、统计表、聚合存取与查询。

Unity：

- `CalculationScript/Jiandan/JiandanAssembly`：独立的客户端提示计算内核。
- `JiandanExternal`：`CalculationScriptAssembly` 内面向游戏层的唯一计算桥接。
- `GameStateNetworkManager` / `RoomNetworkManager`：Jiandan 路由。
- `DataNetworkManager` / `PlayerInfoPanel`：Jiandan 历史战绩与番种统计。
- 四个最小提示桥接点：`TipsBlock`、`TipsContainer`、`TileCard`、`RecordChongHintCalculator`。

## 验证

- 提交前曾在本地通过 112 项专项验证，覆盖计分、听牌、动作、广播、房间、状态机、统计和路由；按维护者要求，测试脚本不随本 PR 入库。
- Unity `6000.4.7f1` 编译通过并完成实际联机试玩。
- 已验证建房、跨局计时、座次轮转、计分板、中文番名，以及机器人摸牌后逐张刷新余牌。
- `git diff --check` 通过；`Game3DManager*`、`NormalGameStateManager*`、结果面板、牌谱主流程、`Response.cs` 和 Unity 包配置均无内容改动。

## 明确不在本次范围

多阶段赢家动画、续打协议、主游戏流程重构、和牌结果布局修复均应另行沟通并单独提交 PR。
