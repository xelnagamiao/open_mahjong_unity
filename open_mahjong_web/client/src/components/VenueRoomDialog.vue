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
      <el-alert
        v-else
        title="当前仅国标房间提供完整对局配置；其他规则仍按服务端默认参数创建。"
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
import GuobiaoEmptyRoomConfig from '@/components/GuobiaoEmptyRoomConfig.vue'

defineProps({
  modelValue: { type: Boolean, default: false },
  form: { type: Object, required: true },
  title: { type: String, default: '创建房间' },
  confirmText: { type: String, default: '创建' },
  loading: { type: Boolean, default: false },
  showReason: { type: Boolean, default: false },
  showPassword: { type: Boolean, default: true },
  roomRuleOptions: { type: Array, required: true },
})

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
