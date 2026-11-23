<template>
  <section class="inventory-panel account-feature" aria-label="物品栏" v-loading="loading">
    <header class="panel-heading"><h1>物品栏</h1></header>
    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <div v-if="assets.pending" class="pending-operation"><span>操作结果待确认</span><el-button :loading="busy" @click="submit">重试操作</el-button></div>
    <div class="inventory-tabs account-tabs" role="group" aria-label="物品分类"><button v-for="tab in tabs" :key="tab.key" :aria-pressed="category === tab.key" @click="category = tab.key">{{ tab.label }}<span>{{ count(tab.key) }}</span></button></div>
    <p v-if="!loading && !items.length" class="empty-note">暂无此类物品</p>
    <div class="item-list"><div v-if="items.length" class="item-columns" aria-hidden="true"><span>物品</span><span>说明</span><span>数量</span><span>操作</span></div><article v-for="item in items" :key="item.item_id" class="item-row" :class="{ equipped: isEquipped(item) }">
      <div class="item-info"><h2>{{ item.name }}</h2><div class="item-meta"><span>{{ slotNames[item.slot] || '道具' }}</span><span v-if="isEquipped(item)" class="equipped-tag">使用中</span><span v-else-if="item.is_default" class="default-tag">默认</span></div></div>
      <p class="item-description">{{ item.description || '—' }}</p>
      <span class="quantity">× {{ item.quantity }}</span>
      <div class="item-actions">
        <el-button v-if="item.effect === 'rename_credit'" size="small" @click="router.replace('/account#sec-account')">前往改名</el-button>
        <el-button v-else-if="item.slot" size="small" :disabled="busy || !!assets.pending || (isEquipped(item) && item.item_id === 2301) || !item.use_enabled" @click="operate('equip', item)">{{ isEquipped(item) ? (item.item_id === 2301 ? '使用中' : '卸下') : '使用' }}</el-button>
        <el-button v-else-if="item.effect === 'fixed_bundle' && item.use_enabled" size="small" :disabled="busy || !!assets.pending" @click="operate('use', item)">使用 1 个</el-button>
        <span v-else class="collectible" aria-label="无可用操作">—</span>
      </div>
    </article></div>
    <RewardReceipt :receipt="receipt" @confirm="receipt = null" />
  </section>
</template>
<script setup>
import '@/styles/account-theme.css'
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { usePlayerAuthStore } from '@/stores/playerAuth'
import { usePlayerAssetsStore } from '@/stores/playerAssets'
import playerApi from '@/api/playerClient'
import RewardReceipt from '@/components/RewardReceipt.vue'
const auth = usePlayerAuthStore(), assets = usePlayerAssetsStore(), router = useRouter()
const loading = ref(false), busy = ref(false), error = ref(''), category = ref('all'), receipt = ref(null)
let epoch = 0
const tabs = [{ key: 'all', label: '全部' }, { key: 'item', label: '道具' }, { key: 'character', label: '角色' }, { key: 'cosmetic', label: '装扮' }]
const slotNames = { avatar: '头像', avatar_frame: '头像框', character: '角色', voice: '语音', title: '称号' }
const owned = computed(() => (assets.inventory?.owned || [])
  .map(row => ({ ...assets.inventory.catalog.find(item => item.item_id === row.item_id), ...row }))
  .filter(item => item.quantity > 0 && item.name &&
    (item.use_enabled || !['avatar.reimu', 'avatar.cirno'].includes(item.asset_key))))
