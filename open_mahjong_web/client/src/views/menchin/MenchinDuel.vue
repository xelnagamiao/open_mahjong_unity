<template>
  <div class="duel">
    <header class="title"><h1>门清大战</h1></header>
    <nav class="nav" aria-label="对战导航">
      <router-link class="back" to="/menchin">返回门清大战</router-link>
      <button v-if="room" type="button" :disabled="!canPlay" @click="leave">
        {{ room.phase === 'playing' ? '认输退出' : room.phase === 'finished' ? '返回对战页' : '退出房间' }}
      </button>
    </nav>
    <h2 class="bar">■ 双人万字对战</h2>
    <p v-if="!auth.token" class="panel login">使用平台账号参与对战。
      <router-link class="button primary" :to="{ path: '/login', query: { redirect: route.fullPath } }">登录</router-link>
    </p>
    <template v-else>
      <p v-if="error" class="notice" role="alert">{{ error }}
        <span v-if="!connected && room?.phase === 'playing'">断线后保留座位 30 秒。</span>
        <button v-if="!connected" type="button" @click="retry">重连</button>
      </p>
      <p v-else-if="!connected" class="notice" role="status">正在连接对战服务</p>

      <template v-if="!room">
        <div class="actions">
          <button type="button" class="card create" :disabled="!canPlay || queued" @click="play('create')">创建房间<small>好友约战</small></button>
          <button type="button" class="card join" :disabled="!canPlay || queued" :aria-expanded="panel === 'join'" @click="toggle('join')">加入房间<small>房间码 / 房间列表</small></button>
          <button type="button" class="card ranked" :disabled="!canPlay" @click="play(queued ? 'cancel' : 'queue')">
            {{ queued ? '取消匹配' : '排位匹配' }}<small>{{ queued ? '等待对手' : '计入 Elo' }}</small>
          </button>
          <button type="button" class="card board" :disabled="!connected" :aria-expanded="panel === 'board'" @click="toggle('board')">排位排行<small v-if="rating != null">我的 Elo {{ rating }}</small></button>
        </div>
        <section v-if="panel === 'join'" class="panel">
          <form class="row" @submit.prevent="join(code)">
            <label>房间码 <input v-model.trim="code" inputmode="numeric" pattern="[0-9]{6}" maxlength="6" placeholder="六位房间码" required /></label>
            <button class="primary" :disabled="!canPlay || queued || !/^\d{6}$/.test(code)">加入</button>
          </form>
          <p v-if="!rooms.length">暂无等待中的房间</p>
          <ul v-else class="rooms">
            <li v-for="entry in rooms" :key="entry.code">
              <span>{{ entry.code }}　<span data-no-translate>{{ entry.host }}</span></span>
              <button type="button" :disabled="!canPlay || queued" @click="join(entry.code)">加入房间</button>
            </li>
          </ul>
        </section>
        <section v-else-if="panel === 'board'" class="panel">
          <h3>排位排行</h3>
          <p v-if="standingsStatus === 'loading'" role="status">正在读取排行</p>
          <p v-else-if="standingsStatus === 'error'" role="status">排行榜读取失败 <button type="button" @click="readStandings">重试</button></p>
          <p v-else-if="!standings.length">暂无排位成绩</p>
          <table v-else>
            <thead><tr><th>名次</th><th>玩家</th><th>Elo</th></tr></thead>
            <tbody><tr v-for="(entry, i) in standings" :key="entry.id">
              <td>{{ i + 1 }}</td><td data-no-translate>{{ entry.name }}</td><td>{{ entry.score }}</td>
            </tr></tbody>
          </table>
        </section>
      </template>

      <section v-else-if="room.phase === 'waiting'" class="panel">
        <h3>房间码 {{ room.code }}</h3>
        <ul class="players">
          <li v-for="(p, i) in room.players" :key="p.id">
            <span data-no-translate>{{ p.name }}</span> · {{ i === 0 ? '房主' : '玩家' }}{{ p.online ? '' : ' · 离线' }}
          </li>
          <li v-if="room.players.length < 2">等待对手加入</li>
        </ul>
        <button v-if="room.seat === 0" type="button" class="primary" :disabled="!canPlay || room.players.length !== 2 || !room.players.every(p => p.online)" @click="play('start')">开始对战</button>
        <p v-else>等待房主开始</p>
        <div v-if="room.players.length === 1" class="row invite">
          <button type="button" @click="copyInvite">{{ inviteCopied ? '邀请链接已复制' : '复制邀请链接' }}</button>
          <button type="button" :disabled="!canPlay" @click="switchToJoin">退出并加入其他房间</button>
          <input v-if="inviteFallback" :value="inviteFallback" aria-label="邀请链接" readonly @focus="$event.target.select()" />
        </div>
      </section>
      <template v-else>
        <div v-if="room.phase === 'finished'" class="outcome" role="status">
          <span>{{ matchResult }}</span>
          <span v-if="room.forfeit != null">{{ room.forfeit === room.seat ? '我方退出' : '对方退出' }}</span>
          <span v-if="ratingChange">Elo {{ ratingChange.rating }}（{{ ratingChange.delta > 0 ? '+' : '' }}{{ ratingChange.delta }}）</span>
          <span v-else-if="room.ranked && room.winner == null">Elo 不变</span>
          <span v-if="room.saving === 'saving'">正在保存成绩</span>
          <span v-if="room.saving === 'retry'">正在重试保存成绩</span>
        </div>
        <MenchinDuelTable v-if="room.game" :room="room" :seconds="seconds" :enabled="canPlay" @move="move => play('move', { move })" />
      </template>
    </template>
    <details v-if="!room || room.phase === 'waiting'" class="rules">
      <summary>对战规则</summary>
      <p>一至九万各四张。共 20 局，双方交替先手。和牌得 1 分，诈和给对手 1 分，流局双方 0 分。总分相同为平局。</p>
      <p>可吃、碰、杠，允许七对，无起和番数和振听限制。摸牌或对方出牌时均可宣和，宣和后判定牌形。</p>
      <p>摸牌后 10 秒内打牌，响应副露、和牌限时 5 秒。排位计入 Elo，好友房仅计本场比分。</p>
    </details>
  </div>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import { onBeforeRouteLeave, useRoute, useRouter } from 'vue-router'
