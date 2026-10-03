# 国标多场匹配

客户端和服务端需一起更新。原有 12 个国标队列与段位资格保持不变，同一玩家最多加入 4 个等待队列。

- `match/join_queue`：`queue_type` 指定追加场次；重复请求幂等，不改变原有队列顺序。
- `match/leave_queue`：带 `queue_type` 仅取消该场次；省略或传 null 取消全部。已经配对成功则拒绝取消。
- 两种操作可以携带字符串 `match_request_id`，在对应 `*_queue_done` 回应中原样返回。失败也使用对应操作回应，附带权威状态。
- 操作回应和 `match/queue_status` 都包含 `my_queues`（有序数组，空队列为 `[]`）、`match_revision`（玩家状态递增版本）、`match_committed`。保留 `my_queue` 作为首个队列供旧调用方读取。
- `match/queue_status` 的 `queue_status` 仍提供每场等待/对局人数；`match_player_count` 是排队及已配对玩家的去重总人数。
- `match/match_found` 携带 `match_queue_type` 指明最终场次，等待队列已全部移除。开局失败通过 `match/failed` 解除客户端匹配状态，并允许重新入队。

玩家的加入/取消请求按用户锁串行处理。断线代数防止清理观战期间断开的请求重新入队。凑齐四人时，先同步移除四人的所有等待队列并设置承诺锁，再执行任何异步清理和网络通知，避免在竞争队列中重复配对。

Unity 端顺序发送操作，以请求 ID 识别确认，以版本号拒绝过时轮询状态。匹配成功清空未发送操作；追加或移除部分队列保持原计时。所有 UI 为 MainScene 中已有的可编辑对象。

验证：`python -m unittest server.match.test_multi_queue server.event.test_auto_match`。测试使用内存玩家和替身数据库，不加入真实匹配队列。
