<template>
  <section class="record-browser" aria-labelledby="record-browser-title">
    <header class="record-browser__header">
      <div>
        <p class="record-browser__eyebrow">RECORD</p>
        <h2 id="record-browser-title">牌谱阅览</h2>
      </div>
      <div class="record-browser__lookup">
        <el-input v-model="recordId" clearable autocomplete="off" placeholder="牌谱 ID 或 2D/3D 分享链接" @keyup.enter="openRecord" />
        <el-button type="primary" @click="openRecord">打开</el-button>
      </div>
    </header>

    <div class="record-browser__tabs" role="tablist" aria-label="牌谱分类">
      <button :class="{ active: activeTab === 'mine' }" role="tab" @click="switchTab('mine')">我的牌谱</button>
      <button :class="{ active: activeTab === 'favorite' }" role="tab" @click="switchTab('favorite')">收藏</button>
      <button :class="{ active: activeTab === 'local' }" role="tab" @click="switchTab('local')">本地牌谱</button>
      <button :class="{ active: activeTab === 'ladder' }" role="tab" @click="switchTab('ladder')">最近天梯</button>
    </div>

    <div v-if="requiresLogin" class="record-browser__login">
      <div>
        <strong>登录后查看自己的牌谱与收藏</strong>
        <span>网站账户与游戏账户互通；公开分享链接、最近天梯和本地牌谱仍可直接查看。</span>
      </div>
      <el-button type="primary" @click="goLogin">网站登录</el-button>
    </div>

    <div v-else class="record-browser__content" v-loading="loading">
      <div v-if="records.length" class="record-list">
        <article v-for="record in records" :key="record.game_id" class="record-row">
          <div class="record-row__main">
            <div class="record-row__topline">
              <strong>{{ recordLabel(record) }}</strong>
              <time>{{ formatTime(record.created_at) }}</time>
            </div>
            <div class="record-row__players">
              <span v-for="player in record.players" :key="`${record.game_id}-${player.user_id}`" :class="{ 'record-row__self': isCurrentPlayer(player) }">
                <em>{{ player.rank ?? '—' }} 位</em><span class="record-player-name">{{ playerName(player) }}</span> <b>{{ formatScore(player.score) }}</b>
              </span>
            </div>
          </div>
          <div class="record-row__side">
            <el-button v-if="activeTab !== 'ladder' && activeTab !== 'local'" text class="record-favorite" :aria-label="record.is_favorite ? '取消收藏' : '收藏牌谱'" :loading="favoriteBusy === record.game_id" @click="toggleFavorite(record)">
              {{ record.is_favorite ? '★ 已收藏' : '☆ 收藏' }}
            </el-button>
            <el-button text @click="openGame(record.game_id)">回放</el-button>
          </div>
        </article>
      </div>
      <el-empty v-else :description="emptyText" :image-size="72" />
      <footer v-if="records.length && records.length < total" class="record-browser__footer">
        <el-button :loading="loadingMore" @click="loadMore">加载更多（{{ records.length }}/{{ total }}）</el-button>
      </footer>
    </div>
  </section>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { usePlayerAuthStore } from '@/stores/playerAuth'
import { game2dPlayerApi, publicApiGet } from '@/game2d/salasasa/api'
import { parseRecordShareInput } from '@/utils/recordShareLink'
import { getLocalGameRecord, isLocalOnlyGameId, listLocalGameRecords } from '@/utils/localGameRecordStore'

const router = useRouter()
const auth = usePlayerAuthStore()
const activeTab = ref('mine')
const recordId = ref('')
const records = ref([])
const total = ref(0)
const loading = ref(false)
const loadingMore = ref(false)
const favoriteBusy = ref('')
const PAGE_SIZE = 12

const requiresLogin = computed(() => !['ladder', 'local'].includes(activeTab.value) && !auth.isLoggedIn)
const emptyText = computed(() => {
  if (activeTab.value === 'favorite') return '还没有收藏的国标牌谱'
  if (activeTab.value === 'ladder') return '暂无最近天梯牌谱'
  if (activeTab.value === 'local') return '还没有本地牌谱。含机器人的对局会保存在这里。'
  return '还没有可阅览的国标牌谱'
})

