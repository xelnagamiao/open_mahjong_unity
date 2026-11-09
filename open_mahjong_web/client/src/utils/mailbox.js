export const MAX_MAIL_REWARDS = 8

export function rewardLimit(item) {
  return item?.category === 'item' ? Math.min(item.max_quantity, 1000000) : 1
}

export function validateMail(body, catalog, now = Date.now()) {
  if (!Number.isSafeInteger(body.user_id) || body.user_id < 11) return '请选择有效的收件玩家'
  for (const [key, label, max] of [['sender', '发件人', 80], ['title', '标题', 160], ['content', '正文', 20000], ['reason', '操作备注', 500]]) {
    if (typeof body[key] !== 'string' || !body[key].trim() || [...body[key]].length > max) return `${label}须为 1–${max} 字`
    if (/[<>\x00-\x08\x0b\x0c\x0e-\x1f\x7f]/.test(body[key])) return `${label}不能包含标签或控制字符`
  }
  if (body.expires_at !== null && (!Number.isFinite(Date.parse(body.expires_at)) || Date.parse(body.expires_at) <= now)) return '有效期须晚于当前时间'
  if (!Array.isArray(body.rewards) || body.rewards.length > MAX_MAIL_REWARDS) return '每封邮件最多包含 8 种物品'
  const ids = new Set()
  for (const reward of body.rewards) {
    const item = catalog.find(i => i.item_id === reward.item_id)
    if (!item || item.is_default || !item.grant_enabled) return '请选择可发放的物品'
    if (ids.has(reward.item_id)) return '同一种物品只能添加一次，请调整数量'
    ids.add(reward.item_id)
    if (!Number.isSafeInteger(reward.quantity) || reward.quantity < 1 || reward.quantity > rewardLimit(item)) return `${item.name}的数量须为 1–${rewardLimit(item)} 的整数`
  }
  return ''
}

export function mailRewardStatus(mail) {
  if (!mail.rewards?.length) return '无附件'
  if (mail.claimed) return '已领取'
  return mail.expired ? '已过期' : '待领取'
}

// Only known, bundled images are rendered; catalog paths are not arbitrary URLs.
export function mailIcon(item) {
  const icons = {
    'image/Inventory/grass_tile': '/inventory-icons/grass_tile.png',
    'image/Inventory/good_tile': '/inventory-icons/good_tile.png',
    'image/Profiles/1': '/inventory-icons/1.png',
    'image/Profiles/2302': '/inventory-icons/2302.png',
    'image/Profiles/2303': '/inventory-icons/2303.png',
  }
  return icons[item?.icon_path] || ''
}
