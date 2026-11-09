<template>
  <div class="mailbox">
    <h2>邮箱 <span>游戏内邮件</span></h2>
    <p class="intro">向玩家游戏内的邮件投递通知和物品，无需绑定电子邮箱。物品在玩家领取后进入背包。</p>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <el-alert v-if="pending" title="有一封邮件等待确认投递结果，原投递编号已保留。请重试原投递，避免重复发送。" type="warning" :closable="false" />
    <el-card>
      <el-form inline @submit.prevent="search" :disabled="sending || !!pending">
        <el-form-item label="收件玩家"><el-input v-model="query" placeholder="玩家 ID 或用户名" clearable /></el-form-item>
        <el-form-item><el-button native-type="submit" :loading="searching">搜索玩家</el-button></el-form-item>
      </el-form>
      <el-select v-model="selectedId" placeholder="请从搜索结果中选择玩家" filterable :disabled="sending || !!pending" class="recipient" @change="selectPlayer">
        <el-option v-for="user in users" :key="user.user_id" :value="user.user_id" :label="`${user.username} · ID ${user.user_id}`" />
      </el-select>
      <p v-if="recipient">收件人：{{ recipient.username }} · ID {{ recipient.user_id }}</p>
    </el-card>
    <el-tabs v-model="tab">
      <el-tab-pane label="写邮件" name="compose">
        <el-card>
          <el-form label-width="90px" :disabled="sending || !!pending" @submit.prevent>
            <el-form-item label="发件人" required><el-input v-model="form.sender" maxlength="80" /></el-form-item>
            <el-form-item label="标题" required><el-input v-model="form.title" maxlength="160" show-word-limit /></el-form-item>
            <el-form-item label="正文" required><el-input v-model="form.content" type="textarea" :rows="7" maxlength="20000" show-word-limit placeholder="纯文本，支持换行" /></el-form-item>
            <el-form-item label="有效期">
              <el-radio-group v-model="expiryMode"><el-radio value="days">30 天</el-radio><el-radio value="custom">自定义</el-radio><el-radio value="forever">永久</el-radio></el-radio-group>
              <el-date-picker v-if="expiryMode === 'custom'" v-model="expiresAt" type="datetime" placeholder="请选择到期时间" />
            </el-form-item>
            <el-form-item :label="`附件 ${form.rewards.length} / 8`">
              <div class="attachments">
                <div v-for="(reward, index) in form.rewards" :key="index" class="reward-row">
                  <img v-if="mailIcon(definition(reward.item_id))" :src="mailIcon(definition(reward.item_id))" alt="" class="item-icon" />
                  <span v-else class="item-placeholder">物品</span>
                  <el-select v-model="reward.item_id" filterable placeholder="选择物品" @change="reward.quantity = 1">
                    <el-option v-for="item in catalog" :key="item.item_id" :value="item.item_id" :label="`${item.name} · ID ${item.item_id}`" :disabled="form.rewards.some((r, i) => i !== index && r.item_id === item.item_id)" />
                  </el-select>
                  <el-input-number v-model="reward.quantity" :min="1" :max="rewardLimit(definition(reward.item_id))" :precision="0" :step="1" aria-label="物品数量" />
                  <el-button @click="form.rewards.splice(index, 1)">移除</el-button>
                </div>
                <el-button :disabled="form.rewards.length >= 8 || !catalog.length" @click="form.rewards.push({ item_id: null, quantity: 1 })">添加物品</el-button>
                <span class="hint">最多 8 种；留空可发送普通通知。</span>
              </div>
            </el-form-item>
            <el-form-item label="操作备注" required><el-input v-model="form.reason" maxlength="500" placeholder="用于后台审计，玩家不可见" /></el-form-item>
          </el-form>
          <el-button type="primary" :disabled="!recipient || sending" @click="preview">{{ pending ? '查看待确认投递' : '预览并发送' }}</el-button>
          <el-button :disabled="sending || !!pending" @click="loadCatalog">刷新物品目录</el-button>
        </el-card>
        <el-alert v-if="result" class="result" :title="`已确认投递至 ${result.recipient}，邮件 ID：${result.mail_id}${result.duplicate ? '（重试确认，未重复发送）' : ''}`" type="success" :closable="false" />
      </el-tab-pane>
      <el-tab-pane label="投递记录" name="history">
        <el-form inline @submit.prevent="searchHistory">
          <el-form-item label="玩家 UID"><el-input v-model="historyUid" placeholder="输入 UID 查询收到的全部邮件" clearable /></el-form-item>
          <el-form-item><el-button type="primary" native-type="submit" :loading="historyLoading">搜索邮件</el-button></el-form-item>
        </el-form>
        <div class="history-toolbar"><span>{{ historyRecipient ? `UID ${historyRecipient.user_id} · 共 ${mails.length} 封已送达邮件` : '输入玩家 UID 即可查询' }}</span><el-button :disabled="!historyRecipient" :loading="historyLoading" @click="loadHistory()">刷新记录</el-button></div>
        <p class="hint">查询包含已读、未读、已领取和已过期邮件。记录存在表示已送达游戏内邮箱，不代表玩家已阅读或领取。</p>
        <el-alert v-if="historyError" :title="historyError" type="error" :closable="false" />
        <el-table :data="mails" v-loading="historyLoading" :empty-text="historyRecipient ? '未查询到该 UID 收到的邮件' : '请先输入 UID 搜索邮件'" @row-click="showMail">
          <el-table-column prop="mail_id" label="ID" width="85" />
          <el-table-column prop="title" label="标题" min-width="200" />
          <el-table-column prop="sender" label="发件人" width="120" />
          <el-table-column label="阅读" width="85"><template #default="{ row }">{{ row.is_read ? '已读' : '未读' }}</template></el-table-column>
          <el-table-column label="附件" width="100"><template #default="{ row }">{{ mailRewardStatus(row) }}</template></el-table-column>
          <el-table-column label="投递时间" width="180"><template #default="{ row }">{{ date(row.created_at) }}</template></el-table-column>
          <el-table-column label="有效期" width="190"><template #default="{ row }">{{ date(row.expires_at) }}{{ row.expired ? '（已过期）' : '' }}</template></el-table-column>
          <el-table-column label="操作" width="85"><template #default="{ row }"><el-button link @click.stop="showMail(row)">详情</el-button></template></el-table-column>
        </el-table>
      </el-tab-pane>
    </el-tabs>
    <el-dialog v-model="previewVisible" :title="viewingHistory ? '邮件详情' : '确认发送游戏内邮件'" width="min(760px, 94vw)" :close-on-click-modal="false" :close-on-press-escape="!sending" :show-close="!sending" class="mailbox-dialog">
      <template v-if="snapshot">
        <p>收件人：{{ snapshot.recipient.username }} · ID {{ snapshot.body.user_id }}</p>
        <p>发件人：{{ snapshot.body.sender }}</p>
        <h3>{{ snapshot.body.title }}</h3>
        <div class="mail-body">{{ snapshot.body.content }}</div>
        <div class="reward-preview"><div v-for="reward in snapshot.body.rewards" :key="reward.item_id" class="reward-card"><img v-if="mailIcon(reward)" :src="mailIcon(reward)" alt="" /><span>{{ reward.name || definition(reward.item_id)?.name || `物品 ${reward.item_id}` }}</span><span>× {{ reward.quantity }}</span></div></div>
        <p>到期时间：{{ date(snapshot.body.expires_at) }}</p>
        <p v-if="!viewingHistory">操作备注：{{ snapshot.body.reason }}</p>
        <p v-else>阅读：{{ snapshot.body.is_read ? '已读' : '未读' }} · 附件：{{ mailRewardStatus(snapshot.body) }}</p>
        <el-alert v-if="sendError" :title="sendError" type="warning" :closable="false" />
      </template>
      <template #footer><el-button :disabled="sending" @click="previewVisible = false">{{ viewingHistory ? '关闭' : '返回' }}</el-button><el-button v-if="!viewingHistory" type="primary" :loading="sending" @click="send">{{ pending ? '重试原投递' : '确认发送' }}</el-button></template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { ElMessage } from 'element-plus'
