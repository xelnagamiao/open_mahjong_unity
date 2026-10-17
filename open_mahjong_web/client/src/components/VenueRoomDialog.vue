<template>
  <el-dialog
    :model-value="modelValue"
    :title="title"
    width="min(720px, calc(100vw - 24px))"
    top="5vh"
    class="venue-room-dialog"
    destroy-on-close
    :close-on-click-modal="false"
    :close-on-press-escape="false"
    :show-close="!loading"
    @close="$emit('update:modelValue', false)"
  >
    <el-form label-position="top" class="room-form" :disabled="loading" @submit.prevent="confirm">
      <slot name="before-settings" />
      <div class="room-top">
        <el-form-item label="规则">
          <el-select v-model="form.room_rule" style="width: 100%" @change="changeEventRoomRule(form, $event)">
            <el-option
              v-for="opt in roomRuleOptions"
              :key="opt.value"
              :label="opt.label"
              :value="opt.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="房间名">
          <el-input v-model="form.room_name" clearable placeholder="可选" />
        </el-form-item>
        <el-form-item label="创建数量">
          <el-select v-model="form.room_count" aria-label="创建数量" style="width: 100%">
            <el-option v-for="count in EVENT_ROOM_COUNTS" :key="count" :label="paired ? `${count} 组（${count * 2} 间）` : `${count} 个房间`" :value="count" />
          </el-select>
        </el-form-item>
        <el-form-item v-if="showReason" label="操作原因" required class="room-top-full">
          <el-input v-model="form.reason" clearable placeholder="审计必填" />
        </el-form-item>
      </div>
      <GuobiaoEmptyRoomConfig v-if="form.room_rule === 'guobiao'" :model-value="form" :show-password="showPassword" :auto-duplicate="autoDuplicate" :show-duplicate-field="false" />
      <ShanxiRoomConfig v-else-if="form.room_rule === 'shanxi'" :form="form" :show-password="showPassword" />
      <HongKongRoomConfig v-else-if="form.room_rule === 'hongkong'" :form="form" />
      <SichuanRoomConfig v-else-if="form.room_rule === 'sichuan'" :form="form" :allow-paired="showAutoDuplicate" />
      <TuidaoRoomConfig v-else-if="form.room_rule === 'guangdong'" :form="form" :show-password="showPassword" />
      <GuizhouRoomConfig v-else-if="form.room_rule === 'guizhou'" :form="form" :show-password="showPassword" />
      <SingaporeRoomConfig v-else-if="form.room_rule === 'singapore'" :form="form" :show-password="showPassword" />
      <JiujiangRoomConfig v-else-if="form.room_rule === 'jiujiang'" :form="form" :show-password="showPassword" />
      <WuhanRoomConfig v-else-if="form.room_rule === 'wuhan'" :form="form" :show-password="showPassword" />
      <HongzhongRoomConfig v-else-if="form.room_rule === 'hongzhong'" :form="form" :show-password="showPassword" />
      <ChangshaRoomConfig v-else-if="form.room_rule === 'changsha'" :form="form" :show-password="showPassword" />
      <ChangchunRoomConfig v-else-if="form.room_rule === 'changchun'" :form="form" :show-password="showPassword" />
      <YixingRoomConfig v-else-if="form.room_rule === 'yixing'" :form="form" :show-password="showPassword" />
      <MalaysiaRoomConfig v-else-if="form.room_rule === 'malaysia'" :form="form" :show-password="showPassword" />
      <ShenyangRoomConfig v-else-if="form.room_rule === 'shenyang'" :form="form" :show-password="showPassword" />
      <NanjingRoomConfig v-else-if="form.room_rule === 'nanjing'" :form="form" :show-password="showPassword" />
      <FuzhouRoomConfig v-else-if="form.room_rule === 'fuzhou'" :form="form" :show-password="showPassword" />
      <WenzhouRoomConfig v-else-if="form.room_rule === 'wenzhou'" :form="form" :show-password="showPassword" />
      <NanchangRoomConfig v-else-if="form.room_rule === 'nanchang'" :form="form" :show-password="showPassword" />
      <HangzhouRoomConfig v-else-if="form.room_rule === 'hangzhou'" :form="form" :show-password="showPassword" />
      <RiichiRoomConfig v-else-if="form.room_rule === 'riichi'" :form="form" />
      <el-alert
        v-else
        title="此规则按默认参数创建。"
        type="info"
        :closable="false"
        show-icon
      />
      <div v-if="paired" class="paired-settings">
        <div class="paired-preview" aria-label="开闭室配对">
          <div><strong>开室</strong><span>东西蓝队 · 南北红队</span></div>
          <span class="paired-link" aria-hidden="true">⇄</span>
          <div><strong>闭室</strong><span>东西红队 · 南北蓝队</span></div>
        </div>
        <el-form-item label="比赛副数">
          <el-select v-model="form.duplicate_round_count" aria-label="比赛副数" :disabled="Boolean(form.duplicate_key?.trim())">
            <el-option v-for="count in DUPLICATE_ROUND_COUNTS" :key="count" :label="`${count} 副`" :value="count" />
          </el-select>
        </el-form-item>
        <template v-if="form.sub_rule === 'sichuan/xueliu_doubles_duplicate'">
          <DuplicateRoomField v-model="form.duplicate_key" />
          <p class="duplicate-help">可填入本比赛的血流手动、种子或密钥牌山；副数跟随密钥。留空则为每组自动生成牌山。</p>
        </template>
        <p class="duplicate-help">{{ form.sub_rule === 'sichuan/doubles_duplicate' ? '逐副按 SBR IMP 计分，保留副差分明细。' : '逐副分差以 80 分封顶累计，同时记录 JJ IMP；按副分判定，平分时两室同步加副。' }}每组自动生成同一套牌墙，两室同时就绪后开始。</p>
      </div>
      <div v-if="supportsDuplicateKey(form) && !paired" class="duplicate-settings">
        <div class="room-top">
          <el-form-item label="复式">
            <el-switch v-model="form.duplicate_enabled" aria-label="复式" />
          </el-form-item>
          <el-form-item v-if="form.duplicate_enabled && showAutoDuplicate" label="自动生成复式密钥">
            <el-switch v-model="form.auto_duplicate" aria-label="自动生成复式密钥" />
          </el-form-item>
        </div>
        <template v-if="form.duplicate_enabled">
          <div v-if="autoDuplicate" class="duplicate-generation">
            <el-form-item label="复式局数">
              <el-select v-model="form.duplicate_round_count" aria-label="复式局数" style="width: 100%">
                <el-option v-for="count in DUPLICATE_ROUND_COUNTS" :key="count" :label="duplicateRoundLabel(count)" :value="count" />
              </el-select>
            </el-form-item>
            <p class="duplicate-help">每个房间生成并绑定一个独立密钥，本次创建 {{ form.room_count }} 个密钥。花牌使用上方设置，局数跟随密钥。</p>
            <p v-if="duplicateQuota" class="duplicate-help">今日已创建 {{ duplicateQuota.created_today }} / {{ duplicateQuota.daily_limit }} 个，剩余 {{ duplicateQuota.remaining_today }} 个；可保存 {{ duplicateQuota.remaining_storage }} 个。额度由赛事管理员共用，北京时间零点刷新。</p>
          </div>
          <DuplicateRoomField v-else v-model="form.duplicate_key" required />
          <el-alert v-if="creationProblem" :title="creationProblem" type="warning" :closable="false" show-icon />
        </template>
      </div>
    </el-form>
    <template #footer>
      <el-button :disabled="loading" @click="$emit('update:modelValue', false)">取消</el-button>
      <el-button type="primary" :loading="loading" :disabled="Boolean(creationProblem)" @click="confirm">{{ paired ? `创建 ${form.room_count} 组开闭室` : form.room_count > 1 ? `创建 ${form.room_count} 个房间` : confirmText }}</el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import SichuanRoomConfig from '@/components/SichuanRoomConfig.vue'
