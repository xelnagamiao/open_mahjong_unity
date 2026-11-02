<template>
  <div class="redemption-admin" v-loading="loading">
    <div class="toolbar"><h2>兑换码</h2><el-button type="primary" @click="edit()">创建兑换码</el-button></div>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <div class="toolbar"><span>普通码按码领取，活动码按批次限领。启用后奖励和领取规则固定。</span><el-button @click="load">刷新</el-button></div>
    <el-table :data="items" empty-text="暂无兑换码">
      <el-table-column prop="name" label="批次名称" min-width="160" />
      <el-table-column label="用途 / 领取规则" min-width="220"><template #default="{ row }"><el-tag :type="row.purpose === 'ordinary' ? 'info' : 'warning'">{{ row.purpose === 'ordinary' ? '普通兑换码' : '活动兑换码' }}</el-tag><div class="claim-policy">{{ policy(row) }}</div></template></el-table-column>
      <el-table-column label="码形式" width="100"><template #default="{ row }">{{ row.kind === 'shared' ? '通用码' : '一次性码' }}</template></el-table-column>
      <el-table-column label="奖励" min-width="200"><template #default="{ row }">{{ rewardText(row) }}</template></el-table-column>
      <el-table-column label="领取 / 上限" width="130"><template #default="{ row }">{{ row.claimed_count }} / {{ row.max_claims || (row.kind === 'unique' ? row.code_count : '不限') }}</template></el-table-column>
      <el-table-column label="有效期" min-width="180"><template #default="{ row }"><div>{{ date(row.starts_at, '立即生效') }}</div><div>{{ date(row.ends_at, '长期有效') }}</div></template></el-table-column>
      <el-table-column label="状态" width="90"><template #default="{ row }"><el-tag :type="row.status === 'active' ? 'success' : 'info'">{{ states[row.status] }}</el-tag></template></el-table-column>
      <el-table-column label="操作" width="270"><template #default="{ row }">
        <el-button v-if="row.status === 'draft'" size="small" @click="edit(row)">编辑</el-button>
        <el-button v-if="row.status === 'draft' || row.status === 'paused'" size="small" type="primary" @click="status(row, 'active')">{{ row.status === 'draft' ? '启用' : '恢复' }}</el-button>
        <el-button v-if="row.status === 'active'" size="small" @click="status(row, 'paused')">暂停</el-button>
        <el-button size="small" @click="downloadCodes(row)">导出码</el-button>
        <el-button size="small" @click="showClaims(row)">记录</el-button>
        <el-button v-if="row.status !== 'ended'" size="small" type="danger" plain @click="status(row, 'ended')">结束</el-button>
      </template></el-table-column>
    </el-table>
    <el-pagination v-model:current-page="page" :page-size="20" :total="total" layout="prev,pager,next,total" @current-change="load" />
    <el-dialog v-model="visible" :title="editing ? '编辑草稿' : '创建兑换码'" width="650px" top="5vh" class="redeem-editor" :close-on-click-modal="false" :before-close="close">
      <el-form label-width="100px" :disabled="saving || !!pending">
        <el-form-item label="用途"><el-radio-group v-model="form.purpose" :disabled="!!editing" @change="purposeChanged"><el-radio value="ordinary">普通兑换码</el-radio><el-radio value="activity">活动兑换码</el-radio></el-radio-group></el-form-item>
        <el-form-item label="领取规则"><div class="policy-description">{{ policy(form) }}<div v-if="form.purpose === 'ordinary'">例如同一玩家有三张改名卡兑换码，可以分别领取三次。</div></div></el-form-item>
        <el-form-item label="批次名称" required><el-input v-model="form.name" maxlength="100" placeholder="例如：改名卡、周年礼物" /></el-form-item>
        <el-form-item v-if="form.purpose === 'activity'" label="码形式"><el-radio-group v-model="form.kind" :disabled="!!editing"><el-radio value="shared">通用码</el-radio><el-radio value="unique">一次性码</el-radio></el-radio-group></el-form-item>
        <el-form-item v-if="form.kind === 'shared' && !editing" label="兑换码" required><el-input v-model="form.code" maxlength="32" placeholder="8–32 位英文或数字，不区分大小写" /></el-form-item>
        <el-form-item v-if="form.kind === 'unique'" label="生成数量"><el-input-number v-model="form.code_count" :min="1" :max="10000" :disabled="!!editing" /><span class="hint">1 个即为单张兑换码，也可批量生成</span></el-form-item>
        <el-form-item label="总领取上限"><el-input-number v-model="form.max_claims" :min="1" :max="10000000" placeholder="不限" /><span class="hint">留空表示不限</span></el-form-item>
        <el-form-item label="开始时间"><el-date-picker v-model="form.starts_at" type="datetime" placeholder="启用后立即生效" /></el-form-item>
        <el-form-item label="结束时间"><el-date-picker v-model="form.ends_at" type="datetime" placeholder="长期有效" /></el-form-item>
        <el-form-item label="奖励" required><div class="rewards">
          <div v-for="(reward, i) in form.rewards" :key="i" class="reward-row"><el-select v-model="reward.item_id" filterable placeholder="选择物品"><el-option v-for="item in catalog" :key="item.item_id" :value="item.item_id" :label="`${item.name} · ${inventoryCategoryLabel(item.category)}`" /></el-select><el-input-number v-model="reward.quantity" :min="1" :max="limit(reward.item_id)" /><el-button @click="form.rewards.splice(i, 1)">移除</el-button></div>
          <el-button :disabled="form.rewards.length >= 20" @click="form.rewards.push({ item_id: null, quantity: 1 })">添加物品</el-button>
        </div></el-form-item>
        <el-form-item label="操作原因" required><el-input v-model="form.reason" maxlength="500" /></el-form-item>
      </el-form>
      <el-alert v-if="saveError" :title="saveError" type="warning" :closable="false" />
      <template #footer><el-button :disabled="saving" @click="visible = false">关闭</el-button><el-button type="primary" :loading="saving" @click="save">{{ pending ? '重试保存' : '保存草稿' }}</el-button></template>
    </el-dialog>
    <el-dialog v-model="claimsVisible" :title="`领取记录 · ${claimsCampaign?.name || ''}`" width="850px" class="redeem-claims">
      <el-table :data="claims" empty-text="暂无领取记录"><el-table-column prop="user_id" label="玩家 UID" width="130" /><el-table-column prop="code_hint" label="兑换码" width="120" /><el-table-column label="实际获得" min-width="220"><template #default="{ row }">{{ row.result.reward_receipt.items.map(i => `${i.name} × ${i.quantity}`).join('、') }}</template></el-table-column><el-table-column label="时间" width="180"><template #default="{ row }">{{ date(row.created_at) }}</template></el-table-column></el-table>
      <el-pagination v-model:current-page="claimsPage" :page-size="100" :total="claimsTotal" layout="prev,pager,next,total" @current-change="loadClaims" />
      <template #footer><el-button :loading="exporting" @click="downloadClaims">导出全部记录</el-button><el-button @click="claimsVisible = false">关闭</el-button></template>
    </el-dialog>
  </div>
