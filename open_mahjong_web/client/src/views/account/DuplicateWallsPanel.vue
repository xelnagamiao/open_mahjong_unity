<template>
  <div class="duplicate-panel">
    <div class="panel-heading"><div><h2>{{ scope === 'event' ? '赛事复式密钥' : '个人复式密钥' }}</h2></div><router-link to="/duplicate">复式使用说明</router-link></div>
    <el-form v-if="scope === 'event'" label-position="top">
      <el-form-item label="所属赛事" required><el-select v-model="eventId" placeholder="选择赛事" :disabled="busy" style="width: min(100%, 400px)"><el-option v-for="event in eligibleEvents" :key="event.event_id" :value="String(event.event_id)" :label="event.name || event.event_name || String(event.event_id)" /></el-select></el-form-item>
      <el-alert v-if="!eligibleEvents.length" title="暂无可管理的赛事。申办赛事或成为赛事管理员后可创建。" type="info" :closable="false" />
    </el-form>
    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon class="notice" />
    <template v-if="scope === 'personal' || eventId">
      <div class="quota-row"><span>今日创建 <b>{{ quota?.created_today ?? '—' }} / {{ quota?.daily_limit ?? (scope === 'event' ? 25 : 5) }}</b></span><span>保留密钥 <b>{{ quota?.stored ?? '—' }} / {{ quota?.storage_limit ?? (scope === 'event' ? 100 : 25) }}</b></span><el-button text :loading="loading" :disabled="busy" @click="loadWalls">刷新</el-button></div>
      <el-card v-loading="loadingCatalog" class="create-card" shadow="never">
        <template #header>创建复式密钥</template>
        <el-form label-position="top" :disabled="busy || loading || loadingCatalog" @submit.prevent="createWall">
          <div class="form-grid">
            <el-form-item label="密钥名称（可选）"><el-input v-model="form.name" maxlength="80" show-word-limit placeholder="例如：第一轮 · A 组" /></el-form-item>
            <el-form-item label="规则" required><el-select v-model="form.rule" placeholder="选择规则"><el-option v-for="rule in rules" :key="rule.rule" :value="rule.rule" :label="rule.label" /></el-select></el-form-item>
          </div>
          <el-form-item label="局数" required>
            <el-radio-group v-model="form.round_count"><el-radio-button v-for="count in DUPLICATE_ROUND_COUNTS" :key="count" :value="count">{{ count }} 局</el-radio-button></el-radio-group>
          </el-form-item>
          <el-form-item label="花牌"><el-switch v-model="form.use_flowers" :disabled="form.rule === 'guobiao/lanshi'" active-text="有花" inactive-text="无花" /><span class="flower-summary">{{ selectedRule?.tile_count || '—' }} 张</span></el-form-item>
          <el-form-item label="牌山类型" required>
            <div class="type-grid" role="group" aria-label="牌山类型">
              <button v-for="type in wallTypes" :key="type.value" type="button" class="type-card" :class="{ selected: form.wall_type === type.value }" :aria-pressed="form.wall_type === type.value" :disabled="busy || loading || loadingCatalog" @click="form.wall_type = type.value">
                <strong>{{ type.label }}</strong><span>{{ type.description }}</span>
              </button>
            </div>
          </el-form-item>
          
          <el-form-item v-if="form.wall_type === 'seed'" label="随机种子" required><el-input v-model="form.seed" maxlength="128" show-word-limit placeholder="1–128 个字符的文本种子" autocomplete="off" /><p class="help">一个种子生成整场牌山。相同规则、花牌设置、局数和种子生成相同的手牌与牌山。</p></el-form-item>
          <template v-if="form.wall_type === 'manual' && selectedRule">
            <div class="round-tabs" role="group" aria-label="编辑各局牌山"><button v-for="round in form.round_count" :key="round" type="button" :class="{ selected: editingRound === round }" :aria-pressed="editingRound === round" :disabled="busy" @click="editingRound = round"><strong>{{ duplicateRoundLabel(round) }}</strong><span>{{ flattenDuplicateDraft(manualDrafts[round - 1]).length }} / {{ selectedRule.tile_count }} 张</span></button></div>
            <p class="help">{{ duplicateRoundLabel(editingRound) }} · 玩家编号按开房座位固定，风位随国标换庄、换位。</p>
            <DuplicateManualEditor :key="`${form.rule}-${form.use_flowers}-${editingRound}`" v-model="manualDraft" :rule="selectedRule" :round="editingRound" :disabled="busy || loading || loadingCatalog" />
          </template>
          <p class="help">密钥默认锁定，解除锁定后公开牌谱。</p>
          <el-button type="primary" native-type="submit" :loading="creating" :disabled="!canCreate">创建密钥</el-button>
          <span v-if="quota && (quota.remaining_today <= 0 || quota.remaining_storage <= 0)" class="quota-warning">{{ quota.remaining_today <= 0 ? '今日创建次数已用完' : '密钥数量已达上限' }}</span>
        </el-form>
      </el-card>
      <el-card v-if="createdWall" class="created-card" shadow="never"><template #header>创建成功 · {{ wallTypeLabel(createdWall.wall_type) }} · {{ createdWall.round_count || 1 }} 局</template><p>开房时填入此密钥。</p><div class="key-line"><code>{{ createdWall.key }}</code><el-button @click="copyKey(createdWall.key)">复制密钥</el-button></div></el-card>
      <el-card shadow="never"><template #header>创建的复式密钥</template><el-table v-loading="loading" :data="walls" empty-text="尚未创建复式密钥" row-key="id">
        <el-table-column label="名称 / 密钥" min-width="235"><template #default="{ row }"><strong>{{ row.name || '未命名密钥' }}</strong><div class="table-key"><code>{{ row.key }}</code><el-button link type="primary" @click="copyKey(row.key)">复制</el-button></div></template></el-table-column>
        <el-table-column label="类型 / 规则" min-width="130"><template #default="{ row }">{{ wallTypeLabel(row.wall_type) }}<div class="help">{{ ruleLabel(row.rule) }} · {{ row.use_flowers === false ? '无花' : '有花' }} · {{ row.round_count || 1 }} 局</div></template></el-table-column>
        <el-table-column label="创建时间" min-width="165"><template #default="{ row }">{{ duplicateDate(row.created_at) }}</template></el-table-column>
        <el-table-column label="解锁时间" min-width="165"><template #default="{ row }">{{ duplicateDate(row.unlocked_at) }}</template></el-table-column>
        <el-table-column label="密钥状态" min-width="180"><template #default="{ row }"><el-tag :type="row.is_unlocked ? 'success' : 'info'">{{ row.is_unlocked ? '已解除锁定' : '锁定' }}</el-tag><el-button v-if="!row.is_unlocked" link type="primary" :loading="mutatingId === row.id" :disabled="busy || loading" @click="unlockWall(row)">解除锁定</el-button></template></el-table-column>
        <el-table-column label="操作" min-width="140"><template #default="{ row }"><el-button v-if="row.wall_type !== 'key'" link @click="inspectedWall = row">查看手牌与牌山</el-button><el-button v-if="row.is_unlocked" link type="primary" @click="$router.push({ path: '/player-data/duplicate', query: { key: row.key } })">查看对局</el-button><el-button link type="danger" :disabled="busy || !row.is_unlocked" :title="row.is_unlocked ? '删除后公开记录仍保留' : '解除锁定后可删除'" @click="deleteWall(row)">删除</el-button></template></el-table-column>
      </el-table></el-card>
    </template>
    <el-dialog :model-value="Boolean(inspectedWall)" title="手牌与牌山" width="min(760px, 96vw)" @close="inspectedWall = null"><template v-if="inspectedWall && inspectedWall.wall_type !== 'key'"><p>{{ inspectedWall.name }} · {{ wallTypeLabel(inspectedWall.wall_type) }}</p><p v-if="inspectedWall.seed != null" class="seed-value">种子：{{ inspectedWall.seed }}</p><el-select v-if="inspectedWall.round_count > 1" v-model="inspectedRound" aria-label="查看牌山局数"><el-option v-for="round in inspectedWall.round_count" :key="round" :value="round" :label="duplicateRoundLabel(round)" /></el-select><div v-for="(wall, seat) in splitDuplicateWall(inspectedTiles, inspectedWall.tile_count)" :key="seat" class="seat-wall"><strong>{{ seat + 1 }} 号 · {{ ['东', '南', '西', '北'][inspectedSeats[seat]] }}位</strong><p class="help">起始手牌 {{ inspectedHandCount(seat) }} 张</p><div class="tile-strip readonly"><TileChip v-for="(tile, index) in wall.slice(0, inspectedHandCount(seat))" :key="index" :tile-id="tile" size="sm" /></div><p class="help">剩余牌山</p><div class="tile-strip readonly"><TileChip v-for="(tile, index) in wall.slice(inspectedHandCount(seat))" :key="index" :tile-id="tile" size="sm" /></div></div></template></el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { duplicateWallsApi } from '@/api/duplicateWalls'