import GuobiaoEmptyRoomConfig from '@/components/GuobiaoEmptyRoomConfig.vue'
import DuplicateRoomField from '@/components/DuplicateRoomField.vue'
import ShanxiRoomConfig from '@/components/ShanxiRoomConfig.vue'
import HongKongRoomConfig from '@/components/HongKongRoomConfig.vue'
import TuidaoRoomConfig from '@/components/TuidaoRoomConfig.vue'
import GuizhouRoomConfig from '@/components/GuizhouRoomConfig.vue'
import YixingRoomConfig from '@/components/YixingRoomConfig.vue'
import MalaysiaRoomConfig from '@/components/MalaysiaRoomConfig.vue'
import ShenyangRoomConfig from '@/components/ShenyangRoomConfig.vue'
import NanjingRoomConfig from '@/components/NanjingRoomConfig.vue'
import FuzhouRoomConfig from '@/components/FuzhouRoomConfig.vue'
import WenzhouRoomConfig from '@/components/WenzhouRoomConfig.vue'
import NanchangRoomConfig from '@/components/NanchangRoomConfig.vue'
import HangzhouRoomConfig from '@/components/HangzhouRoomConfig.vue'
import SingaporeRoomConfig from '@/components/SingaporeRoomConfig.vue'
import JiujiangRoomConfig from '@/components/JiujiangRoomConfig.vue'
import WuhanRoomConfig from '@/components/WuhanRoomConfig.vue'
import HongzhongRoomConfig from '@/components/HongzhongRoomConfig.vue'
import ChangshaRoomConfig from '@/components/ChangshaRoomConfig.vue'
import ChangchunRoomConfig from '@/components/ChangchunRoomConfig.vue'
import RiichiRoomConfig from '@/components/RiichiRoomConfig.vue'
import { changeEventRoomRule, clearUnsupportedDuplicateRoom } from '@/utils/eventRoomSettings'
import { EVENT_ROOM_COUNTS } from '@/utils/eventRoomCreation'
import { DUPLICATE_ROUND_COUNTS } from '@/utils/duplicateWalls'
import { isSichuanDuplicate, supportsDuplicateKey } from '@/utils/sichuanDuplicate'

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  form: { type: Object, required: true },
  title: { type: String, default: '创建房间' },
  confirmText: { type: String, default: '创建' },
  loading: { type: Boolean, default: false },
  showReason: { type: Boolean, default: false },
  showPassword: { type: Boolean, default: true },
  showAutoDuplicate: { type: Boolean, default: false },
  loadDuplicateQuota: { type: Function, default: null },
  roomRuleOptions: { type: Array, required: true },
})

