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
    :show-close="!busy"
    @close="$emit('update:modelValue', false)"
  >
    <el-form label-position="top" class="room-form" :disabled="busy" @submit.prevent="confirm">
      <slot name="before-settings" />
      <EventRoomFieldsLayout>
      <div class="room-top">
        <el-form-item label="规则" class="room-rule-field">
          <el-select v-model="form.room_rule" style="width: 100%" @change="changeEventRoomRule(form, $event)">
            <el-option
              v-for="opt in roomRuleOptions"
              :key="opt.value"
              :label="opt.label"
              :value="opt.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="房间名" class="room-name-field">
          <el-input v-model="form.room_name" clearable placeholder="可选" />
        </el-form-item>
        <el-form-item label="创建数量" class="room-count-field">
          <el-select v-model="form.room_count" aria-label="创建数量" style="width: 100%">
            <el-option v-for="count in EVENT_ROOM_COUNTS" :key="count" :label="paired ? `${count} 个八人房间` : `${count} 个房间`" :value="count" />
          </el-select>
        </el-form-item>
        <el-form-item v-if="showReason" label="操作原因" required class="room-top-full">
          <el-input v-model="form.reason" clearable placeholder="审计必填" />
        </el-form-item>
      </div>
      <EventRoomFixedSubRuleField :form="form" />
      <el-form-item v-if="showPassword" label="设置密码">
        <el-switch v-model="form.password_enabled" aria-label="设置密码" />
      </el-form-item>
      <el-form-item v-if="showPassword && form.password_enabled" label="房间密码">
        <el-input v-model="form.password" type="password" show-password clearable aria-label="房间密码" />
      </el-form-item>
      <GuobiaoEmptyRoomConfig v-if="form.room_rule === 'guobiao'" :model-value="form" :show-password="false" :auto-duplicate="autoDuplicate" :show-duplicate-field="false" />
      <ShanxiRoomConfig v-else-if="form.room_rule === 'shanxi'" :form="form" :show-password="false" />
      <HongKongRoomConfig v-else-if="form.room_rule === 'hongkong'" :form="form" />
      <SichuanRoomConfig v-else-if="form.room_rule === 'sichuan'" :form="form" :allow-paired="allowPaired" />
      <TuidaoRoomConfig v-else-if="form.room_rule === 'guangdong'" :form="form" :show-password="false" />
      <GuizhouRoomConfig v-else-if="form.room_rule === 'guizhou'" :form="form" :show-password="false" />
      <SingaporeRoomConfig v-else-if="form.room_rule === 'singapore'" :form="form" :show-password="false" />
      <JiujiangRoomConfig v-else-if="form.room_rule === 'jiujiang'" :form="form" :show-password="false" />
      <WuhanRoomConfig v-else-if="form.room_rule === 'wuhan'" :form="form" :show-password="false" />
      <HongzhongRoomConfig v-else-if="form.room_rule === 'hongzhong'" :form="form" :show-password="false" />
      <ChangshaRoomConfig v-else-if="form.room_rule === 'changsha'" :form="form" :show-password="false" />
      <ChangchunRoomConfig v-else-if="form.room_rule === 'changchun'" :form="form" :show-password="false" />
      <YixingRoomConfig v-else-if="form.room_rule === 'yixing'" :form="form" :show-password="false" />
      <MalaysiaRoomConfig v-else-if="form.room_rule === 'malaysia'" :form="form" :show-password="false" />
      <ShenyangRoomConfig v-else-if="form.room_rule === 'shenyang'" :form="form" :show-password="false" />
      <NanjingRoomConfig v-else-if="form.room_rule === 'nanjing'" :form="form" :show-password="false" />
      <FuzhouRoomConfig v-else-if="form.room_rule === 'fuzhou'" :form="form" :show-password="false" />
      <WenzhouRoomConfig v-else-if="form.room_rule === 'wenzhou'" :form="form" :show-password="false" />
      <NanchangRoomConfig v-else-if="form.room_rule === 'nanchang'" :form="form" :show-password="false" />
      <ShangraoRoomConfig v-else-if="form.room_rule === 'shangrao'" :form="form" :show-password="false" />
      <HongqueRoomConfig v-else-if="form.room_rule === 'hongque'" :form="form" :show-password="false" />
      <BeijingRoomConfig v-else-if="form.room_rule === 'beijing'" :form="form" :show-password="false" />
      <DuocaiRoomConfig v-else-if="form.room_rule === 'duocai'" :form="form" :show-password="false" />
      <HangzhouRoomConfig v-else-if="form.room_rule === 'hangzhou'" :form="form" :show-password="false" />
      <RiichiRoomConfig v-else-if="form.room_rule === 'riichi'" :form="form" />
      <RoomTimerFields v-else :form="form" />
      <EventRoomSupplementFields :form="form" :show-password="false" />
      <div v-if="paired" class="paired-settings">
        <SichuanWallSourceFields :form="form" :generate-duplicate-key="generateDuplicateKey" @generating="generatingKey = $event" />
        <el-form-item label="复式模式" class="paired-mode-selection">
          <el-radio-group v-model="form.duplicate_sync_mode" aria-label="复式模式" class="room-segmented-options">
            <el-radio-button :label="false">非同步模式</el-radio-button>
            <el-radio-button :label="true">同步模式</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <div class="paired-help" aria-label="牌山与模式说明">
          <p v-if="wallSourceHelp">{{ wallSourceHelp }}</p>
          <p>{{ form.duplicate_sync_mode ? '同步模式：适合复盘的对局，两桌对局的过程中，每打完一副牌都对比IMP' : '非同步模式：正常对局，两桌打完后对比IMP' }}</p>
        </div>
        <el-alert v-if="creationProblem" :title="creationProblem" type="warning" :closable="false" show-icon />
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
            <p v-if="duplicateQuota" class="duplicate-help">今日已创建 {{ duplicateQuota.created_today }} / {{ duplicateQuota.daily_limit }} 个，剩余 {{ duplicateQuota.remaining_today }} 个；可保存 {{ duplicateQuota.remaining_storage }} 个。额度由场馆管理员共用，北京时间零点刷新。</p>
          </div>
          <DuplicateRoomField v-else v-model="form.duplicate_key" required :sichuan="form.room_rule === 'sichuan'" :create-to="{ path: '/account', hash: '#sec-duplicate-event' }" />
          <el-alert v-if="creationProblem" :title="creationProblem" type="warning" :closable="false" show-icon />
        </template>
      </div>
      </EventRoomFieldsLayout>
    </el-form>
    <template #footer>
      <el-button :disabled="busy" @click="$emit('update:modelValue', false)">取消</el-button>
      <el-button type="primary" :loading="loading" :disabled="busy || Boolean(creationProblem)" @click="confirm">{{ paired ? `创建 ${form.room_count} 个八人房间` : form.room_count > 1 ? `创建 ${form.room_count} 个房间` : confirmText }}</el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import EventRoomSupplementFields from '@/components/EventRoomSupplementFields.vue'
