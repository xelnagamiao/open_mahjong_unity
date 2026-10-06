# 算番对抗积分

国标+立直、立直两个评分池独立计分。新玩家初始 R 为 1500。
系统匹配使用 `E = 1 / (1 + 10^((对手 R - 自己 R) / 2000))`，
`变化值 = round(32 × (S - E))`，胜为 1、负为 0。沿用整数积分及最低 0 分的规则，自建房不计分。

旧版初始分 1000、预期分母 400。历史积分按迁移前备份修正一次：

`新 R = 旧 R + 500`

原迁移曾执行 `1500 + (旧 R - 1000) × 5`，会把历史积分差放大五倍。
此次按确认的口径只平移基准，现行分母 2000、K＝32 保留；历史相对胜率预期随之改变。
胜负、场数、连胜、用户名、更新时间不因修复改变。数据库只有累计评分，没有逐场对手和胜负顺序，无法精确重放历史 Elo。

若备份后已有新比赛，保留其实际已结算的净积分变化：
`修正 R = max(0, 旧 R + 500 + 当前 R − [1500 + (旧 R − 1000) × 5])`。
这是去掉历史放大的备份修正，不会重新计算这些比赛的胜率预期。
脚本核对场数、胜场、连胜和每场最多 32 分的变化范围；无法解释的变动及备份之外的新账户保留并写入报告。

`ensureGuessFanTables()` 在 Node 启动、开始接收连接之前自动运行修复。
`guess_fan_rating_migrations` 的 `elo_1500_2000_v1` 保存迁移前原始行；
`elo_1500_2000_history_unscaled_v2` 保存本次修复前完整行及修复报告。
事务和数据库锁保证多进程启动只执行一次，失败时整笔回滚并阻止 Web 服务启动。
尚未迁移的旧数据库直接执行 +500；已经修复的数据库重复启动不会改分。

在 Web 项目根目录也可手动预览、执行（需要已有 v1 原始备份）：

```sh
node server/utils/guessFanRatingRepair.js --dry-run --report /path/to/preview.json
node server/utils/guessFanRatingRepair.js --report /path/to/applied.json
```

预览会回滚所有数据库修改。报告文件应放在本机工作区或部署备份目录，不加入 Git。

验证：`node --test server/utils/guessFanElo.test.js`；数据库测试需显式设置
`GUESS_FAN_TEST_DATABASE=1` 后运行 `server/utils/guessFanTables.test.js` 和 `server/utils/guessFanRatingRepair.test.js`。
后者只接受本机数据库连接，使用临时 schema，并在结束后清理。
