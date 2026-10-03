# 四人立直麻将规则预设

规则目录为 `server/game_calculation/riichi/rule_options.json`。普通建房、比赛建房、服务端计算、Unity 创建房间及牌谱使用同一组键名。Unity 副本位于 `Assets/Resources/RiichiRuleOptions.json`，目录一致性由 `test_riichi_rule_config.py` 检查。

## 预设定义

| 预设 | 规则范围 | 起始／返还点 | 顺位点 | 多家荣和 | 赤牌／一发／里宝 |
| --- | --- | --- | --- | --- | --- |
| 雀魂规 | 四人段位战，半庄 | 25000／25000 | +15/+5/-5/-15 | 多响 | 有／有／有 |
| 天凤规 | 四人食断赤牌，半庄 | 25000／30000 | +20/+10/-10/-20 | 三家荣和流局 | 有／有／有 |
| A规 | 日本职业麻将联盟 A 规则 | 30000／30000 | 联盟浮动顺位点 | 头跳 | 无／无／无 |
| ML规 | M 联赛四人规则 | 25000／30000 | +30/+10/-10/-30 | 头跳 | 有／有／有 |

A 规按日本职业麻将联盟 A 规则解释，ML 规按 M 联赛解释。预设包含牌局与计分设置；平台段位分、赛事身份及线下处罚流程不由房间预设模拟。起始与返还点的差额作为头名奖励结算，表中的顺位点不重复包含这部分。

选择预设会同时应用详细规则和相关基础设置，包括场次、起始点、赤牌、食替、西入、击飞及多响方式。房间名称、密码、计时、权限和随机种子保持当前输入。手动改动后显示为自定义规则。详细面板内选择预设后，只有“应用规则”才同步基础设置；取消恢复编辑前状态。

`riichi/standard` 与 `riichi/langyong` 保持为独立子规则。四个预设应用到标准日麻；原有浪涌玩法和配置仍保留。

## 新增配置

37 项详细配置按以下分组显示，所有分组同时存在，顶部选择用于滚动定位。

| 分组 | 配置键 |
| --- | --- |
| 役种、宝牌与符数 | `open_tanyao`、`ippatsu`、`ura_dora`、`kan_dora`、`kan_ura_dora`、`double_yakuman`、`multiple_yakuman`、`kiriage_mangan`、`kazoe_limit`、`yakuman_limit`、`double_wind_pair_fu` |
| 杠、立直与振听 | `kan_dora_timing`、`kokushi_ankan_ron`、`riichi_kan_rule`、`riichi_min_live_tiles`、`furiten_clear` |
| 流局与连庄 | `kyuushu_abort`、`four_winds_abort`、`four_riichi_abort`、`four_kan_abort`、`nagashi_mangan`、`nagashi_allow_calls`、`noten_penalty`、`tenpai_renchan` |
| 终局 | `agari_yame`、`tenpai_yame`、`target_score`、`extension_rounds`、`return_score`、`rank_points`、`tie_break`、`end_deposits` |
| 包牌与错和 | `pao`、`pao_suukantsu`、`pao_scope`、`pao_honba`、`chombo_penalty` |

有效值、默认值及界面解释以规则目录中的 `values`、`default`、`help` 为准。服务端严格校验类型和值，拒绝未知键，不把字符串或整数自动转换为布尔值。旧房间和旧牌谱缺少详细配置时采用兼容默认值。

需区分以下设置：

- `target_score=0`：起始点的 1.2 倍；`extension_rounds=0`：不限制延长局数；`return_score=0`：与起始点相同。
- `kazoe_limit` 控制累计番数上限；真正役满由 `double_yakuman`、`multiple_yakuman`、`yakuman_limit` 控制，宝牌不会叠加到真正役满上。
- `kan_dora_timing` 控制明杠／加杠后的翻牌时机，暗杠即时翻牌。取消杠宝牌时不产生其指示牌或对应杠里宝牌收益。
- `riichi_kan_rule` 可要求仅听牌相同、拆解不变，或拆解与役数条件不降低；暗杠抢和仅允许已启用的国士无双分支。
- `chombo_penalty=penalty_20000` 从最终比赛点扣 20，不把桌面实点直接减 20000。本局已成立立直的供托退回，前局留存供托保留。
- 同分顺位点在剩余供托分配前确定。ML 规三人并列头名时，每根剩余供托按 400/300/300 分配。

