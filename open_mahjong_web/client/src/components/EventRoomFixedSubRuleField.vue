<template>
  <el-form-item v-if="!EVENT_RULES_WITH_SUB_RULE_SELECTOR.has(form.room_rule)" label="子规则" class="room-sub-rule-field">
    <el-input :model-value="eventRoomSubRuleLabel(form)" readonly aria-label="子规则" />
  </el-form-item>
</template>
<script setup>
import { EVENT_RULES_WITH_SUB_RULE_SELECTOR, eventRoomSubRuleLabel } from '@/utils/eventRoomSettings.js'
defineProps({ form: { type: Object, required: true } })
</script>
