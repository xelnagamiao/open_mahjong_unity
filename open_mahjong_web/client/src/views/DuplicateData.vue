<template>
  <section class="duplicate-data">
    <div class="heading"><div><h1>复式密钥查询</h1><p>查看已解除锁定的密钥、创建时间、解锁时间和关联对局。</p></div><router-link to="/duplicate">创建复式密钥 →</router-link></div>
    <el-form class="search" @submit.prevent="search"><el-input v-model="query" clearable maxlength="40" placeholder="输入复式密钥；留空查看最近解除锁定的密钥" aria-label="复式密钥" /><el-button type="primary" native-type="submit" :loading="loading">查询</el-button></el-form>
    <el-alert v-if="error" :title="error" type="error" :closable="false" class="notice" />
    <p class="help">锁定的密钥不公开。解除锁定后可在这里查询，且不能重新锁定。复式对局不保存本地牌谱。</p>
    <el-card shadow="never"><el-table v-loading="loading" :data="items" empty-text="没有找到已解禁的复式密钥">
      <el-table-column label="名称 / 密钥" min-width="260"><template #default="{ row }"><strong>{{ row.name || '未命名密钥' }}</strong><div><code>{{ row.key }}</code></div></template></el-table-column>
      <el-table-column label="创建者 / 所属赛事" min-width="170">
        <template #default="{ row }">
          <router-link v-if="row.scope === 'event'" :to="`/events/${encodeURIComponent(row.event_id)}`">{{ row.event_name || row.event_id }}</router-link>
          <router-link v-else-if="row.owner_user_id" :to="{ path: '/player-data', query: { player: String(row.owner_user_id) } }">{{ row.owner_username || `用户 ${row.owner_user_id}` }}</router-link>
          <span v-else>—</span>
        </template>
      </el-table-column>
      <el-table-column label="牌山类型 / 局数" min-width="120"><template #default="{ row }">{{ duplicateWallLabel(row.wall_type) }} · {{ row.round_count || 1 }} 局</template></el-table-column>
      <el-table-column label="规则" min-width="90"><template #default="{ row }">{{ ruleLabel(row.rule) }}</template></el-table-column>
      <el-table-column label="最近对局结束时间" min-width="180"><template #default="{ row }">{{ duplicateDate(row.ended_at) }}</template></el-table-column>
      <el-table-column label="创建时间 / 解锁时间" min-width="185"><template #default="{ row }"><div>创建 {{ duplicateDate(row.created_at) }}</div><div>解锁 {{ duplicateDate(row.unlocked_at) }}</div></template></el-table-column>
      <el-table-column label="删除状态" min-width="180"><template #default="{ row }"><el-tag :type="row.is_deleted || row.deleted_at ? 'info' : 'success'">{{ row.is_deleted || row.deleted_at ? '已删除' : '未删除' }}</el-tag><div v-if="row.deleted_at" class="help">{{ duplicateDate(row.deleted_at) }}</div></template></el-table-column>
      <el-table-column label="" width="115" fixed="right"><template #default="{ row }"><el-button link type="primary" @click="showDetail(row.key)">查看全部对局</el-button></template></el-table-column>
    </el-table></el-card>
    <el-card v-if="selectedKey" v-loading="detailLoading" class="detail" shadow="never">
      <template #header><div class="detail-heading"><strong>{{ detail?.wall?.name || '密钥关联对局' }}</strong><el-button text @click="showDetail(selectedKey)">刷新</el-button></div><code>{{ selectedKey }}</code></template>
      <template v-if="detail">
        <div class="detail-meta">
          <span v-if="detail.wall.scope === 'event'">所属赛事：<router-link :to="`/events/${encodeURIComponent(detail.wall.event_id)}`">{{ detail.wall.event_name || detail.wall.event_id }}</router-link></span>
          <span v-else>创建者：<router-link v-if="detail.wall.owner_user_id" :to="{ path: '/player-data', query: { player: String(detail.wall.owner_user_id) } }">{{ detail.wall.owner_username || `用户 ${detail.wall.owner_user_id}` }}</router-link><span v-else>—</span></span>
        </div>
        <div class="detail-meta"><el-tag>{{ duplicateWallLabel(detail.wall.wall_type) }}</el-tag><span>{{ ruleLabel(detail.wall.rule) }}</span><span>最近对局结束时间：{{ duplicateDate(detail.wall.ended_at) }}</span><span>每场 {{ detail.wall.round_count || 1 }} 局</span><span>对局数：{{ detail.games.length }}</span></div>
        <p class="help">结束时间取该密钥最近一场已结束对局的时间。</p>
        <div class="detail-meta"><span>创建时间：{{ duplicateDate(detail.wall.created_at) }}</span><span>解锁时间：{{ duplicateDate(detail.wall.unlocked_at) }}</span><span>{{ detail.wall.is_deleted || detail.wall.deleted_at ? `已删除 · ${duplicateDate(detail.wall.deleted_at)}` : '未删除' }}</span><span>花牌：{{ detail.wall.use_flowers === false ? '无花' : '有花' }}</span></div>
        <el-table :data="detail.games" empty-text="该密钥还没有对局记录">
          <el-table-column prop="game_id" label="对局编号" min-width="140" />
          <el-table-column label="所属赛事" min-width="150"><template #default="{ row }"><router-link v-if="row.event_id" :to="`/events/${encodeURIComponent(row.event_id)}`">{{ row.event_name || row.event_id }}</router-link><span v-else>个人对局</span></template></el-table-column>
          <el-table-column label="玩家" min-width="240"><template #default="{ row }">{{ playerNames(row) }}</template></el-table-column>
          <el-table-column label="结束时间" min-width="180"><template #default="{ row }">{{ duplicateDate(row.ended_at || row.end_time || row.created_at) }}</template></el-table-column>
          <el-table-column label="牌谱" width="110"><template #default="{ row }"><div class="record-links"><router-link v-if="String(row.rule).startsWith('guobiao')" :to="`/2d/record/${encodeURIComponent(row.game_id)}`">2D</router-link><router-link :to="{ path: '/game-unity', query: { recordId: row.game_id } }">3D</router-link></div></template></el-table-column>
        </el-table>
      </template>
    </el-card>
  </section>
