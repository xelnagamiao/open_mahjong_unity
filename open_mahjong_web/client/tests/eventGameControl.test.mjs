import test from 'node:test'
import assert from 'node:assert/strict'
import fs from 'node:fs'
import vm from 'node:vm'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'

const source = fs.readFileSync(new URL('../src/components/EventGameControl.vue', import.meta.url), 'utf8')
const functions = ['loadGames', 'callControl', 'onPause', 'onResume', 'onEnd', 'isPauseDisabled', 'isResumeDisabled'].map(name => {
  const match = source.match(new RegExp(`(?:async )?function ${name}\\([^)]*\\) \\{[\\s\\S]*?^\\}`, 'm'))
  assert.ok(match, `${name} must come from the real component`)
  return match[0]
}).join('\n')
const response = items => ({ data: { success: true, data: { items } } })
const deferred = () => {
  let resolve, reject
  const promise = new Promise((a, b) => { resolve = a; reject = b })
  return { promise, resolve, reject }
}

function setup({ get = async () => response([]), post = async () => response([]), confirm = async () => {} } = {}) {
  const requests = [], messages = [], confirmations = []
  const state = {
    props: { eventId: 'event-a', platformAdmin: false }, games: { value: [] }, loadingGames: { value: false },
    controllingGameId: { value: '' }, roomsRefreshed: 0,
    eventAdminApi: {
      get: url => { requests.push({ method: 'GET', url }); return get(url) },
      post: (url, body) => { requests.push({ method: 'POST', url, body }); return post(url, body) },
    },
    ElMessageBox: { confirm: (...args) => { confirmations.push(args); return confirm(...args) } },
    ElMessage: Object.fromEntries(['success', 'error'].map(level => [level, text => messages.push({ level, text })])),
    emit: event => { if (event === 'ended') state.roomsRefreshed++ },
  }
  state.adminApi = state.eventAdminApi
  vm.createContext(state)
  vm.runInContext(`let gameRequestVersion = 0; let gamePanelMounted = true;\n${functions}`, state)
  return { state, requests, messages, confirmations }
}

test('the management panel compiles with its live games tab', () => {
  for (const filename of ['components/EventGameControl.vue', 'components/EventManagePanel.vue', 'views/admin/EventDetail.vue']) {
    const { descriptor, errors } = parse(fs.readFileSync(new URL(`../src/${filename}`, import.meta.url), 'utf8'))
    assert.deepEqual(errors, [])
    const script = compileScript(descriptor, { id: 'event-game-control' })
    const result = compileTemplate({ source: descriptor.template.content, filename, id: 'event-game-control', compilerOptions: { bindingMetadata: script.bindings } })
    assert.deepEqual(result.errors, [])
    const tabs = [...descriptor.template.content.matchAll(/<el-tab-pane[^>]* name="([^"]+)"/g)].map(match => match[1])
    assert.equal(new Set(tabs).size, tabs.length, 'tab names must be unique')
  }
})

test('pause, resume and end use the selected venue and refresh actual server state', async () => {
  for (const [method, action] of [['onPause', 'pause'], ['onResume', 'resume'], ['onEnd', 'end']]) {
    const items = action === 'end' ? [] : [{ gamestate_id: 'g/a', vote_phase: action === 'pause' ? 'pause_pending' : 'idle' }]
    const { state, requests, confirmations } = setup({ get: async () => response(items) })
    await state[method]({ gamestate_id: 'g/a' })
    assert.equal(requests[0].url, `/events/event-a/games/g%2Fa/${action}`)
    assert.equal(requests[0].method, 'POST')
    assert.equal(requests[1].url, '/events/event-a/games')
    assert.equal(state.games.value, items)
    assert.equal(state.controllingGameId.value, '')
    assert.equal(state.roomsRefreshed, action === 'end' ? 1 : 0)
    assert.match(confirmations[0][0], /g\/a/)
  }
})

test('canceling the destructive confirmation sends no request', async () => {
  const { state, requests, messages } = setup({ confirm: async () => { throw 'cancel' } })
  await state.onEnd({ gamestate_id: 'game-a' })
  assert.equal(requests.length, 0)
  assert.equal(messages.length, 0)
  assert.equal(state.controllingGameId.value, '')
})