import DuplicateManualEditor from '@/components/DuplicateManualEditor.vue'
import TileChip from '@/components/TileChip.vue'
import { DUPLICATE_ROUND_COUNTS, duplicateRoundLabel, duplicateRoundSeats, createDuplicateDraft, duplicateDate, duplicateRuleWithFlowers, duplicateWallLabel, flattenDuplicateDraft, isDuplicateWallRule, splitDuplicateWall, validateDuplicateDraft, validateDuplicateSeed } from '@/utils/duplicateWalls'

const wallTypes = [
  { value: 'manual', label: '手动牌山', description: '手动设置四家的手牌和牌山。' },
  { value: 'seed', label: '复现牌山', description: '输入随机种子，生成手牌和牌山。' },
  { value: 'key', label: '密钥牌山', description: '新建随机种子。解锁前只提供密钥，手牌和牌山保密。' },
]
const wallTypeLabel = value => wallTypes.find(type => type.value === value)?.label || duplicateWallLabel(value)

const props = defineProps({ scope: { type: String, default: 'personal' }, events: { type: Array, default: () => [] } })
const eligibleEvents = computed(() => props.events.filter(event => event.kind !== 'base'))
const eventId = ref('')
const rules = ref([])
const quota = ref(null)
const walls = ref([])
const manualDrafts = ref(Array.from({ length: 16 }, createDuplicateDraft))
const editingRound = ref(1)
const manualDraft = computed({ get: () => manualDrafts.value[editingRound.value - 1], set: value => { manualDrafts.value[editingRound.value - 1] = value } })
const error = ref('')
const loading = ref(false)
const loadingCatalog = ref(false)
const creating = ref(false)
const mutatingId = ref(null)
const createdWall = ref(null)
const inspectedWall = ref(null)
const inspectedRound = ref(1)
const inspectedTiles = computed(() => inspectedWall.value?.round_tiles?.[inspectedRound.value - 1] || inspectedWall.value?.tiles || [])
const inspectedSeats = computed(() => duplicateRoundSeats(inspectedRound.value))
const inspectedHandCount = seat => inspectedSeats.value[seat] === 0 ? 14 : 13
const form = reactive({ name: '', rule: '', wall_type: 'key', seed: '', use_flowers: false, round_count: 1 })
const selectedRule = computed(() => duplicateRuleWithFlowers(rules.value.find(rule => rule.rule === form.rule), form.use_flowers))
const busy = computed(() => creating.value || mutatingId.value !== null)
const canCreate = computed(() => !busy.value && !loading.value && Boolean(selectedRule.value) && quota.value?.remaining_today > 0 && quota.value?.remaining_storage > 0 && (props.scope === 'personal' || Boolean(eventId.value)))
const scopeParams = () => ({ scope: props.scope, ...(props.scope === 'event' ? { event_id: eventId.value } : {}) })
let loadVersion = 0
function message(err) { return err.response?.data?.message || err.message || '操作失败，请重试' }
function ruleLabel(rule) { return rules.value.find(item => item.rule === rule)?.label || rule }

