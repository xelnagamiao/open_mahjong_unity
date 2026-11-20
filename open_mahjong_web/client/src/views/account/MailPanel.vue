<template>
  <section ref="panel" class="mail-panel account-feature" aria-label="邮件">
    <header class="panel-heading"><h1>邮件</h1></header>
    <div class="mail-tabs account-tabs" role="group" aria-label="邮件筛选"><button v-for="tab in tabs" :key="tab.key" :aria-pressed="filter === tab.key" @click="changeFilter(tab.key)">{{ tab.label }}<span v-if="tab.count">{{ tab.count }}</span></button></div>
    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <div class="mail-workspace" :class="{ 'show-detail': selected }">
      <div class="inbox" v-loading="loading">
        <p v-if="!loading && !mails.length" class="empty-note">{{ filter === 'all' ? '暂无邮件' : '暂无符合条件的邮件' }}</p>
        <button v-for="mail in mails" :key="mail.mail_id" class="mail-row" :class="{ active: selected?.mail_id === mail.mail_id, unread: !mail.is_read }" @click="open(mail)">
          <div class="mail-row-top"><span>{{ mail.sender }}</span><time>{{ date(mail.created_at) }}</time></div>
          <div class="mail-title"><i v-if="!mail.is_read" aria-label="未读"></i>{{ mail.title }}</div><p>{{ mail.content }}</p>
          <span v-if="mail.rewards.length" class="attachment-tag">{{ mailRewardStatus(mail) }} · {{ mail.rewards.length }} 种物品</span>
        </button>
        <el-pagination v-if="total > 20" class="mail-pagination" small layout="prev, pager, next" :pager-count="5" :page-size="20" :total="total" :current-page="page" @current-change="changePage" />
      </div>
      <article class="mail-detail">
        <template v-if="selected">
          <el-button class="back-button" link @click="selected = null">← 返回邮件列表</el-button>
          <div class="detail-meta"><span>{{ selected.sender }}</span><time>{{ date(selected.created_at, true) }}</time></div>
          <h2>{{ selected.title }}</h2><p class="mail-content">{{ selected.content }}</p>
          <el-button v-if="safeMailAction(selected.action_path)" class="application-link" @click="router.push(selected.action_path)">查看申请 →</el-button>
          <div v-if="selected.rewards.length" class="attachments"><h3>邮件附件</h3><div class="attachment-list"><div v-for="item in selected.rewards" :key="item.item_id" class="attachment"><span>{{ item.name }}</span><span class="attachment-quantity">× {{ item.quantity }}</span></div></div>
            <div class="claim-row"><el-button type="primary" :loading="claiming" :disabled="selected.claimed || selected.expired" @click="claim">{{ mailRewardStatus(selected) === '待领取' ? '领取附件' : mailRewardStatus(selected) }}</el-button><span v-if="selected.expires_at">有效期至 {{ date(selected.expires_at, true) }}</span></div>
          </div>
          <RewardReceipt :receipt="receipt" @confirm="receipt = null" />
        </template>
      </article>
    </div>
  </section>