import adminApi from '@/api/adminClient'
import { useAdminAuthStore } from '@/stores/adminAuth'
import { mailIcon, mailRewardStatus, rewardLimit, validateMail } from '@/utils/mailbox'

const auth = useAdminAuthStore()
const storageKey = computed(() => `mailbox-pending:${auth.userId}`)
const query = ref(''), users = ref([]), selectedId = ref(null), recipient = ref(null)
const searching = ref(false), catalog = ref([]), error = ref(''), tab = ref('compose')
const form = reactive({ sender: 'Salasasa', title: '', content: '', reason: '', rewards: [] })
const expiryMode = ref('days'), expiresAt = ref(null)
const pending = ref(null), snapshot = ref(null), previewVisible = ref(false), viewingHistory = ref(false)
const sending = ref(false), sendError = ref(''), result = ref(null)
const mails = ref([]), historyLoading = ref(false), historyError = ref('')
const historyUid = ref(''), historyRecipient = ref(null)
let searchGeneration = 0, historyGeneration = 0
const definition = id => catalog.value.find(i => i.item_id === id)
const date = value => value ? new Date(value).toLocaleString('zh-CN') : '永久'

async function loadCatalog() {
  error.value = ''
  try { const { data } = await adminApi.get('/inventory/catalog'); catalog.value = data.data.items.filter(i => !i.is_default && i.grant_enabled) }
  catch (e) { error.value = e.response?.data?.message || '物品目录加载失败，可重试刷新' }
}
async function search() {
  if (sending.value || pending.value) return
  if (!query.value.trim()) return ElMessage.warning('请输入玩家 ID 或用户名')
  const generation = ++searchGeneration
  searching.value = true
  try {
    const { data } = await adminApi.get('/users/search', { params: { q: query.value.trim() } })
    if (generation !== searchGeneration || pending.value) return
    users.value = data.data.items.map(user => ({ ...user, user_id: Number(user.user_id) }))
    if (!users.value.length) ElMessage.info('未找到玩家')
  } catch (e) { ElMessage.error(e.response?.data?.message || '玩家搜索失败') }
  finally { if (generation === searchGeneration) searching.value = false }
}
function selectPlayer() {
  recipient.value = users.value.find(u => u.user_id === selectedId.value) || null
  mails.value = []; result.value = null
  if (recipient.value) {
    historyUid.value = String(recipient.value.user_id)
    loadHistory(historyUid.value)
  }
}
function searchHistory() {
  const uid = historyUid.value.trim()
  if (!/^[1-9]\d{0,18}$/.test(uid) || BigInt(uid) < 11n || BigInt(uid) > 9223372036854775807n) {
    ElMessage.warning('请输入有效的玩家 UID（大于 10 的整数）')
    return
  }
  loadHistory(uid)
}
async function loadHistory(userId = historyRecipient.value?.user_id) {
  const generation = ++historyGeneration
  if (!userId) return
  historyLoading.value = true; historyError.value = ''; mails.value = []; historyRecipient.value = null
  try {
    const { data } = await adminApi.get(`/mailbox/users/${userId}`)
    if (generation === historyGeneration) {
      mails.value = data.data.mails
      historyRecipient.value = { user_id: String(userId), username: '' }
    }
  } catch (e) { if (generation === historyGeneration) historyError.value = e.response?.data?.message || '邮件记录加载失败' }
  finally { if (generation === historyGeneration) historyLoading.value = false }
}
function preview() {
  sendError.value = ''; viewingHistory.value = false
  if (pending.value) snapshot.value = pending.value
  else {
    const expires = expiryMode.value === 'days' ? new Date(Date.now() + 30 * 86400000) : expiryMode.value === 'custom' ? new Date(expiresAt.value || NaN) : null
    if (expires && !Number.isFinite(expires.getTime())) return ElMessage.warning('请选择有效的到期时间')
    const body = { ...form, user_id: recipient.value?.user_id, expires_at: expires?.toISOString() || null,
      rewards: form.rewards.map(r => ({ ...r, name: definition(r.item_id)?.name, icon_path: definition(r.item_id)?.icon_path })) }
    const message = validateMail(body, catalog.value)
    if (message) return ElMessage.warning(message)
    snapshot.value = { body, recipient: { user_id: recipient.value.user_id, username: recipient.value.username } }
  }
  previewVisible.value = true
}
function showMail(mail) {
  if (sending.value || !historyRecipient.value) return
  viewingHistory.value = true; sendError.value = ''
  snapshot.value = { body: { ...mail, user_id: historyRecipient.value.user_id }, recipient: { ...historyRecipient.value } }
  previewVisible.value = true
}
async function send() {
  if (sending.value || viewingHistory.value) return
  if (!pending.value) {
    const message = validateMail(snapshot.value.body, catalog.value)
    if (message) { sendError.value = message; return }
    const operation = { ...snapshot.value, body: { ...snapshot.value.body, request_id: crypto.randomUUID() } }
    try { sessionStorage.setItem(storageKey.value, JSON.stringify(operation)) }
    catch { sendError.value = '浏览器无法保存投递编号，请启用会话存储后再发送'; return }
    pending.value = operation
  }
  sending.value = true; sendError.value = ''
  const operation = pending.value
  try {
    const { data } = await adminApi.post('/mailbox/send', { ...operation.body,
      rewards: operation.body.rewards.map(({ item_id, quantity }) => ({ item_id, quantity })) })
    result.value = { recipient: `${operation.recipient.username} · ${operation.body.user_id}`, mail_id: data.data.mail.mail_id, duplicate: data.data.duplicate }
    sessionStorage.removeItem(storageKey.value); pending.value = null; previewVisible.value = false
    form.title = ''; form.content = ''; form.reason = ''; form.rewards = []
    ElMessage.success('游戏内邮件已投递'); await loadHistory()
  } catch (e) {
    sendError.value = e.response?.data?.message || '投递结果尚未确认，请重试原投递'
    if (e.response && !e.response.data?.retryable && e.response.status < 500 && e.response.status !== 429) {
      sessionStorage.removeItem(storageKey.value); pending.value = null
    }
  } finally { sending.value = false }
}
onBeforeRouteLeave(() => !sending.value)
onMounted(() => {
  try {
    const saved = JSON.parse(sessionStorage.getItem(storageKey.value) || 'null')
    if (saved?.body?.request_id && saved?.recipient) {
      pending.value = saved; recipient.value = saved.recipient; users.value = [saved.recipient]; selectedId.value = saved.body.user_id
      Object.assign(form, saved.body); historyUid.value = String(saved.body.user_id); loadHistory(historyUid.value)
    }
  } catch { error.value = '无法恢复上次投递，请先核对玩家投递记录' }
  loadCatalog()
})
</script>