import EventRoomFieldsLayout from '@/components/EventRoomFieldsLayout.vue'
import EventRoomFixedSubRuleField from '@/components/EventRoomFixedSubRuleField.vue'
import SichuanWallSourceFields from '@/components/SichuanWallSourceFields.vue'
import { isTrainingWall, SICHUAN_WALL_SOURCES } from '@/utils/sichuanWallSource.js'
import SichuanRoomConfig from '@/components/SichuanRoomConfig.vue'
import RoomTimerFields from '@/components/RoomTimerFields.vue'
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
import ShangraoRoomConfig from '@/components/ShangraoRoomConfig.vue'
import HongqueRoomConfig from '@/components/HongqueRoomConfig.vue'
import BeijingRoomConfig from '@/components/BeijingRoomConfig.vue'
import DuocaiRoomConfig from '@/components/DuocaiRoomConfig.vue'
import HangzhouRoomConfig from '@/components/HangzhouRoomConfig.vue'
import SingaporeRoomConfig from '@/components/SingaporeRoomConfig.vue'
import JiujiangRoomConfig from '@/components/JiujiangRoomConfig.vue'
import WuhanRoomConfig from '@/components/WuhanRoomConfig.vue'
import HongzhongRoomConfig from '@/components/HongzhongRoomConfig.vue'
import ChangshaRoomConfig from '@/components/ChangshaRoomConfig.vue'
import ChangchunRoomConfig from '@/components/ChangchunRoomConfig.vue'
import RiichiRoomConfig from '@/components/RiichiRoomConfig.vue'
import { changeEventRoomRule, clearUnsupportedDuplicateRoom } from '@/utils/eventRoomSettings'
import { EVENT_ROOM_COUNTS, buildEventRoomCreation, sharedWallConfirmation } from '@/utils/eventRoomCreation'
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
  allowPaired: { type: Boolean, default: false },
  loadDuplicateQuota: { type: Function, default: null },
  generateDuplicateKey: { type: Function, default: null },
  roomRuleOptions: { type: Array, required: true },
})