## 客户端与牌谱协议

`detailed_config` 同时写入房间、GameInfo 与牌谱标题，回放估分使用牌谱自身配置。立直扣点发生在宣言牌无人荣和、宣言成立之后；`c` 的 `H` 表示横置宣言牌，`riichi` tick 表示实际支付 1000 点，二者不可合并。

杠先声明，确认无人抢杠后才消耗手牌并更新副露。被抢杠时只移除实际被抢的一张牌，保留原碰或暗刻，并广播 `rob_kan`、记录 `rk`。赤牌使用真实牌号 `105/205/305`；成功加杠 tick 可在第 4 项保存实际加杠牌。详细格式见 `server/gamestate/public/game_record_format.md`。

标题 `riichi_final_scores` 按原始玩家顺序记录剩余供托结算后的实点；`riichi_final_sticks` 保存最终剩余供托数；`riichi_points` 保存返还点、顺位点及竞技扣分结算后的结果。最终实点仅在最后一局 `end` 节点应用，避免把终局结算提前显示在牌局中途。

MJAI 以固定玩家编号记录动作，本项目每次换庄会轮转本局座位。互转时必须映射 `actor/target/oya`、手牌、分数及分变，不能将本局座位直接当作固定玩家编号。MJAI 的基础动作和实点可以往返；平台扩展配置、浪涌倍率及竞技扣分请以本项目原始牌谱为准。

## 验证入口

在服务端虚拟环境中运行：

```powershell
python -m pytest server/game_calculation/riichi server/gamestate/game_riichi server/room/test_riichi_rule_config.py server/room/test_riichi_starting_score.py server/gamestate/verifier/test_riichi_replay.py -q
```

`test_match_simulation.py` 驱动四个预设及浪涌的真实完整对局循环，检查牌张数量、点数守恒、结束条件和记录配置。其他测试覆盖配置有效值、计分、振听、立直、杠、抢杠、多响、包牌、终局、重连、超时与实际消息序列化。

2026-09-24 验收：服务端组合回归 384 项及 11 个子测试通过，C# / Python 计分对照 4520 组无差异，Web 相关回归 31 项通过。主场景两个实际 Windows 客户端完成四预设的开房与加入（96 项检查），并与两个机器人完成 ML 规半庄 11 局、真实断线重连及终局结算（84 项检查）。机器人对局按现有策略仅保存本机牌谱，云端牌谱查询不会返回该记录。

最终主场景包构建成功，0 errors。真实对局本机牌谱的可见回放通过 93 项检查，覆盖 11 局起始／结算点数、和牌手牌、展开的 3D 手牌、荣和展示副本及回退重置，运行无 Error/Exception。牌谱展开手牌中的荣和末张使用展示副本，保留牌河或抢杠来源处的实体牌；切局和跳回局首会清除此标记。

覆盖率统计不含测试文件，但保留未被生产入口调用的旧日麻 `get_action.py`：语句 82%、分支 74%。有限用例与生成式对照不能证明所有理论牌局已被穷尽。

## 规则来源

规则核对日期：2026-09-24。

- [雀魂官方四人段位战规则](https://mahjongsoul.com/news/46)
- [天凤官方手册](https://tenhou.net/man/)
- [日本职业麻将联盟官方竞技规则](https://www.ma-jan.or.jp/activity/game_rule.html)，A 规参考现行规则表。
- [M 联赛官方规则](https://m-league.jp/about/)

计分适配器以项目固定版本 `mahjong 2.0.0` 的 MIT 代码为基础；许可证保存在 `server/game_calculation/riichi/MAHJONG_LICENSE.txt`。
