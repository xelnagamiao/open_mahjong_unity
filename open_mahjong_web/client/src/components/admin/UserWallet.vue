<template>
  <el-card class="wallet-card" v-loading="loading">
    <template #header>萨拉币</template>
    <p>当前余额：{{ wallet ? money(wallet.balance_minor) : '—' }} 萨拉币
      <el-button link type="primary" @click="load">刷新</el-button>
    </p>
    <el-form inline class="admin-inline-form" @submit.prevent="grant">
      <el-form-item label="发放金额">
        <el-input-number v-model="amount" :min="0.01" :max="1000000" :precision="2" :step="1" :disabled="!!pending || busy" />
      </el-form-item>
      <el-form-item label="发放原因">
        <el-input v-model="reason" maxlength="500" placeholder="必填，最多 500 字" :disabled="!!pending || busy" />
      </el-form-item>
      <el-form-item>
        <el-button type="primary" :loading="busy" :disabled="!wallet" @click="grant">{{ pending ? '重试本次发放' : '发放萨拉币' }}</el-button>
      </el-form-item>
    </el-form>
    <p v-if="pending" class="wallet-hint">上次发放结果未确认，请重试本次发放。重复请求不会重复入账。</p>
    <p class="wallet-hint">最近 20 次后台发放记录；完整操作记录可在审计日志查询。</p>
    <el-table :data="wallet?.grants || []">
      <el-table-column label="时间" min-width="170"><template #default="{ row }">{{ new Date(row.created_at).toLocaleString('zh-CN') }}</template></el-table-column>
      <el-table-column label="发放金额" width="130"><template #default="{ row }">{{ money(row.delta_minor) }}</template></el-table-column>
      <el-table-column label="发放后余额" width="140"><template #default="{ row }">{{ money(row.balance_after) }}</template></el-table-column>
      <el-table-column label="操作人 ID" width="130"><template #default="{ row }">{{ row.metadata.admin_user_id }}</template></el-table-column>
      <el-table-column label="原因" min-width="180"><template #default="{ row }">{{ row.metadata.reason }}</template></el-table-column>
    </el-table>
  </el-card>
</template>

<script setup>
import { ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import adminApi from '@/api/adminClient'

const props = defineProps({ userId: { type: [String, Number], required: true } })
const wallet = ref(null)
const amount = ref(1)
const reason = ref('')
const pending = ref(null)
const busy = ref(false)
const loading = ref(false)
const storageKey = () => `admin-wallet-grant:${props.userId}`
function money(value) {
  const n = BigInt(value ?? 0)
  return `${n / 100n}.${String(n % 100n).padStart(2, '0')}`
}
async function load() {
  loading.value = true
  try {
    wallet.value = (await adminApi.get(`/wallet/${props.userId}`)).data.data
  } catch (error) {
    ElMessage.error(error.response?.data?.message || '读取萨拉币失败')
  } finally { loading.value = false }
}
function clearPending() {
  sessionStorage.removeItem(storageKey())
  pending.value = null
}
async function grant() {
  if (busy.value) return
  if (!pending.value && (!Number.isFinite(amount.value) || amount.value < 0.01 || !reason.value.trim())) {
    ElMessage.warning('请填写发放金额和原因')
    return
  }
  busy.value = true
  try {
    if (!pending.value) {
      const payload = { amount_minor: Math.round(amount.value * 100), reason: reason.value.trim(), request_id: crypto.randomUUID() }
      await ElMessageBox.confirm(`向用户 ${props.userId} 发放 ${money(payload.amount_minor)} 萨拉币？原因：${payload.reason}`, '确认发放', { type: 'warning' })
      // Persist before sending so a refresh or lost response can retry the same operation.
      sessionStorage.setItem(storageKey(), JSON.stringify(payload))
      pending.value = payload
    }
    const result = (await adminApi.post(`/wallet/${props.userId}/grant`, pending.value)).data.data
    clearPending()
    reason.value = ''
    ElMessage.success(result.duplicate ? '本次发放已入账，未重复发放' : '萨拉币已发放')
    await load()
  } catch (error) {
    if (error === 'cancel' || error === 'close') return
    if (error.response?.status >= 400 && error.response?.status < 500) clearPending()
    ElMessage.error(error.response?.data?.message || '发放结果未确认，请重试本次发放')
  } finally { busy.value = false }
}
watch(() => props.userId, () => {
  wallet.value = null
  try { pending.value = JSON.parse(sessionStorage.getItem(storageKey()) || 'null') } catch { pending.value = null }
  if (pending.value) { amount.value = pending.value.amount_minor / 100; reason.value = pending.value.reason }
  load()
}, { immediate: true })
</script>

<style scoped>
.wallet-card { margin-top: 16px; font-weight: 400; }
.wallet-hint { color: #909399; font-size: 13px; }
</style>