test('repeated clicks while confirmation or submission is pending send only one control', async () => {
  const confirmation = deferred(), posting = deferred()
  const { state, requests, confirmations } = setup({ confirm: () => confirmation.promise, post: () => posting.promise })
  const first = state.onPause({ gamestate_id: 'game-a' })
  await state.onEnd({ gamestate_id: 'game-a' })
  assert.equal(confirmations.length, 1)
  confirmation.resolve(); await Promise.resolve()
  await state.onPause({ gamestate_id: 'game-a' })
  assert.equal(requests.filter(r => r.method === 'POST').length, 1)
  posting.resolve(response([])); await first
  assert.equal(state.controllingGameId.value, '')
})

test('switching venue or closing the panel while confirming cannot operate an old game', async () => {
  for (const close of [false, true]) {
    const confirmation = deferred()
    const { state, requests } = setup({ confirm: () => confirmation.promise })
    const pending = state.onEnd({ gamestate_id: 'game-a' })
    if (close) vm.runInContext('gamePanelMounted = false', state)
    else state.props.eventId = 'base-b'
    confirmation.resolve(); await pending
    assert.equal(requests.length, 0)
  }
})

test('out-of-order list responses cannot leak the previous venue or overwrite newer state', async () => {
  const old = deferred(), latest = [{ gamestate_id: 'base-game' }]
  let count = 0
  const { state } = setup({ get: () => ++count === 1 ? old.promise : Promise.resolve(response(latest)) })
  const first = state.loadGames()
  state.props.eventId = 'base-b'
  await state.loadGames()
  old.resolve(response([{ gamestate_id: 'event-game' }]))
  await first
  assert.equal(state.games.value, latest)
  assert.equal(state.loadingGames.value, false)
})

test('late control responses do not refresh or announce success in a different venue', async () => {
  const posting = deferred(), started = deferred()
  const { state, requests, messages } = setup({ post: () => { started.resolve(); return posting.promise } })
  const pending = state.onEnd({ gamestate_id: 'event-game' })
  await started.promise
  state.props.eventId = 'base-b'
  posting.resolve(response([])); await pending
  assert.equal(requests.length, 1)
  assert.equal(messages.length, 0)
  assert.equal(state.roomsRefreshed, 0)
})

test('missing games and rejected operations show errors and resynchronize the list', async () => {
  for (const post of [
    async () => { throw { response: { data: { message: '对局不存在或已结束' } } } },
    async () => ({ data: { success: false, message: '无操作权限' } }),
  ]) {
    const { state, requests, messages } = setup({ post })
    state.games.value = [{ gamestate_id: 'old' }]
    await state.onEnd({ gamestate_id: 'old' })
    assert.equal(messages.length, 1)
    assert.equal(messages[0].level, 'error')
    assert.equal(requests.at(-1).method, 'GET')
    assert.equal(state.games.value.length, 0)
    assert.equal(state.controllingGameId.value, '')
  }
})

test('available controls match running, pending, paused and resuming states', () => {
  const { state } = setup()
  for (const phase of ['pause_pending', 'paused', 'resume_voting', 'resume_countdown']) {
    assert.equal(state.isPauseDisabled({ vote_phase: phase }), true, phase)
    assert.equal(state.isResumeDisabled({ vote_phase: phase }), false, phase)
  }
  assert.equal(state.isPauseDisabled({ vote_phase: 'idle' }), false)
  assert.equal(state.isResumeDisabled({ vote_phase: 'idle' }), true)
  assert.equal(state.isPauseDisabled({ vote_phase: 'end_countdown' }), true)
  assert.equal(state.isResumeDisabled({ vote_phase: 'end_countdown' }), true)
})

test('platform administration uses the admin API and only shows the selected venue', async () => {
  const games = [{ gamestate_id: 'game-a', event_id: 'event-a' }, { gamestate_id: 'game-b', event_id: 'base-b' }, { gamestate_id: 'lobby-game' }]
  const { state, requests } = setup({ get: async () => response(games) })
  state.props.platformAdmin = true
  await state.loadGames()
  assert.equal(requests[0].url, '/game-control/list')
  assert.equal(state.games.value.length, 1)
  assert.equal(state.games.value[0].gamestate_id, 'game-a')
  for (const [method, action] of [['onPause', 'pause'], ['onResume', 'resume'], ['onEnd', 'end']]) {
    await state[method](games[0])
    const request = requests.filter(request => request.method === 'POST').at(-1)
    assert.equal(request.url, `/game-control/${action}`)
    assert.equal(request.body.gamestate_id, 'game-a')
  }
  const count = requests.length
  await state.onEnd(games[1])
  assert.equal(requests.length, count)
})
