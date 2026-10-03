<template>
  <div class="fun-data">
    <header class="page-heading"><div><h1>其他数据</h1><p>查看平台周榜和精选牌谱。</p></div></header>
    <p class="as-of-tip">
      <template v-if="week.date_from && week.date_to">
        本周统计区间 <strong>{{ week.date_from }}</strong> 至 <strong>{{ week.date_to }}</strong>
        （北京时间 04:00 切日，每天 04:00 刷新；按段位 PT 净变更累计）
      </template>
      <template v-else>北京时间每天 04:00 刷新最近七个完整统计日的段位 PT 变更榜。</template>
    </p>
    <p v-if="week.missing_pt_records > 0" class="as-of-tip">
      部分历史对局未保存 PT，当前榜单仅统计已记录的 PT 变更。
    </p>

    <div class="board-grid" v-loading="loading">
      <section class="section-card">
        <h3 class="section-title">本周上分最多</h3>
        <p v-if="!gainers.length" class="empty-hint">暂无上分记录</p>
        <ol v-else class="rank-list">
          <li v-for="row in gainers" :key="`up-${row.user_id}`" class="rank-row">
            <span class="place" :class="placeClass(row.place)">{{ row.place }}</span>
            <router-link class="name" :to="{ path: '/player-data', query: { player: String(row.user_id) } }">
              {{ row.username }}
            </router-link>
            <span class="games">{{ gamesLabel(row.games) }}</span>
            <span class="score pos">{{ formatScore(row.total_pt_change) }} PT</span>
          </li>
        </ol>
      </section>
      <section class="section-card">
        <h3 class="section-title">本周下分最多</h3>
        <p v-if="!losers.length" class="empty-hint">暂无下分记录</p>
        <ol v-else class="rank-list">
          <li v-for="row in losers" :key="`down-${row.user_id}`" class="rank-row">
            <span class="place" :class="placeClass(row.place)">{{ row.place }}</span>
            <router-link class="name" :to="{ path: '/player-data', query: { player: String(row.user_id) } }">
              {{ row.username }}
            </router-link>
            <span class="games">{{ gamesLabel(row.games) }}</span>
            <span class="score neg">{{ formatScore(row.total_pt_change) }} PT</span>
          </li>
        </ol>
      </section>
    </div>

    <section class="section-card" v-loading="loading">
      <h3 class="section-title">精选牌谱</h3>
      <p v-if="!loading && !classics.length" class="empty-hint">暂无经典牌谱</p>
      <ul v-else-if="classics.length" class="classic-list">
        <li v-for="item in classics" :key="item.id" class="classic-card">
          <div class="classic-main">
            <div class="classic-title">{{ item.description }}</div>
            <div class="classic-meta">
              <span v-if="item.round">{{ roundLabel(item.round) }}</span>
              <span v-if="item.node != null">{{ nodeLabel(item.node) }}</span>
              <span v-if="item.missing" class="missing">牌谱已失效</span>
            </div>
            <div v-if="item.players?.length" class="classic-players">
              <router-link
                v-for="p in item.players"
                :key="`${item.id}-${p.user_id}`"
                class="player"
                :to="{ path: '/player-data', query: { player: String(p.user_id) } }"
              >
                <span class="rank-badge" :class="`rank-${p.rank}`">{{ p.rank }}</span>
                {{ p.username }}
                <span class="player-score" :class="scoreClass(p.score)">{{ formatScore(p.score) }}</span>
              </router-link>
            </div>
          </div>
          <div class="classic-actions">
            <a
              v-if="item.show_2d !== false"
              class="replay-link"
              :href="item.url_2d"
              target="_blank"
              rel="noopener"
            >2D 回放</a>
            <a class="replay-link" :href="item.url_3d" target="_blank" rel="noopener">3D 回放</a>
          </div>
        </li>
      </ul>
    </section>
  </div>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import axios from 'axios'
import { ElMessage } from 'element-plus'

const loading = ref(false)
const week = ref({})
const gainers = ref([])
const losers = ref([])
const classics = ref([])

function formatScore(score) {
  const n = Number(score)
  if (!Number.isFinite(n)) return '-'
  return n > 0 ? `+${n}` : String(n)
}

function scoreClass(score) {
  const n = Number(score)
  if (n > 0) return 'pos'
  if (n < 0) return 'neg'
  return ''
}

function gamesLabel(games) {
  return `${games} 局`
}

function roundLabel(round) {
  return `第${round}局`
}

function nodeLabel(node) {
  return `节点 ${node}`
}

function placeClass(place) {
  if (place === 1) return 'place-1'
  if (place === 2) return 'place-2'
  if (place === 3) return 'place-3'
  return ''
}