const emit = defineEmits(['update:modelValue', 'confirm'])
const duplicateQuota = ref(null)
const paired = computed(() => props.form.room_rule === 'sichuan' && isSichuanDuplicate(props.form.sub_rule))
watch(() => props.form.sub_rule, value => {
  if (isSichuanDuplicate(value)) props.form.duplicate_round_count = value === 'sichuan/doubles_duplicate' ? 8 : 16
})
const quotaLoading = ref(false)
const quotaError = ref('')
let quotaVersion = 0
const autoDuplicate = computed(() => props.showAutoDuplicate && supportsDuplicateKey(props.form) && !paired.value && props.form.duplicate_enabled && props.form.auto_duplicate)
const quotaProblem = computed(() => {
  if (!props.modelValue || !autoDuplicate.value) return ''
  if (quotaLoading.value) return '正在读取复式密钥额度…'
  if (quotaError.value || !duplicateQuota.value) return quotaError.value || '无法读取复式密钥额度，请重新打开面板重试'
  if (duplicateQuota.value.remaining_today < props.form.room_count) return `今日剩余 ${duplicateQuota.value.remaining_today} 个密钥额度，本次需要 ${props.form.room_count} 个`
  if (duplicateQuota.value.remaining_storage < props.form.room_count) return `剩余可保存 ${duplicateQuota.value.remaining_storage} 个密钥，本次需要 ${props.form.room_count} 个`
  return ''
})
const creationProblem = computed(() => {
  if (quotaProblem.value) return quotaProblem.value
  if (paired.value && props.form.duplicate_key?.trim() && props.form.room_count !== 1) return '一个已有牌山只能绑定一组开闭室'
  if (supportsDuplicateKey(props.form) && !paired.value && props.form.duplicate_enabled && !autoDuplicate.value && !props.form.duplicate_key?.trim()) {
    return props.showAutoDuplicate ? '请输入复式密钥或开启自动生成复式密钥' : '请输入复式密钥'
  }
  return ''
})

