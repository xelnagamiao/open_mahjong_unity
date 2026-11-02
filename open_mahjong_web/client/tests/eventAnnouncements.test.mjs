import test from 'node:test'
import assert from 'node:assert/strict'
import fs from 'node:fs'
import vm from 'node:vm'
import { parse, compileTemplate } from '@vue/compiler-sfc'

const source = fs.readFileSync(new URL('../src/components/EventManagePanel.vue', import.meta.url), 'utf8')
const functions = ['loadAnnouncements', 'publishAnnouncement'].map(name => {
  const match = source.match(new RegExp(`async function ${name}\\(\\) \\{[\\s\\S]*?^\\}`, 'm'))
  assert.ok(match, `${name} must be tested from the component itself`)
  return match[0]
}).join('\n')
const deferred = () => { let resolve, reject; const promise = new Promise((a, b) => { resolve = a; reject = b }); return { promise, resolve, reject } }

function setup(api) {
  const messages = []
  const state = {
    props: { eventId: 'notice-test' }, announcements: { value: [{ announcement_id: 'old', title: '原公告' }] },
    loadingAnnouncements: { value: false }, publishingAnnounce: { value: false }, announceEditorOpen: { value: true },
    announceForm: { title: ' 新公告 ', body: ' 公告内容\n第二行 ' }, eventAdminApi: api,
    ElMessage: Object.fromEntries(['success', 'error', 'warning'].map(level => [level, text => messages.push({ level, text })])),
  }
  vm.createContext(state)
  vm.runInContext(`let announcementRequestVersion = 0;\n${functions}`, state)
  return { state, messages }
}
const response = items => ({ data: { success: true, data: { items }, message: '公告已发布' } })

test('successful publication displays the returned announcement, closes the editor and shows the requested message', async () => {
  const items = [{ announcement_id: 'new', title: '新公告' }, { announcement_id: 'old', title: '原公告' }]
  let sent
  const { state, messages } = setup({ post: async (url, body) => { sent = { url, body }; return response(items) } })
  await state.publishAnnouncement()
  assert.equal(sent.url, '/events/notice-test/announcements')
  assert.equal(sent.body.title, '新公告'); assert.equal(sent.body.body, '公告内容\n第二行')
  assert.equal(state.announcements.value, items)
  assert.equal(state.announceEditorOpen.value, false)
  assert.equal(state.announceForm.title, ''); assert.equal(state.announceForm.body, '')
  assert.equal(state.publishingAnnounce.value, false)
  assert.deepEqual(messages, [{ level: 'success', text: '公告发布完成' }])
})

test('a business failure keeps both the draft and existing announcements', async () => {
  const { state, messages } = setup({ post: async () => ({ data: { success: false, message: '无发布权限' } }) })
  await state.publishAnnouncement()
  assert.equal(state.announceEditorOpen.value, true)
  assert.equal(state.announceForm.title, ' 新公告 ')
  assert.equal(state.announcements.value[0].announcement_id, 'old')
  assert.deepEqual(messages, [{ level: 'error', text: '无发布权限' }])
  assert.equal(state.publishingAnnounce.value, false)
})

test('a delayed list load cannot overwrite the successful publish response', async () => {
  const read = deferred()
  const items = [{ announcement_id: 'new', title: '新公告' }]
  const { state } = setup({ get: () => read.promise, post: async () => response(items) })
  const loading = state.loadAnnouncements()
  await state.publishAnnouncement()
  read.resolve(response([])); await loading
  assert.equal(state.announcements.value, items)
  assert.equal(state.loadingAnnouncements.value, false)
})

test('double submit sends one publication request', async () => {
  const pending = deferred(); let calls = 0
  const { state } = setup({ post: () => { calls++; return pending.promise } })
  const first = state.publishAnnouncement()
  await state.publishAnnouncement()
  assert.equal(calls, 1)
  pending.resolve(response([{ announcement_id: 'new' }]))
  await first
  assert.equal(state.announceEditorOpen.value, false)
})

test('failed refresh preserves already displayed notices', async () => {
  const { state, messages } = setup({ get: async () => { throw new Error('连接失败') } })
  await state.loadAnnouncements()
  assert.equal(state.announcements.value[0].announcement_id, 'old')
  assert.equal(state.loadingAnnouncements.value, false)
  assert.deepEqual(messages, [{ level: 'error', text: '连接失败' }])
})

test('publication response for a previous venue does not clear the newly opened venue draft', async () => {
  const pending = deferred()
  const { state, messages } = setup({ post: () => pending.promise })
  const publishing = state.publishAnnouncement()
  state.props.eventId = 'another-venue'; state.announceForm.title = '另一个基地的草稿'
  pending.resolve(response([{ announcement_id: 'new' }]))
  await publishing
  assert.equal(state.announceForm.title, '另一个基地的草稿')
  assert.equal(state.announceEditorOpen.value, true)
  assert.equal(messages.length, 0)
})

test('the real announcement editor is conditionally visible and the component template compiles', () => {
  const { descriptor } = parse(source)
  const result = compileTemplate({ source: descriptor.template.content, filename: 'EventManagePanel.vue', id: 'announcements-test' })
  assert.deepEqual(result.errors, [])
  assert.match(descriptor.template.content, /<el-form v-if="announceEditorOpen"[^>]*@submit.prevent="publishAnnouncement"/)
  assert.match(descriptor.template.content, /v-if="!announceEditorOpen"[^>]*@click="announceEditorOpen = true"/)
})
