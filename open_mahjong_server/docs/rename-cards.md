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

`user_inventory.quantity` 是唯一余额来源。账号、后台详情和库存返回 `rename_card_count`（全部持有量）与 `rename_card_usable_count`（启用物品持有量）；数量都是实时聚合结果，不另存余额列。历史改名次数已完成折算；兼容列 `users.rename_count` 保持为 0，正常逻辑不读取或增加。初始化不再回填旧次数。旧客户端调用 `inventory/use` 使用改名卡会收到前往账号中心的提示，事务不扣卡。

Node 自助改名与 Python 发放/回收统一按“目录共享锁 → 用户行锁 → 库存行锁”顺序执行。扣卡、`inventory_operations(action=rename)`、`inventory_ledger`、用户名及历史牌谱全部同事务提交；并发请求不能透支。提交后通过已有用户名同步入口向在线玩家私发新库存；通知失败不影响已提交结果，玩家可刷新背包。

## 已完成的历史折算

历史 `rename_count` 已按 1:1 折算为库存中的内置改名卡，原次数归零。一次性迁移工具及其专用测试已归档，不再随项目源码交付，也不在启动时执行。

历史流水仍保留在 `inventory_operations(action=migrate_rename_cards)`、`inventory_ledger` 与 `admin_audit_log` 中，审计标记为 `inventory.rename_credit_migration / 20261028_rename_cards_v1`。现有余额以库存及后续消费流水为准；需要恢复历史数据时应核对数据库备份和逐用户流水。

## 验证

- `INVENTORY_TEST_DATABASE_URL`：运行 `python -m unittest server.database.test_inventory -v`，覆盖旧兑换关闭、聚合余额、发放与使用开关、库存事务及失败回滚。
- `RENAME_CARDS_TEST_DATABASE_URL`（PostgreSQL URL）：在 Web 项目运行 `node --env-file=.env --test server/services/renameCards.integration.test.js server/routes/player/auth.test.js server/utils/gameRecordUsername.test.js`，覆盖真实数据库改名、历史同步、失败不扣卡、停用与活动卡、最后一张卡并发竞争、重名竞争和重复提交。测试自动创建并清理独立 schema，不修改现有账号。
- `node --test client/tests/playerRename.test.js` 验证响应丢失后的确认流程。
