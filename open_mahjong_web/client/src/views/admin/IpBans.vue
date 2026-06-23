<template>
  <div>
    <h2 class="page-title">IP 封禁</h2>
    <el-card>
      <template #header>新增 / 更新封禁</template>
      <el-form inline class="admin-inline-form" @submit.prevent="submitBan">
        <el-form-item label="IP 地址">
          <el-input v-model="form.ip_address" clearable style="width: 180px" placeholder="如 1.2.3.4" />
        </el-form-item>
        <el-form-item label="到期时间">
          <el-date-picker
            v-model="form.ban_expires_at"
            type="datetime"
            placeholder="留空=永久"
            value-format="YYYY-MM-DD HH:mm:ss"
            style="width: 220px"
            clearable
          />
        </el-form-item>
        <el-form-item label="封禁原因">
          <el-input v-model="form.ban_reason" clearable style="width: 200px" />
        </el-form-item>
        <el-form-item label="操作原因">
          <el-input v-model="form.reason" clearable style="width: 200px" placeholder="审计必填" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="submitBan" :loading="saving">封禁</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card style="margin-top: 16px">
      <template #header>封禁列表</template>
      <el-table :data="items" v-loading="loading" size="small">
        <el-table-column prop="ip_address" label="IP" min-width="140" />
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="banStatusOf(row).tagType" size="small">{{ banStatusOf(row).label }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="到期" min-width="160">
          <template #default="{ row }">
            {{ row.ban_expires_at ? formatDate(row.ban_expires_at) : '永久' }}
          </template>
        </el-table-column>
        <el-table-column prop="ban_reason" label="原因" min-width="160" show-overflow-tooltip />
        <el-table-column label="更新时间" min-width="160">
          <template #default="{ row }">{{ formatDate(row.updated_at) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="100">
          <template #default="{ row }">
            <el-button link type="danger" @click="unban(row)">解封</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import adminApi from '@/api/adminClient'

const items = ref([])
const loading = ref(false)
const saving = ref(false)
const form = reactive({
  ip_address: '',
  ban_expires_at: null,
  ban_reason: '',
  reason: '',
})

function formatDate(v) {
  return v ? new Date(v).toLocaleString('zh-CN') : '-'
}

function banStatusOf(row) {
  if (!row.ban_expires_at) {
    return { label: '永久', tagType: 'danger' }
  }
  const expires = new Date(row.ban_expires_at).getTime()
  if (expires <= Date.now()) {
    return { label: '已过期', tagType: 'info' }
  }
  return { label: '生效中', tagType: 'warning' }
}

async function load() {
  loading.value = true
  try {
    const res = await adminApi.get('/ip-bans')
    items.value = res.data.data.items
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '加载失败')
  } finally {
    loading.value = false
  }
}

async function submitBan() {
  if (!form.ip_address.trim()) {
    ElMessage.warning('请输入 IP 地址')
    return
  }
  if (!form.reason.trim()) {
    ElMessage.warning('请填写操作原因')
    return
  }
  saving.value = true
  try {
    await adminApi.post('/ip-bans', {
      ip_address: form.ip_address.trim(),
      ban_expires_at: form.ban_expires_at,
      ban_reason: form.ban_reason,
      reason: form.reason.trim(),
    })
    ElMessage.success('IP 封禁已保存')
    form.ip_address = ''
    form.ban_expires_at = null
    form.ban_reason = ''
    form.reason = ''
    await load()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '操作失败')
  } finally {
    saving.value = false
  }
}

async function unban(row) {
  const { value: reason } = await ElMessageBox.prompt('请输入解封原因', '解除 IP 封禁', {
    inputValue: `解除封禁 ${row.ip_address}`,
    type: 'warning',
  }).catch(() => null)
  if (!reason?.trim()) return
  try {
    await adminApi.delete(`/ip-bans/${encodeURIComponent(row.ip_address)}`, {
      data: { reason: reason.trim() },
    })
    ElMessage.success('已解封')
    await load()
  } catch (e) {
    ElMessage.error(e.response?.data?.message || '解封失败')
  }
}

onMounted(load)
</script>

<style scoped>
.page-title {
  margin: 0 0 16px;
}
</style>
