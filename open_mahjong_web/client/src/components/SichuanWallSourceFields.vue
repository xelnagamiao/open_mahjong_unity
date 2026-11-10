<template>
  <section class="sichuan-wall-source" aria-label="牌山来源">
    <el-form-item label="牌山来源" class="wall-source-selection">
      <el-radio-group :model-value="form.wall_source" aria-label="牌山来源" class="room-segmented-options wall-source-options" @update:model-value="selectSource">
        <el-radio-button v-for="source in SICHUAN_WALL_SOURCES" :key="source.value" :label="source.value">{{ source.label }}</el-radio-button>
      </el-radio-group>
    </el-form-item>
    <div v-if="training" class="wall-source-parameters">
      <el-form-item label="随机种子" required class="training-seed-field">
        <el-input v-model="form.random_seed" maxlength="66" placeholder="64位十六进制随机种子" aria-label="训练牌山随机种子" />
        <el-button @click="form.random_seed = generateTrainingSeed()">随机生成</el-button>
        <el-button :disabled="!form.random_seed" @click="copySeed">复制</el-button>
      </el-form-item>
    </div>
    <template v-else>
      <el-form-item v-if="!form.wall_source || form.wall_source === 'key'" label="密钥获取方式" class="wall-key-acquisition-field">
        <el-radio-group :model-value="form.duplicate_key_source" aria-label="密钥获取方式" @update:model-value="selectSichuanKeySource(form, $event)">
          <el-radio label="auto">自动生成</el-radio><el-radio label="existing">输入已有密钥</el-radio>
        </el-radio-group>
      </el-form-item>
      <DuplicateRoomField v-model="form.duplicate_key" :required="existing" :disabled="!existing" :placeholder="existing ? '' : '创建房间时自动生成独立密钥'" sichuan :create-to="{ path: '/account', hash: '#sec-duplicate-event' }">
        <template v-if="keySource" #after-input>
          <el-button :disabled="!existing || !generateDuplicateKey" :loading="generating" @click="generateKey">随机密钥</el-button>
        </template>
      </DuplicateRoomField>
    </template>
    <el-form-item label="比赛副数">
      <el-select v-if="training || !existing || (keySource && !form.duplicate_key?.trim())" v-model="form.duplicate_round_count" aria-label="比赛副数">
        <el-option v-for="count in DUPLICATE_ROUND_COUNTS" :key="count" :label="`${count} 副`" :value="count" />
      </el-select>
      <span v-else class="wall-source-help">跟随已有密钥</span>
    </el-form-item>
  </section>
</template>
<script setup>
import { computed, onBeforeUnmount, ref } from 'vue'
import { ElMessage } from 'element-plus'
import DuplicateRoomField from './DuplicateRoomField.vue'
import { DUPLICATE_ROUND_COUNTS } from '@/utils/duplicateWalls.js'
import { SICHUAN_WALL_SOURCES, isTrainingWall, selectSichuanWallSource, selectSichuanKeySource, buildSichuanKeyRequest, generateTrainingSeed } from '@/utils/sichuanWallSource.js'
const props = defineProps({ form: { type: Object, required: true }, generateDuplicateKey: { type: Function, default: null } })
const emit = defineEmits(['generating'])
const training = computed(() => isTrainingWall(props.form))
const keySource = computed(() => !props.form.wall_source || props.form.wall_source === 'key')
const existing = computed(() => ['seed', 'manual'].includes(props.form.wall_source) || props.form.duplicate_key_source === 'existing')
const generating = ref(false)
let disposed = false
onBeforeUnmount(() => { disposed = true })
function selectSource(source) {
  selectSichuanWallSource(props.form, source)
}
async function generateKey() {
  if (generating.value || !existing.value || !keySource.value || !props.generateDuplicateKey) return
  const originalForm = props.form
  const originalRule = originalForm.sub_rule
  generating.value = true
  emit('generating', true)
  try {
    const wall = await props.generateDuplicateKey(buildSichuanKeyRequest(originalForm))
    if (!wall?.key) throw new Error('未收到生成的复式密钥，请到赛事与基地复式密钥中查看')
    if (!disposed && props.form === originalForm && props.form.sub_rule === originalRule && keySource.value && existing.value) {
      props.form.duplicate_key = wall.key
      props.form.duplicate_round_count = wall.round_count || props.form.duplicate_round_count
    }
    ElMessage.success('复式密钥已生成并保存')
  } catch (error) {
    ElMessage.error(error.response?.data?.message || error.message || '生成复式密钥失败')
  } finally {
    generating.value = false
    emit('generating', false)
  }
}
async function copySeed() {
  try { await navigator.clipboard.writeText(props.form.random_seed); ElMessage.success('随机种子已复制') }
  catch { ElMessage.error('复制失败，请手动选择并复制随机种子') }
}
</script>
<style scoped>
.sichuan-wall-source { grid-column: 1 / -1; }
.wall-source-options.room-segmented-options { grid-template-columns: repeat(4, minmax(0, 1fr)); }
.wall-key-acquisition-field :deep(.el-radio-group) { display: flex; flex-wrap: wrap; }
.wall-key-acquisition-field :deep(.el-radio) { margin-right: 12px; }
.wall-source-parameters { margin-top: 12px; }
.training-seed-field :deep(.el-form-item__content) { gap: 8px; }
.training-seed-field :deep(.el-input) { flex: 1 1 300px; }
.wall-source-help { width: 100%; margin: 4px 0; font-weight: 400; color: var(--el-text-color-secondary); font-size: 12px; line-height: 1.7; }
</style>