import { ElMessageBox } from 'element-plus'
import MenchinDuelTable from './MenchinDuelTable.vue'
import { useMenchinDuel } from './useMenchinDuel'

const route = useRoute()
const router = useRouter()
const { auth, connected, queued, rooms, room, busy, error, seconds, rating, standings, standingsStatus, readStandings, play, retry } = useMenchinDuel()
const panel = ref('')
const code = ref('')
const inviteCopied = ref(false)
const inviteFallback = ref('')
const canPlay = computed(() => connected.value && !busy.value)
const matchResult = computed(() => room.value.winner == null ? '平局' : room.value.winner === room.value.seat ? '我方胜' : '对方胜')
const ratingChange = computed(() => room.value?.changes?.[room.value.players[room.value.seat]?.id])
watch(() => room.value?.id, () => {
  panel.value = ''
  code.value = ''
  inviteCopied.value = false
  inviteFallback.value = ''
})
watch([() => route.query.entry, () => auth.token, connected, busy], async ([entry, token, online, pending]) => {
  if (route.name !== 'MenchinDuel' || !['match', 'create', 'join', 'joinRoom'].includes(entry)) return
  if (!token) {
    await router.replace({ path: '/login', query: { redirect: route.fullPath } })
    return
  }
  if (!online || pending) return
  const query = { ...route.query }
  const joining = entry === 'join' || entry === 'joinRoom'
  const inviteCode = typeof query.code === 'string' && /^\d{6}$/.test(query.code) ? query.code : ''
  delete query.entry
  if (joining) delete query.code
  if (await router.replace({ path: route.path, query, hash: route.hash })) return
  if (route.name !== 'MenchinDuel' || room.value || queued.value) return
  if (joining) {
    panel.value = 'join'
    code.value = inviteCode
    if (entry === 'joinRoom') {
      if (inviteCode) await play('join', { code: inviteCode })
      else error.value = '请输入六位房间码'
    }
  } else {
    await play(entry === 'match' ? 'queue' : 'create')
  }
}, { immediate: true })
async function switchToJoin() {
  if (await play('leave')) panel.value = 'join'
}
async function copyInvite() {
  const invitation = router.resolve({ name: 'MenchinDuel', query: { entry: 'join', code: room.value.code } })
  const url = new URL(invitation.href, window.location.href).href
  try {
    await navigator.clipboard.writeText(url)
    inviteCopied.value = true
  } catch {
    inviteFallback.value = url
  }
}
function toggle(value) {
  panel.value = panel.value === value ? '' : value
  if (panel.value === 'board') readStandings()
}
function join(value) {
  if (/^\d{6}$/.test(value)) play('join', { code: String(value) })
}
async function leave() {
  if (room.value?.phase === 'playing') {
    try {
      await ElMessageBox.confirm('本场将判负。', '认输并退出对战？', {
        confirmButtonText: '认输退出', cancelButtonText: '继续对战', customClass: 'menchin-duel-confirm',
      })
    } catch { return false }
  }
  return play('leave')
}
onBeforeRouteLeave(async () => {
  if (!connected.value) return true
  if (room.value) return await leave()
  if (queued.value) return await play('cancel')
})
</script>