const items = computed(() => owned.value.filter(item => category.value === 'all' || item.category === category.value))
const count = key => owned.value.filter(item => key === 'all' || item.category === key).length
const isEquipped = item => assets.inventory?.equipment?.some(slot => slot.item_id === item.item_id && slot.slot === item.slot)
async function load() { const revision = ++epoch; loading.value = true; error.value = ''; try { await assets.refreshInventory() } catch (e) { if (revision === epoch) error.value = e.response?.data?.message || '物品栏暂时无法加载' } finally { if (revision === epoch) loading.value = false } }
async function operate(action, item) {
  if (busy.value || assets.pending) return
  const owner = auth.userId
  if (action === 'use') { try { await ElMessageBox.confirm(`使用 1 个「${item.name}」？`, '使用道具', { confirmButtonText: '使用', cancelButtonText: '取消' }) } catch { return } }
  if (owner !== auth.userId) return
  assets.setPending({ action, body: { request_id: crypto.randomUUID(), item_id: action === 'equip' && isEquipped(item) ? 0 : item.item_id, ...(action === 'equip' ? { slot: item.slot } : { quantity: 1 }) } })
  await submit()
}
async function submit() {
  const pending = assets.pending, owner = auth.userId, revision = epoch
  if (!pending || busy.value || !['equip', 'use'].includes(pending.action)) return
  busy.value = true; error.value = ''; receipt.value = null
  try { const { data } = await playerApi.post(`/inventory/${pending.action}`, pending.body); if (owner !== auth.userId) return
    assets.applyInventory(data.data.inventory_state); assets.setPending(null); if (revision === epoch) { receipt.value = data.data.reward_receipt; ElMessage.success(pending.action === 'equip' ? '装扮已更新' : '道具已使用') }
  } catch (e) { if (owner === auth.userId) { error.value = e.response?.data?.message || '操作结果暂时无法确认，请重试'; if ([400, 403, 404, 409, 422].includes(e.response?.status)) assets.setPending(null) } }
  finally { if (owner === auth.userId) busy.value = false }
}
watch(() => auth.userId, () => { epoch++; assets.syncOwner(); receipt.value = null; busy.value = false; if (auth.isLoggedIn) load() }, { immediate: true })
onBeforeUnmount(() => { epoch++ })
</script>
<style scoped>
.panel-heading { display:flex; align-items:center; justify-content:space-between; gap:12px; margin:0 0 12px; }.panel-heading h1 { margin:0; font-size:24px; line-height:1.4; letter-spacing:-.3px; }
.item-list { border:1px solid var(--account-border); border-radius:8px; overflow:hidden; background:white; }.item-list:empty { display:none; }
.item-columns,.item-row { display:grid; grid-template-columns:minmax(130px,.7fr) minmax(0,2fr) 54px 88px; gap:16px; align-items:center; padding:10px 16px; box-sizing:border-box; }
.item-columns { font-size:12px; color:var(--account-muted); background:var(--account-surface); padding-top:9px; padding-bottom:9px; }.item-columns span:nth-last-child(-n+2) { text-align:right; }
.item-row { min-height:58px; border-top:1px solid var(--account-divider); }.item-row:hover { background:#f8faff; }.item-row.equipped { background:#f5f9ff; }
.item-info { min-width:0; }.item-info h2 { font-size:14px; margin:0 0 3px; line-height:1.4; }.item-meta { display:flex; align-items:center; gap:8px; color:var(--account-subtle); font-size:11px; line-height:1.4; }.equipped-tag { color:var(--account-blue); }.default-tag { color:var(--account-subtle); }
.item-description { color:var(--account-muted); font-size:12px; line-height:1.5; margin:0; overflow-wrap:anywhere; }.quantity { color:var(--account-text); font-size:13px; font-variant-numeric:tabular-nums; text-align:right; white-space:nowrap; }.item-actions { text-align:right; }.collectible { color:var(--account-subtle); font-size:12px; }
.pending-operation { display:flex; flex-wrap:wrap; align-items:center; justify-content:space-between; gap:8px; padding:12px; border:1px solid #b6d6fc; background:#f5f9ff; border-radius:8px; margin:12px 0; font-size:12px; }.empty-note { color:var(--account-muted); text-align:center; padding:48px 12px; font-size:13px; }
@media(max-width:650px) { .item-columns { display:none; }.item-row { grid-template-columns:minmax(0,1fr) auto; gap:6px 12px; padding:9px 12px; }.item-row:first-of-type { border-top:0; }.item-info { grid-column:1; grid-row:1; }.item-info h2 { display:inline; margin-right:8px; }.item-meta { display:inline-flex; }.quantity { grid-column:2; grid-row:1; }.item-description { grid-column:1; grid-row:2; font-size:11px; }.item-actions { grid-column:2; grid-row:2; align-self:end; }.panel-heading h1 { font-size:22px; } }
</style>
