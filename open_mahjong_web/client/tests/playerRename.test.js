import test from 'node:test'
import assert from 'node:assert/strict'
import { renamePlayer, RenameResultUnconfirmedError } from '../src/api/playerRename.js'

const userId = 101
const newUsername = '新的名字'
const currentAccount = {
  user_id: userId,
  username: newUsername,
  rename_count: 0,
  email: null,
  email_verified: false,
  is_event_admin: false,
  events: [],
}

function failingApi(error, account = currentAccount, refreshError) {
  const calls = []
  return {
    calls,
    async post(url, body) {
      calls.push({ method: 'POST', url, body })
      throw error
    },
    async get(url) {
      calls.push({ method: 'GET', url })
      if (refreshError) throw refreshError
      return { data: { success: true, data: account } }
    },
  }
}

test('normal rename returns the server response without a follow-up request', async () => {
  const response = { success: true, data: { ...currentAccount, token: 'new-token' }, message: '改名成功' }
  const api = {
    async post(url, body) {
      assert.equal(url, '/auth/rename')
      assert.deepEqual(body, { new_username: newUsername })
      return { data: response }
    },
    get() { assert.fail('Successful rename must not require another request') },
  }
  assert.equal(await renamePlayer(api, newUsername, userId), response)
})

for (const [label, error] of [
  ['timeout after commit', Object.assign(new Error('timeout'), { code: 'ECONNABORTED' })],
  ['connection lost after commit', new Error('Network Error')],
  ['post-commit server error', { response: { status: 500, data: { message: '改名已生效，请重新登录' } } }],
  ['gateway timeout after commit', { response: { status: 504 } }],
]) {
  test(`${label} is confirmed from the current account without resubmitting`, async () => {
    const api = failingApi(error)
    const result = await renamePlayer(api, newUsername, userId)
    assert.equal(result.success, true)
    assert.equal(result.data.username, newUsername)
    assert.equal(result.data.rename_count, 0)
    assert.match(result.message, /改名成功/)
    assert.deepEqual(api.calls, [
      { method: 'POST', url: '/auth/rename', body: { new_username: newUsername } },
      { method: 'GET', url: '/auth/me' },
    ])
  })
}

test('confirmation follows server Unicode normalization and accepts numeric string IDs', async () => {
  const api = failingApi(new Error('timeout'), { ...currentAccount, user_id: '101', username: 'Café' })
  const result = await renamePlayer(api, ' Cafe\u0301 ', userId)
  assert.equal(result.success, true)
  assert.equal(result.data.username, 'Café')
})

test('confirmation preserves refreshed player and event-admin sessions', async () => {
  const account = {
    ...currentAccount,
    token: 'refreshed-player-token',
    is_event_admin: true,
    event_admin_token: 'refreshed-event-token',
    events: [{ event_id: 'test-event' }],
  }
  const result = await renamePlayer(failingApi(new Error('timeout'), account), newUsername, userId)
  assert.deepEqual(result.data, account)
})

for (const status of [400, 401, 403, 409, 429]) {
  test(`explicit HTTP ${status} rejection retains its original error`, async () => {
    const error = { response: { status, data: { message: '明确失败原因' } } }
    const api = failingApi(error)
    await assert.rejects(renamePlayer(api, newUsername, userId), actual => actual === error)
    assert.equal(api.calls.length, 1)
  })
}

for (const [label, account] of [
  ['rename still pending', { ...currentAccount, username: '原来的名字', rename_count: 1 }],
  ['different account with matching name', { ...currentAccount, user_id: 202 }],
  ['missing account data', null],
]) {
  test(`${label} reports an unconfirmed result instead of a false failure or success`, async () => {
    const error = new Error('timeout')
    const api = failingApi(error, account)
    await assert.rejects(renamePlayer(api, newUsername, userId), actual => {
      assert.ok(actual instanceof RenameResultUnconfirmedError)
      assert.equal(actual.cause, error)
      assert.match(actual.message, /结果暂未确认/)
      assert.match(actual.message, /勿重复提交/)
      return true
    })
    assert.equal(api.calls.filter(call => call.method === 'POST').length, 1)
  })
}

test('failed account refresh leaves the outcome unconfirmed', async () => {
  const api = failingApi(new Error('timeout'), currentAccount, new Error('account refresh offline'))
  await assert.rejects(renamePlayer(api, newUsername, userId), RenameResultUnconfirmedError)
  assert.equal(api.calls.length, 2)
})