async function loadFunStats() {
  loading.value = true
  try {
    const res = await axios.get('/api/platform/fun-stats')
    const data = res.data?.data || {}
    week.value = data.week || {}
    gainers.value = data.gainers || []
    losers.value = data.losers || []
    classics.value = data.classics || []
  } catch (err) {
    week.value = {}
    gainers.value = []
    losers.value = []
    classics.value = []
    ElMessage.error(err.response?.data?.message || '其他数据读取失败')
  } finally {
    loading.value = false
  }
}

onMounted(loadFunStats)
</script>

<style scoped>
.fun-data { color: #1f2329; }
.page-heading { display:flex; align-items:flex-end; justify-content:space-between; gap:16px; margin: 8px 0 18px; padding-bottom:14px; border-bottom:1px solid #dcdfe6; }
.page-heading h1 { margin:0 0 5px; font-size:24px; font-weight:700; color:#1f2329; }
.page-heading p { margin:0; color:#606266; font-size:13px; }
.as-of-tip {
  margin: 0 0 14px;
  font-size: 13px;
  color: #606266;
  padding: 10px 12px;
  background: #fff;
  border: 1px solid #ebeef5;
  border-radius: 6px;
}
.board-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
  margin-bottom: 16px;
  min-height: 120px;
}
.section-card {
  background: #fff;
  border: 1px solid #ebeef5;
  border-radius: 8px;
  padding: 16px;
  margin-bottom: 16px;
}
.board-grid .section-card { margin-bottom: 0; }
.section-title {
  margin: 0 0 12px;
  font-size: 16px;
  font-weight: 600;
}
.empty-hint {
  margin: 0;
  font-size: 13px;
  color: #94a3b8;
}
.rank-list {
  list-style: none;
  margin: 0;
  padding: 0;
}
.rank-row {
  display: grid;
  grid-template-columns: 28px minmax(0, 1fr) auto auto;
  gap: 8px;
  align-items: center;
  padding: 7px 0;
  border-bottom: 1px dashed #eef0f3;
  font-size: 13px;
}
.rank-row:last-child { border-bottom: none; }
.place {
  width: 22px;
  height: 22px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-family: Consolas, Menlo, monospace;
  font-weight: 700;
  font-size: 12px;
  color: #64748b;
}
.place-1 { background: #ffc832; color: #5a4500; }
.place-2 { background: #c9d4e0; color: #2c3848; }
.place-3 { background: #e6a173; color: #4a2f1a; }
.name {
  color: #303133;
  text-decoration: none;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.name:hover { color: #409eff; }
.games {
  color: #94a3b8;
  font-size: 12px;
  font-family: Consolas, Menlo, monospace;
  text-align: right;
  white-space: nowrap;
}
.score {
  font-weight: 700;
  font-family: Consolas, Menlo, monospace;
  min-width: 88px;
  margin-left: 16px;
  text-align: right;
  white-space: nowrap;
}
.score.pos, .player-score.pos { color: #c0392b; }
.score.neg, .player-score.neg { color: #2c7a2c; }
.classic-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.classic-card {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  padding: 12px;
  border: 1px solid #eef0f3;
  border-radius: 6px;
}
.classic-title {
  font-size: 15px;
  font-weight: 600;
  color: #1f2329;
}
.classic-meta {
  margin-top: 4px;
  font-size: 12px;
  color: #94a3b8;
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
}
.missing { color: #e6a23c; }
.classic-players {
  margin-top: 8px;
  display: flex;
  flex-wrap: wrap;
  gap: 8px 14px;
}
.player {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  color: #475569;
  text-decoration: none;
  font-size: 12px;
}
.player:hover { color: #409eff; }
.player-score {
  font-family: Consolas, Menlo, monospace;
  font-weight: 700;
}
.rank-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 16px;
  height: 16px;
  padding: 0 3px;
  font-weight: 700;
  font-size: 11px;
  font-family: Consolas, Menlo, monospace;
}
.rank-1 { background: #6fd86f; color: #1f5e1f; }
.rank-2 { background: #5dadff; color: #fff; }
.rank-3 { background: #aab4c2; color: #2c3848; }
.rank-4 { background: #ff7a7a; color: #fff; }
.classic-actions {
  display: flex;
  flex-direction: column;
  gap: 6px;
  flex-shrink: 0;
}
.replay-link {
  font-size: 13px;
  color: #409eff;
  text-decoration: none;
  white-space: nowrap;
}
.replay-link:hover { text-decoration: underline; }
@media (max-width: 720px) {
  .board-grid { grid-template-columns: 1fr; }
  .classic-card {
    flex-direction: column;
  }
  .classic-actions { flex-direction: row; }
}
</style>
