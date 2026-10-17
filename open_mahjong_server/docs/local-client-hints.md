# 客户端提示计算边界

对局中的听牌、试切后听牌、未来和牌番数与分数提示由客户端计算。新增规则适配不得在开局、摸切牌广播、重连或观战快照中调用提示求解器，也不得为提示向服务器计算池提交任务。

服务器仍负责真实动作合法性、报听与锁手资格、过和限制、实际和牌、结算和机器人决策。这些校验所需的听牌结构属于对局逻辑，不能用客户端提示结果代替。

## 协议

- 国标四癞子、花混、十六张使用已有本人手牌、副露和公开规则状态；服务器不再声明或发送 `four_jokers_hints`。
- 地方规则的 `client_context.py` 只复制本人的手牌、副露、合法出牌集合及计分需要的规则状态。`local_request` 包含 `rule`、`hand`、`melds`、`context`，需要限制试切范围时另含 `cuts`。不得加入预计算的听牌或分值结果。
- 长沙换六张使用 `private_state.local_hint_context`，包含本人报听、明牌、相公、禁小胡和锁定牌型状态；不再发送 `private_state.hints`。
- 红雀仅发送 `local_win_context`，不再发送 `win_hint`、`waiting_tiles`、`waiting_hints`。
- 部分地方规则外层仍沿用 `*_hints`、`*_waits`、`guangdong_tips` 或牌谱 `hints` 事件名，以兼容现有客户端及旧牌谱读取器。这些外层名字不表示服务端提示计算接口；新消息只装载本地计算输入，不含旧版空结果占位字段。

输入快照必须复制嵌套容器，防止后续摸切、过和状态或配置变化污染已发消息。只能发给本人及获得授权的观战视角；离线牌谱按原有权限和延迟记录。Unity 使用有界任务队列和缓存，WebGL 的托管算牌器分帧执行，其余使用本地 Worker。

红中 `action_score.py` 只计算实际摸牌的和牌资格和分数，属于真实对局校验；不枚举未来待牌或试切提示。

## Web 算分工具

`server/webapi/calc.py` 的 `/calc/*` HTTP API 服务于 Web 独立算分工具，本次保留。其底层算分、分解、听牌和国标特殊玩法算法仍可供该工具使用。对局广播与 Unity 提示不得调用这些 HTTP API。

## 验证

`tests/local_hint_oracles/` 保存离线对照算牌器，只由测试和测试数据生成器引用；生产 `server/` 模块不得依赖它们。`gamestate/public/test_local_hint_context.py` 检查输入快照、旧入口移除与生产依赖边界。各规则协议测试验证隐私、提示开关、重连、观战及牌谱输入；Unity `LocalHintValidation` 对照独立生成的结果验证客户端算法。
