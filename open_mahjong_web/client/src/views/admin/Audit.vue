<template>
  <div>
    <h2 class="page-title">操作审计</h2>
    <el-card>
      <el-form inline class="admin-inline-form" @submit.prevent="load">
        <el-form-item label="管理员 ID">
          <el-input
            v-model="filters.admin_id"
            clearable
            style="width: 140px"
          />
        </el-form-item>
        <el-form-item label="操作">
          <el-input v-model="filters.action" placeholder="rank.update" clearable style="width: 160px" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="load" :loading="loading">刷新</el-button>
        </el-form-item>
      </el-form>
      <el-table :data="items" v-loading="loading" style="margin-top: 16px">
        <el-table-column prop="id" label="#" width="70" />
        <el-table-column prop="created_at" label="时间" width="170">
          <template #default="{ row }">{{ formatDate(row.created_at) }}</template>
        </el-table-column>
        <el-table-column prop="admin_user_id" label="管理员" width="100" />
        <el-table-column prop="action" label="操作" width="140" />
        <el-table-column label="目标" width="140">
          <template #default="{ row }">
            {{ row.target_type }} / {{ row.target_id }}
          </template>
        </el-table-column>
        <el-table-column prop="reason" label="原因" show-overflow-tooltip />
        <el-table-column label="详情" width="80">
          <template #default="{ row }">
            <el-button link @click="showPayload(row)">查看</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dialogVisible" title="审计详情" width="560px">
      <pre class="payload-json">{{ payloadText }}</pre>
    </el-dialog>
  </div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue'
import adminApi from '@/api/adminClient'

const filters = reactive({ admin_id: '10000001', action: '' })
const items = ref([])
const loading = ref(false)
const dialogVisible = ref(false)
const payloadText = ref('')

function formatDate(v) {
  return v ? new Date(v).toLocaleString('zh-CN') : '-'
}

function showPayload(row) {
  payloadText.value = JSON.stringify(row.payload, null, 2) || '(无)'
  dialogVisible.value = true
}

async function load() {
  loading.value = true
  try {
    const params = { limit: 50 }
    if (filters.admin_id) params.admin_id = filters.admin_id
    if (filters.action) params.action = filters.action
    const res = await adminApi.get('/audit', { params })
    items.value = res.data.data.items
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<style scoped>
.page-title {
  margin: 0 0 16px;
}
.payload-json {
  max-height: 400px;
  overflow: auto;
  font-size: 12px;
}
</style>
