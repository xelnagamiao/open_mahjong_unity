<template>
  <el-form-item :label="tr('复式密钥')" class="duplicate-room-field" :required="required">
    <el-input :model-value="modelValue" :aria-label="tr('复式密钥')" clearable maxlength="128" :placeholder="tr(required ? '填入已创建牌墙的密钥' : '可选，填入已创建牌墙的密钥')" autocomplete="off" @update:model-value="$emit('update:modelValue', $event)" />
    <p class="duplicate-room-help">{{ tr('局数和花牌跟随密钥设置。房间不显示密钥，解锁后可在牌谱中查看。') }}<router-link to="/duplicate" target="_blank">{{ tr('创建与说明') }}</router-link></p>
  </el-form-item>
</template>

<script setup>
import { tr } from '@/i18n'
defineProps({ modelValue: { type: String, default: '' }, required: { type: Boolean, default: false } })
defineEmits(['update:modelValue'])
</script>

<style scoped>
.duplicate-room-field { grid-column: 1 / -1; }
.duplicate-room-help { width: 100%; margin: 5px 0 0; color: #737d88; font-size: 12px; line-height: 1.6; }
.duplicate-room-help a { color: #17756a; margin-inline-start: 0.25em; }
</style>
