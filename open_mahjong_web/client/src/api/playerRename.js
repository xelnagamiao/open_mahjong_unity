export class RenameResultUnconfirmedError extends Error {
  constructor(cause) {
    super('改名结果暂未确认，请刷新账号信息后查看，勿重复提交。', { cause })
    this.name = 'RenameResultUnconfirmedError'
  }
}

export async function renamePlayer(api, newUsername, userId) {
  try {
    const response = await api.post('/auth/rename', { new_username: newUsername })
    return response.data
  } catch (error) {
    // 明确的业务拒绝仍按原错误处理；超时或 5xx 可能发生在事务提交之后。
    if (error.response?.status && error.response.status < 500) throw error

    let account
    try {
      const response = await api.get('/auth/me')
      if (response.data?.success) account = response.data.data
    } catch {
      // 查询也失败时无法判断写入结果，下面统一提示待确认，不能重发改名请求。
    }

    const expectedName = String(newUsername ?? '').normalize('NFC').trim()
    if (account && String(account.user_id) === String(userId) && account.username === expectedName) {
      return {
        success: true,
        data: account,
        message: '改名成功，已同步历史牌谱',
      }
    }
    throw new RenameResultUnconfirmedError(error)
  }
}
