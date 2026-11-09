<template>
  <div class="shop-admin">
    <div class="heading"><h2>商店管理</h2><el-button v-if="active === 'offers'" type="primary" @click="editOffer()">上架商品</el-button></div>
    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />
    <el-tabs v-model="active" @tab-change="onTabChange">
      <el-tab-pane label="上架商品" name="offers">
        <div class="filters"><el-input v-model="search" placeholder="搜索物品名称、商品编号或物品 ID" clearable /><el-select v-model="availability"><el-option label="全部商品" value="all" /><el-option label="已上架" value="enabled" /><el-option label="已下架" value="disabled" /></el-select><el-button :loading="loadingOffers" @click="loadOffers">重新载入</el-button></div>
        <el-table v-loading="loadingOffers" :data="filteredOffers" empty-text="暂无商品">
          <el-table-column label="物品" min-width="140"><template #default="{ row }">{{ row.item.name }}<div class="subtle">物品 ID：{{ row.item_id }}</div></template></el-table-column>
          <el-table-column prop="offer_id" label="商品编号" min-width="160" />
          <el-table-column label="单价（萨拉币）" width="160"><template #default="{ row }">{{ money(row.unit_price_minor) }}</template></el-table-column>
          <el-table-column label="状态" width="125"><template #default="{ row }"><el-tag :type="row.enabled ? 'success' : 'info'">{{ row.enabled ? '已上架' : '已下架' }}</el-tag><div v-if="!row.item.grant_enabled || !row.item.use_enabled" class="subtle">物品已停用</div></template></el-table-column>
          <el-table-column label="禁止重复购买" width="140"><template #default="{ row }">{{ row.prevent_duplicate_purchase || row.item.category !== 'item' ? '已开启' : '未开启' }}</template></el-table-column>
          <el-table-column prop="max_purchase_quantity" label="单次上限" width="95" />
          <el-table-column prop="sort_order" label="排序" width="80" />
          <el-table-column label="操作" width="90"><template #default="{ row }"><el-button size="small" @click="editOffer(row)">编辑</el-button></template></el-table-column>
        </el-table>
      </el-tab-pane>
      <el-tab-pane label="交易记录" name="orders">
        <div class="filters order-filters">
          <el-input v-model="orderQuery.userId" placeholder="玩家 UID" inputmode="numeric" clearable @keyup.enter="searchOrders" />
          <el-select v-model="orderQuery.offerId" placeholder="全部商品" clearable filterable><el-option v-for="offer in offers" :key="offer.offer_id" :label="`${offer.item.name} · ${offer.offer_id}`" :value="offer.offer_id" /></el-select>
          <el-input v-model="orderQuery.orderId" placeholder="完整订单编号" clearable @keyup.enter="searchOrders" />
          <el-date-picker v-model="orderQuery.period" type="datetimerange" start-placeholder="开始时间" end-placeholder="结束时间" />
          <el-button type="primary" :loading="loadingOrders" @click="searchOrders">查询</el-button>
        </div>
        <el-table v-loading="loadingOrders" :data="orders" empty-text="暂无交易记录">
          <el-table-column label="成交时间" width="175"><template #default="{ row }">{{ time(row.created_at) }}</template></el-table-column>
          <el-table-column label="玩家" min-width="155"><template #default="{ row }">{{ row.username }}<div class="subtle">{{ row.user_id }}</div></template></el-table-column>
          <el-table-column label="物品" min-width="145"><template #default="{ row }">{{ row.item_name }}<div class="subtle">{{ row.offer_id }}</div></template></el-table-column>
          <el-table-column prop="quantity" label="数量" width="75" />
          <el-table-column label="成交单价" width="115"><template #default="{ row }">{{ money(row.unit_price_minor) }}</template></el-table-column>
          <el-table-column label="实付萨拉币" width="125"><template #default="{ row }">{{ money(row.total_minor) }}</template></el-table-column>
          <el-table-column label="交易后余额" width="125"><template #default="{ row }">{{ row.balance_after === null ? '—' : money(row.balance_after) }}</template></el-table-column>
          <el-table-column label="订单" width="90"><template #default="{ row }"><el-button size="small" @click="selectedOrder = row">详情</el-button></template></el-table-column>
        </el-table>
        <el-pagination v-model:current-page="page" :page-size="30" :total="total" layout="total, prev, pager, next" class="pagination" @current-change="loadOrders" />
      </el-tab-pane>
    </el-tabs>
    <el-dialog v-model="editing" :title="editingId ? '编辑商品' : '上架商品'" width="620px" :close-on-click-modal="!saving" :before-close="closeEditor" top="6vh">
      <el-form label-width="120px" :disabled="saving" @submit.prevent="save">
        <el-form-item label="商品编号" required><el-input v-model="form.offer_id" :disabled="!!editingId" maxlength="64" placeholder="字母、数字、下划线或短横线，创建后固定" /></el-form-item>
        <el-form-item label="物品" required><el-select v-model="form.item_id" :disabled="!!editingId" filterable style="width:100%"><el-option v-for="item in items" :key="item.item_id" :label="`${item.name} · ${item.item_id}${!item.grant_enabled || !item.use_enabled ? '（已停用）' : ''}`" :value="item.item_id" /></el-select></el-form-item>
        <el-form-item label="单价（萨拉币）" required><el-input-number v-model="form.price" :min="0.01" :max="1000000" :step="0.05" :precision="2" controls-position="right" /></el-form-item>
        <el-form-item label="上架"><el-switch v-model="form.enabled" /></el-form-item>
        <el-form-item label="禁止重复购买"><el-switch v-model="form.prevent_duplicate_purchase" :disabled="isPermanent" /><div class="field-hint">已拥有该物品时不能购买，每次最多购买 1 件；消耗完后可以再次购买。永久装扮始终禁止重复购买。</div></el-form-item>
        <el-form-item label="单次购买上限"><el-input-number v-model="form.max_purchase_quantity" :min="1" :max="100" :precision="0" :disabled="form.prevent_duplicate_purchase || isPermanent" /></el-form-item>
        <el-form-item label="排序"><el-input-number v-model="form.sort_order" :min="-1000000" :max="1000000" :precision="0" /><span class="field-hint">数字越小越靠前</span></el-form-item>
        <el-form-item label="变更原因" required><el-input v-model="form.reason" type="textarea" :rows="2" maxlength="500" show-word-limit /></el-form-item>
        <el-alert v-if="saveError" :title="saveError" type="error" :closable="false" show-icon />
      </el-form>
      <template #footer><el-button :disabled="saving" @click="editing = false">取消</el-button><el-button type="primary" :loading="saving" @click="save">保存</el-button></template>
    </el-dialog>
    <el-dialog :model-value="!!selectedOrder" title="交易详情" width="680px" @update:model-value="selectedOrder = null">
      <el-descriptions v-if="selectedOrder" :column="1" border>
        <el-descriptions-item label="订单编号">{{ selectedOrder.order_id }}</el-descriptions-item>
        <el-descriptions-item label="请求编号">{{ selectedOrder.request_id }}</el-descriptions-item>
        <el-descriptions-item label="玩家">{{ selectedOrder.username }}（{{ selectedOrder.user_id }}）</el-descriptions-item>
        <el-descriptions-item label="成交物品">{{ selectedOrder.item_name }}（物品 {{ selectedOrder.item_id }} / 商品 {{ selectedOrder.offer_id }}）</el-descriptions-item>
        <el-descriptions-item label="数量与单价">{{ selectedOrder.quantity }} × {{ money(selectedOrder.unit_price_minor) }} 萨拉币</el-descriptions-item>
        <el-descriptions-item label="实付">{{ money(selectedOrder.total_minor) }} 萨拉币</el-descriptions-item>
        <el-descriptions-item label="交易后余额">{{ selectedOrder.balance_after === null ? '—' : money(selectedOrder.balance_after) }} 萨拉币</el-descriptions-item>
        <el-descriptions-item label="成交商品版本">{{ selectedOrder.price_version ?? '—' }}</el-descriptions-item>
        <el-descriptions-item label="成交时间">{{ time(selectedOrder.created_at) }}</el-descriptions-item>
      </el-descriptions>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import adminApi from '@/api/adminClient'