</template>
<script setup>
import '@/styles/account-theme.css'
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { usePlayerAuthStore } from '@/stores/playerAuth'
import { usePlayerAssetsStore } from '@/stores/playerAssets'
import playerApi from '@/api/playerClient'
import RewardReceipt from '@/components/RewardReceipt.vue'
import { mailRewardStatus } from '@/utils/mailbox'
import { safeMailAction } from '@/utils/accountActivity'
const auth = usePlayerAuthStore(), assets = usePlayerAssetsStore(), router = useRouter()
const panel = ref(null)
const mails = ref([]), selected = ref(null), filter = ref('all'), page = ref(1), total = ref(0), loading = ref(false), claiming = ref(false), error = ref(''), receipt = ref(null)
let epoch = 0, selection = 0
const tabs = computed(() => [{ key: 'all', label: '全部邮件' }, { key: 'unread', label: '未读', count: assets.unread }, { key: 'rewards', label: '待领取', count: assets.claimable }])
const date = (value, full = false) => new Date(value).toLocaleString('zh-CN', { timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit', ...(full ? { hour: '2-digit', minute: '2-digit' } : {}) })
const message = e => e.response?.data?.message || '邮件暂时无法加载，请重试'
function patchMail(mail) { const index = mails.value.findIndex(item => item.mail_id === mail.mail_id); if (index >= 0) mails.value[index] = mail; if (selected.value?.mail_id === mail.mail_id) selected.value = mail }
async function load(silent = false) {
  if (!auth.isLoggedIn) return
  const revision = ++epoch, owner = auth.userId
  if (!silent) { loading.value = true; error.value = '' }
  try { const { data } = await playerApi.get('/mail', { params: { page: page.value, filter: filter.value } }); if (revision !== epoch || owner !== auth.userId) return
    const lastPage = Math.max(1, Math.ceil(data.data.total / 20)); if (page.value > lastPage) { page.value = lastPage; return await load(silent) }
    mails.value = data.data.mails; total.value = data.data.total; assets.unread = data.data.unread_count; assets.claimable = data.data.claimable_count
    const current = mails.value.find(item => item.mail_id === selected.value?.mail_id); if (current) selected.value = current
  } catch (e) { if (revision === epoch && owner === auth.userId && !silent) error.value = message(e) }
  finally { if (revision === epoch) loading.value = false }
}
function changeFilter(value) { filter.value = value; page.value = 1; selected.value = null; receipt.value = null; selection++; load() }
function changePage(value) { page.value = value; selected.value = null; receipt.value = null; selection++; load() }
async function open(mail) {
  selected.value = mail; receipt.value = null; error.value = ''; const revision = ++selection, owner = auth.userId
  await nextTick()
  if (window.matchMedia('(max-width:800px)').matches) panel.value?.scrollIntoView({ block: 'start' })
  if (mail.is_read) return
  try { const { data } = await playerApi.post(`/mail/${mail.mail_id}/read`); if (owner !== auth.userId || revision !== selection) return
    patchMail(data.data.mail); assets.unread = data.data.unread_count
  } catch (e) { if (owner === auth.userId && revision === selection) error.value = message(e) }
}
async function claim() {
  if (!selected.value || claiming.value) return
  const id = selected.value.mail_id, owner = auth.userId, revision = selection
  claiming.value = true; error.value = ''
  try { const { data } = await playerApi.post(`/mail/${id}/claim`); if (owner !== auth.userId) return
    patchMail(data.data.mail); assets.applyInventory(data.data.inventory_state); if (revision === selection) receipt.value = data.data.reward_receipt
    await assets.refreshSummary().catch(() => {})
  } catch (e) { if (owner === auth.userId) error.value = e.response?.data?.message || '领取结果暂时无法确认，请再次领取核实，物品不会重复发放' }
  finally { if (owner === auth.userId) claiming.value = false }
}
watch(() => auth.userId, () => { epoch++; selection++; mails.value = []; selected.value = null; receipt.value = null; claiming.value = false; assets.syncOwner(); load() }, { immediate: true })
const timer = setInterval(() => { if (!document.hidden && !claiming.value) load(true) }, 30000)
onBeforeUnmount(() => { epoch++; selection++; clearInterval(timer) })
</script>
<style scoped>
.panel-heading { display:flex; justify-content:space-between; align-items:center; gap:16px; margin:0 0 12px; }.panel-heading h1 { font-size:24px; margin:0; line-height:1.4; letter-spacing:-.3px; }
.mail-workspace { display:grid; grid-template-columns:minmax(260px,.85fr) minmax(0,1.5fr); background:#fff; border:1px solid var(--account-border); border-radius:8px; overflow:hidden; height:min(68vh,700px); min-height:420px; }
.inbox { border-right:1px solid var(--account-border); min-width:0; }.inbox,.mail-detail { min-height:0; overflow-y:auto; }
.mail-row { display:block; width:100%; text-align:left; border:0; border-bottom:1px solid var(--account-divider); padding:16px; background:#fff; cursor:pointer; color:var(--account-text); font:inherit; }.mail-row:hover { background:#f6f8fa; }.mail-row.active { background:#eef5ff; box-shadow:inset 3px 0 var(--account-blue); }
.mail-row-top { display:flex; justify-content:space-between; gap:10px; color:var(--account-muted); font-size:11px; }.mail-title { font-size:14px; margin:8px 0 6px; line-height:1.5; }.mail-title i { display:inline-block; width:6px; height:6px; border-radius:50%; background:var(--account-blue); margin-right:8px; vertical-align:middle; }.mail-row p { overflow:hidden; text-overflow:ellipsis; white-space:nowrap; color:var(--account-muted); font-size:12px; margin:0; }.attachment-tag { display:inline-block; margin-top:10px; padding:1px 6px; color:#0550ae; background:#e5f0ff; border:1px solid #c8dffc; border-radius:4px; font-size:11px; }
.mail-detail { padding:24px; min-width:0; }.detail-meta { display:flex; justify-content:space-between; flex-wrap:wrap; gap:12px; color:var(--account-muted); font-size:12px; }.mail-detail h2 { font-size:20px; margin:20px 0 16px; line-height:1.5; overflow-wrap:anywhere; }.mail-content { white-space:pre-wrap; overflow-wrap:anywhere; font-size:14px; line-height:1.8; color:#424a53; }.application-link { margin-top:12px; }
.attachments { border-top:1px solid var(--account-divider); margin-top:24px; padding-top:18px; }.attachments h3 { font-size:13px; margin:0 0 12px; }.attachment-list { display:flex; flex-wrap:wrap; gap:8px; }.attachment { display:flex; gap:16px; align-items:center; font-size:12px; padding:8px 12px; border:1px solid var(--account-border); border-radius:6px; background:var(--account-surface); }.attachment-quantity { color:var(--account-blue); font-variant-numeric:tabular-nums; }.claim-row { display:flex; flex-wrap:wrap; align-items:center; gap:12px; margin-top:16px; }.claim-row>span { font-size:11px; color:var(--account-muted); }.mail-pagination { justify-content:center; margin:14px 0; }.back-button { display:none; }.empty-note { color:var(--account-muted); text-align:center; font-size:13px; margin:0; padding:64px 16px; }
@media(max-width:800px) { .mail-workspace { grid-template-columns:1fr; height:auto; min-height:320px; }.inbox { border-right:0; overflow-y:visible; }.mail-detail { display:none; padding:18px; overflow-y:visible; }.show-detail .inbox { display:none; }.show-detail .mail-detail { display:block; }.back-button { display:inline-flex; margin-bottom:20px; }.panel-heading { align-items:flex-start; }.mail-detail h2 { font-size:18px; } }
</style>