async function loadCatalog() {
  loadingCatalog.value = true
  try {
    rules.value = ((await duplicateWallsApi.catalog())?.rules || []).filter(rule => isDuplicateWallRule(rule.rule))
    if (!rules.value.some(rule => rule.rule === form.rule)) form.rule = rules.value[0]?.rule || ''
  }
  catch (err) { error.value = message(err) }
  finally { loadingCatalog.value = false }
}

async function loadWalls() {
  const version = ++loadVersion
  if (props.scope === 'event' && !eventId.value) return
  loading.value = true
  error.value = ''
  if (!rules.value.length) await loadCatalog()
  try {
    const data = await duplicateWallsApi.mine(scopeParams())
    if (version !== loadVersion) return
    walls.value = data?.items || []
    quota.value = data?.quota || null
  } catch (err) { if (version === loadVersion) { quota.value = null; walls.value = []; error.value = message(err) } }
  finally { if (version === loadVersion) loading.value = false }
}

async function createWall() {
  if (!canCreate.value) return
  let validation = form.wall_type === 'seed' ? validateDuplicateSeed(form.seed) : ''
  if (form.wall_type === 'manual') {
    for (let index = 0; index < form.round_count; index++) {
      const problem = validateDuplicateDraft(manualDrafts.value[index], selectedRule.value, duplicateRoundSeats(index + 1).indexOf(0))
      if (problem) { editingRound.value = index + 1; validation = `${duplicateRoundLabel(index + 1)}：${problem}`; break }
    }
  }
  if (validation) { ElMessage.warning(validation); return }
  creating.value = true
  error.value = ''
  try {
    const data = await duplicateWallsApi.create({ ...scopeParams(), name: form.name.trim(), rule: form.rule, round_count: form.round_count, use_flowers: selectedRule.value.use_flowers, wall_type: form.wall_type, ...(form.wall_type === 'manual' ? { round_tiles: manualDrafts.value.slice(0, form.round_count).map(flattenDuplicateDraft) } : form.wall_type === 'seed' ? { seed: form.seed.trim() } : {}) })
    createdWall.value = data.wall
    quota.value = data.quota
    form.name = ''
    ElMessage.success('复式密钥已创建')
    await loadWalls()
  } catch (err) { error.value = message(err) }
  finally { creating.value = false }
}