<style scoped>
.mailbox { max-width:1200px; font-weight:400; font-synthesis:none; }
h2,h3 { font-weight:400; margin:0 0 16px; } h2 span { font-size:15px; color:#64748b; margin-left:12px; }
.intro,.hint { color:#64748b; line-height:1.7; }.hint { margin-left:12px; font-size:13px; }
.el-alert,.el-card,.el-tabs { margin-bottom:18px; }.recipient { width:min(100%,520px); }
.attachments { width:100%; }.reward-row { display:flex; gap:10px; align-items:center; margin-bottom:12px; flex-wrap:wrap; }
.reward-row .el-select { width:260px; }.item-icon,.item-placeholder { width:40px; height:40px; object-fit:contain; flex-shrink:0; }
.item-placeholder { display:grid; place-items:center; background:#edf2f7; color:#64748b; font-size:12px; border-radius:6px; }
.history-toolbar { display:flex; justify-content:space-between; align-items:center; margin-bottom:16px; gap:12px; }
.mail-body { white-space:pre-wrap; overflow-wrap:anywhere; max-height:320px; overflow:auto; padding:20px; background:#f3f6fa; line-height:1.8; }
.reward-preview { display:grid; grid-template-columns:repeat(4,minmax(0,1fr)); gap:10px; margin-top:16px; }
.reward-card { display:flex; flex-direction:column; align-items:center; gap:8px; padding:12px; background:#f3f6fa; overflow-wrap:anywhere; }
.reward-card img { width:48px; height:48px; object-fit:contain; }
:deep(.el-form-item__label),:deep(.el-table th),:deep(.el-button),:deep(.el-tabs__item) { font-weight:400; }
@media(max-width:650px) { .reward-preview { grid-template-columns:repeat(2,minmax(0,1fr)); }.reward-row .el-select { width:190px; } }
</style>
<style>
.mailbox-dialog { font-synthesis:none; font-weight:400; }.mailbox-dialog .el-dialog__title,.mailbox-dialog .el-button { font-weight:400; }
</style>