const active = ref('offers'), offers = ref([]), items = ref([]), orders = ref([])
const loadingOffers = ref(false), loadingOrders = ref(false), error = ref('')
const search = ref(''), availability = ref('all'), page = ref(1), total = ref(0), selectedOrder = ref(null)
const orderQuery = reactive({ userId: '', offerId: '', orderId: '', period: null })
const editing = ref(false), editingId = ref(''), saving = ref(false), saveError = ref('')
const form = reactive({})
const isPermanent = computed(() => items.value.find(item => item.item_id === form.item_id)?.category !== 'item' && !!form.item_id)
const filteredOffers = computed(() => offers.value.filter(offer => {
  const needle = search.value.trim().toLowerCase()
  return (!needle || `${offer.offer_id} ${offer.item.name} ${offer.item_id}`.toLowerCase().includes(needle))
    && (availability.value === 'all' || offer.enabled === (availability.value === 'enabled'))
}))
const money = value => (Number(value) / 100).toFixed(2)
const time = value => value ? new Date(value).toLocaleString() : '—'
const message = err => err.response?.data?.message || err.message || '请求失败，请稍后重试'

async function loadOffers() {
  loadingOffers.value = true; error.value = ''
  try {
    const { data } = await adminApi.get('/shop/offers')
    offers.value = data.data.offers; items.value = data.data.items
  } catch (err) { error.value = message(err) }
  finally { loadingOffers.value = false }
}
async function loadOrders() {
  loadingOrders.value = true; error.value = ''
  try {
    const uid = orderQuery.userId.trim()
    if (uid && (!/^\d+$/.test(uid) || !Number.isSafeInteger(Number(uid)) || Number(uid) < 1)) throw new Error('请输入有效的玩家 UID')
    const params = { page: page.value, limit: 30, user_id: uid || undefined,
      offer_id: orderQuery.offerId || undefined, order_id: orderQuery.orderId.trim() || undefined,
      from_time: orderQuery.period?.[0]?.toISOString(), to_time: orderQuery.period?.[1]?.toISOString() }
    const { data } = await adminApi.get('/shop/orders', { params })
    orders.value = data.data.items; total.value = data.data.total
  } catch (err) { error.value = message(err) }
  finally { loadingOrders.value = false }
}
function searchOrders() { page.value = 1; loadOrders() }
function onTabChange(name) { if (name === 'orders') loadOrders() }
function editOffer(offer) {
  editingId.value = offer?.offer_id || ''; saveError.value = ''
  Object.assign(form, { offer_id: offer?.offer_id || '', item_id: offer?.item_id || null,
    price: offer ? Number(offer.unit_price_minor) / 100 : 0.05, enabled: offer?.enabled ?? true,
    prevent_duplicate_purchase: offer?.prevent_duplicate_purchase ?? false,
    max_purchase_quantity: offer?.max_purchase_quantity ?? 1, sort_order: offer?.sort_order ?? 0,
    expected_version: offer?.price_version, reason: '' })
  editing.value = true
}
watch(() => [form.item_id, form.prevent_duplicate_purchase], () => {
  if (isPermanent.value || form.prevent_duplicate_purchase) form.max_purchase_quantity = 1
  if (isPermanent.value) form.prevent_duplicate_purchase = true
  if (!editingId.value && !form.offer_id) form.offer_id = items.value.find(item => item.item_id === form.item_id)?.code || ''
})
function closeEditor(done) { if (!saving.value) done() }
async function save() {
  if (saving.value) return
  saveError.value = ''
  if (!form.reason?.trim()) { saveError.value = '请填写变更原因'; return }
  if (!form.item_id || !form.offer_id || !Number.isFinite(form.price) || form.price < 0.01) { saveError.value = '请填写商品编号、物品和有效价格'; return }
  saving.value = true
  try {
    const payload = { offer_id: form.offer_id, item_id: form.item_id, unit_price_minor: Math.round(form.price * 100),
      enabled: form.enabled, prevent_duplicate_purchase: form.prevent_duplicate_purchase,
      max_purchase_quantity: form.max_purchase_quantity, sort_order: form.sort_order,
      expected_version: form.expected_version, reason: form.reason.trim() }
    if (editingId.value) await adminApi.patch(`/shop/offers/${editingId.value}`, payload)
    else await adminApi.post('/shop/offers', payload)
    editing.value = false; ElMessage.success('商品已保存'); await loadOffers()
  } catch (err) { saveError.value = message(err) }
  finally { saving.value = false }
}
onMounted(loadOffers)
</script>

<style scoped>
.heading{display:flex;justify-content:space-between;align-items:center;gap:16px;margin-bottom:16px}
h2{margin:0;font-weight:400}.filters{display:flex;flex-wrap:wrap;align-items:center;gap:10px;margin:12px 0 16px}
.filters>.el-input{width:300px}.filters>.el-select{width:180px}.order-filters>.el-input{width:185px}.order-filters>.el-select{width:220px}
.subtle,.field-hint{color:#606266;font-size:12px;line-height:1.6}.field-hint{display:block;margin-left:8px;max-width:330px}
.pagination{margin-top:18px}.shop-admin :deep(.el-descriptions__label),.shop-admin :deep(.el-table th),.shop-admin :deep(.el-tabs__item){font-weight:400}
@media(max-width:768px){.filters>.el-input,.filters>.el-select{width:100%}.heading{align-items:flex-start}.field-hint{margin-left:0}}
</style>