function duplicateRoundLabel(count) {
  const mode = { 4: '东风战', 8: '东南战', 12: '东西战', 16: '全庄战' }[count]
  return mode ? `${count} 局（${mode}）` : `${count} 局`
}

watch(() => [props.form.room_rule, props.form.sub_rule], () => {
  clearUnsupportedDuplicateRoom(props.form)
  if (!supportsDuplicateKey(props.form) || paired.value) {
    props.form.duplicate_enabled = false
    props.form.auto_duplicate = false
  }
}, { immediate: true, flush: 'sync' })

watch(() => props.form.duplicate_enabled, enabled => {
  if (!enabled) {
    props.form.auto_duplicate = false
    props.form.duplicate_key = ''
  }
}, { immediate: true, flush: 'sync' })

watch(() => [props.modelValue, autoDuplicate.value, props.loadDuplicateQuota], async ([open, automatic]) => {
  const version = ++quotaVersion
  duplicateQuota.value = null
  quotaError.value = ''
  quotaLoading.value = false
  if (!open || !automatic) return
  props.form.duplicate_key = ''
  quotaLoading.value = true
  try {
    const quota = await props.loadDuplicateQuota?.()
    if (version === quotaVersion) duplicateQuota.value = quota || null
  } catch (error) {
    if (version === quotaVersion) quotaError.value = error.response?.data?.message || error.message || '读取复式密钥额度失败'
  } finally {
    if (version === quotaVersion) quotaLoading.value = false
  }
}, { immediate: true })

function confirm() {
  if (!props.loading && !creationProblem.value) emit('confirm')
}
</script>

<style scoped>
.paired-settings { margin-top: 16px; padding: 16px; background: #f0f5fc; border: 1px solid #d9e5f5; border-radius: 12px; }
.paired-preview { display: grid; grid-template-columns: 1fr auto 1fr; align-items: center; gap: 12px; margin-bottom: 18px; }
.paired-preview > div { display: flex; flex-direction: column; gap: 6px; padding: 14px; background: #fff; border-radius: 8px; }
.paired-preview strong { color: #344f73; font-size: 15px; }
.paired-preview span { color: #657b98; font-size: 12px; }
.paired-preview .paired-link { font-size: 23px; }
@media (max-width: 440px) { .paired-preview { gap: 6px; } .paired-preview > div { padding: 10px; } }
.room-top {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0 16px;
}
.room-top-full {
  grid-column: 1 / -1;
}
:global(.venue-room-dialog.el-dialog) {
  display: flex;
  flex-direction: column;
  max-height: 90vh;
  max-height: 90dvh;
}
:global(.venue-room-dialog .el-dialog__body) {
  min-height: 0;
  overflow-y: auto;
}
:global(.venue-room-dialog .el-dialog__header),
:global(.venue-room-dialog .el-dialog__footer) {
  flex-shrink: 0;
}
.duplicate-settings { margin-top: 4px; padding-top: 12px; border-top: 1px solid #ebeef5; }
.duplicate-generation { margin-bottom: 12px; padding: 12px; background: #f5f7fa; border-radius: 6px; }
.duplicate-help { margin: 4px 0 8px; color: #737d88; font-size: 12px; line-height: 1.6; }
.room-form :deep(.el-form-item) {
  margin-bottom: 12px;
}
@media (max-width: 640px) {
  .room-top {
    grid-template-columns: 1fr;
  }
}
</style>