</template>

<script setup>
import { onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { duplicateWallsApi } from '@/api/duplicateWalls'
import { duplicateDate, duplicateWallLabel } from '@/utils/duplicateWalls'

const route = useRoute()
const router = useRouter()
const query = ref(String(route.query.key || ''))
const items = ref([])
const rules = ref([])
const detail = ref(null)
const selectedKey = ref('')
const loading = ref(false)
const detailLoading = ref(false)
const error = ref('')
let searchVersion = 0
let detailVersion = 0
function ruleLabel(rule) { return rules.value.find(item => item.rule === rule)?.label || rule }
function playerNames(game) {
  if (Array.isArray(game.players)) return game.players.map(player => typeof player === 'string' ? player : player.username || player.name || player.user_id).filter(Boolean).join(' / ') || '—'
  return [0, 1, 2, 3].map(index => game[`p${index}_name`]).filter(Boolean).join(' / ') || '—'
}
function failure(err) { return err.response?.data?.message || err.message || '查询失败，请重试' }
async function showDetail(key) {
  const version = ++detailVersion
  selectedKey.value = key
  detail.value = null
  detailLoading.value = true
  error.value = ''
  try { const data = await duplicateWallsApi.publicDetail(key); if (version === detailVersion) detail.value = { ...data, games: data?.games || [] } }
  catch (err) { if (version === detailVersion) error.value = failure(err) }
  finally { if (version === detailVersion) detailLoading.value = false }
}
async function load() {
  const version = ++searchVersion
  ++detailVersion
  detailLoading.value = false
  detail.value = null
  selectedKey.value = ''
  items.value = []
  error.value = ''
  loading.value = true
  const key = query.value.trim()
  try { const data = await duplicateWallsApi.publicList(key); if (version !== searchVersion) return; items.value = data?.items || []; if (key && items.value.some(item => item.key === key)) await showDetail(key) }
  catch (err) { if (version === searchVersion) error.value = failure(err) }
  finally { if (version === searchVersion) loading.value = false }
}
async function search() {
  const key = query.value.trim()
  if (key === String(route.query.key || '')) await load()
  else await router.replace({ query: key ? { key } : {} })
}
watch(() => route.query.key, value => { query.value = String(value || ''); load() })
onMounted(async () => { const results = await Promise.allSettled([duplicateWallsApi.catalog(), load()]); if (results[0].status === 'fulfilled') rules.value = results[0].value?.rules || [] })
</script>

<style scoped>
.heading { display: flex; justify-content: space-between; align-items: baseline; gap: 20px; margin: 14px 0 24px; }
h1 { font-size: 26px; margin: 0 0 8px; }
.heading p { margin: 0; color: #606266; }
a { color: #17756a; }
.heading a { white-space: nowrap; }
.search { display: flex; gap: 10px; max-width: 760px; }
.help { color: #737d88; font-size: 12px; line-height: 1.7; }
.notice, .detail { margin-top: 20px; }
code { font-size: 12px; word-break: break-all; }
.detail-heading, .detail-meta { display: flex; align-items: center; flex-wrap: wrap; gap: 18px; }
.detail-heading { justify-content: space-between; }
.record-links { display: flex; gap: 14px; }
.detail-meta { font-size: 14px; }
@media (max-width: 640px) { .heading { flex-direction: column; gap: 12px; } }
</style>