async function copyKey(key) {
  try { await navigator.clipboard.writeText(key); ElMessage.success('密钥已复制') }
  catch { ElMessage.info('自动复制不可用，请选中密钥文本复制') }
}

async function unlockWall(row) {
  if (busy.value || loading.value || row.is_unlocked) return
  mutatingId.value = row.id
  error.value = ''
  try {
    try {
      await ElMessageBox.confirm('确定要解除锁定吗，解除锁定后密钥将被公开，无法再重新锁定', '解除锁定', {
        type: 'warning', confirmButtonText: '解除锁定', cancelButtonText: '取消',
      })
    } catch { return }
    const data = await duplicateWallsApi.unlock(row.id)
    Object.assign(row, data.wall)
    if (createdWall.value?.id === row.id) createdWall.value = null
    ElMessage.success('已解除锁定，密钥和关联牌谱已公开')
  }
  catch (err) { error.value = message(err) }
  finally { mutatingId.value = null }
}

async function deleteWall(row) {
  if (busy.value || !row.is_unlocked) return
  try { await ElMessageBox.confirm(`删除「${row.name || row.key}」？删除后不能再用此密钥开房，不返还今日创建次数。历史信息和对局仍保留在数据站。`, '删除复式密钥', { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消' }) }
  catch { return }
  mutatingId.value = row.id
  error.value = ''
  try { await duplicateWallsApi.remove(row.id); if (createdWall.value?.id === row.id) createdWall.value = null; await loadWalls(); ElMessage.success('密钥已删除') }
  catch (err) { error.value = message(err) }
  finally { mutatingId.value = null }
}

watch(() => form.rule, () => { form.use_flowers = false; manualDrafts.value = Array.from({ length: 16 }, createDuplicateDraft) })
watch(() => form.use_flowers, () => { manualDrafts.value = Array.from({ length: 16 }, createDuplicateDraft) })
watch(() => form.round_count, count => { editingRound.value = Math.min(editingRound.value, count) })
watch(inspectedWall, () => { inspectedRound.value = 1 })
watch(eligibleEvents, events => { if (!events.some(event => String(event.event_id) === eventId.value)) eventId.value = events[0] ? String(events[0].event_id) : '' }, { immediate: true })
watch(eventId, () => { walls.value = []; quota.value = null; createdWall.value = null; inspectedWall.value = null; loadWalls() })
onMounted(() => { if (props.scope === 'personal' || eventId.value) loadWalls(); else loadCatalog() })
</script>

<style scoped>
.round-tabs { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 12px; }
.round-tabs button { display: flex; flex-direction: column; gap: 4px; min-width: 92px; padding: 10px 12px; border: 1px solid #dcdfe6; border-radius: 4px; background: #fff; color: #303133; cursor: pointer; }
.round-tabs button.selected { border-color: #17756a; background: #eff8f5; }
.round-tabs span { font-size: 12px; color: #737d88; }
.duplicate-panel { max-width: 1200px; margin: 0 auto; }
.panel-heading { display: flex; align-items: baseline; justify-content: space-between; gap: 20px; margin-bottom: 20px; }
.panel-heading h2 { margin: 0 0 8px; color: #303133; }
.panel-heading a { white-space: nowrap; color: #17756a; }
.quota-row, .wall-heading, .key-line { display: flex; align-items: center; flex-wrap: wrap; gap: 20px; }
.quota-row { margin: 14px 0; }
.wall-heading { justify-content: space-between; }
.type-grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; width: 100%; }
.type-card { display: flex; flex-direction: column; gap: 8px; padding: 16px; text-align: left; font: inherit; line-height: 1.6; color: #303133; background: #fff; border: 1px solid #dcdfe6; border-radius: 4px; cursor: pointer; }
.type-card strong { font-size: 15px; }
.type-card span { font-size: 13px; color: #606266; }
.type-card:hover:not(:disabled), .type-card.selected { border-color: #17756a; }
.type-card.selected { background: #eff8f5; }
.type-card:focus-visible { outline: 2px solid #17756a; outline-offset: 2px; }
.form-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 20px; }
.form-grid :deep(.el-select) { width: 100%; }
.create-card, .created-card { margin-bottom: 20px; }
.created-card { border-color: #b1d7c5; }
.notice { margin: 14px 0; }
.help { color: #737d88; font-size: 12px; line-height: 1.6; }
.quota-warning { margin-left: 12px; color: #b45b26; }
.flower-summary { margin-left: 12px; color: #737d88; font-size: 12px; }
.manual-builder { padding: 16px; background: #f6f8f7; border: 1px solid #dce5df; border-radius: 8px; }
.tile-palette { display: flex; flex-wrap: wrap; gap: 6px; padding-bottom: 18px; }
.tile-button { display: flex; flex-direction: column; align-items: center; padding: 8px 11px; min-width: 52px; gap: 4px; cursor: pointer; color: #234b42; background: #fff; border: 1px solid #b8c8c0; border-radius: 5px; }
.tile-button small { color: #737d88; font-size: 10px; }
button:disabled { opacity: .45; cursor: not-allowed; }
.seat-wall { padding: 12px 0; border-top: 1px solid #dce5df; }
.seat-label { font-weight: 600; font-size: 13px; margin-bottom: 8px; }
.seat-label small { margin-left: 10px; color: #737d88; font-weight: 400; }
.tile-strip { display: flex; flex-wrap: wrap; gap: 4px; }
.tile-strip button, .tile-strip span:not(.help) { border: 1px solid #d3dad6; border-radius: 3px; background: #fff; padding: 5px 6px; font-size: 12px; }
.tile-strip button { cursor: pointer; }
.tile-strip button:hover { background: #fef0ef; border-color: #efaaaa; }
.readonly { margin-top: 10px; }
.key-line code, .table-key code, .seed-value { overflow-wrap: anywhere; word-break: break-all; }
.key-line code { font-size: 17px; }
.table-key { display: flex; align-items: center; gap: 10px; font-size: 12px; }
@media (max-width: 640px) { .type-grid { grid-template-columns: 1fr; } .form-grid { grid-template-columns: 1fr; gap: 0; } .panel-heading { flex-direction: column; gap: 8px; } .manual-builder { padding: 10px; } .quota-row { gap: 12px; } }
</style>
