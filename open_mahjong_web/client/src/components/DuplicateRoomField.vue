<template>
  <el-form-item :label="tr('复式密钥')" class="duplicate-room-field" :required="required">
    <div class="duplicate-room-input">
      <el-input :model-value="modelValue" :disabled="disabled" :aria-label="tr('复式密钥')" clearable maxlength="128" :placeholder="placeholder || tr(required ? '填入已创建牌墙的密钥' : '可选，填入已创建牌墙的密钥')" autocomplete="off" @update:model-value="$emit('update:modelValue', $event)" />
      <slot name="after-input" />
    </div>
    <p class="duplicate-room-help">{{ tr(sichuan ? '副数和牌山布局跟随密钥，血战与血流密钥不能混用。' : '局数和花牌跟随密钥设置。房间不显示密钥，解锁后可在牌谱中查看。') }}<router-link :to="createTo" target="_blank">{{ tr('创建与说明') }}</router-link></p>
  </el-form-item>
</template>

<script setup>
import { tr } from '@/i18n'
defineProps({ modelValue: { type: String, default: '' }, required: { type: Boolean, default: false }, disabled: Boolean, placeholder: { type: String, default: '' }, sichuan: Boolean, createTo: { type: [String, Object], default: '/duplicate' } })
defineEmits(['update:modelValue'])
</script>

<style scoped>
.duplicate-room-field { grid-column: 1 / -1; }
.duplicate-room-input { display: flex; align-items: center; gap: 8px; width: 100%; min-width: 0; }
.duplicate-room-input :deep(.el-input) { flex: 1; min-width: 0; }
.duplicate-room-help { width: 100%; margin: 5px 0 0; color: #737d88; font-size: 12px; line-height: 1.6; }
.duplicate-room-help a { color: #17756a; margin-inline-start: 0.25em; }
</style>
