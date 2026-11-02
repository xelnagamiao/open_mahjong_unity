<template>
  <el-card class="redeem-panel">
    <template #header>兑换码</template>
    <p>输入兑换码，获取奖励。</p>
    <el-form @submit.prevent="redeem">
      <el-form-item label="兑换码">
        <el-input v-model="code" maxlength="128" placeholder="请输入兑换码" :disabled="busy || !!pending" autocomplete="off" />
      </el-form-item>
      <el-button native-type="submit" type="primary" :loading="busy">{{ pending ? '重试领取' : '确定' }}</el-button>
      <el-button v-if="pending" :disabled="busy" @click="reset">更换兑换码</el-button>
    </el-form>
    <el-alert v-if="message" :title="message" type="warning" :closable="false" show-icon />
    <RewardReceipt :receipt="receipt" @confirm="receipt = null" />
  </el-card>
</template>
<script setup>
import { ref, watch } from 'vue'
import playerApi from '@/api/playerClient'
import { usePlayerAuthStore } from '@/stores/playerAuth'
import RewardReceipt from './RewardReceipt.vue'
const auth = usePlayerAuthStore()
const code = ref(''), busy = ref(false), message = ref(''), receipt = ref(null), pending = ref(null)
let generation = 0
function reset() { generation++; pending.value = null; message.value = ''; receipt.value = null; code.value = '' }
watch(() => auth.userId, reset)
async function redeem() {
  if (busy.value) return
  const raw = code.value.replace(/[\s-]/g, '')
  if (!/^[A-Za-z0-9]{8,32}$/.test(raw)) { message.value = '请输入 8–32 位字母或数字兑换码'; return }
  const value = raw.toUpperCase()
  pending.value ||= { request_id: crypto.randomUUID(), code: value }
  const owner = auth.userId, revision = generation
  busy.value = true; receipt.value = null; message.value = ''
  try {
    const { data } = await playerApi.post('/redeem', pending.value)
    if (owner !== auth.userId || revision !== generation) return
    receipt.value = data.data.reward_receipt
    pending.value = null; code.value = ''
    await auth.fetchMe().catch(() => {})
  } catch (e) {
    if (owner !== auth.userId || revision !== generation) return
    message.value = e.response?.data?.message || '领取结果暂未确认，请重试原操作'
    if (e.response && !e.response.data?.retryable && e.response.status < 500 && e.response.status !== 429) pending.value = null
  } finally { busy.value = false }
}
</script>
<style scoped>
.redeem-panel { max-width:900px; }.redeem-panel p { line-height:1.8; color:#536477; }.el-input { max-width:480px; }.el-alert { margin-top:20px; }
</style>