<style scoped>
.duel { color: #333; font-weight: 400; }
.duel h1, .duel h2, .duel h3, .duel th { font-weight: 400; margin: 0; }
.title { margin-bottom: 16px; padding: 22px 20px; background: #1a1a1a; color: white; text-align: center; }
.title h1 { font-size: 1.55rem; }
.bar { background: #414141; color: white; padding: 6px 12px; font-size: 13px; }
.nav, .row, .rooms li, .outcome { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 12px; }
.nav { margin-bottom: 12px; }
.nav a, .button, button, input { box-sizing: border-box; min-height: 42px; padding: 8px 14px; border: 1px solid #c8c8c8; background: white; color: #333; font: inherit; text-decoration: none; }
.button, button, .nav a { display: inline-flex; align-items: center; justify-content: center; cursor: pointer; }
button:disabled { opacity: .55; cursor: default; }
.nav .back { background: #454545; border-color: #454545; color: white; }
.actions { display: grid; grid-template-columns: repeat(4,minmax(0,1fr)); gap: 16px; margin: 12px 0; }
.actions .card { flex-direction: column; align-items: flex-start; min-height: 98px; padding: 14px 16px; border: none; color: white; box-shadow: 0 4px 14px rgba(0,0,0,.12); }
.card small { margin-top: 6px; font-size: 12px; }
.create { background: #45b7d1; }
.join { background: #e6a23c; }
.ranked { background: #c0392b; }
.board { background: #9b59b6; }
.primary { border-color: #2475b9; background: #2475b9; color: white; }
.panel, .rules, .notice, .outcome { margin: 12px 0; padding: 12px 14px; background: white; border: 1px solid #ddd; }
.panel p:last-child { margin-bottom: 0; }
.panel h3 { font-size: 17px; }
.panel input { width: 11em; }
.login .button { margin-left: 8px; }
.notice { color: #9c342b; }
.rules { line-height: 1.65; }
.rules summary { cursor: pointer; }
.rules p { margin: 8px 0 0; }
.outcome { border-left: 4px solid #c0392b; }
.bar + :deep(.duel-table) { margin-top: 12px; }
.rooms, .players { list-style: none; padding: 0; }
.rooms li { padding: 8px 0; border-bottom: 1px solid #eee; justify-content: space-between; }
.players li { margin: 6px 0; }
.invite { margin-top: 10px; }
table { width: 100%; border-collapse: collapse; }
th, td { padding: 7px 4px; border-bottom: 1px solid #eee; text-align: left; overflow-wrap: anywhere; }
td:last-child, th:last-child { text-align: right; }
:global(.menchin-duel-confirm .el-message-box__title) { font-weight: 400; }
@media (max-width:900px) { .actions { grid-template-columns: repeat(2,minmax(0,1fr)); } }
@media (max-width:600px) {
  .actions { gap: 8px; }
  .actions .card { min-height: 76px; padding: 10px 12px; }
  .nav a, .nav button { min-height: 44px; }
}
</style>
