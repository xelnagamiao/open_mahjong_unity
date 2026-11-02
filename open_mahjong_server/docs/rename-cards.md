# 改名卡统一与历史余额迁移

更新：2026-10-28。

## 玩家规则

平台只保留一种可用资产：仓库中的改名卡。网页账号中心修改用户名成功时自动扣 1 张；背包中的“前往改名”只打开账号中心，不扣卡，也不兑换次数。游客仍不能改名。新注册账号不额外赠卡，延续原有默认待遇。

| 情况 | 行为 |
|---|---|
| 改名成功 | 扣 1 张，记录流水，同时更新用户名、历史参与记录和牌谱快照 |
| 重名、与当前同名、格式不合法、事务失败 | 不扣卡 |
| 请求超时、提交后响应丢失 | 网页读取实际账号确认结果，不自动重发改名请求 |
| 卡片停止发放 | 已持有的卡仍可改名 |
| 卡片停止使用 | 保留持有量，不能支付改名；重新启用后恢复 |
| 管理员在用户详情改名 | 保留管理操作，不扣玩家的卡 |

内置物品保持 `item_id=3001`、`code=rename_coupon`、`asset_key=item.rename_credit`；其中 `rename_credit` 仅为原有资源/效果标识，不再代表另一份余额。管理员通过同一模板创建的活动改名卡同样可用于改名；先扣启用的内置卡，再按 `sort_order,item_id` 选择启用的活动卡。

`user_inventory.quantity` 是唯一余额来源。账号、后台详情和库存返回 `rename_card_count`（全部持有量）与 `rename_card_usable_count`（启用物品持有量）；数量都是实时聚合结果，不另存余额列。旧 `users.rename_count` 暂时保留供迁移，迁移后为 0，正常逻辑不读取或增加。初始化不再回填旧次数。旧客户端调用 `inventory/use` 使用改名卡会收到前往账号中心的提示，事务不扣卡。

Node 自助改名与 Python 发放/回收统一按“目录共享锁 → 用户行锁 → 库存行锁”顺序执行。扣卡、`inventory_operations(action=rename)`、`inventory_ledger`、用户名及历史牌谱全部同事务提交；并发请求不能透支。提交后通过已有用户名同步入口向在线玩家私发新库存；通知失败不影响已提交结果，玩家可刷新背包。

## 一次性折算脚本

脚本：[scripts/migrate_rename_cards.py](../scripts/migrate_rename_cards.py)。默认只读预览，必须显式 `--apply` 才写入；正常服务启动不会调用它。

- 每个用户的新内置卡数量 = 原内置卡数量 + 原 `rename_count`，随后将旧次数归零。覆盖全部有余额的用户，包括游客；不会删除已有卡或改动活动卡。
- 不受发放/使用开关影响，不改变目录开关。负数、空余额、物品目录冲突或折算后超过 1,000,000 上限时整批拒绝，不截断余额。
- 全部用户的补卡、清零、库存流水与审计标记在一个事务内完成；任何一处失败均整批回滚。
- `inventory_operations(action=migrate_rename_cards)` 保留逐用户的旧次数、原卡量和新增量；`admin_audit_log` 的 `inventory.rename_credit_migration / 20261028_rename_cards_v1` 保存整批明细，系统操作者为 0。
- 重复执行会返回 `already_complete`，不会重复发卡。迁移后如旧版服务又写入次数，脚本拒绝再次折算，要求先核对旧服务和流水。
- `--expect-fingerprint` 可锁定预览中的余额；余额变化时拒绝写入。`--report` 创建完整 JSON 报告，不覆盖已有文件；控制台只显示汇总。

使用现有 Python 依赖即可运行。连接优先取 `RENAME_CARDS_DATABASE_URL`，否则与游戏服一样加载 `server/local_config.py`，缺失时加载可提交的 `server/test_config.py`。正式操作前核对目标数据库；脚本不会自动定位生产库。

在仓库根目录，本机预览示例：

```powershell
New-Item -ItemType Directory -Path '.om_workspace/rename-cards' -Force | Out-Null
& '.\open_mahjong_server\.venv\Scripts\python.exe' '.\open_mahjong_server\scripts\migrate_rename_cards.py' --dry-run --report '.om_workspace/rename-cards/preview.json'
```

部署顺序：先备份数据库并安排维护窗口，停止旧 Node/Python 写入（尤其是旧版改名和道具兑换），更新 Python、Node/Vue 和 Unity 客户端源码，确保库存表已完成正常初始化。维护期间再次预览，核对人数、总张数及指纹，随后执行：

```powershell
& '.\open_mahjong_server\.venv\Scripts\python.exe' '.\open_mahjong_server\scripts\migrate_rename_cards.py' --apply --expect-fingerprint '<刚刚预览的 fingerprint>' --report '.om_workspace/rename-cards/applied.json'
```

脚本会锁定目录及用户/库存写入，最多等待锁 10 秒；超时则回滚。迁移完成后启动新版服务，检查旧次数全部为 0、内置改名卡总量恰好增加预览总张数，刷新网页账号和游戏背包，再开放改名入口。不要回滚到仍会累加旧次数的服务版本；数据恢复应基于备份与逐用户流水核对，不能在玩家已经消费新卡后直接还原旧数量。

## 验证

- `INVENTORY_TEST_DATABASE_URL`：运行 `python -m unittest server.database.test_inventory -v`，覆盖旧兑换关闭、聚合余额、只读预览、全用户补卡、重复执行、超限/无效余额、指纹变化及整批回滚。
- `RENAME_CARDS_TEST_DATABASE_URL`（PostgreSQL URL）：在 Web 项目运行 `node --env-file=.env --test server/services/renameCards.integration.test.js server/routes/player/auth.test.js server/utils/gameRecordUsername.test.js`，覆盖真实数据库改名、历史同步、失败不扣卡、停用与活动卡、最后一张卡并发竞争、重名竞争和重复提交。测试自动创建并清理独立 schema，不修改现有账号。
- `node --test client/tests/playerRename.test.js` 验证响应丢失后的确认流程。
