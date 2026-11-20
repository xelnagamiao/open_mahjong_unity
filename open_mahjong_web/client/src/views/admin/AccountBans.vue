<template>
  <section class="account-bans">
    <h2 class="page-title">账号封禁名单</h2>
    <el-card shadow="never">
      <el-form inline class="admin-inline-form" @submit.prevent="search">
        <el-form-item label="封禁类型">
          <el-select v-model="type" class="select-filter" @change="search">
            <el-option label="全部类型" value="all" />
            <el-option v-for="(label, key) in banLabels" :key="key" :label="label" :value="key" />
          </el-select>
        </el-form-item>
        <el-form-item label="状态">
          <el-select v-model="status" class="select-filter" @change="search">
            <el-option label="生效中" value="active" />
            <el-option label="已过期" value="expired" />
            <el-option label="全部状态" value="all" />
          </el-select>
        </el-form-item>
        <el-form-item label="用户">
          <el-input v-model="keyword" clearable placeholder="用户 ID 或用户名" @clear="search" />
        </el-form-item>
        <el-form-item><el-button native-type="submit" type="primary">查询</el-button></el-form-item>
      </el-form>
      <el-alert v-if="error" type="error" :title="error" :closable="false" />
      <el-table v-loading="loading" :data="items" empty-text="没有符合条件的封禁记录">
        <el-table-column prop="user_id" label="用户 ID" width="115" />
        <el-table-column prop="username" label="用户名" min-width="130" />
        <el-table-column label="封禁类型" width="115">
          <template #default="{ row }">{{ banLabels[row.ban_type] }}</template>
        </el-table-column>
        <el-table-column label="状态" width="90">
          <template #default="{ row }">
            <el-tag :type="row.active ? 'danger' : 'info'" size="small">{{ row.active ? '生效中' : '已过期' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="到期时间" min-width="165">
          <template #default="{ row }">{{ row.ban_expires_at ? new Date(row.ban_expires_at).toLocaleString('zh-CN') : '永久' }}</template>
        </el-table-column>
        <el-table-column label="封禁原因" min-width="180">
          <template #default="{ row }">{{ row.ban_reason || '—' }}</template>
        </el-table-column>
        <el-table-column label="操作" width="80" fixed="right">
          <template #default="{ row }"><router-link :to="{ name: 'AdminUserDetail', params: { userId: row.user_id }, query: { from: 'bans' } }">详情</router-link></template>
        </el-table-column>
      </el-table>
      <el-pagination class="pages" layout="total, prev, pager, next" :pager-count="5" :total="total"
        :page-size="20" :current-page="page" @current-change="load" />
    </el-card>
  </section>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import adminApi from '@/api/adminClient'
import { banLabels } from '@/utils/accountBans'

const type = ref('all')
const status = ref('active')
const keyword = ref('')
let appliedKeyword = ''
const items = ref([])
const total = ref(0)
const page = ref(1)
const loading = ref(false)
const error = ref('')
let latest = 0

function search() {
  appliedKeyword = keyword.value.trim()
  load(1)
}

async function load(requestedPage) {
  const request = ++latest
  loading.value = true
  error.value = ''
  try {
    const { data } = await adminApi.get('/users/bans', {
      params: { type: type.value === 'all' ? undefined : type.value, status: status.value, q: appliedKeyword, page: requestedPage },
    })
    if (request !== latest) return
    items.value = data.data.items
    total.value = data.data.total
    page.value = data.data.page
  } catch (e) {
    if (request !== latest) return
    items.value = []
    total.value = 0
    error.value = e.response?.data?.message || '读取封禁名单失败，请重新查询'
  } finally {
    if (request === latest) loading.value = false
  }
}

onMounted(search)
</script>

<style scoped>
.account-bans { font-synthesis: none; }
.account-bans :deep(th), .account-bans :deep(.el-button) { font-weight: 400; }
.page-title { margin: 0 0 16px; font-weight: 400; }
.select-filter { width: 135px; }
.pages { margin-top: 16px; flex-wrap: wrap; }
</style>
