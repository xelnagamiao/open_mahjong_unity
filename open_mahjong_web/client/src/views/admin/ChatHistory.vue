<template>
  <section class="history">
    <h2>聊天记录</h2>
    <el-card shadow="never">
      <el-form inline class="admin-inline-form" @submit.prevent="search">
        <el-form-item label="频道">
          <el-select v-model="filters.kind" style="width: 130px" @change="search">
            <el-option label="全部频道" value="all" />
            <el-option label="好友私聊" value="dm" />
            <el-option label="场馆群聊" value="venue" />
          </el-select>
        </el-form-item>
        <el-form-item label="发送人">
          <el-input v-model="filters.sender" clearable placeholder="用户 ID 或用户名" maxlength="255" />
        </el-form-item>
        <el-form-item label="消息内容">
          <el-input v-model="filters.keyword" clearable placeholder="正文关键词" maxlength="500" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" native-type="submit">查询</el-button>
        </el-form-item>
      </el-form>
      <el-alert v-if="error" :title="error" type="error" :closable="false" />
      <el-table :data="rows" row-key="message_id" v-loading="loading" empty-text="没有符合条件的聊天记录">
        <el-table-column label="发送时间" width="174">
          <template #default="{ row }">{{ new Date(row.created_at).toLocaleString('zh-CN', { hour12: false }) }}</template>
        </el-table-column>
        <el-table-column label="会话" min-width="155">
          <template #default="{ row }">
            <div>{{ titleOf(row) }}</div>
            <small>{{ row.kind === 'dm' ? '好友私聊' : row.venue_kind === 'base' ? '基地群聊' : '赛事群聊' }}</small>
          </template>
        </el-table-column>
        <el-table-column label="发送人" min-width="125">
          <template #default="{ row }">
            <router-link :to="{ name: 'AdminUserDetail', params: { userId: row.sender_user_id } }">{{ row.sender_name }}</router-link>
            <small>UID {{ row.sender_user_id }}</small>
          </template>
        </el-table-column>
        <el-table-column label="消息内容" min-width="240">
          <template #default="{ row }"><div class="content">{{ row.content }}</div></template>
        </el-table-column>
        <el-table-column label="操作" width="90" fixed="right">
          <template #default="{ row }"><el-button link type="primary" @click="selected = row">查看会话</el-button></template>
        </el-table-column>
      </el-table>
      <div class="pages">
        <el-button :disabled="loading || cursors.length < 2" @click="load(cursors.slice(0, -1))">上一页</el-button>
        <span>第 {{ cursors.length }} 页</span>
        <el-button :disabled="loading || !hasMore" @click="load([...cursors, rows[rows.length - 1].message_id])">下一页</el-button>
      </div>
    </el-card>
    <ChatConversation v-if="selected" :key="selected.message_id" :message="selected"
      :title="titleOf(selected)" @close="selected = null" />
  </section>
</template>

<script setup>
import { useAdminListState } from '@/composables/useAdminListState'
import { onMounted, reactive, ref } from 'vue'
import adminApi from '@/api/adminClient'
import ChatConversation from './ChatConversation.vue'

const { saved, remember } = useAdminListState()
const filters = reactive(saved.filters ?? { kind: 'all', sender: '', keyword: '' })
let applied = { ...filters }
const rows = ref([])
const cursors = ref(saved.cursors ?? [null])
const selected = ref(null)
const hasMore = ref(false)
const loading = ref(false)
const error = ref('')
let latest = 0

function titleOf(row) {
  if (row.kind === 'venue') return row.venue_name || row.event_id
  return [row.low_name || row.peer_low, row.high_name || row.peer_high].join(' ↔ ')
}

function search() {
  applied = { kind: filters.kind, sender: filters.sender.trim(), keyword: filters.keyword.trim() }
  cursors.value = [null]
  rows.value = []
  hasMore.value = false
  load(cursors.value)
}

async function load(pages) {
  remember({ filters: applied, cursors: [...pages] })
  const request = ++latest
  loading.value = true
  error.value = ''
  try {
    const { data } = await adminApi.get('/chat-history', {
      params: { ...applied, before: pages[pages.length - 1] || undefined },
    })
    if (request !== latest) return
    rows.value = data.data.items
    hasMore.value = data.data.has_more
    cursors.value = pages
  } catch (e) {
    if (request === latest) error.value = e.response?.data?.message || '读取聊天记录失败，请重试'
  } finally {
    if (request === latest) loading.value = false
  }
}

onMounted(() => load(cursors.value))
</script>

<style scoped>
.history { font-synthesis: none; }
h2 { margin: 0 0 16px; font-weight: 400; }
.history :deep(th), .history :deep(.el-button) { font-weight: 400; }
small { display: block; color: var(--el-text-color-secondary); }
.content { white-space: pre-wrap; overflow-wrap: anywhere; }
.pages { display: flex; align-items: center; gap: 12px; margin-top: 16px; font-size: 14px; }
</style>
