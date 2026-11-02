# 玩家邮件

邮件由游戏服务器保存，Unity 读取本人列表、内容和附件。没有自动生成邮件或客户端发奖逻辑。

## 客户端消息

登录后的游戏 WebSocket 接受以下消息；用户身份从连接的登录会话读取，忽略客户端传入的 `user_id`。

- `{"type":"mail/list","request_id":"..."}`：返回 `mails` 和 `unread_count`。
- `{"type":"mail/read","request_id":"...","mail_id":123}`：持久化已读状态，返回 `mail`。
- `{"type":"mail/claim","request_id":"...","mail_id":123}`：领取整封邮件的附件，返回 `mail` 和最新 `inventory_state`。

响应沿用 `type`、`request_id`、`success`、`message`。断线或暂时不可用时可重试：邮件领取以邮件 ID 为准，同一封邮件始终只发放一次。过期邮件仍可阅读，未领取附件不可再领取。

`mail/changed` 是在线投递后的刷新通知，客户端收到后重新请求列表。离线投递在下次打开邮箱时读取。

## 管理投递

`POST /admin/mail` 使用与背包管理相同的 `Authorization: Bearer ...` 管理员认证。请求示例：

```json
{
  "request_id": "unique-delivery-id",
  "user_id": 12345,
  "sender": "Salasasa",
  "title": "活动奖励",
  "content": "感谢参与活动。\n奖励已随信附上。",
  "rewards": [{"item_id": 3011, "quantity": 1}],
  "expires_at": "2027-01-01T00:00:00+08:00",
  "reason": "活动奖励投递"
}
```

`rewards` 可为空，最多 8 种物品。`expires_at` 可省略，有值时必须带时区。投递编号全局唯一；重试同一编号和同样内容不会重复投递，不同内容复用编号返回冲突。

`GET /admin/mail/users/{user_id}` 供已认证的管理员查询投递结果。邮件内容为纯文本，不允许富文本标签。

附件使用现有物品目录。库存变更、领取状态和库存流水在同一数据库事务中提交；任一附件不能领取时，全部回滚。永久物品重复领取不叠加数量。

## 验证

`server.database.test_mail` 在独立临时 PostgreSQL schema 中覆盖权限、读状态、并发领取、失败回滚、过期和投递幂等。运行前设置 `INVENTORY_TEST_DATABASE_URL`；不会操作现有玩家。
