<template>
  <el-dialog
    :model-value="modelValue"
    :title="title"
    width="min(720px, calc(100vw - 24px))"
    class="venue-room-dialog"
    destroy-on-close
    :close-on-click-modal="!loading"
    :close-on-press-escape="!loading"
    :show-close="!loading"
    @close="$emit('update:modelValue', false)"
  >
    <el-form label-position="top" class="room-form" @submit.prevent="$emit('confirm')">
      <slot name="before-settings" />
      <div class="room-top">
        <el-form-item label="规则">
          <el-select v-model="form.room_rule" style="width: 100%">
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
        <el-form-item v-if="showReason" label="操作原因" required>
          <el-input v-model="form.reason" clearable placeholder="审计必填" />
        </el-form-item>
      </div>
      <GuobiaoEmptyRoomConfig v-if="form.room_rule === 'guobiao'" :model-value="form" :show-password="showPassword" />
      <ShanxiRoomConfig v-else-if="form.room_rule === 'shanxi'" :form="form" :show-password="showPassword" />
      <HongKongRoomConfig v-else-if="form.room_rule === 'hongkong'" :form="form" />
      <TuidaoRoomConfig v-else-if="form.room_rule === 'guangdong'" :form="form" :show-password="showPassword" />
      <GuizhouRoomConfig v-else-if="form.room_rule === 'guizhou'" :form="form" :show-password="showPassword" />
      <HongzhongRoomConfig v-else-if="form.room_rule === 'hongzhong'" :form="form" :show-password="showPassword" />
      <ChangchunRoomConfig v-else-if="form.room_rule === 'changchun'" :form="form" :show-password="showPassword" />
      <YixingRoomConfig v-else-if="form.room_rule === 'yixing'" :form="form" :show-password="showPassword" />
      <WenzhouRoomConfig v-else-if="form.room_rule === 'wenzhou'" :form="form" :show-password="showPassword" />
      <HangzhouRoomConfig v-else-if="form.room_rule === 'hangzhou'" :form="form" :show-password="showPassword" />
      <el-form-item v-else-if="form.room_rule === 'riichi'" label="起始点数">
        <el-input-number v-model="form.starting_score" :min="1000" :max="1000000" :step="100" step-strictly :precision="0" />
      </el-form-item>
      <el-alert
        v-else
        title="此规则按默认参数创建。"
        type="info"
        :closable="false"
        show-icon
      />
    </el-form>
    <template #footer>
      <el-button :disabled="loading" @click="$emit('update:modelValue', false)">取消</el-button>
      <el-button type="primary" :loading="loading" @click="$emit('confirm')">{{ confirmText }}</el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { watch } from 'vue'
import GuobiaoEmptyRoomConfig from '@/components/GuobiaoEmptyRoomConfig.vue'
import ShanxiRoomConfig from '@/components/ShanxiRoomConfig.vue'
import HongKongRoomConfig from '@/components/HongKongRoomConfig.vue'
import TuidaoRoomConfig from '@/components/TuidaoRoomConfig.vue'
import GuizhouRoomConfig from '@/components/GuizhouRoomConfig.vue'
import YixingRoomConfig from '@/components/YixingRoomConfig.vue'
import WenzhouRoomConfig from '@/components/WenzhouRoomConfig.vue'
import HangzhouRoomConfig from '@/components/HangzhouRoomConfig.vue'
import HongzhongRoomConfig from '@/components/HongzhongRoomConfig.vue'
import ChangchunRoomConfig from '@/components/ChangchunRoomConfig.vue'
import { clearUnsupportedDuplicateRoom } from '@/utils/eventRoomSettings'

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  form: { type: Object, required: true },
  title: { type: String, default: '创建房间' },
  confirmText: { type: String, default: '创建' },
  loading: { type: Boolean, default: false },
  showReason: { type: Boolean, default: false },
  showPassword: { type: Boolean, default: true },
  roomRuleOptions: { type: Array, required: true },
})

watch(() => props.form.room_rule, () => clearUnsupportedDuplicateRoom(props.form), { immediate: true, flush: 'sync' })

defineEmits(['update:modelValue', 'confirm'])
</script>

<style scoped>
.room-top {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0 16px;
}
.room-top :deep(.el-form-item:nth-child(3)) {
  grid-column: 1 / -1;
}
.room-form :deep(.el-form-item) {
  margin-bottom: 12px;
}
@media (max-width: 640px) {
  .room-top {
    grid-template-columns: 1fr;
  }
}
</style>