const emit = defineEmits(['update:modelValue', 'confirm'])
const duplicateQuota = ref(null)
const generatingKey = ref(false)
const confirming = ref(false)
const busy = computed(() => props.loading || generatingKey.value || confirming.value)
const paired = computed(() => props.form.room_rule === 'sichuan' && isSichuanDuplicate(props.form.sub_rule))
const wallSourceHelp = computed(() => SICHUAN_WALL_SOURCES.find(source => source.value === props.form.wall_source)?.description || '')
const pairedExisting = computed(() => !isTrainingWall(props.form) && (['seed', 'manual'].includes(props.form.wall_source) || props.form.duplicate_key_source === 'existing'))
watch(() => props.form.sub_rule, value => {
  if (isSichuanDuplicate(value) && !props.form.duplicate_key?.trim()) {
    props.form.duplicate_round_count = value === 'sichuan/doubles_duplicate' ? 8 : 16
    props.form.wall_source ||= 'key'
    props.form.duplicate_key_source = ['seed', 'manual'].includes(props.form.wall_source) ? 'existing' : 'auto'
    props.form.duplicate_key = ''
    props.form.scene_reproduction = false
  }
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
  if (paired.value && isTrainingWall(props.form) && !props.form.random_seed?.trim()) return '请输入训练牌山随机种子或随机生成'
  if (paired.value && pairedExisting.value && !props.form.duplicate_key?.trim()) return '请输入复式密钥'
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
  if (!enabled && !paired.value) {
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

async function confirm() {
  if (busy.value || creationProblem.value) return
  confirming.value = true
  try {
    const payload = buildEventRoomCreation(props.form)
    const message = sharedWallConfirmation(payload)
    if (message) await ElMessageBox.confirm(message, '确认使用相同牌山', {
      confirmButtonText: '确认创建', cancelButtonText: '取消', type: 'warning', closeOnClickModal: false,
    })
    if (props.modelValue) emit('confirm', payload)
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') ElMessage.error(error.message || '创建失败')
  } finally {
    confirming.value = false
  }
}
</script>

<style scoped>
.paired-settings { display: contents; }
.paired-help { grid-column: 1 / -1; order: 3; display: grid; gap: 4px; color: #737d88; font-size: 12px; font-weight: 400; line-height: 1.6; }
.paired-help p { margin: 0; }
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
.room-form :deep(.event-room-fields .el-form-item) {
  margin-bottom: 0;
}
@media (max-width: 640px) {
  .room-top {
    grid-template-columns: 1fr;
  }
}
</style>
