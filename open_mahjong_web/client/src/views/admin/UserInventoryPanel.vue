<template>
  <el-card v-loading="loading">
    <template #header><div class="header"><span>角色、道具与装扮</span><el-button link type="primary" :disabled="busy" @click="load">刷新</el-button></div></template>
    <p class="hint">玩家点击主菜单“背包”，在背包分类中使用改名卡；每张增加一次账号改名机会。</p>
    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />
    <div class="form">
      <el-select v-model="selected" placeholder="选择要发放的物品" filterable :disabled="busy || !!pending || loading || !!error"><el-option v-for="item in grantOptions" :key="item.item_id" :value="item.item_id" :label="`${categories[item.category]} · ${item.name}`" /></el-select>
      <el-input-number v-model="quantity" :min="1" :max="selectedItem?.category === 'item' ? 1000000 : 1" :disabled="busy || !!pending" />
      <el-input v-model="reason" maxlength="500" placeholder="发放原因（必填）" :disabled="busy || !!pending" />
      <el-button type="primary" :disabled="!selected || loading || !!error || !!pending" :loading="busy" @click="grant">发放</el-button>
    </div>
    <el-alert v-if="pending && !busy" type="warning" :closable="false" show-icon title="上次操作结果尚未确认，原操作编号已保留。重试不会重复发放或回收。"><el-button size="small" @click="submitPending">重试原操作</el-button></el-alert>
    <el-table :data="owned" empty-text="暂无物品">
      <el-table-column prop="name" label="物品" min-width="150" />
      <el-table-column label="分类" width="90"><template #default="{ row }">{{ categories[row.category] }}</template></el-table-column>
      <el-table-column prop="quantity" label="数量" width="90" />
      <el-table-column label="状态" min-width="160"><template #default="{ row }"><el-tag v-if="row.is_default">默认可用</el-tag><el-tag v-if="row.equipped" type="success">已装备</el-tag><el-tag v-if="!row.use_enabled" type="info">已停用</el-tag></template></el-table-column>
      <el-table-column label="操作" width="85"><template #default="{ row }"><el-button link type="danger" :disabled="busy || !!pending || row.is_default" @click="openRevoke(row)">回收</el-button></template></el-table-column>
    </el-table>
    <el-collapse class="ledger"><el-collapse-item title="物品流水（最近 100 条）"><el-table :data="ledger" size="small" empty-text="暂无变更流水"><el-table-column prop="name" label="物品" min-width="130" /><el-table-column prop="delta" label="变动" width="70" /><el-table-column prop="quantity_after" label="变动后" width="80" /><el-table-column prop="action" label="操作" width="90" /><el-table-column prop="reason" label="原因" min-width="180" /><el-table-column label="时间" min-width="180"><template #default="{ row }">{{ new Date(row.created_at).toLocaleString() }}</template></el-table-column></el-table></el-collapse-item></el-collapse>
    <el-dialog v-model="revokeVisible" title="回收物品" width="460px" :close-on-click-modal="false">
      <p>{{ revokeForm.name }} · 当前数量 {{ revokeForm.max }}</p>
      <el-form label-width="75px"><el-form-item label="数量"><el-input-number v-model="revokeForm.quantity" :min="1" :max="revokeForm.max" /></el-form-item><el-form-item label="原因" required><el-input v-model="revokeForm.reason" maxlength="500" /></el-form-item></el-form>
      <p class="hint">回收最后一份时自动取消装备，并保留操作流水。</p>
      <template #footer><el-button @click="revokeVisible = false">取消</el-button><el-button type="danger" @click="revoke">回收</el-button></template>
    </el-dialog>
  </el-card>
</template>
<script setup>
import { computed, reactive, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import adminApi from '@/api/adminClient'
const props = defineProps({ userId: { type: [String, Number], required: true } })
const categories = { character: '角色', item: '道具', cosmetic: '装扮' }
const state = ref(null), ledger = ref([]), selected = ref(null), quantity = ref(1), reason = ref('')
const loading = ref(false), busy = ref(false), error = ref(''), pending = ref(null), revokeVisible = ref(false)
const revokeForm = reactive({})
let version = 0
const selectedItem = computed(() => state.value?.catalog.find(i => i.item_id === selected.value))
const grantOptions = computed(() => state.value?.catalog.filter(i => !i.is_default && i.grant_enabled) || [])
const owned = computed(() => (state.value?.owned || []).map(o => ({ ...state.value.catalog.find(i => i.item_id === o.item_id), ...o, equipped: state.value.equipment.some(e => e.item_id === o.item_id) })))
watch(selected, () => { quantity.value = 1 })
async function load() {
  const v = ++version; loading.value = true; error.value = ''
  try {
    const [a, b] = await Promise.all([adminApi.get(`/inventory/users/${props.userId}`), adminApi.get(`/inventory/users/${props.userId}/ledger`)])
    if (v !== version) return
    state.value = a.data.data; ledger.value = b.data.data
  } catch (e) { if (v === version) error.value = e.response?.data?.message || '背包加载失败' }
  finally { if (v === version) loading.value = false }
}
function grant() {
  if (!reason.value.trim()) return ElMessage.warning('请填写发放原因')
  pending.value = { userId: props.userId, action: 'grant', body: { item_id: selected.value, quantity: quantity.value, reason: reason.value, request_id: crypto.randomUUID() } }
  submitPending()
}
function openRevoke(row) { Object.assign(revokeForm, { userId: props.userId, item_id: row.item_id, name: row.name, max: row.quantity, quantity: 1, reason: '' }); revokeVisible.value = true }
function revoke() {
  if (!revokeForm.reason.trim()) return ElMessage.warning('请填写回收原因')
  if (revokeForm.userId !== props.userId) { revokeVisible.value = false; return }
  pending.value = { userId: props.userId, action: 'revoke', body: { item_id: revokeForm.item_id, quantity: revokeForm.quantity, reason: revokeForm.reason, request_id: crypto.randomUUID() } }
  revokeVisible.value = false; submitPending()
}
async function submitPending() {
  if (busy.value || !pending.value) return
  const operation = pending.value; busy.value = true
  try {
    const { data } = await adminApi.post(`/inventory/users/${operation.userId}/${operation.action}`, operation.body)
    if (data.synced_online) ElMessage.success(data.data.message); else ElMessage.warning('操作已保存，在线通知未完成，请玩家刷新背包')
    if (pending.value === operation) pending.value = null
    reason.value = ''; await load()
  } catch (e) {
    // A definite 4xx means the transaction was rejected. Timeouts/5xx retain the
    // original command for a safe retry even if the first request committed.
    if (e.response?.status >= 400 && e.response.status < 500 && pending.value === operation) pending.value = null
    ElMessage.error(e.response?.data?.message || '操作结果未确认，请重试原操作')
  } finally { busy.value = false }
}
watch(() => props.userId, () => { state.value = null; ledger.value = []; selected.value = null; pending.value = null; revokeVisible.value = false; reason.value = ''; load() }, { immediate: true })
</script>
<style scoped>
.header { display:flex; align-items:center; justify-content:space-between; }.hint { color:#606266; line-height:1.6; font-size:13px; }.form { display:flex; flex-wrap:wrap; gap:12px; margin:16px 0; }.form .el-select { width:260px; }.form .el-input { max-width:280px; }.form .el-input-number { width:130px; }.ledger { margin-top:16px; }
</style>