function recordLabel(record) {
  if (activeTab.value === 'ladder') return `${ladderTierLabel(record.match_tier)} · 天梯对局`
  if (activeTab.value === 'local') {
    if (isLocalOnlyGameId(record.game_id)) return '本地对局'
    return record.room_type === 'match' ? '匹配对局' : '自定义房间'
  }
  return record.room_type === 'match' ? '匹配对局' : '自定义房间'
}

function ladderTierLabel(tier) {
  return ({ beginner: '初级场', intermediate: '中级场', advanced: '高级场', mcrpl: 'MCRPL' })[String(tier || '').toLowerCase()] || '天梯'
}

function formatTime(value) {
  if (!value) return '—'
  let date = new Date(value)
  if (Number.isNaN(date.getTime()) && typeof value === 'string') {
    date = new Date(value.replace(' ', 'T'))
  }
  if (Number.isNaN(date.getTime())) return String(value)
  return new Intl.DateTimeFormat('zh-CN', { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' }).format(date)
}

function formatScore(value) {
  const score = Number(value)
  if (!Number.isFinite(score)) return '—'
  return score > 0 ? `+${score}` : String(score)
}

function isCurrentPlayer(player) {
  return auth.userId != null && Number(player?.user_id) === Number(auth.userId)
}

function playerName(player) {
  const value = String(player?.username || `玩家 ${player?.user_id ?? ''}`)
  return value.length > 10 ? `${value.slice(0, 10)}…` : value
}

function openGame(gameId, query = {}) {
  const path = `/2d/record/${encodeURIComponent(String(gameId))}`
  const hasQuery = query.round != null || query.node != null
  router.push(hasQuery ? { path, query } : path)
}

function openRecord() {
  const parsed = parseRecordShareInput(recordId.value)
  if (!parsed) {
    ElMessage.warning('请输入正确的牌谱 ID 或 2D/3D 牌谱分享链接')
    return
  }
  const query = {}
  if (parsed.round != null) query.round = String(parsed.round)
  if (parsed.node != null) query.node = String(parsed.node)
  void (async () => {
    if (isLocalOnlyGameId(parsed.gameId) && !await getLocalGameRecord(parsed.gameId)) {
      ElMessage.warning('本机没有这份牌谱')
      return
    }
    openGame(parsed.gameId, query)
  })()
}

function goLogin() {
  router.push({ path: '/login', query: { redirect: '/2d' } })
}

async function loadRecords({ append = false } = {}) {
  if (requiresLogin.value) {
    records.value = []
    total.value = 0
    return
  }
  const offset = append ? records.value.length : 0
  if (append) loadingMore.value = true
  else loading.value = true
  try {
    if (activeTab.value === 'local') {
      const items = (await listLocalGameRecords()).filter((item) => !item.rule || item.rule === 'guobiao')
      records.value = items
      total.value = items.length
      return
    }
    let data
    if (activeTab.value === 'ladder') {
      data = await publicApiGet(`/platform/recent-records?limit=${PAGE_SIZE}&offset=${offset}`)
    } else {
      data = await game2dPlayerApi(`/my-records?limit=${PAGE_SIZE}&offset=${offset}&favorites_only=${activeTab.value === 'favorite' ? 1 : 0}`)
    }
    records.value = append ? [...records.value, ...(data.items || [])] : (data.items || [])
    total.value = Number(data.total || 0)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '牌谱列表加载失败')
  } finally {
    loading.value = false
    loadingMore.value = false
  }
}

async function toggleFavorite(record) {
  favoriteBusy.value = record.game_id
  try {
    const data = await game2dPlayerApi(`/my-records/${encodeURIComponent(record.game_id)}/favorite`, {
      method: 'POST',
      body: JSON.stringify({ is_favorite: !record.is_favorite }),
    })
    if (activeTab.value === 'favorite' && !data.is_favorite) {
      records.value = records.value.filter((item) => item.game_id !== record.game_id)
      total.value = Math.max(0, total.value - 1)
    } else {
      record.is_favorite = data.is_favorite
    }
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '收藏状态更新失败')
  } finally {
    favoriteBusy.value = ''
  }
}

function switchTab(tab) {
  if (activeTab.value === tab) return
  activeTab.value = tab
}

