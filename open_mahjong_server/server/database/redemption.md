# 兑换码与统一奖励回执

## 部署

安装 `requirements.txt` 后重启 Python。`DatabaseManager` 在启动时创建兑换活动、兑换码、奖励、领取记录和限流表；不会发放任何测试奖励。Node 和 Web 前端也需更新。

管理后台入口为 `/admin/redemption`，网页玩家入口为 `/account#sec-redeem`。Unity 仓库左侧分类栏底部是“兑换码”，与其他分类同样式。兑换窗口和获得物品窗口使用仓库配色；获得物品只展示实际获得的图标、名称、数量与确定按钮。场景对象位于 `OverlayCanvas/RedeemPanel` 和 `OverlayCanvas/RewardPresentation/RewardPanel`，可直接在编辑器中调整。固定布局由 `RewardUiBaker` 烘焙，不在运行时生成整套面板。

兑换码导出使用 [cryptography Fernet](https://cryptography.io/en/latest/fernet/) 加密保存。部署时设置 `REDEEM_CODE_KEY`，或设置 `REDEEM_CODE_KEY_FILE` 指向持久化的私有密钥文件；也可在不提交 Git 的 `server/local_config.py` 中设置 `Config.redeem_code_key_file`。所有实例使用同一密钥，并单独备份。可使用 `Fernet.generate_key()` 生成密钥，切勿写入项目、前端、日志或导出的活动记录。未配置密钥时创建/导出明确报错，不会偷偷生成新密钥；已有兑换码的领取不需要解密密钥。

Python 通过既有 Node 管理员 `/api/admin/auth/me` 验证管理员；玩家 Web 身份通过 `/api/player/auth/me` 回查。自定义服务部署可指定 `INVENTORY_ADMIN_AUTH_URL`、`REDEEM_PLAYER_AUTH_URL`，目标不能由请求提供。

## 统一领取结果

任何服务端业务成功发放库存后，都可以在正常响应中附带 `reward_receipt`，无需 Unity 对该业务页面单独实现领取弹窗。Web 领取、后台直接发放等异步通知使用 `type: "reward/received"`。响应示意：

```json
{
  "type": "reward/redeem",
  "request_id": "客户端生成并在超时后复用的操作编号",
  "success": true,
  "inventory_state": { "user_id": 101, "catalog": [], "owned": [] },
  "reward_receipt": {
    "version": 1,
    "receipt_id": "库存操作的 UUID",
    "user_id": 101,
    "source": "redeem",
    "source_id": "兑换码批次 ID",
    "items": [{ "item_id": 3011, "name": "草牌", "description": "物品说明", "icon_path": "image/Inventory/grass_tile", "quantity": 3, "quantity_after": 8 }],
    "skipped_items": []
  }
}
```

`items` 只包含本次实际增加的物品，`quantity_after` 是本次提交后的数量。永久物品已拥有时不累计，出现在 `skipped_items`。物品元信息和回执保存在 `inventory_operations.result`，重试返回同一回执。更新名称或奖励配置不会改变已经发放的回执。

`NetworkManager.AcceptRewardReceipt` 统一处理消息：校验账号、协议版本与回执 UUID，更新库存，再按回执 ID 去重。`RewardPresentation` 顺序展示，多件物品超出时滚动；不显示回执注释、跳过物品或物品说明。玩家离线时仍保留库存；此阶段的主动弹窗通知不作为可靠消息队列，离线后通过仓库/邮件查询结果。客户端同一次运行中保留最近 512 个回执的展示去重记录，库存本身始终由服务端永久幂等约束保护。

邮件 `mail/claim` 和管理员 `inventory/grant` 已使用同样的回执；普通背包消耗、装备切换不会弹出获得物品页。未来活动应在服务端先验证活动资格，和库存写入一起提交事务，用 `make_reward_receipt(cursor, operation_id, user_id, 'activity', activity_id, changes)` 保存回执，再将它附在活动领取响应上。若需要主动推送，在提交成功后调用 `notify_reward(server, result)`；推送失败不得回滚已到账物品。**不得提供接收客户端任意物品列表的通用发奖接口。**

## 兑换规则

- 普通兑换码（`purpose: "ordinary"`）：只能使用一次性码 `kind: "unique"`，可生成单张或 1–10000 张。同一个注册账号可领取同批次的多个不同码，每个码全服只可使用一次。例如购买三张改名卡兑换码，可以依次到账三张改名卡。
- 活动兑换码（`purpose: "activity"`）：同批次每个注册账号限领一次，即使换成该批次的其他码也不能重复领取。可选择多人共用的通用码 `kind: "shared"`，或批量生成的一次性码 `kind: "unique"`；两种形式均可设总名额。
- 新后台默认创建普通兑换码、数量为 1。没有传 `purpose` 的旧管理接口仍按活动处理；已存在批次迁移为活动，领取历史和旧限领规则保持不变。接口路径和内部 `campaign_id` 保留，代表兑换码批次，不要求存在游戏赛事或活动页面。
- 不区分英文字母大小写，忽略空格及连字符。输入 8–32 位英文字母/数字；随机码为 16 位，避开 I/O/0/1。
- 草稿可编辑奖励、时限和上限。启用后这些字段冻结；可暂停、恢复、结束。结束不可恢复。码类型和生成数量在创建后固定。
- 允许 1–20 种现有、非默认、允许发放的物品；头衔系统不是库存物品，不混用 ID。永久物品数量为 1。
- 任一奖励超上限、停发或事务失败，库存、领取名额、码使用状态一并回滚。全部永久奖励已拥有时不消耗码；部分已拥有则跳过重复物品。
- 数据库锁顺序：目录共享锁 → 账号行 → 批次行 → 兑换码行。批次锁内检查活动限领和总名额，码锁检查单码消耗；操作唯一约束保护请求重试，`(code_id,user_id)` 唯一索引保护重复领取。启动迁移移除旧 `(campaign_id,user_id)` 唯一约束，建立活动限领查询索引；迁移可重复运行且保留所有历史记录。
- Web 和游戏共用数据库领取限流：每账号每分钟 10 次，包含失败尝试。游戏连接另限每来源地址每分钟 50 次；Web 网关按自身取得的玩家 IP 每分钟限制 20 次。Python 不将网关地址误当作所有玩家的同一 IP，也不采信客户端伪造的转发 IP。
- 明文兑换码不进入 WebSocket 消息日志和审计日志；审计记录创建、状态变更、导出行为。只在管理员主动导出时解密。

## API

| 入口 | 用途 |
|---|---|
| 游戏 `reward/redeem`：`code`、`request_id` | 兑换，用户身份来自登录连接 |
| Node `POST /api/player/redeem` | 玩家网页兑换，转发实际 Bearer 身份 |
| Node `GET /api/admin/redemption/campaigns?page=1` | 兑换码批次分页，每页 20，含用途和领取规则 |
| Node `POST /api/admin/redemption/campaigns` | 创建草稿，传入 `purpose`；同一 `request_id` 重试不生成新批次 |
| Node `PATCH /api/admin/redemption/campaigns/{id}` | 编辑草稿 |
| Node `POST /api/admin/redemption/campaigns/{id}/status` | 状态和操作原因 |
| Node `POST /api/admin/redemption/campaigns/{id}/export` | 解密导出原有批次，不重新生成码 |
| Node `GET /api/admin/redemption/campaigns/{id}/claims?page=1` | 领取记录，每页 100 |

所有变更接口需认证。管理员操作还会由 Python 再次验证角色。未知或未确认的领取结果保留原 `request_id` 重试。生产发布前备份数据库及密钥；本功能未自动发布到线上环境。
