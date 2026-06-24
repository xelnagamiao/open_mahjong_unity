<template>
  <div>
    <h2 class="page-title">用户管理</h2>
    <el-card>
      <el-form inline class="admin-inline-form" @submit.prevent="search">
        <el-form-item label="用户 ID / 用户名">
          <el-input v-model="query" clearable style="width: 220px" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="search" :loading="loading">搜索</el-button>
        </el-form-item>
      </el-form>
      <el-table :data="items" v-loading="loading" style="margin-top: 16px">
        <el-table-column prop="user_id" label="ID" width="120" />
        <el-table-column prop="username" label="用户名" />
        <el-table-column label="类型" width="90">
          <template #default="{ row }">
            <el-tag :type="row.is_tourist ? 'info' : 'success'" size="small">
              {{ row.is_tourist ? '游客' : '注册' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="赞助" min-width="180">
          <template #default="{ row }">
            <el-tag :type="sponsorOf(row).tagType" size="small">{{ sponsorOf(row).label }}</el-tag>
            <span v-if="sponsorOf(row).remaining" class="sponsor-remaining">
              {{ sponsorOf(row).remaining }}
            </span>
          </template>
        </el-table-column>
        <el-table-column label="封禁" width="110">
          <template #default="{ row }">
            <el-tag :type="banOf(row).tagType" size="small">{{ banOf(row).label }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="牌谱" width="80">
          <template #default="{ row }">{{ row.has_game_records ? '有' : '无' }}</template>
        </el-table-column>
        <el-table-column prop="created_at" label="注册时间" width="180">
          <template #default="{ row }">{{ formatDate(row.created_at) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="100">
          <template #default="{ row }">
            <el-button link type="primary" @click="$router.push(`/admin/users/${row.user_id}`)">
              详情
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { ref } from 'vue'
import { ElMessage } from 'element-plus'
import adminApi from '@/api/adminClient'
import { getSponsorStatus } from '@/utils/sponsor'

const query = ref('10000001')
const items = ref([])
const loading = ref(false)

function formatDate(v) {
  if (!v) return '-'
  return new Date(v).toLocaleString('zh-CN')
}

function sponsorOf(row) {
  return getSponsorStatus(row.sponsor_expires_at)
}

const BAN_TYPE_LABELS = {
  login: '禁登',
  chat: '禁言',
  match: '禁排',
  full: '全封',
}

function banOf(row) {
  if (!row.ban_type) {
    return { label: '正常', tagType: 'success' }
  }
  const expires = row.ban_expires_at ? new Date(row.ban_expires_at).getTime() : null
  if (expires !== null && expires <= Date.now()) {
    return { label: '已过期', tagType: 'info' }
  }
  return {
    label: BAN_TYPE_LABELS[row.ban_type] || row.ban_type,
    tagType: row.ban_type === 'login' || row.ban_type === 'full' ? 'danger' : 'warning',
  }
}

async function search() {
  if (!query.value.trim()) {
    ElMessage.warning('请输入搜索关键词')
    return
  }
  loading.value = true
  try {
    const res = await adminApi.get('/users/search', { params: { q: query.value.trim() } })
    items.value = res.data.data.items
    if (items.value.length === 0) ElMessage.info('未找到用户')
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '搜索失败')
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.page-title {
  margin: 0 0 16px;
}
.sponsor-remaining {
  margin-left: 8px;
  font-size: 12px;
  color: #606266;
}
</style>