function loadMore() {
  if (activeTab.value === 'local') return
  void loadRecords({ append: true })
}

watch([activeTab, () => auth.isLoggedIn], () => void loadRecords(), { immediate: true })
</script>

<style scoped>
.record-browser { overflow: hidden; background: #fff; border: 1px solid #e3e7ed; border-radius: 10px; box-shadow: 0 6px 18px rgba(0,0,0,.035); }
.record-browser__header { min-height: 76px; padding: 14px 22px; display: flex; align-items: center; justify-content: space-between; gap: 24px; border-bottom: 1px solid #e8edf3; }
.record-browser__eyebrow { margin: 0 0 3px; color: #6b7d93; font-size: 11px; font-weight: 700; letter-spacing: .12em; }
.record-browser h2 { margin: 0; color: #202936; font-size: 22px; font-weight: 700; }
.record-browser__header p:not(.record-browser__eyebrow) { margin: 5px 0 0; color: #778397; font-size: 13px; }
.record-browser__lookup { width: min(450px, 45%); display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 8px; }
.record-browser__tabs { display: flex; border-bottom: 1px solid #e8edf3; background: #fbfcfe; }
.record-browser__tabs button { min-width: 130px; height: 48px; padding: 0 20px; color: #66758a; font: inherit; font-size: 14px; font-weight: 600; background: transparent; border: 0; border-right: 1px solid #e8edf3; cursor: pointer; }
.record-browser__tabs button.active { color: #1677ff; background: #fff; box-shadow: inset 0 -3px #1677ff; }
.record-browser__login { min-height: 180px; padding: 28px 30px; display: flex; align-items: center; justify-content: space-between; gap: 20px; }
.record-browser__login strong, .record-browser__login span { display: block; }
.record-browser__login strong { color: #283548; font-size: 16px; }
.record-browser__login span { margin-top: 5px; color: #7c8798; font-size: 13px; }
.record-browser__content { min-height: 240px; }
.record-list { display: grid; }
.record-row { min-height: 62px; display: flex; align-items: stretch; border-bottom: 1px solid #edf0f4; }
.record-row:hover { background: #f9fbfe; }
.record-row__main { width: max-content; max-width: calc(100% - 142px); min-width: 0; padding: 8px 20px 7px; flex: 0 1 auto; }
.record-row__topline { padding-left: .15em; display: flex; align-items: center; justify-content: flex-start; gap: 12px; }
.record-row__topline strong { color: #253248; font-size: 14px; }
.record-row__topline time { color: #8a95a6; font-size: 12px; white-space: nowrap; }
.record-row__players { width: max-content; max-width: 100%; margin-top: 4px; display: grid; grid-template-columns: repeat(4, 15.8em); gap: 0 8px; overflow: hidden; color: #5d6b7d; font-size: 14px; line-height: 1.35; }
.record-row__players > span { display: grid; grid-template-columns: 2.3em minmax(0, 10em) auto; align-items: center; min-width: 0; white-space: nowrap; }
.record-row__players em { margin-right: 4px; color: #8a95a6; font-style: normal; text-align: right; }
.record-player-name { max-width: 10em; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.record-row__players .record-row__self { color: #1a6fd1; font-weight: 700; }
.record-row__players .record-row__self em { color: inherit; }
.record-row__players b { margin-left: 2px; font-weight: 600; font-variant-numeric: tabular-nums; }
.record-row__side { padding: 0; display: flex; flex: 0 0 auto; align-items: center; justify-content: flex-start; gap: 4px; }
.record-favorite { width: 74px; justify-content: flex-start; }
.record-browser__footer { padding: 14px; display: flex; justify-content: center; }
@media (max-width: 720px) { .record-browser__header { align-items: stretch; flex-direction: column; } .record-browser__lookup { width: 100%; } .record-browser__tabs { overflow-x: auto; } .record-browser__tabs button { flex: 1 0 116px; } .record-row__main { max-width: calc(100% - 108px); padding-inline: 14px; } .record-row__topline { gap: 8px; } .record-row__players { grid-template-columns: repeat(2, 15.8em); gap: 4px 8px; } .record-row__side { padding-right: 4px; } .record-favorite { min-width: 0; } }
</style>
