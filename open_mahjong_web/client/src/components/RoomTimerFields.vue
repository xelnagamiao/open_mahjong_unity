<template>
  <div class="room-timers">
    <el-form-item label="局时（秒）"><el-input-number v-model="form.round_timer" aria-label="局时（秒）" :min="0" :max="1000" :precision="0" /></el-form-item>
    <el-form-item label="步时（秒）"><el-input-number v-model="form.step_timer" aria-label="步时（秒）" :min="0" :max="100" :precision="0" /></el-form-item>
  </div>
</template>
<script setup>
defineProps({ form: { type: Object, required: true } })
</script>
<style scoped>
.room-timers { display: flex; flex-wrap: wrap; gap: 0 24px; }
</style>