</template>
<script setup>
import { onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import adminApi from '@/api/adminClient'
import { inventoryCategoryLabel } from '@/constants/inventoryCategories'
const items = ref([]), catalog = ref([]), loading = ref(false), error = ref(''), page = ref(1), total = ref(0)
const visible = ref(false), saving = ref(false), editing = ref(null), saveError = ref(''), pending = ref(null)
const form = reactive({ rewards: [] })
const states = { draft: '草稿', active: '已启用', paused: '已暂停', ended: '已结束' }
const claimsVisible = ref(false), claimsCampaign = ref(null), claims = ref([]), claimsPage = ref(1), claimsTotal = ref(0), exporting = ref(false)
const date = (value, fallback = '') => value ? new Date(value).toLocaleString() : fallback
const rewardText = row => row.rewards.map(r => `${r.name} × ${r.quantity}`).join('、')
const policy = row => row.purpose === 'ordinary' ? '每码仅可使用一次；同一玩家可领取本批次的多个不同码。' : '每个账号在本活动批次限领一次，更换兑换码也不能重复领取。'
function purposeChanged() { form.kind = form.purpose === 'ordinary' ? 'unique' : 'shared' }
const limit = id => catalog.value.find(i => i.item_id === id)?.max_quantity || 1
async function load() {
  loading.value = true; error.value = ''
  try {
    const [a,b] = await Promise.all([adminApi.get('/redemption/campaigns', { params: { page: page.value } }), adminApi.get('/inventory/catalog')])
    items.value = a.data.data.items; total.value = a.data.data.total
    catalog.value = b.data.data.items.filter(i => !i.is_default && i.grant_enabled)
  } catch (e) { error.value = e.response?.data?.message || '加载失败' } finally { loading.value = false }
}
function edit(row) {
  // Preserve an uncertain create operation until explicitly retried.
  if (pending.value) { visible.value = true; return }
  editing.value = row?.campaign_id || null; saveError.value = ''
  Object.assign(form, row ? { purpose:'activity',...JSON.parse(JSON.stringify(row)) } : { name:'',purpose:'ordinary',kind:'unique',code:'',code_count:1,max_claims:null,starts_at:null,ends_at:null,rewards:[{item_id:null,quantity:1}] })
  form.reason = ''; visible.value = true
}
function close(done) { if (!saving.value) done() }
async function save() {
  if (saving.value) return
  if (!pending.value) {
    if (!form.name.trim() || !form.reason.trim() || !form.rewards.length || form.rewards.some(r => !r.item_id || !r.quantity)) { saveError.value = '请填写批次名称、奖励和操作原因'; return }
    pending.value = { request_id: crypto.randomUUID(), name:form.name,purpose:form.purpose,kind:form.kind,code:form.code,code_count:form.code_count,max_claims:form.max_claims || null,
      starts_at:form.starts_at ? new Date(form.starts_at).toISOString() : null, ends_at:form.ends_at ? new Date(form.ends_at).toISOString() : null,
      rewards:form.rewards.map(r => ({item_id:r.item_id,quantity:r.quantity})),reason:form.reason }
  }
  saving.value = true; saveError.value = ''
  try {
    if (editing.value) await adminApi.patch(`/redemption/campaigns/${editing.value}`, pending.value)
    else await adminApi.post('/redemption/campaigns', pending.value)
    pending.value = null; visible.value = false; ElMessage.success('草稿已保存'); await load()
  } catch (e) { saveError.value = e.response?.data?.message || '结果尚未确认，请重试保存'; if (e.response && e.response.status < 500 && e.response.status !== 429) pending.value = null }
  finally { saving.value = false }
}
async function status(row, value) {
  try {
    const warning = value === 'active' ? `确认启用「${row.name}」？奖励：${rewardText(row)}。${policy(row)} 启用后不可修改奖励与领取规则。` : `确认${value === 'ended' ? '结束' : '暂停'}「${row.name}」？`
    const { value: reason } = await ElMessageBox.prompt(warning, states[value], { inputPlaceholder:'操作原因',inputValidator:v => !!v?.trim() || '请填写原因' })
    await adminApi.post(`/redemption/campaigns/${row.campaign_id}/status`, {status:value,reason}); await load()
  } catch (e) { if (e !== 'cancel' && e !== 'close') ElMessage.error(e.response?.data?.message || '操作失败') }
}
function csv(name, rows) {
  const quote = value => '"' + String(value ?? '').replace(/^[=+@-]/, "'$&").replace(/"/g,'""') + '"'
  const blob = new Blob(['\uFEFF' + rows.map(r => r.map(quote).join(',')).join('\r\n')],{type:'text/csv;charset=utf-8'})
  const url = URL.createObjectURL(blob), link = document.createElement('a'); link.href=url;link.download=name;link.click();setTimeout(() => URL.revokeObjectURL(url),1000)
}
async function downloadCodes(row) {
  try { const {data} = await adminApi.post(`/redemption/campaigns/${row.campaign_id}/export`,{}); csv(`兑换码-${row.campaign_id}.csv`,[['兑换码','已领取次数'],...data.data.map(r => [r.code,r.claimed_count])]) }
  catch (e) { ElMessage.error(e.response?.data?.message || '导出失败') }
}
async function showClaims(row) { claimsCampaign.value=row;claimsPage.value=1;claimsVisible.value=true;await loadClaims() }
async function loadClaims() {
  try { const {data}=await adminApi.get(`/redemption/campaigns/${claimsCampaign.value.campaign_id}/claims`,{params:{page:claimsPage.value}});claims.value=data.data.items;claimsTotal.value=data.data.total }
  catch(e) { ElMessage.error(e.response?.data?.message || '记录加载失败') }
}
async function downloadClaims() {
  exporting.value=true
  try {
    const rows=[];let p=1,count=0
    do { const {data}=await adminApi.get(`/redemption/campaigns/${claimsCampaign.value.campaign_id}/claims`,{params:{page:p++}});count=data.data.total;rows.push(...data.data.items) } while(rows.length<count)
    csv(`兑换记录-${claimsCampaign.value.campaign_id}.csv`,[['UID','兑换码','获得物品','时间','操作编号'],...rows.map(r=>[r.user_id,r.code_hint,r.result.reward_receipt.items.map(i=>`${i.name} × ${i.quantity}`).join('、'),date(r.created_at),r.operation_id])])
  } catch(e) { ElMessage.error(e.response?.data?.message || '导出失败') } finally { exporting.value=false }
}
onMounted(load)
</script>
<style scoped>
.toolbar { display:flex;justify-content:space-between;align-items:center;gap:16px;margin:16px 0; }.toolbar h2 { font-weight:400; }.toolbar span,.hint { color:#627181;font-size:13px; }.hint { margin-left:8px; }
.rewards { width:100%; }.reward-row { display:flex;gap:8px;margin-bottom:10px; }.reward-row .el-select { flex:1; }.reward-row .el-input-number { width:125px; }.el-pagination { margin-top:18px; }.el-table .el-button { margin:3px; }
.claim-policy { margin-top:6px;line-height:1.5;font-size:12px;color:#536477; }.policy-description { line-height:1.7;color:#394955; }.policy-description div { margin-top:4px;color:#667583;font-size:13px; }
:deep(.el-table th.el-table__cell) { font-weight:400; }
</style>
<style>.redeem-editor { display:flex;flex-direction:column;max-height:90vh;max-width:95vw; }.redeem-editor .el-dialog__body { overflow:auto; }.redeem-claims .el-table th.el-table__cell { font-weight:400; }</style>
